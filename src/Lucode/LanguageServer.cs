using System.Text;
using System.Text.RegularExpressions;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lucode;

/// <summary>
/// `lucode --lsp`: a tiny Language Server Protocol server over stdin/stdout.
/// It re-reads the whole file on every change and shows the first error the
/// lexer or parser finds (the same message `lucode file.luc` would print),
/// underlining that line. Errors that only appear while running are not shown.
/// </summary>
public static class LanguageServer
{
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Every error the lexer and parser find (the first on each line), in line order.</summary>
    public static List<LucodeError> CheckAll(string source, string? path = null)
    {
        string name = path is null ? "" : Path.GetFileName(path);
        SourceMap.Reset(name);
        var errors = new List<LucodeError>();
        try
        {
            // With a file path, the modules named by 'use' are read from next to it.
            var modules = path is null ? null : ModuleLoader.ForDirectory(Path.GetDirectoryName(path) ?? "", name, confine: true);
            var tokens = new Lexer(source, 0, errors).Tokenize();
            new Parser(tokens, null, modules, name).ParseAll(errors);
        }
        catch (LucodeError error)
        {
            errors.Add(error);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            errors.Add(new LucodeError(1, "Lucode could not check this file: " + e.Message));
        }
        return errors.GroupBy(e => e.Line).Select(g => g.First()).OrderBy(e => e.Line).ToList();
    }

    /// <summary>The first error the parser would report for this text, or null if it reads fine.</summary>
    public static LucodeError? Check(string source, string? path = null) => CheckAll(source, path).FirstOrDefault();

    public static int Run(string version, Stream? input = null, Stream? output = null)
    {
        input ??= Console.OpenStandardInput();
        output ??= Console.OpenStandardOutput();
        bool shutdown = false;
        var documents = new Dictionary<string, string>();

        while (ReadMessage(input) is { } message)
        {
            string? method = message["method"]?.GetValue<string>();
            var id = message["id"];
            var args = message["params"];

            switch (method)
            {
                case "initialize":
                    Reply(output, id, new JsonObject
                    {
                        ["capabilities"] = new JsonObject
                        {
                            ["textDocumentSync"] = 1, // 1 = send the whole text
                            ["hoverProvider"] = true,
                            ["completionProvider"] = new JsonObject(),
                        },
                        ["serverInfo"] = new JsonObject { ["name"] = "lucode", ["version"] = version },
                    });
                    break;

                case "shutdown":
                    shutdown = true;
                    Reply(output, id, null);
                    break;

                case "exit":
                    return shutdown ? 0 : 1;

                case "textDocument/didOpen":
                    Open(output, documents, args?["textDocument"]?["uri"]?.GetValue<string>(),
                        args?["textDocument"]?["text"]?.GetValue<string>());
                    break;

                case "textDocument/didChange":
                    var changes = args?["contentChanges"]?.AsArray();
                    if (changes is { Count: > 0 })
                        Open(output, documents, args?["textDocument"]?["uri"]?.GetValue<string>(),
                            changes[^1]?["text"]?.GetValue<string>());
                    break;

                case "textDocument/didClose":
                    string? closed = args?["textDocument"]?["uri"]?.GetValue<string>();
                    if (closed is not null) documents.Remove(closed);
                    Publish(output, closed, null);
                    break;

                case "textDocument/hover":
                    Reply(output, id, Hover(documents, args));
                    break;

                case "textDocument/completion":
                    string? completing = args?["textDocument"]?["uri"]?.GetValue<string>();
                    Reply(output, id, Completions(completing is not null && documents.TryGetValue(completing, out var open) ? open : ""));
                    break;

                default:
                    // A request we don't handle must still get an answer; notifications (no id) are ignored.
                    if (id is not null && method is not null)
                        Send(output, new JsonObject
                        {
                            ["jsonrpc"] = "2.0",
                            ["id"] = id.DeepClone(),
                            ["error"] = new JsonObject { ["code"] = -32601, ["message"] = $"Not supported: {method}" },
                        });
                    break;
            }
        }
        return 1; // input ended without 'exit'
    }

    private static void Open(Stream output, Dictionary<string, string> documents, string? uri, string? text)
    {
        if (uri is not null && text is not null) documents[uri] = text;
        Publish(output, uri, text);
    }

    private static void Publish(Stream output, string? uri, string? text)
    {
        if (uri is null) return;

        var diagnostics = new JsonArray();
        string? path = Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile ? parsed.LocalPath : null;
        if (text is not null)
        {
            var lines = text.Split('\n');
            foreach (var error in CheckAll(text, path))
            {
                // An error inside a module is shown on the first line, saying which file it is in.
                int line = error.File is null ? Math.Clamp(SourceMap.LineIn(error.Line) - 1, 0, lines.Length - 1) : 0;
                string content = lines[line].TrimEnd('\r');
                int start = content.Length - content.TrimStart().Length;
                diagnostics.Add(new JsonObject
                {
                    ["range"] = new JsonObject
                    {
                        ["start"] = new JsonObject { ["line"] = line, ["character"] = start },
                        ["end"] = new JsonObject { ["line"] = line, ["character"] = Math.Max(content.Length, start) },
                    },
                    ["severity"] = 1,
                    ["source"] = "lucode",
                    ["message"] = error.File is null ? error.Message : $"{error.File}, {error}",
                });
            }
        }

        Send(output, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "textDocument/publishDiagnostics",
            ["params"] = new JsonObject { ["uri"] = uri, ["diagnostics"] = diagnostics },
        });
    }

    // ----- Hover and completion: they work from the text of the document, not from running it -----

    private static JsonNode? Hover(Dictionary<string, string> documents, JsonNode? args)
    {
        string? uri = args?["textDocument"]?["uri"]?.GetValue<string>();
        int? line = args?["position"]?["line"]?.GetValue<int>();
        int? character = args?["position"]?["character"]?.GetValue<int>();
        if (uri is null || line is null || character is null || !documents.TryGetValue(uri, out var text))
            return null;

        string? word = WordAt(text, line.Value, character.Value);
        string? markdown = word is null ? null : Describe(text, word);
        return markdown is null
            ? null
            : new JsonObject { ["contents"] = new JsonObject { ["kind"] = "markdown", ["value"] = markdown } };
    }

    private static string? WordAt(string text, int line, int character)
    {
        var lines = text.Split('\n');
        if (line < 0 || line >= lines.Length) return null;
        string content = lines[line].TrimEnd('\r');
        static bool InWord(char c) => char.IsLetterOrDigit(c) || c == '_';

        int at = Math.Clamp(character, 0, content.Length);
        int start = at;
        while (start > 0 && InWord(content[start - 1])) start--;
        int end = at;
        while (end < content.Length && InWord(content[end])) end++;
        return end > start ? content[start..end] : null;
    }

    /// <summary>What to show for a word: how this file defines it, or what Lucode says it means.</summary>
    private static string? Describe(string text, string word)
    {
        string name = Regex.Escape(word);
        var command = Regex.Match(text,
            $@"^(?<comment>(?:[ \t]*#[^\n]*\n)*)[ \t]*to[ \t]+{name}(?<inputs>(?:[ \t]+\w+)*)[ \t]*\r?$", RegexOptions.Multiline);
        if (command.Success)
        {
            string notes = string.Join("\n", command.Groups["comment"].Value
                .Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim().TrimStart('#').Trim()));
            string signature = $"```lucode\nto {word}{command.Groups["inputs"].Value}\n```";
            return notes.Length == 0 ? signature : $"{signature}\n\n{notes}";
        }

        var rule = Regex.Match(text, $@"^[ \t]*rule[ \t]+{name}[ \t]*=[ \t]*(?<formula>[^\r\n]+)", RegexOptions.Multiline);
        if (rule.Success)
            return $"```lucode\nrule {word} = {rule.Groups["formula"].Value.Trim()}\n```\n\nA rule: worked out again every time it is read.";

        var variable = Regex.Match(text, $@"^[ \t]*{name}[ \t]*=(?!=)[ \t]*(?<value>[^\r\n]+)", RegexOptions.Multiline);
        if (variable.Success)
            return $"```lucode\n{word} = {variable.Groups["value"].Value.Trim()}\n```\n\nA variable (first set here).";

        return Docs.Words.TryGetValue(word, out var doc)
            ? $"**{word}**: {doc.Summary}\n\n```lucode\n{doc.Example}\n```"
            : null;
    }

    private static JsonArray Completions(string text)
    {
        var items = new JsonArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        static JsonObject Item(string label, int kind, string? detail)
        {
            var item = new JsonObject { ["label"] = label, ["kind"] = kind };
            if (detail is not null) item["detail"] = detail;
            return item;
        }

        // Kinds are from the protocol: 3 = function, 5 = field, 6 = variable, 14 = keyword.
        foreach (var (word, doc) in Docs.Words)
            if (seen.Add(word)) items.Add(Item(word, 14, doc.Summary));

        foreach (Match command in Regex.Matches(text, @"^[ \t]*to[ \t]+(?<name>\w+)(?<inputs>(?:[ \t]+\w+)*)", RegexOptions.Multiline))
        {
            if (seen.Add(command.Groups["name"].Value))
                items.Add(Item(command.Groups["name"].Value, 3, $"command {command.Groups["name"].Value}{command.Groups["inputs"].Value}"));
            foreach (Match input in Regex.Matches(command.Groups["inputs"].Value, @"\w+"))
                if (seen.Add(input.Value)) items.Add(Item(input.Value, 6, null));
        }
        foreach (Match name in Regex.Matches(text, @"^[ \t]*(?:rule[ \t]+)?(?<name>[A-Za-z_]\w*)[ \t]*=(?!=)", RegexOptions.Multiline))
            if (seen.Add(name.Groups["name"].Value)) items.Add(Item(name.Groups["name"].Value, 6, null));
        foreach (Match name in Regex.Matches(text, @"\bfor[ \t]+each[ \t]+(?<name>[A-Za-z_]\w*)"))
            if (seen.Add(name.Groups["name"].Value)) items.Add(Item(name.Groups["name"].Value, 6, null));
        foreach (Match field in Regex.Matches(text, @"\b(?<name>[A-Za-z_]\w*):"))
            if (seen.Add(field.Groups["name"].Value)) items.Add(Item(field.Groups["name"].Value, 5, null));
        return items;
    }

    private static void Reply(Stream output, JsonNode? id, JsonNode? result) =>
        Send(output, new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["result"] = result });

    private static void Send(Stream output, JsonObject message)
    {
        byte[] body = Encoding.UTF8.GetBytes(message.ToJsonString(Json));
        byte[] header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        output.Write(header);
        output.Write(body);
        output.Flush();
    }

    /// <summary>Reads one "Content-Length: N" framed message, or null when the input ends.</summary>
    private static JsonNode? ReadMessage(Stream input)
    {
        int length = -1;
        while (true)
        {
            string? line = ReadHeaderLine(input);
            if (line is null) return null;
            if (line.Length == 0) break; // blank line ends the headers
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                if (!int.TryParse(line["Content-Length:".Length..].Trim(), out length) || length < 0 || length > 64 * 1024 * 1024)
                    return null;
            }
        }
        if (length < 0) return null;

        var body = new byte[length];
        int read = 0;
        while (read < length)
        {
            int n = input.Read(body, read, length - read);
            if (n == 0) return null;
            read += n;
        }
        return JsonNode.Parse(body);
    }

    private static string? ReadHeaderLine(Stream input)
    {
        var line = new StringBuilder();
        while (true)
        {
            int b = input.ReadByte();
            if (b < 0) return line.Length == 0 ? null : line.ToString();
            if (b == '\n') return line.ToString().TrimEnd('\r');
            line.Append((char)b);
        }
    }
}
