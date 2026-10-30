# Lucode — Handoff

Paste or attach this file at the start of a new chat to continue the project.
Last updated: 2026-10-20, version 0.14.0 (text words incl. trim/find/replace, `sort` (+ `descending`) and `ask` on top of lists, `why`, loops, commands, REPL, VS Code grammar + icon).

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
| `why` | `why NAME` only (not expressions). Looks through rules to plain variables; `and` names the false side, `or` the true side, both when neither settled it. Several reasons joined with ` and `. |
| Loops | `repeat N times` (whole number >= 0; `times` is only special there) and `while condition`. Both cap at 1,000,000 runs. `for each` waits for lists. `when` is still checked after every assignment inside loops. |
| Commands | `to name inputs` + indented body; call as `name v1 v2` (no parentheses; arity known from the definition, so define before use; use `(...)` to group). `give value` returns. A command without `give` only works as a line of its own. Inside a command, assignments are local and never touch outer variables or trigger `when`; rules always read top-level values. Recursion capped at 200. `to` is top level only. |
| REPL | `lucode` with no file. Persistent state; block openers read until an empty line; `exit`/`quit`/Ctrl+D. Waiting-`when` typo check is skipped (program never ends). |
| Lists | Word-style, positions start at **1**. `list 1, 2, 3` (`list` alone = empty); `item N of xs`; `count of xs`; `add v to xs`; `remove item N from xs`; `for each x in xs`; `v in xs`. `for` and `list` are keywords; `each`, `in`, `item`, `count`, `of`, `from` are contextual (`item` is only the accessor when followed by a number, `(` or a name other than `of`/`in`; `count` only before `of`). Lists are values (copy on assign; `add`/`remove` build a new list and assign it, so `when` fires and commands stay local). `item/count/for each/in` also work on text. Items inside a `list` literal are full expressions, so `list 1, 2 = list 1, 2` doesn't do what it looks like: use variables. Not yet: changing one item in place, list + list, sorting. |
| Text words | Word-style prefix, same feel as `item 2 of xs`, no parentheses or commas: `upper x`, `lower x`, `trim x`, `number x`, `find a in t` (1-based, 0 = none), `replace a with b in t` (a not empty), `ask "prompt"`, `split text by sep`, `join items with sep`. One `BuiltinExpr(word, args)` node; the parser's `Builtins` table lists the little words between values (`by`, `with`, `in`; `replace` has two). Each slot reads **one unary value**, so `upper a + b` is `(upper a) + b`; use `(...)` for more. All are contextual: only special when a value (number, text, `(`, `list`, true/false or a name other than `of`/`in`) follows, so `upper = 1` and `print upper` still work. `split` with an empty separator gives letters. `join` formats any items. `number` rejects anything that isn't a finite number. |
| Sort | `sort xs` is a statement like `add`/`remove` (assigns a new list, so `when` fires and commands stay local). All numbers (ascending) or all text (case-insensitive, stable); mixed or other kinds is an error. No expression form and `sort xs descending` reverses it. |
| Input | `name = ask "prompt?"`: writes the prompt plus a space (no newline), reads one line from stdin, gives text (use `number ask ...` for math). End of input is an error. `Interpreter` takes an optional `TextReader`. `ask` inside a rule or `when` condition would prompt on every read (not guarded). Test cases can ship `name.in`; `run.sh` feeds it (otherwise `/dev/null`). `read "path"` returns a whole file as text (0.7.2; builtin, relative to the working directory, missing file is an error). `write text to "path"` (0.7.3) replaces the file with exactly that text (non-text values formatted like `print`; no automatic newline; missing folder is an error). Not yet: appending, reading line by line. |
| Same-name rules | 0.9.0. Several `rule x = ...` lines are tried top to bottom (`_rules` maps name -> list). A line that is `false` or hits a `MissingValueError` passes to the next; the first line giving `true` or any non-bool wins. None won: `false` if any line was false, else the first missing-value error. One line behaves exactly as before. Identical lines (same token `Signature`) are an error. `why` says which line won (`..., because is_owner is true (rule on line 4)`), or lists each line's reason when none held. Rules are for any value, not just true/false. |
| Build | 0.10.0, untested until run. `lucode build file.luc [-o out]` (`Bundle.cs`) copies `Environment.ProcessPath` (needs the self-contained single-file lucode, not `dotnet lucode.dll`), appends `name\nsource` + 8-byte length + marker `LUCODEPK`, marks it executable and re-signs on macOS (`codesign -s -`). Parse errors stop the build. At startup `Bundle.ReadEmbedded()` looks for the marker at the end of the running exe and, if found, runs that program and ignores the command line. Same platform only (no cross-building). 0.11.0: `--deb` (`Packages.cs`: pure C# `ar` + ustar `tar` + gzip, installs `/usr/bin/<name>`, deps libc6+libstdc++6, amd64/arm64 from the running lucode; `BuildOptions` carries -o/--version/--maintainer/--description; any new format is a `Func<PackageInfo,string?>` given to `Bundle.BuildPackage`). 0.12.0: `--rpm` (`RpmPackage`: writes a spec and shells out to `rpmbuild`, because a hand-made rpm could not be tested here; `__os_install_post` is disabled so strip never cuts the appended program). 0.13.0: `--appimage` (`AppImagePackage`: builds an AppDir with AppRun, .desktop and the embedded `icon.png` resource, then runs `appimagetool` with `ARCH` and `APPIMAGE_EXTRACT_AND_RUN=1`; CI downloads the unpinned `continuous` tool, pin it if that breaks). 0.14.0: `--installer` (`InstallerPackage`: Windows only; writes an Inno Setup script and runs `ISCC.exe`; per-user install, optional PATH task, Start menu entry running the program in `cmd /k`). Never run on a real Windows machine by me: the `windows` job in `release.yml` builds, installs silently and runs it, so check that job first. Planned order: macOS .app/.dmg, then the same for lucode itself. Source is embedded as text; later steps: bytecode (`.lucc`) so errors are found at build time and it runs faster. `tests/build.sh` (publishes, builds, runs; set `RID` off Linux x64) is part of CI. |
| Independence | Lucode is standalone (CLI, later VS Code). Blockliner is an add-on via a language pack, never a dependency. |

## Rules (v0.2) — exact semantics

```
rule can_enter = has_key and is_owner
```

- The expression is **stored, not evaluated**. It is evaluated each time the
  name is read ("recalculate when read", pull-based).
- Rules can reference rules and can be defined before their inputs exist.
- Errors: assigning to a rule name; making a rule with an existing variable's
  name; defining the exact same rule line twice (a different formula under the same name is now "any of them", see below; 0.9.0)
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
                  LogicalExpr, CallExpr; AssignStmt, RuleStmt, PrintStmt, IfStmt,
                  WhenStmt, WhyStmt, RepeatStmt, WhileStmt, CommandStmt,
                  GiveStmt, ExprStmt
  Parser.cs       Recursive descent. Grammar documented at the top of the file.
                  Precedence: or < and < not < comparison < + - < * / % < unary -
                  "else if" is parsed as an else block containing one IfStmt.
                  An identifier at line start not followed by '=' is
                  "Unknown command 'x'" with a did-you-mean hint (unless x is
                  a command defined earlier: then it's a call). _commands maps
                  name -> input count; the REPL shares one table across lines.
  Interpreter.cs  Values are C# double / string / bool / List<object> (treated as immutable).
                  _variables dictionary, _rules dictionary, _evaluating list
                  for cycle detection. _watchers (Watcher: WhenStmt, WasTrue,
                  Waiting) and _firing (stack for the loop limit).
                  _commands, _locals (null at top level) and _callDepth for
                  commands; GiveSignal exception implements 'give'.
                  Explain/Collect implement 'why'.
  LucodeError.cs  LucodeError(line, message); MissingValueError subclass;
                  Suggest.Hint() uses edit distance
                  (with swapped-letter support) for "Did you mean".
  Packages.cs     .deb writer (ar + tar + gzip); more formats planned.
  Bundle.cs       `lucode build`: standalone program = copy of lucode + embedded source (tests/build.sh).
  LanguageServer.cs  `lucode --lsp`: LSP over stdin/stdout, parse errors only (tests/lsp.sh).
  Repl.cs         Interactive prompt (see Decisions).
  Program.cs      `lucode [file.luc]` (no file = REPL), --help, --version. Errors go to stderr as
                  "file.luc, Line N: message", exit code 1.
editors/vscode/   TextMate grammar + language configuration + icon.png (256px, from logo.jpg) for .luc files.
logo.jpg          project logo, shown at the top of the README.
```

Tests: `tests/cases/*.luc` + `*.expected` (stdout+stderr combined; the file is
copied to `test.luc` before running, so error messages say `test.luc`).
Run with `./tests/run.sh` (set `SKIP_BUILD=1` to reuse an existing `build/`).
`./tests/repl.sh` feeds `tests/repl.in` to the REPL.
**103 case tests passed at 0.7.3** (added `write_file`, `err_write_missing_folder`, `err_write_no_to`). **98 case tests + REPL passed at 0.7.1 (0.7.0 verified).** Earlier: 74 case tests + the REPL test passed at 0.5.0.** 0.6.0 added 8 `list_*` cases (passing, 82 total). 0.7.0 added `text_case`, `text_split_join`, `list_sort`, `input_ask` and 6 `err_*` cases, written without a .NET SDK to hand, so **build and run the suite first**.

No NuGet packages are used. If NuGet is unreachable, restore offline:
`dotnet restore src/Lucode --source <empty dir>`.

## Next steps, in order

1. ~~`when`, `why`, loops, commands, REPL, VS Code grammar~~ — **done**.
2. ~~Lists~~ — **done (0.6.0)**. ~~Text operations, sorting, `ask`~~ — **done (0.7.0)**. Done in 0.7.1: trim, find, replace, `sort ... descending`. Done in 0.7.2: `read`. Done in 0.7.3: `write`. Done in 0.7.3 (untested until first tag): `.github/workflows/release.yml` runs tests on every push and, on a `v*` tag, publishes self-contained single-file builds (linux-x64/arm64, win-x64, osx-x64/arm64) to a GitHub release. Done in 0.8.0 (untested until run): `lucode --lsp` (`LanguageServer.cs`, stdio, full-text sync, one diagnostic = first lexer/parser error, whole line underlined; `tests/lsp.sh`; VS Code client `editors/vscode/extension.js` + `npm install`). Next: Blockliner pack, then richer LSP (all errors, undefined names, never-ran `when`, hover, completion).
3. **Tooling:** basic LSP done (0.8.0); the grammar currently can't colour calls like `add 2 3`.
4. **Blockliner language pack** (see below).
5. ~~Multiple rules with the same name = "any of them"~~ — **done (0.9.0)**, untested until run.

Ideas not decided: bare expressions echoing their value in the REPL; letting
commands read/write top-level variables (raises the question of `when` firing
from inside a command).

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