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
    private const int MaxLoopRuns = 1_000_000;

    // Commands, and the inputs / variables of the one currently running (null at the top level).
    private readonly Dictionary<string, CommandStmt> _commands = new();
    private Dictionary<string, object>? _locals;
    private int _callDepth;
    private const int MaxCallDepth = 200;

    /// <summary>Thrown by 'give' to leave a command early with its value.</summary>
    private sealed class GiveSignal(object value) : Exception
    {
        public object Value { get; } = value;
    }

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
                var assigned = Evaluate(assign.Value);
                if (_locals is not null)
                {
                    // Inside a command, assignments stay local; the outside world is untouched.
                    _locals[assign.Name] = assigned;
                    break;
                }
                _variables[assign.Name] = assigned;
                CheckWatchers();
                break;

            case CommandStmt command:
                if (_commands.ContainsKey(command.Name))
                    throw new LucodeError(command.Line, $"Command '{command.Name}' is already defined.");
                if (_variables.ContainsKey(command.Name) || _rules.ContainsKey(command.Name))
                    throw new LucodeError(command.Line,
                        $"'{command.Name}' is already a variable or rule, so it can't also be a command.");
                _commands[command.Name] = command;
                break;

            case GiveStmt give:
                throw new GiveSignal(Evaluate(give.Value));

            case ExprStmt call:
                Call((CallExpr)call.Value);
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

            case RepeatStmt repeat:
                var times = Evaluate(repeat.Count);
                if (times is not double count)
                    throw new LucodeError(repeat.Line,
                        $"'repeat' needs a number, but got {TypeName(times)} {Describe(times)}.");
                if (count < 0 || count != Math.Floor(count))
                    throw new LucodeError(repeat.Line,
                        $"'repeat' needs a whole number of 0 or more, but got {Format(count)}.");
                if (count > MaxLoopRuns)
                    throw new LucodeError(repeat.Line,
                        $"'repeat' can't run more than {MaxLoopRuns} times, but got {Format(count)}.");
                for (double i = 0; i < count; i++)
                    foreach (var inner in repeat.Body)
                        Execute(inner);
                break;

            case WhileStmt loop:
                for (int runs = 0; ; runs++)
                {
                    var test = Evaluate(loop.Condition);
                    if (test is not bool keepGoing)
                        throw new LucodeError(loop.Line,
                            $"'while' needs a true/false condition, but got {TypeName(test)} {Describe(test)}.");
                    if (!keepGoing) break;
                    if (runs >= MaxLoopRuns)
                        throw new LucodeError(loop.Line,
                            $"'while' ran {MaxLoopRuns} times in a row without stopping. Is the condition ever false?");
                    foreach (var inner in loop.Body)
                        Execute(inner);
                }
                break;

            case WhyStmt why:
                _output.WriteLine(Explain(why.Target));
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

    // ----- 'why' -----

    /// <summary>"name is value", plus ", because ..." naming the inputs that decided a rule.</summary>
    private string Explain(VariableExpr target)
    {
        var value = Evaluate(target); // also reports unknown names and rule loops
        string text = $"{target.Name} is {Show(value)}";
        if (_variables.ContainsKey(target.Name) || !_rules.TryGetValue(target.Name, out var rule))
            return text;

        var deciders = new List<(string Name, object Value)>();
        Collect(rule.Value, deciders);
        return deciders.Count == 0
            ? text
            : $"{text}, because {string.Join(" and ", deciders.Select(d => $"{d.Name} is {Show(d.Value)}"))}";
    }

    /// <summary>
    /// Gathers the plain variables that decided an expression's value. Rules are looked through,
    /// and 'and' / 'or' only count the side that settled the answer: with 'and', the first side
    /// that was false; with 'or', the first side that was true; both sides when neither could.
    /// </summary>
    private void Collect(Expr expr, List<(string Name, object Value)> found)
    {
        switch (expr)
        {
            case VariableExpr variable:
                if (_variables.TryGetValue(variable.Name, out var value))
                {
                    if (!found.Any(f => f.Name == variable.Name))
                        found.Add((variable.Name, value));
                }
                else if (_rules.TryGetValue(variable.Name, out var rule))
                    Collect(rule.Value, found);
                break;

            case UnaryExpr unary:
                Collect(unary.Operand, found);
                break;

            case BinaryExpr binary:
                Collect(binary.Left, found);
                Collect(binary.Right, found);
                break;

            case LogicalExpr logical:
                // Same order as EvaluateLogical, so we never look at a side that was skipped.
                bool settles = logical.Operator.Type == TokenType.Or; // the value that ends it early
                if ((bool)Evaluate(logical.Left) == settles)
                    Collect(logical.Left, found);
                else if ((bool)Evaluate(logical.Right) == settles)
                    Collect(logical.Right, found);
                else
                {
                    Collect(logical.Left, found);
                    Collect(logical.Right, found);
                }
                break;
        }
    }

    private static string Show(object value) => value is string s ? $"\"{s}\"" : Format(value);

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
        CallExpr call => Call(call) ?? throw new LucodeError(call.Line,
            $"'{call.Name}' doesn't give a value, so it can't be used as one."),
        _ => throw new InvalidOperationException($"Unknown expression {expr.GetType().Name}"),
    };

    /// <summary>Runs a command with its inputs. Returns what it gave, or null if it gave nothing.</summary>
    private object? Call(CallExpr call)
    {
        if (!_commands.TryGetValue(call.Name, out var command))
            throw new LucodeError(call.Line, $"Unknown command '{call.Name}'.");
        if (_callDepth >= MaxCallDepth)
            throw new LucodeError(call.Line,
                $"'{call.Name}' called itself too deeply ({MaxCallDepth} calls in a row).");

        // Inputs are worked out by the caller, before the command's own names exist.
        var locals = new Dictionary<string, object>();
        for (int i = 0; i < command.Inputs.Count; i++)
            locals[command.Inputs[i]] = Evaluate(call.Args[i]);

        var saved = _locals;
        _locals = locals;
        _callDepth++;
        try
        {
            foreach (var statement in command.Body)
                Execute(statement);
            return null;
        }
        catch (GiveSignal given)
        {
            return given.Value;
        }
        finally
        {
            _locals = saved;
            _callDepth--;
        }
    }

    private object LookUp(VariableExpr variable)
    {
        if (_locals is not null && _locals.TryGetValue(variable.Name, out var local))
            return local;

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
        var savedLocals = _locals;
        _locals = null; // a rule always looks at the top-level values, never a command's own
        try
        {
            return Evaluate(rule.Value);
        }
        finally
        {
            _locals = savedLocals;
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
