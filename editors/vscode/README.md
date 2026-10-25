# Lucode for VS Code

Syntax highlighting, comment toggling (`#`), auto-closing quotes/parentheses,
auto-indent and folding for `.luc` files, plus **errors while you type**: the
extension starts `lucode --lsp` (a small language server built into Lucode) and
underlines the first line the parser can't read, with the same message
`lucode file.luc` would give. Install Lucode first and make sure `lucode` is on
your PATH (or set `lucode.path` in the VS Code settings). Errors that only show
up while the program runs are not shown.

## Try it

Copy or symlink this folder into your VS Code extensions folder, then restart VS Code:

```bash
cd editors/vscode && npm install   # fetches vscode-languageclient
ln -s "$PWD" ~/.vscode/extensions/somore100.lucode-0.3.0
```

Or press F5 inside this folder in VS Code to open a test window.
