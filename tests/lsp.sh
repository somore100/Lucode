#!/usr/bin/env bash
# Starts `lucode --lsp`, opens a file with a typo, then fixes it, and checks the diagnostics that come back.
set -u
cd "$(dirname "$0")/.."
if [ -z "${SKIP_BUILD:-}" ]; then
    dotnet build src/Lucode -c Release -o build --nologo -v quiet >/dev/null || exit 1
fi

frame() { printf 'Content-Length: %d\r\n\r\n%s' "${#1}" "$1"; }
out=$({
    frame '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}'
    frame '{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///a.luc","text":"x = 1\nprnt x\n"}}}'
    frame '{"jsonrpc":"2.0","method":"textDocument/didChange","params":{"textDocument":{"uri":"file:///a.luc"},"contentChanges":[{"text":"x = 1\nprint x\n"}]}}'
    frame '{"jsonrpc":"2.0","id":2,"method":"shutdown"}'
    frame '{"jsonrpc":"2.0","method":"exit"}'
} | dotnet build/lucode.dll --lsp)
code=$?

fail=0
check() { if ! grep -qF -- "$2" <<<"$out"; then echo "FAIL: $1 (missing: $2)"; fail=1; fi; }
check "initialize answers"      '"textDocumentSync":1'
check "typo is reported"        "Unknown command 'prnt'. Did you mean 'print'?"
check "typo is on line 2"       '"start":{"line":1,"character":0}'
check "fixed text clears it"    '"diagnostics":[]'
[ "$code" -eq 0 ] || { echo "FAIL: exit code $code, expected 0"; fail=1; }

# Several errors at once, hover and completion.
out=$({
    frame '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}'
    frame '{"jsonrpc":"2.0","method":"textDocument/didOpen","params":{"textDocument":{"uri":"file:///b.luc","text":"prnt 1\nprint 2\nprnt 3\nx = 1\n"}}}'
    frame '{"jsonrpc":"2.0","id":2,"method":"textDocument/hover","params":{"textDocument":{"uri":"file:///b.luc"},"position":{"line":1,"character":2}}}'
    frame '{"jsonrpc":"2.0","id":3,"method":"textDocument/completion","params":{"textDocument":{"uri":"file:///b.luc"},"position":{"line":3,"character":0}}}'
    frame '{"jsonrpc":"2.0","id":4,"method":"shutdown"}'
    frame '{"jsonrpc":"2.0","method":"exit"}'
} | dotnet build/lucode.dll --lsp)
check "first error is reported"   '"start":{"line":0,"character":0}'
check "second error is reported"  '"start":{"line":2,"character":0}'
check "both capabilities"         '"hoverProvider":true'
check "hover explains print"      'Shows a value.'
check "completion has keywords"   '"label":"print"'
check "completion has variables"  '"label":"x"'
[ "$(grep -o "Unknown command 'prnt'" <<<"$out" | wc -l)" -eq 2 ] || { echo "FAIL: expected two typo errors"; fail=1; }

[ "$fail" -eq 0 ] && echo "language server: passed"
exit $fail
