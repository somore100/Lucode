using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Lucode;

/// <summary>What a package maker needs to know. <c>Executable</c> is the finished standalone program.</summary>
public sealed record PackageInfo(string Name, string Executable, string? Output, string Version, string? Maintainer, string? Description);

/// <summary>A .deb file (Debian, Ubuntu, Mint): an `ar` archive holding two gzipped tars.</summary>
public static class DebPackage
{
    /// <summary>Writes the package. Returns an error message, or null when it worked.</summary>
    public static string? Create(PackageInfo info)
    {
        if (!OperatingSystem.IsLinux())
            return "A .deb can only be made on Linux, because it needs a Linux lucode.";

        string? arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "amd64",
            Architecture.Arm64 => "arm64",
            _ => null,
        };
        if (arch is null)
            return $"A .deb for {RuntimeInformation.ProcessArchitecture} isn't supported (only amd64 and arm64).";

        // Debian package names: lowercase letters, digits and + - . , at least two characters, starting with a letter or digit.
        string name = Regex.Replace(info.Name.ToLowerInvariant(), "[^a-z0-9+.-]", "-").Trim('-', '.', '+');
        if (name.Length < 2)
            return $"'{info.Name}' can't be turned into a package name (needs two or more letters or digits).";
        if (!Regex.IsMatch(info.Version, "^[0-9][A-Za-z0-9.+~-]*$"))
            return $"'{info.Version}' isn't a valid version. It must start with a digit, like 1.0 or 2.3.1.";

        long size = new FileInfo(info.Executable).Length;
        string control =
            $"Package: {name}\n" +
            $"Version: {info.Version}\n" +
            "Section: utils\n" +
            "Priority: optional\n" +
            $"Architecture: {arch}\n" +
            "Depends: libc6, libstdc++6\n" +
            $"Installed-Size: {(size + 1023) / 1024}\n" +
            $"Maintainer: {OneLine(info.Maintainer) ?? "Lucode user <nobody@localhost>"}\n" +
            $"Description: {OneLine(info.Description) ?? $"{name}, a program written in Lucode"}\n";

        string output = info.Output ?? $"{name}_{info.Version}_{arch}.deb";
        string temp = Path.GetDirectoryName(info.Executable)!;
        string controlTar = Path.Combine(temp, "control.tar.gz");
        string dataTar = Path.Combine(temp, "data.tar.gz");

        using (var tar = new TarWriter(new GZipStream(File.Create(controlTar), CompressionLevel.Optimal)))
        {
            tar.AddDirectory("./", 0b111_101_101);
            tar.AddFile("./control", 0b110_100_100, new MemoryStream(Encoding.UTF8.GetBytes(control)));
        }

        using (var tar = new TarWriter(new GZipStream(File.Create(dataTar), CompressionLevel.Optimal)))
        {
            tar.AddDirectory("./", 0b111_101_101);
            tar.AddDirectory("./usr/", 0b111_101_101);
            tar.AddDirectory("./usr/bin/", 0b111_101_101);
            using var exe = File.OpenRead(info.Executable);
            tar.AddFile($"./usr/bin/{name}", 0b111_101_101, exe);
        }

        using (var deb = File.Create(output))
        {
            ArWriter.Start(deb);
            ArWriter.Add(deb, "debian-binary", new MemoryStream("2.0\n"u8.ToArray()));
            using (var c = File.OpenRead(controlTar)) ArWriter.Add(deb, "control.tar.gz", c);
            using (var d = File.OpenRead(dataTar)) ArWriter.Add(deb, "data.tar.gz", d);
        }

        Console.WriteLine($"Built {output} (installs /usr/bin/{name}; try: sudo apt install ./{output})");
        return null;
    }

    private static string? OneLine(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.ReplaceLineEndings(" ").Trim();
}

/// <summary>Just enough of the `ar` format for .deb files.</summary>
internal static class ArWriter
{
    public static void Start(Stream output) => output.Write("!<arch>\n"u8);

    public static void Add(Stream output, string name, Stream content)
    {
        // 16 name, 12 time, 6 owner, 6 group, 8 mode, 10 size, then "`\n".
        string header = name.PadRight(16)
            + DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString().PadRight(12)
            + "0".PadRight(6) + "0".PadRight(6) + "100644".PadRight(8)
            + content.Length.ToString().PadRight(10) + "`\n";
        output.Write(Encoding.ASCII.GetBytes(header));
        content.CopyTo(output);
        if (content.Length % 2 == 1) output.WriteByte((byte)'\n'); // members start on even offsets
    }
}

/// <summary>Just enough of the (ustar) tar format for packages: folders and plain files owned by root.</summary>
internal sealed class TarWriter : IDisposable
{
    private readonly Stream _output;

    public TarWriter(Stream output) => _output = output;

    public void AddDirectory(string name, int mode) => Header(name, 0, mode, '5');

    public void AddFile(string name, int mode, Stream content)
    {
        Header(name, content.Length, mode, '0');
        content.CopyTo(_output);
        int padding = (int)((512 - content.Length % 512) % 512);
        _output.Write(new byte[padding]);
    }

    private void Header(string name, long size, int mode, char type)
    {
        var h = new byte[512];
        Text(h, 0, 100, name);
        Octal(h, 100, 8, mode);
        Octal(h, 108, 8, 0); // owner
        Octal(h, 116, 8, 0); // group
        Octal(h, 124, 12, size);
        Octal(h, 136, 12, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        for (int i = 148; i < 156; i++) h[i] = (byte)' '; // the checksum counts itself as spaces
        h[156] = (byte)type;
        Text(h, 257, 6, "ustar\0");
        Text(h, 263, 2, "00");
        Text(h, 265, 32, "root");
        Text(h, 297, 32, "root");

        int sum = h.Sum(b => (int)b);
        Text(h, 148, 8, Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ");
        _output.Write(h);
    }

    private static void Text(byte[] block, int offset, int width, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        if (bytes.Length > width) throw new IOException($"'{text}' is too long for a tar header.");
        bytes.CopyTo(block, offset);
    }

    private static void Octal(byte[] block, int offset, int width, long value) =>
        Text(block, offset, width, Convert.ToString(value, 8).PadLeft(width - 1, '0') + "\0");

    public void Dispose()
    {
        _output.Write(new byte[1024]); // two empty blocks end a tar
        _output.Dispose();
    }
}

/// <summary>
/// An .rpm file (Fedora, openSUSE, RHEL). The format is fiddly, so this writes a small spec file
/// and lets `rpmbuild` (package rpm-build) do the packing.
/// </summary>
public static class RpmPackage
{
    /// <summary>Writes the package. Returns an error message, or null when it worked.</summary>
    public static string? Create(PackageInfo info)
    {
        if (!OperatingSystem.IsLinux())
            return "An .rpm can only be made on Linux, because it needs a Linux lucode.";

        string? arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x86_64",
            Architecture.Arm64 => "aarch64",
            _ => null,
        };
        if (arch is null)
            return $"An .rpm for {RuntimeInformation.ProcessArchitecture} isn't supported (only x86_64 and aarch64).";

        string name = Regex.Replace(info.Name.ToLowerInvariant(), "[^a-z0-9+._-]", "-").Trim('-', '.', '+', '_');
        if (name.Length < 2)
            return $"'{info.Name}' can't be turned into a package name (needs two or more letters or digits).";
        if (!Regex.IsMatch(info.Version, "^[0-9][A-Za-z0-9._+~^]*$"))
            return $"'{info.Version}' isn't a valid version. It must start with a digit and can't contain '-', like 1.0 or 2.3.1.";

        string temp = Path.GetDirectoryName(info.Executable)!;
        string summary = (string.IsNullOrWhiteSpace(info.Description) ? name + ", a program written in Lucode" : info.Description)
            .ReplaceLineEndings(" ").Trim();

        // No stripping (it would cut the program off the end of the file), no debug package, no guessing of
        // dependencies: the program is one self-contained file that only needs the system's C libraries.
        string spec = string.Join("\n",
            "%global debug_package %{nil}",
            "%define __os_install_post %{nil}",
            "Name: " + name,
            "Version: " + info.Version,
            "Release: 1",
            "Summary: " + summary,
            "License: Unspecified",
            "AutoReqProv: no",
            "Requires: glibc, libstdc++",
            info.Maintainer is null ? "" : "Packager: " + info.Maintainer.ReplaceLineEndings(" ").Trim(),
            "Source0: program",
            "%description",
            summary,
            "%prep",
            "%build",
            "%install",
            "mkdir -p %{buildroot}/usr/bin",
            "install -m 0755 %{SOURCE0} %{buildroot}/usr/bin/" + name,
            "%files",
            "/usr/bin/" + name,
            "");
        string specFile = Path.Combine(temp, name + ".spec");
        File.WriteAllText(specFile, spec);

        string rpmDir = Path.Combine(temp, "rpms");
        var start = new System.Diagnostics.ProcessStartInfo("rpmbuild")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[]
                 {
                     "-bb", "--define", "_topdir " + Path.Combine(temp, "rpmbuild"),
                     "--define", "_sourcedir " + temp, "--define", "_rpmdir " + rpmDir, specFile,
                 })
            start.ArgumentList.Add(argument);

        string text;
        int code;
        try
        {
            using var process = System.Diagnostics.Process.Start(start)!;
            text = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            code = process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "Making an .rpm needs 'rpmbuild'. Install it with: sudo dnf install rpm-build";
        }
        if (code != 0)
            return "rpmbuild failed:\n" + text;

        string built = Directory.GetFiles(rpmDir, "*.rpm", SearchOption.AllDirectories).Single();
        string output = info.Output ?? Path.GetFileName(built);
        File.Copy(built, output, overwrite: true);
        Console.WriteLine($"Built {output} (installs /usr/bin/{name}; try: sudo dnf install ./{output})");
        return null;
    }
}

/// <summary>
/// An AppImage: one file that runs on most Linux systems. Lucode lays out the folder and
/// `appimagetool` (github.com/AppImage/appimagetool) packs it.
/// </summary>
public static class AppImagePackage
{
    /// <summary>Writes the package. Returns an error message, or null when it worked.</summary>
    public static string? Create(PackageInfo info)
    {
        if (!OperatingSystem.IsLinux())
            return "An AppImage can only be made on Linux, because it needs a Linux lucode.";

        string? arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x86_64",
            Architecture.Arm64 => "aarch64",
            _ => null,
        };
        if (arch is null)
            return $"An AppImage for {RuntimeInformation.ProcessArchitecture} isn't supported (only x86_64 and aarch64).";

        string name = Regex.Replace(info.Name.ToLowerInvariant(), "[^a-z0-9._-]", "-").Trim('-', '.', '_');
        if (name.Length < 2)
            return $"'{info.Name}' can't be turned into a program name (needs two or more letters or digits).";

        string temp = Path.GetDirectoryName(info.Executable)!;
        string appDir = Path.Combine(temp, "AppDir");
        Directory.CreateDirectory(Path.Combine(appDir, "usr", "bin"));
        string installed = Path.Combine(appDir, "usr", "bin", name);
        File.Copy(info.Executable, installed);
        File.SetUnixFileMode(installed, (UnixFileMode)0b111_101_101);

        // AppRun is what the AppImage starts: it just runs the program with the same arguments.
        string appRun = Path.Combine(appDir, "AppRun");
        File.WriteAllText(appRun,
            "#!/bin/sh\n" +
            "HERE=\"$(dirname \"$(readlink -f \"$0\")\")\"\n" +
            "exec \"$HERE/usr/bin/" + name + "\" \"$@\"\n");
        File.SetUnixFileMode(appRun, (UnixFileMode)0b111_101_101);

        string comment = string.IsNullOrWhiteSpace(info.Description)
            ? name + ", a program written in Lucode"
            : info.Description.ReplaceLineEndings(" ").Trim();
        File.WriteAllText(Path.Combine(appDir, name + ".desktop"),
            "[Desktop Entry]\nType=Application\n" +
            "Name=" + name + "\nComment=" + comment + "\n" +
            "Exec=" + name + "\nIcon=" + name + "\nCategories=Utility;\nTerminal=true\n" +
            "X-AppImage-Version=" + info.Version + "\n");

        using (var icon = typeof(AppImagePackage).Assembly.GetManifestResourceStream("icon.png")
                          ?? throw new IOException("The built-in icon is missing."))
        using (var file = File.Create(Path.Combine(appDir, name + ".png")))
            icon.CopyTo(file);
        File.Copy(Path.Combine(appDir, name + ".png"), Path.Combine(appDir, ".DirIcon"));

        string output = Path.GetFullPath(info.Output ?? $"{name}-{arch}.AppImage");
        string? tool = FindTool();
        if (tool is null)
            return "Making an AppImage needs 'appimagetool'. Download it from " +
                   "https://github.com/AppImage/appimagetool/releases, make it executable (chmod +x) and put it on your PATH.";

        var start = new System.Diagnostics.ProcessStartInfo(tool)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(appDir);
        start.ArgumentList.Add(output);
        start.Environment["ARCH"] = arch;
        start.Environment["APPIMAGE_EXTRACT_AND_RUN"] = "1"; // appimagetool is itself an AppImage; this avoids needing FUSE

        using var process = System.Diagnostics.Process.Start(start)!;
        string text = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            return "appimagetool failed:\n" + text;

        Console.WriteLine($"Built {output} (run it with ./{Path.GetFileName(output)})");
        return null;
    }

    private static string? FindTool()
    {
        foreach (var name in new[] { "appimagetool", "appimagetool-x86_64.AppImage", "appimagetool-aarch64.AppImage" })
            foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(folder, name);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }
}

/// <summary>
/// A Windows installer (setup .exe) made with Inno Setup (jrsoftware.org/isinfo.php): Lucode writes
/// the script and `ISCC.exe` compiles it. The installer puts the program in the current user's programs
/// folder, adds a Start menu entry, can add it to PATH, and registers an uninstaller.
/// </summary>
public static class InstallerPackage
{
    /// <summary>Writes the installer. Returns an error message, or null when it worked.</summary>
    public static string? Create(PackageInfo info)
    {
        if (!OperatingSystem.IsWindows())
            return "A Windows installer can only be made on Windows, because it needs a Windows lucode.";

        string? arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64compatible",
            Architecture.Arm64 => "arm64",
            _ => null,
        };
        if (arch is null)
            return $"An installer for {RuntimeInformation.ProcessArchitecture} isn't supported (only x64 and arm64).";

        string name = Regex.Replace(info.Name.ToLowerInvariant(), "[^a-z0-9._-]", "-").Trim('-', '.', '_');
        if (name.Length < 2)
            return $"'{info.Name}' can't be turned into a program name (needs two or more letters or digits).";
        if (!Regex.IsMatch(info.Version, "^[0-9][A-Za-z0-9._+~-]*$"))
            return $"'{info.Version}' isn't a valid version. It must start with a digit, like 1.0 or 2.3.1.";

        string? compiler = FindCompiler();
        if (compiler is null)
            return "Making a Windows installer needs Inno Setup. Install it with: winget install JRSoftware.InnoSetup";

        string output = Path.GetFullPath(info.Output ?? $"{name}-{info.Version}-setup.exe");
        string temp = Path.GetDirectoryName(info.Executable)!;
        string scriptFile = Path.Combine(temp, name + ".iss");

        // '{' starts a constant in Inno scripts, so it is doubled in free text.
        static string Safe(string text) => text.ReplaceLineEndings(" ").Trim().Replace("{", "{{");
        string script = string.Join("\r\n",
            "[Setup]",
            "AppName=" + name,
            "AppVersion=" + info.Version,
            info.Maintainer is null ? "" : "AppPublisher=" + Safe(info.Maintainer),
            info.Description is null ? "" : "AppComments=" + Safe(info.Description),
            "DefaultDirName={autopf}\\" + name,
            "DefaultGroupName=" + name,
            "PrivilegesRequired=lowest",
            "ArchitecturesInstallIn64BitMode=" + arch,
            "ArchitecturesAllowed=" + arch,
            "ChangesEnvironment=yes",
            "Compression=lzma2",
            "OutputDir=" + Path.GetDirectoryName(output),
            "OutputBaseFilename=" + Path.GetFileNameWithoutExtension(output),
            "",
            "[Tasks]",
            "Name: addtopath; Description: \"Add " + name + " to the PATH, so it can be run from any terminal\"",
            "",
            "[Files]",
            "Source: \"" + info.Executable + "\"; DestDir: \"{app}\"; DestName: \"" + name + ".exe\"",
            "",
            "[Icons]",
            "Name: \"{group}\\" + name + "\"; Filename: \"{cmd}\"; Parameters: \"/k \"\"{app}\\" + name + ".exe\"\"\"; WorkingDir: \"{userdocs}\"",
            "",
            "[Registry]",
            "Root: HKCU; Subkey: \"Environment\"; ValueType: expandsz; ValueName: \"Path\"; ValueData: \"{olddata};{app}\"; " +
                "Check: NeedsAddPath(ExpandConstant('{app}')); Tasks: addtopath",
            "",
            "[Code]",
            "function NeedsAddPath(Param: string): Boolean;",
            "var OrigPath: string;",
            "begin",
            "  if not RegQueryStringValue(HKEY_CURRENT_USER, 'Environment', 'Path', OrigPath) then",
            "  begin",
            "    Result := True;",
            "    exit;",
            "  end;",
            "  Result := Pos(';' + Uppercase(Param) + ';', ';' + Uppercase(OrigPath) + ';') = 0;",
            "end;",
            "");
        File.WriteAllText(scriptFile, script);

        var start = new System.Diagnostics.ProcessStartInfo(compiler)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("/Q");
        start.ArgumentList.Add(scriptFile);
        using var process = System.Diagnostics.Process.Start(start)!;
        string text = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            return "Inno Setup failed:\n" + text;

        Console.WriteLine($"Built {output} (installs to your programs folder; run it with /VERYSILENT for no questions)");
        return null;
    }

    private static string? FindCompiler()
    {
        var folders = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(new[]
            {
                Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                Environment.GetEnvironmentVariable("ProgramFiles"),
                Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", "Programs"),
            }.Where(f => !string.IsNullOrEmpty(f)).Select(f => Path.Combine(f!, "Inno Setup 6")));
        return folders.Select(f => Path.Combine(f, "ISCC.exe")).FirstOrDefault(File.Exists);
    }
}
