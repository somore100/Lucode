using System.Text;

namespace Lucode;

/// <summary>
/// An interactive prompt. Variables, rules, 'when' blocks and commands stay around between
/// lines. A line that opens a block ('if', 'when', 'repeat', 'while', 'for', 'to') keeps reading
/// lines until an empty one.
/// </summary>
public static class Repl
{
    private static readonly string[] BlockStarters = { "if", "when", "repeat", "while", "to", "for" };

    public static void Run(string version)
    {
        // Prompts would only add noise when input comes from a pipe or file.
        bool interactive = !Console.IsInputRedirected;
        if (interactive)
            Console.WriteLine($"Lucode {version}. Type 'exit' to leave.");

        var interpreter = new Interpreter();
        var commands = new Dictionary<string, int>();

        while (true)
        {
            string? first = Prompt(">>> ", interactive);
            if (first is null) break;
            if (first.Trim().Length == 0) continue;
            if (first.Trim() is "exit" or "quit") break;

            var chunk = new StringBuilder(first);
            if (StartsBlock(first))
            {
                while (true)
                {
                    string? more = Prompt("... ", interactive);
                    if (string.IsNullOrWhiteSpace(more)) break;
                    chunk.Append('\n').Append(more);
                }
            }

            try
            {
                var tokens = new Lexer(chunk.ToString()).Tokenize();
                var program = new Parser(tokens, commands).Parse();
                interpreter.Run(program, finish: false);
            }
            catch (LucodeError error)
            {
                Console.Out.Flush();
                Console.Error.WriteLine(error);
            }
        }
    }

    private static string? Prompt(string text, bool interactive)
    {
        if (interactive) Console.Write(text);
        return Console.ReadLine();
    }

    private static bool StartsBlock(string line)
    {
        string word = line.TrimStart().Split(' ', '\t')[0].ToLowerInvariant();
        return BlockStarters.Contains(word);
    }
}
