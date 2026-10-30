using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace Lucode;

/// <summary>
/// `lucode build file.luc`: makes a standalone program. The running lucode is copied and the
/// program's name and text are added to the end of the copy, followed by their length and a
/// marker. When a lucode starts and finds that marker at the end of itself, it runs the
/// embedded program instead of reading the command line.
/// </summary>
/// <param name="Format">null for a plain executable, otherwise a package kind such as "deb".</param>
public sealed record BuildOptions(string Input, string? Output, string? Format, string? Version, string? Maintainer, string? Description);

public static class Bundle
{
    private static readonly byte[] Marker = Encoding.ASCII.GetBytes("LUCODEPK"); // 8 bytes
    private const int TrailerSize = 8 + 8; // payload length + marker

    /// <summary>The program built into this executable, or null for a plain lucode.</summary>
    public static (string Name, string Source)? ReadEmbedded()
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

            string text = Encoding.UTF8.GetString(payload);
            int split = text.IndexOf('\n');
            return split < 0 ? null : (text[..split], text[(split + 1)..]);
        }
        catch (IOException)
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
        // A program with a typo never becomes an executable.
        if (LanguageServer.Check(source) is { } error)
        {
            Console.Error.WriteLine($"{name}, {error}");
            return 1;
        }

        try
        {
            if (options.Format is null)
                return BuildExecutable(self, input, name, source, options.Output);

            return options.Format switch
            {
                "deb" => BuildPackage(self, input, name, source, options, DebPackage.Create),
                "rpm" => BuildPackage(self, input, name, source, options, RpmPackage.Create),
                "appimage" => BuildPackage(self, input, name, source, options, AppImagePackage.Create),
                "installer" => BuildPackage(self, input, name, source, options, InstallerPackage.Create),
                _ => throw new InvalidOperationException($"Unknown format {options.Format}"),
            };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Can't write the output: {e.Message}");
            return 1;
        }
    }

    private static int BuildExecutable(string self, string input, string name, string source, string? output)
    {
        output ??= OperatingSystem.IsWindows()
            ? Path.ChangeExtension(input, ".exe")
            : Path.ChangeExtension(input, null);
        if (Path.GetFullPath(output) == Path.GetFullPath(input) || Path.GetFullPath(output) == Path.GetFullPath(self))
        {
            Console.Error.WriteLine($"Won't write over '{output}'. Pick another name with -o.");
            return 1;
        }

        WriteExecutable(self, output, name, source);
        Console.WriteLine($"Built {output}");
        return 0;
    }

    /// <summary>Builds the executable in a temporary place and hands it to a package maker.</summary>
    private static int BuildPackage(string self, string input, string name, string source, BuildOptions options,
        Func<PackageInfo, string?> create)
    {
        string temp = Path.Combine(Path.GetTempPath(), "lucode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string exe = Path.Combine(temp, "program");
            WriteExecutable(self, exe, name, source);
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

    private static void WriteExecutable(string self, string output, string name, string source)
    {
        File.Copy(self, output, overwrite: true);
        var payload = Encoding.UTF8.GetBytes(name + "\n" + source);
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
