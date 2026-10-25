using Lucode;

const string Version = "0.7.3";

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
    return 0;
}

if (args[0] is "-v" or "--version")
{
    Console.WriteLine($"Lucode {Version}");
    return 0;
}

string path = args[0];
if (!File.Exists(path))
{
    Console.Error.WriteLine($"Can't find the file '{path}'.");
    return 1;
}

try
{
    string source = File.ReadAllText(path);
    var tokens = new Lexer(source).Tokenize();
    var program = new Parser(tokens).Parse();
    new Interpreter().Run(program);
    return 0;
}
catch (LucodeError error)
{
    Console.Out.Flush();
    Console.Error.WriteLine($"{Path.GetFileName(path)}, {error}");
    return 1;
}
