# Lucode

**A small, readable programming language for logic, math and live relationships.**
Code should say what you mean.

```
has_key = false
is_owner = true

rule can_enter = has_key and is_owner

print "Before the key: " + can_enter     # false

has_key = true
print "After the key: " + can_enter      # true, recalculated automatically

if can_enter
    print "Access granted"
else
    print "Access denied"
```

> **Status:** early development, **v0.3 in progress**. The language works and is tested, but
> syntax may still change before 1.0.

## Why Lucode?

Most languages calculate a value once and forget how it was made. Lucode lets
you write a **rule**: a formula that stays live and is recalculated every time
it's used. Describe a relationship once, and Lucode keeps the answer current.

Everything else is kept deliberately plain:

- **Readable**: `if money > price`, `not is_owner`, `a is not b`
- **No clutter**: no semicolons, no braces, no colons; indentation makes blocks
- **One `=`**: assigns at the start of a line, compares inside a condition
- **Helpful errors**: `Line 5: Unknown command 'prnt'. Did you mean 'print'?`
- **Small on purpose**: a feature is added only when it solves a real problem

## Getting started

Lucode runs on Linux, Windows and macOS. You need the **.NET 8 SDK**:

| System  | Install                                                        |
|---------|----------------------------------------------------------------|
| Fedora  | `sudo dnf install dotnet-sdk-8.0`                              |
| Ubuntu  | `sudo apt install dotnet-sdk-8.0`                              |
| Windows / macOS | [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download) |

Then run a program:

```
git clone https://github.com/somore100/Lucode.git
cd Lucode
dotnet run --project src/Lucode -- examples/rules.luc
```

Lucode files use the `.luc` extension. To build once and run faster afterwards:

```
dotnet build src/Lucode -c Release -o build
dotnet build/lucode.dll examples/shop.luc
```

## The language

### Values

| Kind       | Examples              |
|------------|-----------------------|
| Number     | `42`, `3.5`, `-7`     |
| Text       | `"hello"`, `"a\nb"`   |
| True/false | `true`, `false`       |

### Variables

```
money = 1000
name = "Lu"
money = money - 250
```

A variable is calculated once, when its line runs.

### Rules

A rule is a formula that is recalculated **every time it's used**:

```
price = 1200
discount = 0

rule final_price = price - discount
rule can_buy = money >= final_price

discount = 400        # final_price and can_buy follow automatically
```

- Rules can use other rules, and can be written before the values they use exist.
- A rule can't be set with `=`; change the values it depends on instead.
- A name is either a variable or a rule, never both.
- A rule that depends on itself (`a` uses `b`, `b` uses `a`) is reported as an error.

**Variable or rule?** Use a variable for things that change step by step
(counters, totals, scores). Use a rule for things that are *defined by* other
values (permissions, prices after discount, "is the machine ready").

### Reactions: `when`

`when` runs a block **the moment its condition becomes true**:

```
rule can_enter = has_key and is_owner

when can_enter
    print "Door opened"

has_key = true        # prints "Door opened" right here
```

- It fires on a change from false to true, not again while it stays true.
  Once the condition goes false, the next change to true fires it again.
- Conditions are checked after every assignment. All `when`s look at the
  same moment first, then the ones that became true run, in the order they
  were written.
- A `when` whose condition is already true when it's written runs immediately.
- The condition can use names that don't have a value yet; it just isn't true
  until they do. If a name *never* gets a value (usually a typo), that's
  reported when the program ends.
- A `when` block can change values and set off other `when`s. If they keep
  setting each other off (100 in a row), Lucode stops with an error showing
  the loop.
- `when` goes at the top level of the file, and has no `else`
  (write a second `when` with the opposite condition).

### Output

```
print "Hello"
print "Money left: " + money
```

`+` with text on either side joins the values into text.

### Arithmetic

`+  -  *  /  %` (remainder), with normal math order and parentheses:

```
print 2 + 3 * 4     # 14
print (2 + 3) * 4   # 20
print 0.1 + 0.2     # 0.3
```

### Comparisons

Several spellings mean the same thing, so you can write what reads best:

| Meaning        | You can write                          |
|----------------|----------------------------------------|
| equal          | `a = b`, `a is b`                      |
| not equal      | `a != b`, `a is not b`, `not a = b`    |
| less / greater | `a < b`, `a <= b`, `a > b`, `a >= b`   |

Comparisons don't chain: write `a < b and b < c`.

### Logic

`and`, `or`, `not`. `not` covers the whole comparison after it, so
`not money = 50` means "money is not 50".

### Conditions

```
if score >= 90
    print "A"
else if score >= 70
    print "B"
else
    print "C"
```

Blocks are marked by indentation (spaces, or tabs counted as 4).

### Other details

- `#` starts a comment.
- Keywords ignore case (`IF`, `Print`, `NOT` all work); names don't.
- Errors stop the program and give the line number.

## Roadmap

### Done

- [x] **v0.1 Core:** variables, numbers, text, true/false, arithmetic,
      comparisons, `and`/`or`/`not`, `if`/`else if`/`else`, `print`, comments,
      line-numbered errors with suggestions
- [x] **v0.2 Rules:** live formulas with `rule`, rule chains, loop detection
- [x] **v0.3 Reactions:** `when` runs a block the moment a condition becomes true

### Next

- [ ] **v0.3 Explanations:** `why` shows which values decided a rule
      (`can_enter is false, because is_owner is false`)
- [ ] **v0.4 Repetition:** loops, such as `repeat 3 times` and `while ...`
- [ ] **v0.5 Commands:** user-defined commands (functions) with inputs

### Tooling

- [ ] Interactive mode: type Lucode and see results line by line
- [ ] VS Code extension: syntax highlighting for `.luc` files
- [ ] Language server: errors shown while typing, in any editor that supports LSP
- [ ] Prebuilt downloads for Linux, Windows and macOS (no .NET install needed)

### Visual programming

- [ ] Lucode language pack for [Blockliner](https://github.com/somore100/Blockliner),
      so Lucode programs can be built from blocks and nodes.
      Lucode stays a standalone, text-first language; Blockliner is one way to edit it.

### Later ideas

- [ ] Several rules with the same name meaning "any of these is enough"
      (rule-based reasoning)
- [ ] Text and list operations
- [ ] Reading input from the user and from files

## Project layout

```
src/Lucode/
  Token.cs         token types
  Lexer.cs         source text -> tokens (including indentation)
  Ast.cs           the program tree
  Parser.cs        tokens -> tree (the grammar is documented at the top)
  Interpreter.cs   runs the tree, keeps variables, rules and when-blocks
  LucodeError.cs   errors with line numbers, "did you mean" suggestions
  Program.cs       command-line entry point
examples/          sample .luc programs
tests/cases/       test programs (.luc) with expected output (.expected)
tests/run.sh       runs all tests
docs/HANDOFF.md    design decisions and current state, for contributors
```

## Running the tests

```
./tests/run.sh
```

Each `tests/cases/name.luc` is run and its output compared with
`name.expected`. To add a test, add both files.

## Contributing

Ideas, bug reports and example programs are welcome; open an
[issue](https://github.com/somore100/Lucode/issues). Lucode is young, so
syntax suggestions are especially useful now, before 1.0.
