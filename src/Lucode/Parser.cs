namespace Lucode;

/// <summary>
/// Turns tokens into a tree of statements and expressions.
///
/// Grammar (lowest to highest precedence):
///   program     := statement*
///   statement   := if | print | rule | assignment
///   if          := "if" expr NEWLINE block ( "else" ( if | NEWLINE block ) )?
///   print       := "print" expr NEWLINE
///   rule        := "rule" NAME "=" expr NEWLINE
///   assignment  := NAME "=" expr NEWLINE
///   block       := INDENT statement+ DEDENT
///
///   expr        := or
///   or          := and ( "or" and )*
///   and         := not ( "and" not )*
///   not         := "not" not | comparison
///   comparison  := sum ( ( "=" | "is" | "is not" | "!=" | "<" | "<=" | ">" | ">=" ) sum )?
///   sum         := product ( ( "+" | "-" ) product )*
///   product     := unary ( ( "*" | "/" | "%" ) unary )*
///   unary       := "-" unary | primary
///   primary     := NUMBER | TEXT | "true" | "false" | NAME | "(" expr ")"
/// </summary>
public sealed class Parser
{
    private static readonly string[] Commands = { "print", "if", "else", "rule" };

    private readonly List<Token> _tokens;
    private int _pos;

    public Parser(List<Token> tokens)
    {
        _tokens = tokens;
    }

    public List<Stmt> Parse()
    {
        var statements = new List<Stmt>();
        while (!Check(TokenType.Eof))
            statements.Add(Statement());
        return statements;
    }

    // ----- Statements -----

    private Stmt Statement()
    {
        var token = Peek();

        switch (token.Type)
        {
            case TokenType.If:
                return IfStatement();
            case TokenType.Print:
                return PrintStatement();
            case TokenType.Rule:
                return RuleStatement();
            case TokenType.Identifier:
                return Assignment();
            case TokenType.Else:
                throw Error(token, "'else' must come right after an 'if' block.");
            case TokenType.Indent:
                throw Error(token, "This line is indented, but there's no 'if' above it to belong to.");
            default:
                throw Error(token, $"A line can't start with {token.Describe()}.");
        }
    }

    private Stmt IfStatement()
    {
        var ifToken = Advance();
        var condition = Expression();
        EndOfLine("the 'if' condition");
        var thenBlock = Block("if");

        List<Stmt>? elseBlock = null;
        if (Match(TokenType.Else))
        {
            if (Check(TokenType.If))
            {
                // "else if" — treat it as an else block containing one if.
                elseBlock = new List<Stmt> { IfStatement() };
            }
            else
            {
                EndOfLine("'else'");
                elseBlock = Block("else");
            }
        }

        return new IfStmt(condition, thenBlock, elseBlock, ifToken.Line);
    }

    private Stmt PrintStatement()
    {
        var printToken = Advance();
        if (Check(TokenType.Newline))
            throw Error(printToken, "'print' needs something to print, like: print \"hello\"");

        var value = Expression();
        EndOfLine("the 'print' value");
        return new PrintStmt(value, printToken.Line);
    }

    private Stmt RuleStatement()
    {
        var ruleToken = Advance();

        if (!Check(TokenType.Identifier))
            throw Error(Peek(), $"'rule' needs a name, like: rule can_enter = has_key and is_owner");
        var name = Advance();

        if (!Match(TokenType.Equal))
            throw Error(Peek(), $"Expected '=' after 'rule {name.Text}', like: rule {name.Text} = ...");
        if (Check(TokenType.Newline))
            throw Error(name, $"Rule '{name.Text}' needs a formula after the '='.");

        var value = Expression();
        EndOfLine($"rule '{name.Text}'");
        return new RuleStmt(name.Text, value, ruleToken.Line);
    }

    private Stmt Assignment()
    {
        var name = Advance();

        if (!Match(TokenType.Equal))
        {
            string hint = Suggest.Hint(name.Text, Commands);
            throw Error(name, $"Unknown command '{name.Text}'.{hint}");
        }

        if (Check(TokenType.Newline))
            throw Error(name, $"'{name.Text} =' needs a value after the '='.");

        var value = Expression();
        EndOfLine($"the value of '{name.Text}'");
        return new AssignStmt(name.Text, value, name.Line);
    }

    private List<Stmt> Block(string owner)
    {
        if (!Match(TokenType.Indent))
            throw Error(Peek(), $"Expected an indented block after '{owner}'.");

        var statements = new List<Stmt>();
        while (!Check(TokenType.Dedent) && !Check(TokenType.Eof))
            statements.Add(Statement());

        Match(TokenType.Dedent);
        return statements;
    }

    private void EndOfLine(string after)
    {
        if (Match(TokenType.Newline)) return;
        throw Error(Peek(), $"Unexpected {Peek().Describe()} after {after}.");
    }

    // ----- Expressions -----

    private Expr Expression() => Or();

    private Expr Or()
    {
        var expr = And();
        while (Check(TokenType.Or))
        {
            var op = Advance();
            expr = new LogicalExpr(expr, op, And(), op.Line);
        }
        return expr;
    }

    private Expr And()
    {
        var expr = Not();
        while (Check(TokenType.And))
        {
            var op = Advance();
            expr = new LogicalExpr(expr, op, Not(), op.Line);
        }
        return expr;
    }

    private Expr Not()
    {
        if (Check(TokenType.Not))
        {
            var op = Advance();
            return new UnaryExpr(op, Not(), op.Line);
        }
        return Comparison();
    }

    private Expr Comparison()
    {
        var left = Sum();

        if (Check(TokenType.Is))
        {
            var isToken = Advance();
            // "is not" is another way to write "!=".
            var op = Match(TokenType.Not)
                ? isToken with { Type = TokenType.NotEqual, Text = "is not" }
                : isToken with { Type = TokenType.Equal };
            left = new BinaryExpr(left, op, Sum(), op.Line);
        }
        else if (CheckAny(TokenType.Equal, TokenType.NotEqual, TokenType.Less,
                          TokenType.LessEqual, TokenType.Greater, TokenType.GreaterEqual))
        {
            var op = Advance();
            left = new BinaryExpr(left, op, Sum(), op.Line);
        }

        if (CheckAny(TokenType.Equal, TokenType.NotEqual, TokenType.Less, TokenType.LessEqual,
                     TokenType.Greater, TokenType.GreaterEqual, TokenType.Is))
        {
            throw Error(Peek(), "Comparisons can't be chained. Use 'and', like: a < b and b < c");
        }

        return left;
    }

    private Expr Sum()
    {
        var expr = Product();
        while (CheckAny(TokenType.Plus, TokenType.Minus))
        {
            var op = Advance();
            expr = new BinaryExpr(expr, op, Product(), op.Line);
        }
        return expr;
    }

    private Expr Product()
    {
        var expr = Unary();
        while (CheckAny(TokenType.Star, TokenType.Slash, TokenType.Percent))
        {
            var op = Advance();
            expr = new BinaryExpr(expr, op, Unary(), op.Line);
        }
        return expr;
    }

    private Expr Unary()
    {
        if (Check(TokenType.Minus))
        {
            var op = Advance();
            return new UnaryExpr(op, Unary(), op.Line);
        }
        return Primary();
    }

    private Expr Primary()
    {
        var token = Peek();

        switch (token.Type)
        {
            case TokenType.Number:
            case TokenType.String:
                Advance();
                return new LiteralExpr(token.Value!, token.Line);
            case TokenType.True:
                Advance();
                return new LiteralExpr(true, token.Line);
            case TokenType.False:
                Advance();
                return new LiteralExpr(false, token.Line);
            case TokenType.Identifier:
                Advance();
                return new VariableExpr(token.Text, token.Line);
            case TokenType.LeftParen:
                Advance();
                var inner = Expression();
                if (!Match(TokenType.RightParen))
                    throw Error(Peek(), "Missing ')' to close the '('.");
                return inner;
            case TokenType.Newline:
                throw Error(token, "The line ends too early; a value is missing.");
            default:
                throw Error(token, $"Expected a value but found {token.Describe()}.");
        }
    }

    // ----- Helpers -----

    private Token Peek() => _tokens[_pos];

    private Token Advance() => _tokens[_pos++];

    private bool Check(TokenType type) => Peek().Type == type;

    private bool CheckAny(params TokenType[] types) => types.Contains(Peek().Type);

    private bool Match(TokenType type)
    {
        if (!Check(type)) return false;
        _pos++;
        return true;
    }

    private static LucodeError Error(Token token, string message) => new(token.Line, message);
}
