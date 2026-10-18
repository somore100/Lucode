namespace Lucode;

// ----- Expressions: things that produce a value -----

public abstract record Expr(int Line);

public sealed record LiteralExpr(object Value, int Line) : Expr(Line);

public sealed record VariableExpr(string Name, int Line) : Expr(Line);

public sealed record UnaryExpr(Token Operator, Expr Operand, int Line) : Expr(Line);

public sealed record BinaryExpr(Expr Left, Token Operator, Expr Right, int Line) : Expr(Line);

/// <summary>"and" / "or" get their own node because they stop early (short-circuit).</summary>
public sealed record LogicalExpr(Expr Left, Token Operator, Expr Right, int Line) : Expr(Line);

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
