using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Lucode;

/// <summary>
/// `lucode build file.luc`: makes a standalone program. The running lucode is copied and the
/// program's name, text and the files it uses are added to the end of the copy, followed by their length and a
/// marker. When a lucode starts and finds that marker at the end of itself, it runs the
/// embedded program instead of reading the command line.
/// </summary>
/// <param name="Format">null for a plain executable, otherwise a package kind such as "deb".</param>
public sealed record BuildOptions(string Input, string? Output, string? Format, string? Version, string? Maintainer, string? Description);

/// <summary>What `lucode build` packs into the program: its name and text, and every file it uses (by path from its folder).</summary>
public sealed record EmbeddedProgram(string Name, string Source, Dictionary<string, string> Modules);

public static class Bundle
{
    private static readonly byte[] Marker = Encoding.ASCII.GetBytes("LUCODEP2"); // 8 bytes
    private const int TrailerSize = 8 + 8; // payload length + marker

    /// <summary>The program built into this executable, or null for a plain lucode.</summary>
    public static EmbeddedProgram? ReadEmbedded()
    {
        string? self = Environment.ProcessPath;
        if (self is null || !File.Exists(self)) return null;

        try
        {
            using var file = File.OpenRead(self);
            if (file.Length < TrailerSize) return null;

            var trailer = new byte[TrailerSize];
            file.Seek(-TrailerSize, SeekOrigin.End);
            file.ReadExactly(trailer);
            if (!trailer.AsSpan(8).SequenceEqual(Marker)) return null;

            long length = BinaryPrimitives.ReadInt64LittleEndian(trailer);
            if (length < 0 || length > file.Length - TrailerSize) return null;

            var payload = new byte[length];
            file.Seek(-TrailerSize - length, SeekOrigin.End);
            file.ReadExactly(payload);

            return JsonSerializer.Deserialize<EmbeddedProgram>(payload);
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return null;
        }
    }

    /// <summary>Checks the program, then writes the standalone copy or a package around it. Returns the exit code.</summary>
    public static int Build(BuildOptions options)
    {
        string input = options.Input;
        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"Can't find the file '{input}'.");
            return 1;
        }

        string? self = Environment.ProcessPath;
        if (self is null || Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("'build' needs the standalone lucode program (download it from the Releases page), " +
                                    "not 'dotnet lucode.dll'.");
            return 1;
        }

        string source = File.ReadAllText(input);
        string name = Path.GetFileName(input);
        // A program with a typo never becomes an executable. The same pass finds the files it uses, to pack them too.
        var loader = ModuleLoader.ForDirectory(Path.GetDirectoryName(Path.GetFullPath(input))!, name);
        SourceMap.Reset(name);
        try
        {
            loader.Parse(source);
        }
        catch (LucodeError error)
        {
            Console.Error.WriteLine($"{error.File ?? name}, {error}");
            return 1;
        }
        var program = new EmbeddedProgram(name, source, loader.Files.ToDictionary(f => f.Key, f => f.Source));

        try
        {
            if (options.Format is null)
                return BuildExecutable(self, input, program, options.Output);

            return BuildPackage(self, input, program, options, Creator(options.Format));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Can't write the output: {e.Message}");
            return 1;
        }
    }

    /// <summary>The package maker for a format name such as "deb".</summary>
    private static Func<PackageInfo, string?> Creator(string format) => format switch
    {
        "deb" => DebPackage.Create,
        "rpm" => RpmPackage.Create,
        "appimage" => AppImagePackage.Create,
        "installer" => InstallerPackage.Create,
        "app" => MacPackage.CreateApp,
        "dmg" => MacPackage.CreateDmg,
        _ => throw new InvalidOperationException($"Unknown format {format}"),
    };

    /// <summary>`lucode package --deb`: packages lucode itself (no program inside) instead of a .luc program.</summary>
    public static int PackageSelf(BuildOptions options, string defaultVersion)
    {
        string? self = Environment.ProcessPath;
        if (self is null || Path.GetFileNameWithoutExtension(self).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("'package' needs the standalone lucode program (download it from the Releases page), " +
                                    "not 'dotnet lucode.dll'.");
            return 1;
        }

        string temp = Path.Combine(Path.GetTempPath(), "lucode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            // The packages work from a copy, so their scratch files never land next to the installed lucode.
            string exe = Path.Combine(temp, "program");
            File.Copy(self, exe);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(exe, File.GetUnixFileMode(exe)
                    | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);

            var info = new PackageInfo("lucode", exe, options.Output, options.Version ?? defaultVersion, options.Maintainer,
                options.Description ?? "Lucode, a small readable programming language for logic, math and live relationships");
            string? problem = Creator(options.Format!)(info);
            if (problem is not null)
            {
                Console.Error.WriteLine(problem);
                return 1;
            }
            return 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Can't write the output: {e.Message}");
            return 1;
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static int BuildExecutable(string self, string input, EmbeddedProgram program, string? output)
    {
        output ??= OperatingSystem.IsWindows()
            ? Path.ChangeExtension(input, ".exe")
            : Path.ChangeExtension(input, null);
        if (Path.GetFullPath(output) == Path.GetFullPath(input) || Path.GetFullPath(output) == Path.GetFullPath(self))
        {
            Console.Error.WriteLine($"Won't write over '{output}'. Pick another name with -o.");
            return 1;
        }

        WriteExecutable(self, output, program);
        Console.WriteLine($"Built {output}");
        return 0;
    }

    /// <summary>Builds the executable in a temporary place and hands it to a package maker.</summary>
    private static int BuildPackage(string self, string input, EmbeddedProgram program, BuildOptions options,
        Func<PackageInfo, string?> create)
    {
        string temp = Path.Combine(Path.GetTempPath(), "lucode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string exe = Path.Combine(temp, "program");
            WriteExecutable(self, exe, program);
            var info = new PackageInfo(
                Path.GetFileNameWithoutExtension(input), exe, options.Output,
                options.Version ?? "1.0", options.Maintainer, options.Description);
            string? problem = create(info);
            if (problem is not null)
            {
                Console.Error.WriteLine(problem);
                return 1;
            }
            return 0;
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static void WriteExecutable(string self, string output, EmbeddedProgram program)
    {
        File.Copy(self, output, overwrite: true);
        var payload = JsonSerializer.SerializeToUtf8Bytes(program);
        var length = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(length, payload.Length);
        using (var file = new FileStream(output, FileMode.Append, FileAccess.Write))
        {
            file.Write(payload);
            file.Write(length);
            file.Write(Marker);
        }

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(output, File.GetUnixFileMode(output)
                | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        if (OperatingSystem.IsMacOS())
            Sign(output);
    }

    /// <summary>macOS refuses to run a program whose signature no longer matches, so sign the copy again.</summary>
    private static void Sign(string path)
    {
        try
        {
            using var sign = Process.Start("codesign", new[] { "--force", "--sign", "-", path });
            sign?.WaitForExit();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine("Note: 'codesign' wasn't found; macOS may refuse to run the new program.");
        }
    }
}
