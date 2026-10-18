# Lucode — Handoff

Paste or attach this file at the start of a new chat to continue the project.
Last updated: 2026-10-18, version 0.3.0-dev (`when` done, `why` next).

## What Lucode is

A small, readable programming language focused on logic, math and **live
relationships**. Guiding principle: *code should say what you mean*. Guiding
rule: *keep it small and coherent; add a feature only when it solves a real problem.*

- Interpreter written in **C# / .NET 8**, a tree-walking interpreter.
- File extension: **`.luc`**.
- Repo: `github.com/somore100/lucode` (author's GitHub: `somore100`).
- Local path: `~/Documents/Hot/programs/Lucode` on Fedora.
- Author also maintains **Blockliner** (`github.com/somore100/Blockliner`),
  a Python/Tkinter visual block/node editor that generates code in 9 languages.

## Decisions already made (don't re-litigate without reason)

| Topic | Decision |
|---|---|
| Name | **Lucode**. Checked: no existing language or notable project with that name. |
| Blocks | **Indentation**, no colons, no braces, no `end`. |
| Assignment | `name = value` at the start of a line. |
| Comparison | **Single `=`** inside expressions (`if money = 100`). `==` gives a friendly error. |
| Equality aliases | `=` and `is` mean equal; `!=`, `is not`, and `not a = b` mean not equal. One symbol form plus one word form per operator, no more. |
| Rejected aliases | `=!` (reads as `= !x`), `>>` for "is" (looks like bit shift). |
| Output | `print` (familiar). Not `show`. |
| Logic | `and`, `or`, `not`. `not` binds looser than comparisons. `nor` is still undecided. |
| Keywords | Case-insensitive (`NOT`, `If` work). Variable names are case-sensitive. |
| Text joining | `+` with text on either side joins into text (`"Total: " + 5`). |
| Numbers | All numbers are `double`; printed with 15 significant digits so `0.1 + 0.2` prints `0.3`. |
| Comparison chaining | Not allowed (`1 < 2 < 3` is an error suggesting `and`). |
| Different types | Never equal (`5 = "5"` is false); arithmetic on mismatched types is an error. |
| `fact` keyword | **Dropped.** Plain variables already express facts. |
| `do` keyword | **Dropped.** Indentation marks the body. |
| `rule` keyword | **Kept**, as **opt-in live recalculation** (see below). |
| Live by default? | **No.** Ordinary variables are snapshots; otherwise `count = count + 1` would be circular. |
| `when` firing | Only on a false → true change (edge-triggered). |
| `when` checking | After every assignment; all `when`s observe the same moment, then rising ones fire in declaration order. |
| `when` already true | Fires immediately when declared ("not yet declared" counts as false). |
| `when` side effects | Allowed; cascades allowed; 100 nested firings in a row is an error showing the loop. |
| `when` placement | Top level only; no `else`. |
| Independence | Lucode is standalone (CLI, later VS Code). Blockliner is an add-on via a language pack, never a dependency. |

## Rules (v0.2) — exact semantics

```
rule can_enter = has_key and is_owner
```

- The expression is **stored, not evaluated**. It is evaluated each time the
  name is read ("recalculate when read", pull-based).
- Rules can reference rules and can be defined before their inputs exist.
- Errors: assigning to a rule name; making a rule with an existing variable's
  name; defining the same rule twice (reserved for a future "either rule works"
  meaning); cycles (`Rule 'a' depends on itself: a -> b -> a.`, reported on the
  line where the loop closes).

## `when` (v0.3) — exact semantics

```
when can_enter
    print "Door opens"
```

- The condition is any true/false expression (a rule name, or e.g. `score >= 10`).
- On declaration it's evaluated; if true, the block runs immediately.
- After **every assignment**, two phases: (1) evaluate every `when` condition
  and record its new value, collecting those that went false → true;
  (2) run the collected blocks in declaration order. Recording first means a
  block changing a value can't hide that change from a later `when`
  (test `when_same_moment`).
- Each block runs at most once per rise. A block changing its own inputs
  doesn't re-fire it while the condition stays true.
- A name with no value yet makes the condition "not true yet" (via
  `MissingValueError`, a `LucodeError` subclass). If any `when` is still
  waiting on a missing name at program end: error
  `This 'when' never ran: 'hsa_key' has no value yet. Did you mean 'has_key'?`
- Non-true/false condition: error. Nested firing depth > 100: error
  `'when' blocks keep setting each other off: line 4 -> line 2 -> line 4.`
- Parser errors: `when` inside a block; `else` after a `when` block; missing condition.

## Architecture

```
src/Lucode/
  Token.cs        TokenType enum + Token record (Type, Text, Value, Line)
  Lexer.cs        Line-based; emits Indent/Dedent/Newline. Keywords dictionary.
                  Tabs = 4 spaces. Comments with #. String escapes \n \t \" \\
  Ast.cs          Records: LiteralExpr, VariableExpr, UnaryExpr, BinaryExpr,
                  LogicalExpr; AssignStmt, RuleStmt, PrintStmt, IfStmt, WhenStmt
  Parser.cs       Recursive descent. Grammar documented at the top of the file.
                  Precedence: or < and < not < comparison < + - < * / % < unary -
                  "else if" is parsed as an else block containing one IfStmt.
                  An identifier at line start not followed by '=' is
                  "Unknown command 'x'" with a did-you-mean hint.
  Interpreter.cs  Values are C# double / string / bool.
                  _variables dictionary, _rules dictionary, _evaluating list
                  for cycle detection. _watchers (Watcher: WhenStmt, WasTrue,
                  Waiting) and _firing (stack for the loop limit).
  LucodeError.cs  LucodeError(line, message); MissingValueError subclass;
                  Suggest.Hint() uses edit distance
                  (with swapped-letter support) for "Did you mean".
  Program.cs      `lucode file.luc`, --help, --version. Errors go to stderr as
                  "file.luc, Line N: message", exit code 1.
```

Tests: `tests/cases/*.luc` + `*.expected` (stdout+stderr combined; the file is
copied to `test.luc` before running, so error messages say `test.luc`).
Run with `./tests/run.sh` (set `SKIP_BUILD=1` to reuse an existing `build/`).
**45 tests, all passing** at 0.3.0-dev.

No NuGet packages are used. If NuGet is unreachable, restore offline:
`dotnet restore src/Lucode --source <empty dir>`.

## Next steps, in order

1. ~~`when`~~ — **done** (see semantics above).
2. **`why`** (v0.3, then release 0.3.0): `why can_enter` prints the value and which inputs decided
   it, e.g. `can_enter is false, because is_owner is false`. Needs the evaluator
   to record which sub-expressions determined a short-circuit result.
3. **Loops** (v0.4): syntax not chosen yet. Candidates: `repeat 3 times`,
   `while condition`, `for each item in list`.
4. **Commands/functions** (v0.5): syntax not chosen yet.
5. **Tooling:** REPL, VS Code TextMate grammar (syntax highlighting), later an LSP.
6. **Blockliner language pack** (see below).
7. **Later:** multiple rules with the same name = "any of them" (inference),
   lists, text operations, input.

Still-open questions from the original design notes: how types work long-term,
how editor warnings differ from run errors, whether `nor` is worth adding.

## Blockliner integration notes

- Language packs live in `languages/<lang>/` in the Blockliner repo:
  `manifest.json` (`name`, `version`, `author`, `icon`, `comment_token`),
  `concepts.json`, `node_types.json`, and `blocks/*.json`, one per block.
- Block JSON: `concept_id`, `display_name`, `category`, `template` with
  `[[param]]` slots and `[[body]]` for containers, `params` (name/type/default),
  `is_container`, `empty_body`, and `match` (tree-sitter node kinds for
  code-to-blocks sync).
- Blocks a Lucode pack needs: assign_variable, print_output, if_statement,
  elif_statement (`else if`), else_statement, comment, raw_code; later rule,
  when, loops.
- Limits: code-to-blocks sync uses tree-sitter (only Python has a grammar), so
  Lucode would be blocks-to-code only at first. Running code inside the app is
  in `ui_codegen.py` and only supports Python; Lucode would need a branch that
  calls `lucode file.luc`.

## Working preferences of the author

- Batch terminal commands into one block where safe; avoid
  "run this, tell me the result" loops.
- End completed work with `git add` / `git commit` / `git push` commands; never
  push automatically.
- Delivery from a cloud session: changes come as a `git am`-able `.patch`
  built on the repo's current `main`; the author applies, tests and pushes.
- Author uses Fedora Linux, writes short casual messages, and likes being asked
  before big design decisions are made for them.