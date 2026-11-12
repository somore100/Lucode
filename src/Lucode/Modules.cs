namespace Lucode;

/// <summary>
/// Line numbers carry their file: file number 0 is the program itself and file N adds N million, so a
/// token's line says both where it is and which file it is in. This turns such a number back into words.
/// </summary>
public static class SourceMap
{
    public const int FileSize = 1_000_000;
    private static readonly List<string> Files = new() { "" };

    /// <summary>Starts over with the name of the main file (file number 0).</summary>
    public static void Reset(string mainName)
    {
        Files.Clear();
        Files.Add(mainName);
    }

    /// <summary>The number to add to every line of this file.</summary>
    public static int Register(string name)
    {
        int at = Files.IndexOf(name);
        if (at < 0)
        {
            Files.Add(name);
            at = Files.Count - 1;
        }
        return at * FileSize;
    }

    /// <summary>The file a line is in, or null for the main file.</summary>
    public static string? FileOf(int line) => line / FileSize is > 0 and var index && index < Files.Count ? Files[index] : null;

    public static int LineIn(int line) => line % FileSize;

    /// <summary>"line 4", or "line 4 of helpers.luc" when it isn't in the main file.</summary>
    public static string Where(int line) =>
        FileOf(line) is { } file ? $"line {LineIn(line)} of {file}" : $"line {LineIn(line)}";
}

/// <summary>
/// Finds the files named by `use "file.luc"`. A file is identified by its path from the folder of the main
/// file ("lib/helpers.luc"), and each is read once. Reading goes through a function, so a built program
/// can answer from the files packed inside it.
/// </summary>
public sealed class ModuleLoader
{
    private readonly Func<string, string?> _read;
    private readonly string _mainKey;
    private readonly HashSet<string> _loaded;

    /// <summary>Every module read so far, for `lucode build` to pack.</summary>
    public List<(string Key, string Source)> Files { get; } = new();

    public ModuleLoader(string mainKey, Func<string, string?> read)
    {
        _mainKey = mainKey;
        _read = read;
        _loaded = new HashSet<string> { mainKey };
    }

    /// <summary>A reader that looks for files in a folder.</summary>
    public static Func<string, string?> FileReader(string directory, bool confine = false) => key =>
    {
        try
        {
            string path = Path.GetFullPath(Path.Combine(directory, key));
            if (confine)
            {
                // Editors open files they did not write: only read inside the document's own folder.
                string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!path.StartsWith(root, StringComparison.Ordinal)) return null;
            }
            return ReadCapped(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    };

    private const int MaxModuleBytes = 4 * 1024 * 1024;

    /// <summary>The text of a regular file of reasonable size, or null (so /dev/zero and huge files are not read to the end).</summary>
    private static string? ReadCapped(string path)
    {
        if (!File.Exists(path)) return null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[MaxModuleBytes + 1];
        int total = 0, n;
        while (total < buffer.Length && (n = stream.Read(buffer, total, buffer.Length - total)) > 0)
            total += n;
        return total > MaxModuleBytes ? null : System.Text.Encoding.UTF8.GetString(buffer, 0, total);
    }

    public static ModuleLoader ForDirectory(string directory, string mainKey, bool confine = false) =>
        new(mainKey, FileReader(directory, confine));

    /// <summary>Parses the main file; any `use` in it (and in the files it uses) is read on the way.</summary>
    public List<Stmt> Parse(string source, Dictionary<string, int>? commands = null) =>
        new Parser(new Lexer(source).Tokenize(), commands, this, _mainKey).Parse();

    /// <summary>The path from the main file's folder to the file named by a `use` in the file <paramref name="currentKey"/>.</summary>
    public static string Resolve(string currentKey, string path)
    {
        path = path.Replace('\\', '/');
        if (Path.IsPathRooted(path)) return path;

        int slash = currentKey.LastIndexOf('/');
        string folder = slash < 0 ? "" : currentKey[..slash];
        var parts = new List<string>();
        foreach (var part in (folder.Length == 0 ? path : folder + "/" + path).Split('/'))
        {
            if (part is "" or ".") continue;
            if (part == ".." && parts.Count > 0 && parts[^1] != "..") parts.RemoveAt(parts.Count - 1);
            else parts.Add(part);
        }
        return string.Join("/", parts);
    }

    /// <summary>Reads and parses a module (once; a second `use` of the same file adds nothing).</summary>
    public IncludeStmt Include(string path, string currentKey, Dictionary<string, int> commands, int line)
    {
        string key = Resolve(currentKey, path);
        if (!_loaded.Add(key))
            return new IncludeStmt(path, key, new List<Stmt>(), line);

        string source = _read(key) ?? throw new LucodeError(line, $"Can't find the module \"{path}\".");
        Files.Add((key, source));
        int lineBase = SourceMap.Register(key);
        var body = new Parser(new Lexer(source, lineBase).Tokenize(), commands, this, key).Parse();
        return new IncludeStmt(path, key, body, line);
    }
}
