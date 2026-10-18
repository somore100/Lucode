using System.Globalization;

namespace Lucode;

/// <summary>
/// Runs a parsed Lucode program. Values are plain C# objects:
/// double (number), string (text) and bool (true / false).
/// </summary>
public sealed class Interpreter
{
    private readonly Dictionary<string, object> _variables = new();
    private readonly Dictionary<string, RuleStmt> _rules = new();

    // Rules currently being worked out, in order, so a rule that depends on itself is caught.
    private readonly List<string> _evaluating = new();

    // 'when' blocks, in the order they were declared, and the ones currently running (innermost last).
    private readonly List<Watcher> _watchers = new();
    private readonly List<Watcher> _firing = new();
    private const int MaxWhenDepth = 100;

    private readonly TextWriter _output;

    private sealed class Watcher
    {
        public required WhenStmt When { get; init; }

        /// <summary>The condition's value at the last check; false until it has been seen true.</summary>
        public bool WasTrue { get; set; }

        /// <summary>Set while the condition can't be worked out because a name has no value yet.</summary>
        public MissingValueError? Waiting { get; set; }
    }

    public Interpreter(TextWriter? output = null)
    {
        _output = output ?? Console.Out;
    }

    public void Run(List<Stmt> program)
    {
        foreach (var statement in program)
            Execute(statement);

        // A 'when' still waiting on a missing name at the end almost always means a typo.
        foreach (var watcher in _watchers)
            if (watcher.Waiting is { } missing)
                throw new LucodeError(watcher.When.Line,
                    $"This 'when' never ran: {missing.Message}");
    }

    // ----- Statements -----

    private void Execute(Stmt statement)
    {
        switch (statement)
        {
            case AssignStmt assign:
                if (_rules.TryGetValue(assign.Name, out var existingRule))
                    throw new LucodeError(assign.Line,
                        $"'{assign.Name}' is a rule (line {existingRule.Line}), so it can't be set directly. " +
                        "Change the values it depends on instead.");
                _variables[assign.Name] = Evaluate(assign.Value);
                CheckWatchers();
                break;

            case RuleStmt rule:
                if (_variables.ContainsKey(rule.Name))
                    throw new LucodeError(rule.Line,
                        $"'{rule.Name}' already holds a value, so it can't also be a rule. Pick another name.");
                if (_rules.TryGetValue(rule.Name, out var earlier))
                    throw new LucodeError(rule.Line,
                        $"Rule '{rule.Name}' is already defined on line {earlier.Line}.");
                // Nothing is calculated yet: the formula is stored and worked out each time it's used.
                _rules[rule.Name] = rule;
                break;

            case PrintStmt print:
                _output.WriteLine(Format(Evaluate(print.Value)));
                break;

            case IfStmt ifStmt:
                var condition = Evaluate(ifStmt.Condition);
                if (condition is not bool passed)
                    throw new LucodeError(ifStmt.Line,
                        $"'if' needs a true/false condition, but got {TypeName(condition)} {Describe(condition)}.");

                var branch = passed ? ifStmt.Then : ifStmt.Else;
                if (branch is not null)
                    foreach (var inner in branch)
                        Execute(inner);
                break;

            case WhenStmt whenStmt:
                var watcher = new Watcher { When = whenStmt };
                _watchers.Add(watcher);
                // Before it was declared it counted as false, so a condition that's already true fires now.
                Check(watcher);
                break;

            default:
                throw new InvalidOperationException($"Unknown statement {statement.GetType().Name}");
        }
    }

    // ----- 'when' -----

    /// <summary>
    /// Runs after every assignment. First every 'when' looks at the same moment, then the ones
    /// that went from false to true run, in the order they were declared. Looking first means
    /// one block changing a value can't hide that change from a 'when' declared after it.
    /// </summary>
    private void CheckWatchers()
    {
        var rising = new List<Watcher>();
        foreach (var watcher in _watchers)
        {
            bool isTrue = Test(watcher);
            if (isTrue && !watcher.WasTrue)
                rising.Add(watcher);
            watcher.WasTrue = isTrue; // updated before running, so a block doesn't re-fire itself
        }

        foreach (var watcher in rising)
            Fire(watcher);
    }

    /// <summary>Checks a 'when' that was just declared; until now it counted as false.</summary>
    private void Check(Watcher watcher)
    {
        watcher.WasTrue = Test(watcher);
        if (watcher.WasTrue)
            Fire(watcher);
    }

    private bool Test(Watcher watcher)
    {
        object value;
        try
        {
            value = Evaluate(watcher.When.Condition);
        }
        catch (MissingValueError missing)
        {
            // A name isn't set yet: the condition simply isn't true yet.
            watcher.Waiting = missing;
            return false;
        }

        watcher.Waiting = null;
        if (value is not bool flag)
            throw new LucodeError(watcher.When.Line,
                $"'when' needs a true/false condition, but got {TypeName(value)} {Describe(value)}.");
        return flag;
    }

    private void Fire(Watcher watcher)
    {
        if (_firing.Count >= MaxWhenDepth)
        {
            int start = _firing.LastIndexOf(watcher);
            string chain = start < 0
                ? ""
                : ": " + string.Join(" -> ",
                    _firing.Skip(start).Append(watcher).Select(w => $"line {w.When.Line}"));
            throw new LucodeError(watcher.When.Line,
                $"'when' blocks keep setting each other off{chain}. Stopped after {MaxWhenDepth} in a row.");
        }

        _firing.Add(watcher);
        try
        {
            foreach (var statement in watcher.When.Body)
                Execute(statement);
        }
        finally
        {
            _firing.RemoveAt(_firing.Count - 1);
        }
    }

    // ----- Expressions -----

    private object Evaluate(Expr expr) => expr switch
    {
        LiteralExpr literal => literal.Value,
        VariableExpr variable => LookUp(variable),
        UnaryExpr unary => EvaluateUnary(unary),
        LogicalExpr logical => EvaluateLogical(logical),
        BinaryExpr binary => EvaluateBinary(binary),
        _ => throw new InvalidOperationException($"Unknown expression {expr.GetType().Name}"),
    };

    private object LookUp(VariableExpr variable)
    {
        if (_variables.TryGetValue(variable.Name, out var value))
            return value;

        if (_rules.TryGetValue(variable.Name, out var rule))
            return EvaluateRule(rule, variable.Line);

        string hint = Suggest.Hint(variable.Name, _variables.Keys.Concat(_rules.Keys));
        throw new MissingValueError(variable.Line, variable.Name, $"'{variable.Name}' has no value yet.{hint}");
    }

    private object EvaluateRule(RuleStmt rule, int usedOnLine)
    {
        if (_evaluating.Contains(rule.Name))
        {
            var loop = _evaluating.SkipWhile(name => name != rule.Name).Append(rule.Name);
            throw new LucodeError(usedOnLine,
                $"Rule '{rule.Name}' depends on itself: {string.Join(" -> ", loop)}.");
        }

        _evaluating.Add(rule.Name);
        try
        {
            return Evaluate(rule.Value);
        }
        finally
        {
            _evaluating.RemoveAt(_evaluating.Count - 1);
        }
    }

    private object EvaluateUnary(UnaryExpr unary)
    {
        var operand = Evaluate(unary.Operand);

        return unary.Operator.Type switch
        {
            TokenType.Minus when operand is double number => -number,
            TokenType.Minus => throw new LucodeError(unary.Line,
                $"Can't make {TypeName(operand)} negative."),
            TokenType.Not when operand is bool flag => !flag,
            TokenType.Not => throw new LucodeError(unary.Line,
                $"'not' needs true or false, but got {TypeName(operand)} {Describe(operand)}."),
            _ => throw new InvalidOperationException($"Unknown unary operator {unary.Operator.Text}"),
        };
    }

    private object EvaluateLogical(LogicalExpr logical)
    {
        string word = logical.Operator.Text.ToLowerInvariant();
        bool left = RequireBool(Evaluate(logical.Left), word, logical.Line);

        // Short-circuit: skip the right side when the answer is already known.
        if (logical.Operator.Type == TokenType.Or && left) return true;
        if (logical.Operator.Type == TokenType.And && !left) return false;

        return RequireBool(Evaluate(logical.Right), word, logical.Line);
    }

    private object EvaluateBinary(BinaryExpr binary)
    {
        var left = Evaluate(binary.Left);
        var right = Evaluate(binary.Right);
        var op = binary.Operator;
        int line = binary.Line;

        switch (op.Type)
        {
            case TokenType.Equal:
                return AreEqual(left, right);
            case TokenType.NotEqual:
                return !AreEqual(left, right);

            case TokenType.Plus:
                if (left is double a && right is double b) return a + b;
                // Adding text joins it together: "Total: " + 5 → "Total: 5"
                if (left is string || right is string) return Format(left) + Format(right);
                throw TypeMismatch(op, left, right, line);
        }

        if (left is not double l || right is not double r)
            throw TypeMismatch(op, left, right, line);

        return op.Type switch
        {
            TokenType.Minus => l - r,
            TokenType.Star => l * r,
            TokenType.Slash => r == 0 ? throw new LucodeError(line, "Can't divide by zero.") : l / r,
            TokenType.Percent => r == 0 ? throw new LucodeError(line, "Can't take the remainder of dividing by zero.") : l % r,
            TokenType.Less => l < r,
            TokenType.LessEqual => l <= r,
            TokenType.Greater => l > r,
            TokenType.GreaterEqual => l >= r,
            _ => throw new InvalidOperationException($"Unknown operator {op.Text}"),
        };
    }

    // ----- Helpers -----

    private static bool AreEqual(object left, object right) => (left, right) switch
    {
        (double a, double b) => a == b,
        (string a, string b) => a == b,
        (bool a, bool b) => a == b,
        _ => false, // different kinds of values are never equal
    };

    private static bool RequireBool(object value, string word, int line) =>
        value is bool flag
            ? flag
            : throw new LucodeError(line, $"'{word}' needs true or false on both sides, but got {TypeName(value)} {Describe(value)}.");

    private static LucodeError TypeMismatch(Token op, object left, object right, int line) =>
        new(line, $"Can't use '{op.Text}' on {TypeName(left)} and {TypeName(right)}.");

    private static string TypeName(object value) => value switch
    {
        double => "a number",
        string => "text",
        bool => "true/false",
        _ => "an unknown value",
    };

    private static string Describe(object value) => value is string s ? $"\"{s}\"" : $"({Format(value)})";

    public static string Format(object value) => value switch
    {
        double d when double.IsPositiveInfinity(d) => "infinity",
        double d when double.IsNegativeInfinity(d) => "-infinity",
        // 15 significant digits hides floating-point noise: 0.1 + 0.2 prints as 0.3
        double d => d.ToString("G15", CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        string s => s,
        _ => value.ToString() ?? "",
    };
}
