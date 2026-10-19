namespace Lucode;

public enum TokenType
{
    // Literals and names
    Number,
    String,
    Identifier,

    // Keywords
    If,
    Else,
    Print,
    Rule,
    When,
    Why,
    Repeat,
    While,
    To,
    Give,
    For,
    List,
    And,
    Or,
    Not,
    Is,
    In,           // "in": only a keyword inside a comparison ("3 in scores")
    True,
    False,

    // Arithmetic
    Plus,
    Minus,
    Star,
    Slash,
    Percent,

    // Comparison
    Equal,        // =   (assignment at line start, comparison inside expressions)
    NotEqual,     // !=
    Less,         // <
    LessEqual,    // <=
    Greater,      // >
    GreaterEqual, // >=

    // Grouping
    LeftParen,
    RightParen,
    Comma,

    // Structure
    Newline,
    Indent,
    Dedent,
    Eof,
}

public sealed record Token(TokenType Type, string Text, object? Value, int Line)
{
    /// <summary>How the token should be named in an error message.</summary>
    public string Describe() => Type switch
    {
        TokenType.Newline => "end of line",
        TokenType.Indent => "indentation",
        TokenType.Dedent => "end of block",
        TokenType.Eof => "end of file",
        TokenType.String => $"text \"{Text}\"",
        _ => $"'{Text}'",
    };
}
