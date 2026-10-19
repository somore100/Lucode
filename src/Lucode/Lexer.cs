using System.Globalization;
using System.Text;

namespace Lucode;

/// <summary>
/// Splits source text into tokens. Works line by line so it can track indentation
/// and emit Indent / Dedent tokens, which is how Lucode marks blocks.
/// </summary>
public sealed class Lexer
{
    public static readonly IReadOnlyDictionary<string, TokenType> Keywords = new Dictionary<string, TokenType>
    {
        ["if"] = TokenType.If,
        ["else"] = TokenType.Else,
        ["print"] = TokenType.Print,
        ["rule"] = TokenType.Rule,
        ["when"] = TokenType.When,
        ["why"] = TokenType.Why,
        ["repeat"] = TokenType.Repeat,
        ["while"] = TokenType.While,
        ["to"] = TokenType.To,
        ["give"] = TokenType.Give,
        ["for"] = TokenType.For,
        ["list"] = TokenType.List,
        ["and"] = TokenType.And,
        ["or"] = TokenType.Or,
        ["not"] = TokenType.Not,
        ["is"] = TokenType.Is,
        ["true"] = TokenType.True,
        ["false"] = TokenType.False,
    };

    private const int TabWidth = 4;

    private readonly string _source;
    private readonly List<Token> _tokens = new();
    private readonly Stack<int> _indents = new();

    public Lexer(string source)
    {
        _source = source.Replace("\r\n", "\n").Replace('\r', '\n');
        _indents.Push(0);
    }

    public List<Token> Tokenize()
    {
        var lines = _source.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            int lineNumber = i + 1;
            string line = lines[i];

            int indent = MeasureIndent(line, out int contentStart);
            string content = line[contentStart..];

            // Blank lines and comment-only lines don't affect blocks.
            if (content.Trim().Length == 0 || content.TrimStart().StartsWith('#'))
                continue;

            HandleIndentation(indent, lineNumber);
            ScanLine(content, lineNumber);
            Add(TokenType.Newline, "", lineNumber);
        }

        int lastLine = lines.Length;
        while (_indents.Peek() > 0)
        {
            _indents.Pop();
            Add(TokenType.Dedent, "", lastLine);
        }

        Add(TokenType.Eof, "", lastLine);
        return _tokens;
    }

    private static int MeasureIndent(string line, out int contentStart)
    {
        int width = 0;
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
        {
            width += line[i] == '\t' ? TabWidth : 1;
            i++;
        }
        contentStart = i;
        return width;
    }

    private void HandleIndentation(int indent, int line)
    {
        if (indent > _indents.Peek())
        {
            _indents.Push(indent);
            Add(TokenType.Indent, "", line);
            return;
        }

        while (indent < _indents.Peek())
        {
            _indents.Pop();
            Add(TokenType.Dedent, "", line);
        }

        if (indent != _indents.Peek())
            throw new LucodeError(line, "This line's indentation doesn't match any earlier block.");
    }

    private void ScanLine(string text, int line)
    {
        int pos = 0;

        while (pos < text.Length)
        {
            char c = text[pos];

            if (c == ' ' || c == '\t') { pos++; continue; }
            if (c == '#') break; // comment until end of line

            if (char.IsDigit(c))
            {
                pos = ScanNumber(text, pos, line);
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int start = pos;
                while (pos < text.Length && (char.IsLetterOrDigit(text[pos]) || text[pos] == '_')) pos++;
                string word = text[start..pos];

                // Keywords are matched in lowercase so "If" and "PRINT" still work.
                if (Keywords.TryGetValue(word.ToLowerInvariant(), out var keyword))
                    Add(keyword, word, line);
                else
                    Add(TokenType.Identifier, word, line);
                continue;
            }

            if (c == '"')
            {
                pos = ScanString(text, pos, line);
                continue;
            }

            char next = pos + 1 < text.Length ? text[pos + 1] : '\0';
            switch (c)
            {
                case '+': Add(TokenType.Plus, "+", line); break;
                case '-': Add(TokenType.Minus, "-", line); break;
                case '*': Add(TokenType.Star, "*", line); break;
                case '/': Add(TokenType.Slash, "/", line); break;
                case '%': Add(TokenType.Percent, "%", line); break;
                case '(': Add(TokenType.LeftParen, "(", line); break;
                case ')': Add(TokenType.RightParen, ")", line); break;
                case ',': Add(TokenType.Comma, ",", line); break;
                case '=':
                    if (next == '=')
                        throw new LucodeError(line, "Lucode uses a single '=' for comparison. Write 'a = b' instead of 'a == b'.");
                    if (next == '!')
                        throw new LucodeError(line, "Use '!=' for 'not equal' (not '=!').");
                    Add(TokenType.Equal, "=", line);
                    break;
                case '!':
                    if (next != '=')
                        throw new LucodeError(line, "Unexpected '!'. For 'not', write 'not' or use '!='.");
                    Add(TokenType.NotEqual, "!=", line);
                    pos++;
                    break;
                case '<':
                    if (next == '=') { Add(TokenType.LessEqual, "<=", line); pos++; }
                    else Add(TokenType.Less, "<", line);
                    break;
                case '>':
                    if (next == '=') { Add(TokenType.GreaterEqual, ">=", line); pos++; }
                    else Add(TokenType.Greater, ">", line);
                    break;
                default:
                    throw new LucodeError(line, $"Unexpected character '{c}'.");
            }
            pos++;
        }
    }

    private int ScanNumber(string text, int pos, int line)
    {
        int start = pos;
        while (pos < text.Length && char.IsDigit(text[pos])) pos++;

        if (pos + 1 < text.Length && text[pos] == '.' && char.IsDigit(text[pos + 1]))
        {
            pos++;
            while (pos < text.Length && char.IsDigit(text[pos])) pos++;
        }

        if (pos < text.Length && (char.IsLetter(text[pos]) || text[pos] == '_'))
            throw new LucodeError(line, $"Names can't start with a number: '{text[start..(pos + 1)]}...'.");

        string literal = text[start..pos];
        Add(TokenType.Number, literal, line, double.Parse(literal, CultureInfo.InvariantCulture));
        return pos;
    }

    private int ScanString(string text, int pos, int line)
    {
        var sb = new StringBuilder();
        pos++; // opening quote

        while (pos < text.Length && text[pos] != '"')
        {
            if (text[pos] == '\\' && pos + 1 < text.Length)
            {
                pos++;
                sb.Append(text[pos] switch
                {
                    'n' => '\n',
                    't' => '\t',
                    '"' => '"',
                    '\\' => '\\',
                    var other => throw new LucodeError(line, $"Unknown escape '\\{other}' in text."),
                });
            }
            else
            {
                sb.Append(text[pos]);
            }
            pos++;
        }

        if (pos >= text.Length)
            throw new LucodeError(line, "Text is missing its closing quote (\").");

        string value = sb.ToString();
        Add(TokenType.String, value, line, value);
        return pos + 1; // closing quote
    }

    private void Add(TokenType type, string text, int line, object? value = null) =>
        _tokens.Add(new Token(type, text, value, line));
}
