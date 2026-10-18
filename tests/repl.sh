#!/usr/bin/env bash
# Feeds tests/repl.in to the REPL and compares the output with tests/repl.expected.
set -u
cd "$(dirname "$0")/.."
actual=$(dotnet build/lucode.dll < tests/repl.in 2>&1)
if [ "$actual" == "$(cat tests/repl.expected)" ]; then
    echo "repl: passed"
else
    echo "repl: FAILED"
    diff <(echo "$actual") tests/repl.expected | sed 's/^/    /'
    exit 1
fi
