#!/usr/bin/env bash
# Runs every tests/cases/*.luc and compares the output with its .expected file.
set -u
cd "$(dirname "$0")/.."
if [ -z "${SKIP_BUILD:-}" ]; then
    dotnet build src/Lucode -c Release -o build --nologo -v quiet >/dev/null || exit 1
fi

pass=0; fail=0
tmp=$(mktemp -d)
for test in tests/cases/*.luc; do
    name=$(basename "$test" .luc)
    cp "$test" "$tmp/test.luc"
    # A test may bring its own input for 'ask' as name.in; otherwise there is none.
    input=/dev/null
    [ -f "tests/cases/$name.in" ] && input="tests/cases/$name.in"
    actual=$(dotnet build/lucode.dll "$tmp/test.luc" 2>&1 < "$input")
    if [ "$actual" == "$(cat "tests/cases/$name.expected")" ]; then
        pass=$((pass + 1))
    else
        fail=$((fail + 1))
        echo "FAIL: $name"
        diff <(echo "$actual") "tests/cases/$name.expected" | sed 's/^/    /'
    fi
done
rm -rf "$tmp"
echo "$pass passed, $fail failed"
[ "$fail" -eq 0 ]
