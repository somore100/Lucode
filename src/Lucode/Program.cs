using Lucode;

const string Version = "0.14.0";

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
    Console.WriteLine("       lucode build file.luc [-o name] [--deb|--rpm|--appimage|--installer]   make a standalone program or a package");
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
    const string usage = "Usage: lucode build file.luc [-o name] [--deb | --rpm | --appimage | --installer] [--version 1.0] [--maintainer \"Name <mail>\"] [--description \"text\"]";
    string? input = null, output = null, format = null, version = null, maintainer = null, description = null;
    for (int i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "-o" or "--output" when i + 1 < args.Length: output = args[++i]; break;
            case "--version" when i + 1 < args.Length: version = args[++i]; break;
            case "--maintainer" when i + 1 < args.Length: maintainer = args[++i]; break;
            case "--description" when i + 1 < args.Length: description = args[++i]; break;
            case "--deb" or "--rpm" or "--appimage" or "--installer":
                if (format is not null)
                {
                    Console.Error.WriteLine("Pick one package kind per build: --deb, --rpm, --appimage or --installer.");
                    return 1;
                }
                format = args[i][2..];
                break;
            case var other when other.StartsWith('-'):
                Console.Error.WriteLine($"Unknown option '{other}'.\n{usage}");
                return 1;
            default:
                if (input is not null)
                {
                    Console.Error.WriteLine(usage);
                    return 1;
                }
                input = args[i];
                break;
        }
    }
    if (input is null)
    {
        Console.Error.WriteLine(usage);
        return 1;
    }
    if (format is null && (version ?? maintainer ?? description) is not null)
    {
        Console.Error.WriteLine("--version, --maintainer and --description only apply to packages, like --deb, --rpm, --appimage or --installer.");
        return 1;
    }
    return Bundle.Build(new BuildOptions(input, output, format, version, maintainer, description));
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
