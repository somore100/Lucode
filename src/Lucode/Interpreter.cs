using System.Globalization;

namespace Lucode;

/// <summary>
/// Runs a parsed Lucode program. Values are plain C# objects:
/// double (number), string (text) and bool (true / false).
/// </summary>
public sealed class Interpreter
{
    private readonly Dictionary<string, object> _variables = new();
    // Several rules with one name are kept in the order they were written: "any of them".
    private readonly Dictionary<string, List<RuleStmt>> _rules = new();

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

    // 'give' sets these and every block stops running statements until the command call picks the value up.
    // (An exception here was far too slow: every call that gave a value paid for one.)
    private bool _giving;
    private object? _given;

    private readonly TextWriter _output;
    private readonly TextReader _input;

    private sealed class Watcher
    {
        public required WhenStmt When { get; init; }

        /// <summary>The condition's value at the last check; false until it has been seen true.</summary>
        public bool WasTrue { get; set; }

        /// <summary>Set while the condition can't be worked out because a name has no value yet.</summary>
        public MissingValueError? Waiting { get; set; }
    }

    public Interpreter(TextWriter? output = null, TextReader? input = null)
    {
        _output = output ?? Console.Out;
        _input = input ?? Console.In;
    }

    /// <param name="finish">False in the REPL, where the program isn't over after each chunk.</param>
    public void Run(List<Stmt> program, bool finish = true)
    {
        foreach (var statement in program)
            Execute(statement);

        if (!finish) return;

        // A 'when' still waiting on a missing name at the end almost always means a typo.
        foreach (var watcher in _watchers)
            if (watcher.Waiting is { } missing)
                throw new LucodeError(watcher.When.Line,
                    $"This 'when' never ran: {missing.Message}");
    }

    // ----- Statements -----

    /// <summary>Runs a block's statements in order, stopping early once a 'give' has happened.</summary>
    private void RunBlock(List<Stmt> body)
    {
        foreach (var statement in body)
        {
            Execute(statement);
            if (_giving) return;
        }
    }

    private void Execute(Stmt statement)
    {
        switch (statement)
        {
            case AssignStmt assign:
                Assign(assign.Name, Evaluate(assign.Value), assign.Line);
                break;

            case ForEachStmt each:
                var source = Evaluate(each.Source);
                // Going through a copy, so changing the list inside the loop doesn't change the loop.
                IReadOnlyList<object> things = source switch
                {
                    List<object> list => list.ToArray(),
                    string text => text.Select(c => (object)c.ToString()).ToArray(),
                    _ => throw new LucodeError(each.Line,
                        $"'for each' needs a list or text, but got {TypeName(source)} {Describe(source)}."),
                };
                foreach (var thing in things)
                {
                    Assign(each.Name, thing, each.Line);
                    RunBlock(each.Body);
                    if (_giving) break;
                }
                break;

            case AddStmt add:
                var addTo = CurrentList(add.Target, add.Line, "add to");
                var added = new List<object>(addTo) { Evaluate(add.Value) };
                Assign(add.Target, added, add.Line);
                break;

            case RemoveStmt remove:
                var removeFrom = CurrentList(remove.Target, remove.Line, "remove from");
                int removeAt = Position(Evaluate(remove.Position), removeFrom.Count, "list", remove.Line);
                var removed = new List<object>(removeFrom);
                removed.RemoveAt(removeAt);
                Assign(remove.Target, removed, remove.Line);
                break;

            case SortStmt sort:
                var sortFrom = CurrentList(sort.Target, sort.Line, "sort");
                Assign(sort.Target, Sorted(sortFrom, sort.Descending, sort.Line), sort.Line);
                break;

            case WriteStmt write:
                string writeTo = RequireText(Evaluate(write.Path), "write ... to", write.Line);
                try
                {
                    File.WriteAllText(writeTo, Format(Evaluate(write.Value)));
                }
                catch (DirectoryNotFoundException)
                {
                    throw new LucodeError(write.Line, $"Can't write \"{writeTo}\": there is no such folder.");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    throw new LucodeError(write.Line, $"Can't write \"{writeTo}\": {e.Message}");
                }
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
                // Worked out first: the value may call other commands, which finish their own 'give'.
                _given = Evaluate(give.Value);
                _giving = true;
                break;

            case ExprStmt call:
                Call((CallExpr)call.Value);
                break;

            case RuleStmt rule:
                if (_variables.ContainsKey(rule.Name))
                    throw new LucodeError(rule.Line,
                        $"'{rule.Name}' already holds a value, so it can't also be a rule. Pick another name.");
                if (!_rules.TryGetValue(rule.Name, out var alternatives))
                    _rules[rule.Name] = alternatives = new List<RuleStmt>();
                // The same line twice is almost surely a copy-paste slip; a different formula is another way to get there.
                if (alternatives.FirstOrDefault(other => other.Signature == rule.Signature) is { } same)
                    throw new LucodeError(rule.Line,
                        $"Rule '{rule.Name}' is already defined on line {same.Line}.");
                // Nothing is calculated yet: the formula is stored and worked out each time it's used.
                alternatives.Add(rule);
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
                for (double i = 0; i < count && !_giving; i++)
                    RunBlock(repeat.Body);
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
                    RunBlock(loop.Body);
                    if (_giving) break;
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
                    RunBlock(branch);
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

    /// <summary>Sets a variable. Inside a command it stays local; at the top level 'when' blocks are checked.</summary>
    private void Assign(string name, object value, int line)
    {
        if (_rules.TryGetValue(name, out var existingRule))
            throw new LucodeError(line,
                $"'{name}' is a rule (line {existingRule[0].Line}), so it can't be set directly. " +
                "Change the values it depends on instead.");
        if (_locals is not null)
        {
            // Inside a command, assignments stay local; the outside world is untouched.
            _locals[name] = value;
            return;
        }
        _variables[name] = value;
        CheckWatchers();
    }

    private List<object> CurrentList(string name, int line, string verb)
    {
        var value = LookUp(new VariableExpr(name, line));
        return value as List<object>
            ?? throw new LucodeError(line, $"Can't {verb} '{name}': it's {TypeName(value)} {Describe(value)}, not a list.");
    }

    /// <summary>Turns a position (counting from 1) into a 0-based index, or explains what's wrong with it.</summary>
    private static int Position(object position, int count, string what, int line)
    {
        if (position is not double d || d != Math.Floor(d))
            throw new LucodeError(line, $"A position must be a whole number, but got {TypeName(position)} {Describe(position)}.");
        if (d < 1)
            throw new LucodeError(line, $"Positions start at 1, so there is no item {Format(d)}.");
        string unit = what == "list" ? "item" : "letter";
        if (d > count)
            throw new LucodeError(line, count == 0
                ? $"The {what} is empty, so there is no {unit} {Format(d)}."
                : $"The {what} only has {count} {unit}{(count == 1 ? "" : "s")}, so there is no {unit} {Format(d)}.");
        return (int)d - 1;
    }

    // ----- 'why' -----

    /// <summary>"name is value", plus ", because ..." naming the inputs that decided a rule.</summary>
    private string Explain(VariableExpr target)
    {
        var value = Evaluate(target); // also reports unknown names and rule loops
        string text = $"{target.Name} is {Show(value)}";
        if (_variables.ContainsKey(target.Name) || !_rules.TryGetValue(target.Name, out var lines))
            return text;

        if (lines.Count > 1)
        {
            var winner = Winner(lines);
            if (winner is not null)
                return $"{text}{Because(winner)} (rule on line {winner.Line})";

            var reasons = lines.Select(line => $"line {line.Line}{Because(line, ": ")}");
            return $"{text}, because none of its {lines.Count} rules held ({string.Join("; ", reasons)})";
        }

        var deciders = new List<(string Name, object Value)>();
        Collect(lines[0].Value, deciders);
        return deciders.Count == 0
            ? text
            : $"{text}, because {string.Join(" and ", deciders.Select(d => $"{d.Name} is {Show(d.Value)}"))}";
    }

    /// <summary>The ", because x is 1 and y is 2" part for one rule line (its opening word can be changed).</summary>
    private string Because(RuleStmt line, string opening = ", because ")
    {
        var deciders = new List<(string Name, object Value)>();
        try
        {
            Collect(line.Value, deciders);
        }
        catch (MissingValueError missing)
        {
            return $"{opening}{missing.Name} has no value yet";
        }
        return deciders.Count == 0
            ? ""
            : opening + string.Join(" and ", deciders.Select(d => $"{d.Name} is {Show(d.Value)}"));
    }

    /// <summary>The first line of a same-name rule that isn't false and can be worked out, or null if none.</summary>
    private RuleStmt? Winner(List<RuleStmt> lines)
    {
        foreach (var line in lines)
        {
            try
            {
                if (Evaluate(line.Value) is not false) return line;
            }
            catch (MissingValueError)
            {
                // Not worked out yet: the next line may still do it.
            }
        }
        return null;
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
                else if (_rules.TryGetValue(variable.Name, out var lines))
                {
                    // With several lines only the one that won (or, if none did, every line) explains the value.
                    var used = lines.Count == 1 ? lines : Winner(lines) is { } won ? new List<RuleStmt> { won } : lines;
                    foreach (var line in used)
                    {
                        try
                        {
                            Collect(line.Value, found);
                        }
                        catch (MissingValueError)
                        {
                            // A line that couldn't be worked out has nothing to add.
                        }
                    }
                }
                break;

            case UnaryExpr unary:
                Collect(unary.Operand, found);
                break;

            case BinaryExpr binary:
                Collect(binary.Left, found);
                Collect(binary.Right, found);
                break;

            case ListExpr list:
                foreach (var item in list.Items)
                    Collect(item, found);
                break;

            case ItemExpr item:
                Collect(item.Position, found);
                Collect(item.Source, found);
                break;

            case CountExpr count:
                Collect(count.Source, found);
                break;

            case BuiltinExpr word:
                foreach (var arg in word.Args)
                    Collect(arg, found);
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
        ListExpr list => list.Items.Select(Evaluate).ToList(),
        ItemExpr item => EvaluateItem(item),
        CountExpr count => EvaluateCount(count),
        BuiltinExpr word => EvaluateBuiltin(word),
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
            RunBlock(command.Body);
            if (!_giving) return null;
            var value = _given;
            _given = null;
            return value;
        }
        finally
        {
            _giving = false;
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

        if (_rules.TryGetValue(variable.Name, out var lines))
            return EvaluateRule(variable.Name, lines, variable.Line);

        string hint = Suggest.Hint(variable.Name, _variables.Keys.Concat(_rules.Keys));
        throw new MissingValueError(variable.Line, variable.Name, $"'{variable.Name}' has no value yet.{hint}");
    }

    private object EvaluateRule(string name, List<RuleStmt> lines, int usedOnLine)
    {
        if (_evaluating.Contains(name))
        {
            var loop = _evaluating.SkipWhile(other => other != name).Append(name);
            throw new LucodeError(usedOnLine,
                $"Rule '{name}' depends on itself: {string.Join(" -> ", loop)}.");
        }

        _evaluating.Add(name);
        var savedLocals = _locals;
        _locals = null; // a rule always looks at the top-level values, never a command's own
        try
        {
            if (lines.Count == 1)
                return Evaluate(lines[0].Value);

            // Several lines, "any of them": a line that is false or can't be worked out yet passes to the next.
            bool sawFalse = false;
            MissingValueError? firstMissing = null;
            foreach (var line in lines)
            {
                try
                {
                    var value = Evaluate(line.Value);
                    if (value is false)
                    {
                        sawFalse = true;
                        continue;
                    }
                    return value;
                }
                catch (MissingValueError missing)
                {
                    firstMissing ??= missing;
                }
            }
            if (sawFalse) return false;
            throw firstMissing!;
        }
        finally
        {
            _locals = savedLocals;
            _evaluating.RemoveAt(_evaluating.Count - 1);
        }
    }

    private object EvaluateItem(ItemExpr item)
    {
        var source = Evaluate(item.Source);
        var position = Evaluate(item.Position);
        switch (source)
        {
            case List<object> list:
                return list[Position(position, list.Count, "list", item.Line)];
            case string text:
                return text[Position(position, text.Length, "text", item.Line)].ToString();
            default:
                throw new LucodeError(item.Line, $"'item ... of' needs a list or text, but got {TypeName(source)} {Describe(source)}.");
        }
    }

    private object EvaluateCount(CountExpr count)
    {
        var source = Evaluate(count.Source);
        return source switch
        {
            List<object> list => (double)list.Count,
            string text => (double)text.Length,
            _ => throw new LucodeError(count.Line, $"'count of' needs a list or text, but got {TypeName(source)} {Describe(source)}."),
        };
    }

    private object EvaluateBuiltin(BuiltinExpr word)
    {
        // Every value is worked out before the word runs, left to right.
        var args = word.Args.Select(Evaluate).ToList();

        switch (word.Word)
        {
            case "upper":
                return RequireText(args[0], "upper", word.Line).ToUpperInvariant();
            case "lower":
                return RequireText(args[0], "lower", word.Line).ToLowerInvariant();

            case "trim":
                return RequireText(args[0], "trim", word.Line).Trim();

            case "find":
                string needle = RequireText(args[0], "find", word.Line);
                string haystack = RequireText(args[1], "find ... in", word.Line);
                // Position of the first match counting from 1, or 0 when it isn't there.
                return (double)(haystack.IndexOf(needle, StringComparison.Ordinal) + 1);

            case "replace":
                string oldText = RequireText(args[0], "replace", word.Line);
                string newText = RequireText(args[1], "replace ... with", word.Line);
                string inText = RequireText(args[2], "replace ... in", word.Line);
                if (oldText.Length == 0)
                    throw new LucodeError(word.Line, "'replace' needs some text to look for, but got \"\".");
                return inText.Replace(oldText, newText, StringComparison.Ordinal);

            case "number":
                if (args[0] is double) return args[0];
                string digits = RequireText(args[0], "number", word.Line);
                if (double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    && double.IsFinite(parsed))
                    return parsed;
                throw new LucodeError(word.Line, $"Can't turn \"{digits}\" into a number.");

            case "ask":
                _output.Write(Format(args[0]) + " ");
                _output.Flush();
                return _input.ReadLine()
                    ?? throw new LucodeError(word.Line, "'ask' has no more input to read.");

            case "read":
                string path = RequireText(args[0], "read", word.Line);
                try
                {
                    return File.ReadAllText(path);
                }
                catch (FileNotFoundException)
                {
                    throw new LucodeError(word.Line, $"Can't read \"{path}\": there is no such file.");
                }
                catch (DirectoryNotFoundException)
                {
                    throw new LucodeError(word.Line, $"Can't read \"{path}\": there is no such folder.");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    throw new LucodeError(word.Line, $"Can't read \"{path}\": {e.Message}");
                }

            case "split":
                string text = RequireText(args[0], "split", word.Line);
                string separator = RequireText(args[1], "split ... by", word.Line);
                // An empty separator splits into single letters.
                var pieces = separator.Length == 0
                    ? text.Select(c => c.ToString())
                    : text.Split(separator, StringSplitOptions.None);
                return pieces.Select(piece => (object)piece).ToList();

            case "join":
                if (args[0] is not List<object> items)
                    throw new LucodeError(word.Line,
                        $"'join' needs a list, but got {TypeName(args[0])} {Describe(args[0])}.");
                string glue = RequireText(args[1], "join ... with", word.Line);
                return string.Join(glue, items.Select(Format));

            default:
                throw new InvalidOperationException($"Unknown word {word.Word}");
        }
    }

    private static string RequireText(object value, string word, int line) =>
        value as string
            ?? throw new LucodeError(line, $"'{word}' needs text, but got {TypeName(value)} {Describe(value)}.");

    /// <summary>A sorted copy of a list: all numbers (smallest first) or all text (A to Z, ignoring case).</summary>
    private static List<object> Sorted(List<object> list, bool descending, int line)
    {
        if (list.All(item => item is double))
            return (descending ? list.OrderByDescending(item => (double)item) : list.OrderBy(item => (double)item)).ToList();
        if (list.All(item => item is string))
            return (descending
                ? list.OrderByDescending(item => (string)item, StringComparer.OrdinalIgnoreCase)
                : list.OrderBy(item => (string)item, StringComparer.OrdinalIgnoreCase)).ToList();

        var first = list.First(); // the list isn't empty here: an empty list is all numbers
        var odd = list.FirstOrDefault(item => item.GetType() != first.GetType());
        throw new LucodeError(line, odd is null
            ? $"Can only sort a list of numbers or text, but this one holds {TypeName(first)} {Describe(first)}."
            : $"Can only sort a list of all numbers or all text, but this one has {TypeName(first)} and {TypeName(odd)}.");
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

            case TokenType.In:
                if (right is List<object> members) return members.Any(m => AreEqual(left, m));
                if (right is string haystack && left is string needle) return haystack.Contains(needle, StringComparison.Ordinal);
                throw new LucodeError(line, right is string
                    ? $"Can only look for text inside text, but got {TypeName(left)} {Describe(left)}."
                    : $"'in' needs a list or text on the right, but got {TypeName(right)} {Describe(right)}.");

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
        (List<object> a, List<object> b) => a.Count == b.Count && a.Zip(b).All(p => AreEqual(p.First, p.Second)),
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
        List<object> => "a list",
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
        List<object> list => list.Count == 0 ? "list" : "list " + string.Join(", ", list.Select(Show)),
        _ => value.ToString() ?? "",
    };
}
