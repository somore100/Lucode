namespace Lucode;

/// <summary>
/// Turns tokens into a tree of statements and expressions.
///
/// Grammar (lowest to highest precedence):
///   program     := statement*
///   statement   := if | when | why | repeat | while | to | give | call | print | rule | assignment
///   when        := "when" expr NEWLINE block           (top level only)
///   to          := "to" NAME NAME* NEWLINE block      (top level only)
///   give        := "give" expr NEWLINE                 (inside a 'to' block)
///   call        := NAME primary*                       (one value per input of the command)
///   repeat      := "repeat" expr "times" NEWLINE block
///   while       := "while" expr NEWLINE block
///   for         := "for" "each" NAME "in" expr NEWLINE block
///   change      := "add" expr "to" NAME | "remove" "item" sum "from" NAME | "sort" NAME [ "descending" ]
///   write       := "write" expr "to" expr
///   why         := "why" NAME NEWLINE
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
///   comparison  := sum ( ( "in" | "=" | "is" | "is not" | "!=" | "<" | "<=" | ">" | ">=" ) sum )?
///   sum         := product ( ( "+" | "-" ) product )*
///   product     := unary ( ( "*" | "/" | "%" ) unary )*
///   unary       := "-" unary | primary
///   primary     := NUMBER | TEXT | "true" | "false" | NAME | "(" expr ")"
///               | "list" ( expr ( "," expr )* )? | "item" sum "of" primary | "count" "of" primary
///               | ( "upper" | "lower" | "number" | "ask" | "read" ) unary
///               | "split" unary "by" unary | "join" unary "with" unary
///               | "find" unary "in" unary | "replace" unary "with" unary "in" unary | "trim" unary
/// </summary>
public sealed class Parser
{
    private static readonly string[] Commands = { "print", "if", "else", "rule", "when", "why", "repeat", "while", "to", "give", "for", "add", "remove", "sort", "write" };

    // Words that work on values. Each is only special when a value follows, so they still work as names.
    // The second part is the little word between the two values, if there are two.
    private static readonly Dictionary<string, (string[] Between, string Example)> Builtins =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["upper"] = (Array.Empty<string>(), "upper name"),
            ["lower"] = (Array.Empty<string>(), "lower name"),
            ["trim"] = (Array.Empty<string>(), "trim name"),
            ["number"] = (Array.Empty<string>(), "number text"),
            ["ask"] = (Array.Empty<string>(), "ask \"Name?\""),
            ["read"] = (Array.Empty<string>(), "read \"notes.txt\""),
            ["split"] = (new[] { "by" }, "split text by \",\""),
            ["join"] = (new[] { "with" }, "join items with \", \""),
            ["find"] = (new[] { "in" }, "find \"a\" in text"),
            ["replace"] = (new[] { "with", "in" }, "replace \"a\" with \"b\" in text"),
        };

    private readonly List<Token> _tokens;
    private int _pos;
    private int _blockDepth; // 0 = top level of the file
    private bool _inCommand;  // true while reading the body of a 'to' block

    // Commands defined so far (name -> number of inputs), so a call knows how many values to read.
    private readonly Dictionary<string, int> _commands;

    /// <param name="commands">Commands known from earlier input; the REPL passes the same one each time.</param>
    public Parser(List<Token> tokens, Dictionary<string, int>? commands = null)
    {
        _tokens = tokens;
        _commands = commands ?? new();
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
            case TokenType.When:
                return WhenStatement();
            case TokenType.To:
                return CommandStatement();
            case TokenType.Give:
                return GiveStatement();
            case TokenType.Repeat:
                return RepeatStatement();
            case TokenType.While:
                return WhileStatement();
            case TokenType.Why:
                return WhyStatement();
            case TokenType.For:
                return ForEachStatement();
            case TokenType.Identifier:
                return Assignment();
            case TokenType.Else:
                throw Error(token, "'else' must come right after an 'if' block.");
            case TokenType.Indent:
                throw Error(token, "This line is indented, but there's no 'if' or 'when' above it to belong to.");
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

    private Stmt WhenStatement()
    {
        var whenToken = Advance();
        if (_blockDepth > 0)
            throw Error(whenToken, "'when' must be at the top level of the file, not inside another block.");
        if (Check(TokenType.Newline))
            throw Error(whenToken, "'when' needs a condition, like: when can_enter");

        var condition = Expression();
        EndOfLine("the 'when' condition");
        var body = Block("when");

        if (Check(TokenType.Else))
            throw Error(Peek(), "'when' can't have an 'else'. Write a second 'when' with the opposite condition.");

        return new WhenStmt(condition, body, whenToken.Line);
    }

    private Stmt CommandStatement()
    {
        var toToken = Advance();
        if (_blockDepth > 0)
            throw Error(toToken, "'to' must be at the top level of the file, not inside another block.");
        if (!Check(TokenType.Identifier))
            throw Error(Peek(), "'to' needs a name, like: to add a b");
        var name = Advance();

        var inputs = new List<string>();
        while (Check(TokenType.Identifier))
        {
            var input = Advance();
            if (inputs.Contains(input.Text))
                throw Error(input, $"'{name.Text}' already has an input called '{input.Text}'.");
            inputs.Add(input.Text);
        }
        EndOfLine($"the inputs of '{name.Text}'");

        if (_commands.ContainsKey(name.Text))
            throw Error(name, $"Command '{name.Text}' is already defined.");
        // Registered before the body is read, so a command can call itself.
        _commands[name.Text] = inputs.Count;

        _inCommand = true;
        var body = Block("to");
        _inCommand = false;
        return new CommandStmt(name.Text, inputs, body, toToken.Line);
    }

    private Stmt GiveStatement()
    {
        var giveToken = Advance();
        if (!_inCommand)
            throw Error(giveToken, "'give' can only be used inside a command (a block that starts with 'to').");
        if (Check(TokenType.Newline))
            throw Error(giveToken, "'give' needs a value, like: give a + b");

        var value = Expression();
        EndOfLine("the 'give' value");
        return new GiveStmt(value, giveToken.Line);
    }

    private Stmt RepeatStatement()
    {
        var repeatToken = Advance();
        if (Check(TokenType.Newline))
            throw Error(repeatToken, "'repeat' needs a count, like: repeat 3 times");

        var count = Expression();
        // "times" is only special here, so it stays usable as a variable name elsewhere.
        if (!(Check(TokenType.Identifier) && Peek().Text.Equals("times", StringComparison.OrdinalIgnoreCase)))
            throw Error(Peek(), "Expected 'times' after the repeat count, like: repeat 3 times");
        Advance();
        EndOfLine("'times'");
        var body = Block("repeat");
        return new RepeatStmt(count, body, repeatToken.Line);
    }

    private Stmt WhileStatement()
    {
        var whileToken = Advance();
        if (Check(TokenType.Newline))
            throw Error(whileToken, "'while' needs a condition, like: while count < 10");

        var condition = Expression();
        EndOfLine("the 'while' condition");
        var body = Block("while");
        return new WhileStmt(condition, body, whileToken.Line);
    }

    private Stmt ForEachStatement()
    {
        var forToken = Advance();
        if (!IsWord("each"))
            throw Error(Peek(), "Expected 'each' after 'for', like: for each item in things");
        Advance();
        if (!Check(TokenType.Identifier))
            throw Error(Peek(), "'for each' needs a name for each item, like: for each item in things");
        var name = Advance();
        if (!IsWord("in"))
            throw Error(Peek(), $"Expected 'in' after 'for each {name.Text}', like: for each {name.Text} in things");
        Advance();
        if (Check(TokenType.Newline))
            throw Error(forToken, "'for each' needs a list to go through, like: for each item in things");
        var source = Expression();
        EndOfLine("the 'for each' list");
        var body = Block("for each");
        return new ForEachStmt(name.Text, source, body, forToken.Line);
    }

    /// <summary>"add value to scores" / "remove item 2 from scores" / "sort scores". Reads from the first word.</summary>
    private Stmt ListChange()
    {
        var word = Advance();
        if (word.Text.Equals("sort", StringComparison.OrdinalIgnoreCase))
        {
            var sorted = ListName("sort");
            bool descending = IsWord("descending");
            if (descending) Advance();
            EndOfLine("the 'sort' line");
            return new SortStmt(sorted, descending, word.Line);
        }

        if (word.Text.Equals("add", StringComparison.OrdinalIgnoreCase))
        {
            if (Check(TokenType.Newline))
                throw Error(word, "'add' needs a value and a list, like: add 5 to scores");
            var value = Expression();
            if (!Match(TokenType.To))
                throw Error(Peek(), "Expected 'to' after the value, like: add 5 to scores");
            var target = ListName("add");
            EndOfLine("the 'add' line");
            return new AddStmt(value, target, word.Line);
        }

        if (!IsWord("item"))
            throw Error(Peek(), "'remove' needs an item, like: remove item 2 from scores");
        Advance();
        var position = Sum();
        if (!IsWord("from"))
            throw Error(Peek(), "Expected 'from' after the position, like: remove item 2 from scores");
        Advance();
        var name = ListName("remove");
        EndOfLine("the 'remove' line");
        return new RemoveStmt(position, name, word.Line);
    }

    private string ListName(string after)
    {
        if (!Check(TokenType.Identifier))
            throw Error(Peek(), $"'{after}' needs the name of a list at the end.");
        return Advance().Text;
    }

    private Stmt WhyStatement()
    {
        var whyToken = Advance();
        if (!Check(TokenType.Identifier))
            throw Error(Peek(), "'why' needs a name, like: why can_enter");
        var name = Advance();
        EndOfLine($"'why {name.Text}'");
        return new WhyStmt(new VariableExpr(name.Text, name.Line), whyToken.Line);
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

        if (_commands.ContainsKey(name.Text))
        {
            if (Check(TokenType.Equal))
                throw Error(name, $"'{name.Text}' is a command, so it can't be assigned a value.");
            _pos--; // read it again as a call
            var call = Primary();
            EndOfLine($"the call to '{name.Text}'");
            return new ExprStmt(call, name.Line);
        }

        if (!Check(TokenType.Equal) && (name.Text.Equals("add", StringComparison.OrdinalIgnoreCase)
                                        || name.Text.Equals("remove", StringComparison.OrdinalIgnoreCase)
                                        || name.Text.Equals("sort", StringComparison.OrdinalIgnoreCase)))
        {
            _pos--;
            return ListChange();
        }

        if (!Check(TokenType.Equal) && name.Text.Equals("write", StringComparison.OrdinalIgnoreCase))
        {
            if (Check(TokenType.Newline))
                throw Error(name, "'write' needs text and a file, like: write \"hello\" to \"notes.txt\"");
            var content = Expression();
            if (!Match(TokenType.To))
                throw Error(Peek(), "Expected 'to' after the text, like: write \"hello\" to \"notes.txt\"");
            if (Check(TokenType.Newline))
                throw Error(Peek(), "'write ... to' needs a file name, like: write \"hello\" to \"notes.txt\"");
            var path = Expression();
            EndOfLine("the 'write' line");
            return new WriteStmt(content, path, name.Line);
        }

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
        _blockDepth++;
        while (!Check(TokenType.Dedent) && !Check(TokenType.Eof))
            statements.Add(Statement());
        _blockDepth--;

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
        else if (IsWord("in"))
        {
            var op = Advance() with { Type = TokenType.In };
            left = new BinaryExpr(left, op, Sum(), op.Line);
        }
        else if (CheckAny(TokenType.Equal, TokenType.NotEqual, TokenType.Less,
                          TokenType.LessEqual, TokenType.Greater, TokenType.GreaterEqual))
        {
            var op = Advance();
            left = new BinaryExpr(left, op, Sum(), op.Line);
        }

        if (CheckAny(TokenType.Equal, TokenType.NotEqual, TokenType.Less, TokenType.LessEqual,
                     TokenType.Greater, TokenType.GreaterEqual, TokenType.Is) || IsWord("in"))
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
            case TokenType.List:
                Advance();
                var items = new List<Expr>();
                if (!CheckAny(TokenType.Newline, TokenType.RightParen, TokenType.Eof))
                {
                    do items.Add(Expression());
                    while (Match(TokenType.Comma));
                }
                return new ListExpr(items, token.Line);
            case TokenType.Identifier:
                Advance();
                if (!_commands.ContainsKey(token.Text) && token.Text.Equals("count", StringComparison.OrdinalIgnoreCase)
                    && IsWord("of"))
                {
                    Advance();
                    return new CountExpr(Primary(), token.Line);
                }
                if (!_commands.ContainsKey(token.Text) && token.Text.Equals("item", StringComparison.OrdinalIgnoreCase)
                    && CheckAny(TokenType.Number, TokenType.LeftParen, TokenType.Identifier)
                    && !IsWord("of") && !IsWord("in"))
                {
                    var position = Sum();
                    if (!IsWord("of"))
                        throw Error(Peek(), "Expected 'of' after the position, like: item 2 of scores");
                    Advance();
                    return new ItemExpr(position, Primary(), token.Line);
                }
                if (!_commands.ContainsKey(token.Text) && Builtins.TryGetValue(token.Text, out var builtin)
                    && CheckAny(TokenType.Number, TokenType.String, TokenType.LeftParen, TokenType.List,
                                TokenType.True, TokenType.False, TokenType.Identifier)
                    && !IsWord("of") && !IsWord("in"))
                {
                    var wordArgs = new List<Expr> { Unary() };
                    foreach (var between in builtin.Between)
                    {
                        if (!IsWord(between))
                            throw Error(Peek(), $"Expected '{between}' after the first value, like: {builtin.Example}");
                        Advance();
                        if (CheckAny(TokenType.Newline, TokenType.RightParen, TokenType.Eof))
                            throw Error(Peek(), $"'{token.Text}' needs a second value after '{between}', like: {builtin.Example}");
                        wordArgs.Add(Unary());
                    }
                    return new BuiltinExpr(token.Text.ToLowerInvariant(), wordArgs, token.Line);
                }
                if (_commands.TryGetValue(token.Text, out var arity))
                {
                    var args = new List<Expr>();
                    for (int i = 0; i < arity; i++)
                    {
                        if (CheckAny(TokenType.Newline, TokenType.RightParen, TokenType.Eof))
                            throw Error(Peek(),
                                $"'{token.Text}' needs {arity} value{(arity == 1 ? "" : "s")} but only got {i}.");
                        args.Add(Unary());
                    }
                    return new CallExpr(token.Text, args, token.Line);
                }
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

    /// <summary>True when the next token is this plain word (words like 'each' or 'of' are only special in context).</summary>
    private bool IsWord(string word) =>
        Check(TokenType.Identifier) && Peek().Text.Equals(word, StringComparison.OrdinalIgnoreCase);

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
