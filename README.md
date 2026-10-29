<p align="center">
  <img src="logo.jpg" alt="Lucode logo" width="160">
</p>

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

> **Status:** early development, **v0.7**. The language works and is tested, but
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

Lucode runs on Linux, Windows and macOS. The easy way: download your system's build from the
[Releases page](https://github.com/somore100/Lucode/releases), unpack it and run `lucode file.luc`
(no .NET needed). To build from source you need the **.NET 8 SDK**:

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

**Several rules with one name** mean "any of these will do". They are tried
top to bottom: a line that is false, or that needs a value that doesn't exist
yet, hands over to the next one. The first line that gives something else wins.

```
rule can_enter = has_key
rule can_enter = is_owner        # true if either one is true

rule price = base - discount     # no discount yet? ...
rule price = base                # ... then this line gives the price
```

If no line wins, true/false rules are `false`; otherwise you get the usual
"has no value yet" error. Writing the very same line twice is an error (that is
almost always a copy-paste slip), and `why` says which line won.

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

### Explanations: `why`

`why` shows a value and which inputs decided it:

```
has_key = true
is_owner = false
rule can_enter = has_key and is_owner

why can_enter     # can_enter is false, because is_owner is false
```

- It looks through rules down to plain variables.
- With `and`, only the side that was false is named; with `or`, only the side
  that was true. If neither side could settle it, both are named.
- `why` takes a single name. For a plain variable it just shows the value.

### Loops: `repeat` and `while`

```
total = 0
repeat 3 times
    total = total + 2      # total ends at 6

n = 1
while n < 100
    n = n * 2              # n ends at 128
```

- `repeat` needs a whole number of 0 or more.
- `while` checks its condition before every run, so it may not run at all.
- A loop that runs 1,000,000 times in a row stops with an error.
- `when` conditions are still checked after every assignment, including
  inside loops.

### Lists

```
scores = list 90, 75, 82
print item 1 of scores          # 90 (positions start at 1)
print count of scores           # 3
add 60 to scores
remove item 2 from scores
print scores                    # list 90, 82, 60

for each score in scores
    print score

if 82 in scores
    print "found"
```

- `list` followed by comma-separated values makes a list; `list` alone is empty.
  Items can be any value, including other lists.
- `item N of`, `count of`, `for each` and `in` also work on text (one letter at a time).
- Lists are values, like numbers: `b = a` copies the list, and `add` / `remove`
  give the variable a new list, so `when` blocks notice the change.
- Asking for a position that isn't there is an error (`Positions start at 1`).
- `item`, `count`, `of`, `each`, `in` and `from` are only special in these
  phrases, so they still work as names (`for each item in items`).
- `for` and `list` are keywords.

### Commands: `to` and `give`

```
to add a b
    give a + b

to greet name
    print "Hello, " + name

print add 2 3          # 5
print add (add 1 2) 4  # 7
greet "Sam"            # Hello, Sam
```

- `to name inputs` defines a command; call it by writing its name followed by
  one value per input. No parentheses or commas. Wrap a bigger expression in
  parentheses: `add (x + 1) 2`.
- `give` ends the command and hands back a value. A command that never gives
  can only be used as a line of its own.
- Define a command above the place it's used. A command may call itself.
- Inside a command, inputs and new variables are local, and assigning never
  changes the outside. Rules always look at the top-level values.
- `to` goes at the top level of the file.

### Text

Words that work on text. Like `item 1 of xs`, they read as plain English
and take their values straight after the word, with no parentheses or commas:

```
name = "Lu Code"
print upper name                       # LU CODE
print lower name                       # lu code

parts = split "red,green,blue" by ","  # list "red", "green", "blue"
print join parts with " + "            # red + green + blue
print split "abc" by ""                # list "a", "b", "c"

n = number "42"                        # text to a number

print trim "  hi  "                    # hi
print find "or" in "world"             # 2 (0 when it isn't there)
print replace "a" with "o" in "banana" # bonono
```

- `upper` and `lower` need text. `split text by separator` makes a list of
  text (an empty separator gives single letters). `join items with separator`
  makes text out of any list.
- `trim` removes spaces at both ends. `find a in text` gives the position of
  the first match, counting from 1, or 0 if there is none. `replace a with b in
  text` swaps every `a` for `b` (`a` can't be empty).
- `number` turns text like `"42"` or `"3.5"` into a number, and is an error for
  anything else.
- Wrap a bigger expression in parentheses: `upper (first + last)`. Without them
  the word takes just the next value: `upper a + b` is `(upper a) + b`.
- `upper`, `lower`, `trim`, `number`, `split`, `join`, `find`, `replace` (and `ask`, `sort`, `write`) are only special
  when a value follows, so they still work as names.

### Sorting

```
scores = list 30, 5, 100, 12
sort scores
print scores                  # list 5, 12, 30, 100
```

`sort` puts a list in order: smallest first for numbers, A to Z for text
(ignoring capital letters). A list that mixes numbers and text is an error.
Add `descending` for biggest first or Z to A: `sort scores descending`.
Like `add` and `remove`, it changes the variable, so `b = a` first if you want
to keep the original.

### Input: `ask`

```
name = ask "What is your name?"
print "Hello, " + name

age = number ask "Age?"
print "Next year: " + (age + 1)
```

`ask` shows its prompt, waits for a line and gives it back as text, so use
`number` when you need math. It's an error if there is no more input, for
example when input comes from a file that ran out.

### Files: `read`

```
data = read "scores.csv"
rows = split (trim data) by ","
print count of rows
```

`read path` gives the whole file as text (the path is relative to where you run
`lucode`). Combine it with `split text by ","` to turn it into a list. A file
that doesn't exist is an error.

### Files: `write`

```
write "hello" to "greeting.txt"
write join names with "\n" to "names.txt"
```

`write text to path` puts exactly that text in the file, replacing whatever was
there (add `"\n"` yourself if you want a line break at the end). Numbers and
lists are turned into text the same way `print` does. A folder that doesn't
exist is an error.

### Making a program: `lucode build`

```
lucode build hello.luc            # makes ./hello (hello.exe on Windows)
lucode build hello.luc -o greet   # pick the name
```

Add `--rpm` for Fedora (`sudo dnf install ./hello-1.0-1.x86_64.rpm`; needs `sudo dnf install rpm-build` once)
or `--deb` for a Linux package people can install with `sudo apt install ./hello_1.0_amd64.deb`
(it puts the program in `/usr/bin`). You can set `--version`, `--maintainer` and `--description`.
A `.deb` has to be made on Linux.

The plain result is one file that runs by itself on the same kind of computer (same
system and chip) and needs no Lucode or .NET. It is a copy of `lucode` with
your program inside, so rules, `when` and `why` work exactly as before. The
program is checked first: if it has an error, nothing is built. Use the
downloaded `lucode` from the Releases page; `dotnet lucode.dll build` can't do
this. To make programs for another system, build on that system.

### Trying things out: the prompt

Run `lucode` with no file to get an interactive prompt. Everything you define
stays around for the next line. A line that opens a block (`if`, `when`,
`repeat`, `while`, `for`, `to`) keeps reading lines until you enter an empty one.
Errors are shown and the session carries on. Type `exit` to leave.

```
>>> x = 2
>>> print x * 3
6
```

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
- [x] **v0.3 Explanations:** `why` shows which values decided a rule
      (`can_enter is false, because is_owner is false`)

### Next

- [x] **v0.4 Repetition:** `repeat 3 times` and `while ...` (`for each` waits for lists)
- [x] **v0.5 Commands:** user-defined commands (functions) with inputs
- [x] **v0.6 Lists:** `list 1, 2, 3`, `item N of`, `count of`, `add` / `remove`, `for each`, `in`
- [x] **v0.7 Text and input:** `upper`, `lower`, `split`, `join`, `number`, `sort`, `ask`

### Tooling

- [x] Interactive mode: type Lucode and see results line by line
- [x] VS Code extension: syntax highlighting for `.luc` files (`editors/vscode`, with icon)
- [x] Language server: `lucode --lsp` shows the first parse error while typing, in any editor that supports LSP (VS Code client in `editors/vscode`). Not yet: run-time/static warnings, hover, completion
- [x] Prebuilt downloads for Linux, Windows and macOS (no .NET install needed): pushing a `v*` tag builds them on GitHub (see Releases)
- [x] `lucode build`: turn a program into one standalone file (copy of lucode + your program)
- [x] `lucode build --deb` / `--rpm`: Debian/Ubuntu and Fedora packages (0.11.0 / 0.12.0)
- [ ] More package formats: AppImage, then Windows installer, then macOS `.app`/`.dmg`; later the same for lucode itself
- [ ] Bytecode (`.lucc`): errors found at build time and faster runs

### Visual programming

- [ ] Lucode language pack for [Blockliner](https://github.com/somore100/Blockliner),
      so Lucode programs can be built from blocks and nodes.
      Lucode stays a standalone, text-first language; Blockliner is one way to edit it.

### Later ideas

- [x] Several rules with the same name meaning "any of these is enough" (0.9.0)
      (rule-based reasoning)
- [ ] Writing files

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
editors/vscode/    VS Code extension: highlighting and icon for .luc files
logo.jpg           the Lucode logo
docs/HANDOFF.md    design decisions and current state, for contributors
```

## Running the tests

```
./tests/run.sh
```

Each `tests/cases/name.luc` is run and its output compared with
`name.expected`. To add a test, add both files. A test that uses `ask` can
add `name.in`, which is fed to it as input.

## Contributing

Ideas, bug reports and example programs are welcome; open an
[issue](https://github.com/somore100/Lucode/issues). Lucode is young, so
syntax suggestions are especially useful now, before 1.0.

## License

[MIT](LICENSE)
