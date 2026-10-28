using Lucode;

const string Version = "0.10.0";

// A program built with 'lucode build' carries its own text: run that and ignore the command line.
if (Bundle.ReadEmbedded() is { } embedded)
    return RunSource(embedded.Name, embedded.Source);

if (args.Length == 0)
{
    Repl.Run(Version);
    return 0;
}

if (args[0] is "-h" or "--help")
{
    Console.WriteLine($"Lucode {Version}");
    Console.WriteLine();
    Console.WriteLine("Usage: lucode [file.luc]");
    Console.WriteLine("       lucode            start an interactive prompt");
    Console.WriteLine("       lucode build file.luc [-o name]   make a standalone program");
    Console.WriteLine("       lucode --lsp      language server for editors (talks over stdin/stdout)");
    return 0;
}

if (args[0] is "-v" or "--version")
{
    Console.WriteLine($"Lucode {Version}");
    return 0;
}

if (args[0] == "build")
{
    string? input = null, output = null;
    for (int i = 1; i < args.Length; i++)
    {
        if (args[i] is "-o" or "--output" && i + 1 < args.Length) output = args[++i];
        else if (input is null) input = args[i];
        else
        {
            Console.Error.WriteLine("Usage: lucode build file.luc [-o name]");
            return 1;
        }
    }
    if (input is null)
    {
        Console.Error.WriteLine("Usage: lucode build file.luc [-o name]");
        return 1;
    }
    return Bundle.Build(input, output);
}

if (args[0] == "--lsp")
    return LanguageServer.Run(Version);

string path = args[0];
if (!File.Exists(path))
{
    Console.Error.WriteLine($"Can't find the file '{path}'.");
    return 1;
}

return RunSource(Path.GetFileName(path), File.ReadAllText(path));

static int RunSource(string name, string source)
{
    try
    {
        var tokens = new Lexer(source).Tokenize();
        var program = new Parser(tokens).Parse();
        new Interpreter().Run(program);
        return 0;
    }
    catch (LucodeError error)
    {
        Console.Out.Flush();
        Console.Error.WriteLine($"{name}, {error}");
        return 1;
    }
}
