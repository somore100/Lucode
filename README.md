# Lucode

A small, readable programming language built around logic and math.
**Code should say what you mean.**

```
money = 1000
price = 250 * 2

if money > price
    print "You can buy it"
    money = money - price
else
    print "Not enough money"

print "Money left: " + money
```

Status: **v0.2** (variables, rules, numbers, text, arithmetic, comparisons, conditions, output).
The interpreter is written in C# (.NET 8).

## Running

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download).

```
dotnet run --project src/Lucode -- examples/shop.luc
```

Or build once and run the result:

```
dotnet build src/Lucode -c Release -o build
dotnet build/lucode.dll examples/shop.luc
```

Run the tests:

```
./tests/run.sh
```

## The language

Lucode files use the `.luc` extension. One statement per line.

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
```

A line that starts with a name and `=` assigns a value. Using a variable before
it has a value is an error.

### Rules

A variable is calculated once, when its line runs. A **rule** is a formula that
is recalculated every time it's used, so it always reflects the current values.

```
has_key = false
is_owner = true

rule can_enter = has_key and is_owner

print can_enter     # false
has_key = true
print can_enter     # true: no need to recalculate by hand
```

- Rules can use other rules, and can be defined before the values they use exist.
- A rule can't be set with `=`; change the values it depends on instead.
- A name is either a variable or a rule, not both.
- A rule that depends on itself (`a` uses `b`, `b` uses `a`) is an error.

### Output

```
print "Hello"
print money
print "Money: " + money
```

`+` with text on either side joins the values into text.

### Arithmetic

`+  -  *  /  %` (remainder), with normal math precedence and parentheses:

```
print 2 + 3 * 4     # 14
print (2 + 3) * 4   # 20
```

### Comparisons

Lucode uses a **single `=`**: at the start of a line it assigns, inside a
condition or value it compares. Several spellings mean the same thing:

| Meaning          | You can write                                 |
|------------------|-----------------------------------------------|
| equal            | `a = b`, `a is b`                             |
| not equal        | `a != b`, `a is not b`, `not a = b`           |
| less / greater   | `a < b`, `a <= b`, `a > b`, `a >= b`          |

A comparison can be stored: `rich = money is 1000000`.
Comparisons don't chain; write `a < b and b < c`.

### Logic

`and`, `or`, `not`. `not` applies to the whole comparison after it,
so `not money = 50` means "money is not 50".

### Conditions

Blocks are marked by indentation (spaces, or tabs counted as 4).

```
if score >= 90
    print "A"
else if score >= 70
    print "B"
else
    print "C"
```

Keywords are not case-sensitive (`IF`, `Print`, `NOT` all work). Variable
names are case-sensitive.

### Comments

`#` starts a comment that runs to the end of the line.

### Errors

Errors stop the program and name the line:

```
error.luc, Line 5: Unknown command 'prnt'. Did you mean 'print'?
```

## Project layout

```
src/Lucode/
  Token.cs        token types
  Lexer.cs        source text → tokens (including indentation)
  Ast.cs          the program tree
  Parser.cs       tokens → tree (grammar is documented at the top)
  Interpreter.cs  runs the tree
  Program.cs      command-line entry point
examples/         sample .luc programs
tests/            test cases (.luc + .expected) and run.sh
```

## Roadmap

- **Next:** `when` (run code the moment a rule changes), loops, custom commands/functions
- **Stage 3:** better errors, more examples, docs, tests
- **Stage 4:** releases and downloadable builds
- **Stage 5:** editor support, syntax highlighting, visual mode
