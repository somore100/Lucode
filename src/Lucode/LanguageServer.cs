using System.Text;
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

    /// <summary>The error the parser would report for this text, or null if it reads fine.</summary>
    public static LucodeError? Check(string source)
    {
        try
        {
            new Parser(new Lexer(source).Tokenize()).Parse();
            return null;
        }
        catch (LucodeError error)
        {
            return error;
        }
    }

    public static int Run(string version, Stream? input = null, Stream? output = null)
    {
        input ??= Console.OpenStandardInput();
        output ??= Console.OpenStandardOutput();
        bool shutdown = false;

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
                        ["capabilities"] = new JsonObject { ["textDocumentSync"] = 1 }, // 1 = send the whole text
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
                    Publish(output, args?["textDocument"]?["uri"]?.GetValue<string>(),
                        args?["textDocument"]?["text"]?.GetValue<string>());
                    break;

                case "textDocument/didChange":
                    var changes = args?["contentChanges"]?.AsArray();
                    if (changes is { Count: > 0 })
                        Publish(output, args?["textDocument"]?["uri"]?.GetValue<string>(),
                            changes[^1]?["text"]?.GetValue<string>());
                    break;

                case "textDocument/didClose":
                    Publish(output, args?["textDocument"]?["uri"]?.GetValue<string>(), null);
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

    private static void Publish(Stream output, string? uri, string? text)
    {
        if (uri is null) return;

        var diagnostics = new JsonArray();
        if (text is not null && Check(text) is { } error)
        {
            var lines = text.Split('\n');
            int line = Math.Clamp(error.Line - 1, 0, lines.Length - 1);
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
                ["message"] = error.Message,
            });
        }

        Send(output, new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "textDocument/publishDiagnostics",
            ["params"] = new JsonObject { ["uri"] = uri, ["diagnostics"] = diagnostics },
        });
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
                length = int.Parse(line["Content-Length:".Length..].Trim());
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
