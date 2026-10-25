namespace Lucode;

// ----- Expressions: things that produce a value -----

public abstract record Expr(int Line);

public sealed record LiteralExpr(object Value, int Line) : Expr(Line);

public sealed record VariableExpr(string Name, int Line) : Expr(Line);

public sealed record UnaryExpr(Token Operator, Expr Operand, int Line) : Expr(Line);

public sealed record BinaryExpr(Expr Left, Token Operator, Expr Right, int Line) : Expr(Line);

/// <summary>"and" / "or" get their own node because they stop early (short-circuit).</summary>
public sealed record LogicalExpr(Expr Left, Token Operator, Expr Right, int Line) : Expr(Line);

/// <summary>A call to a command: "add 2 3". Gives back whatever the command gives.</summary>
public sealed record CallExpr(string Name, List<Expr> Args, int Line) : Expr(Line);

/// <summary>"list 1, 2, 3" (or just "list" for an empty one).</summary>
public sealed record ListExpr(List<Expr> Items, int Line) : Expr(Line);

/// <summary>"item 2 of scores": one item of a list (or one letter of a text), counting from 1.</summary>
public sealed record ItemExpr(Expr Position, Expr Source, int Line) : Expr(Line);

/// <summary>"count of scores": how many items a list has (or letters a text has).</summary>
public sealed record CountExpr(Expr Source, int Line) : Expr(Line);

/// <summary>
/// A word that works on values: "upper name", "lower name", "number text", "ask prompt",
/// "split text by sep", "join items with sep". Word is lowercase; Args holds one value per slot.
/// </summary>
public sealed record BuiltinExpr(string Word, List<Expr> Args, int Line) : Expr(Line);

// ----- Statements: things that do something -----

public abstract record Stmt(int Line);

public sealed record AssignStmt(string Name, Expr Value, int Line) : Stmt(Line);

/// <summary>"rule name = expr": the expression is kept and recalculated every time the name is used.</summary>
public sealed record RuleStmt(string Name, Expr Value, int Line) : Stmt(Line);

public sealed record PrintStmt(Expr Value, int Line) : Stmt(Line);

public sealed record IfStmt(Expr Condition, List<Stmt> Then, List<Stmt>? Else, int Line) : Stmt(Line);

/// <summary>
/// "when condition" + block: the block runs each time the condition goes from false (or not yet
/// known) to true. Conditions are re-checked after every assignment.
/// </summary>
public sealed record WhenStmt(Expr Condition, List<Stmt> Body, int Line) : Stmt(Line);

/// <summary>"why name": prints the value of a variable or rule and which inputs decided it.</summary>
public sealed record WhyStmt(VariableExpr Target, int Line) : Stmt(Line);

/// <summary>"repeat count times" + block: runs the block that many times.</summary>
public sealed record RepeatStmt(Expr Count, List<Stmt> Body, int Line) : Stmt(Line);

/// <summary>"while condition" + block: runs the block again and again as long as the condition is true.</summary>
public sealed record WhileStmt(Expr Condition, List<Stmt> Body, int Line) : Stmt(Line);

/// <summary>"to name inputs" + block: defines a command that can be called by name.</summary>
public sealed record CommandStmt(string Name, List<string> Inputs, List<Stmt> Body, int Line) : Stmt(Line);

/// <summary>"give value": ends the command and hands the value back to the caller.</summary>
public sealed record GiveStmt(Expr Value, int Line) : Stmt(Line);

/// <summary>A call used as a line of its own, like: greet "Sam". Any value it gives is ignored.</summary>
public sealed record ExprStmt(Expr Value, int Line) : Stmt(Line);

/// <summary>"for each name in things" + block: runs the block once per item (or letter), with name set to it.</summary>
public sealed record ForEachStmt(string Name, Expr Source, List<Stmt> Body, int Line) : Stmt(Line);

/// <summary>"add value to scores": the list gets the value on the end.</summary>
public sealed record AddStmt(Expr Value, string Target, int Line) : Stmt(Line);

/// <summary>"remove item 2 from scores": the list loses that item.</summary>
public sealed record RemoveStmt(Expr Position, string Target, int Line) : Stmt(Line);

/// <summary>"write text to \"notes.txt\"": the file now holds exactly that text.</summary>
public sealed record WriteStmt(Expr Value, Expr Path, int Line) : Stmt(Line);

/// <summary>"sort scores": the list is put in order (all numbers, or all text).</summary>
public sealed record SortStmt(string Target, bool Descending, int Line) : Stmt(Line);
