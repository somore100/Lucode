#!/usr/bin/env bash
# Publishes the standalone lucode, builds a program with `lucode build`, and runs it from another folder.
# Set RID for other systems than Linux x64 (for example RID=osx-arm64).
set -u
cd "$(dirname "$0")/.."
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

dotnet publish src/Lucode -c Release -r "${RID:-linux-x64}" --self-contained -p:PublishSingleFile=true \
    -p:InvariantGlobalization=true -o "$tmp/pub" --nologo -v quiet >/dev/null || { echo "FAIL: publish"; exit 1; }
lucode="$tmp/pub/lucode"

cat > "$tmp/hello.luc" <<'LUC'
name = ask "Name?"
rule greeting = "Hello, " + name
print greeting
LUC
printf 'prnt 1\n' > "$tmp/broken.luc"

fail=0
"$lucode" build "$tmp/hello.luc" -o "$tmp/hello" >/dev/null || { echo "FAIL: build"; fail=1; }
actual=$(cd / && echo Sam | "$tmp/hello" 2>&1)
[ "$actual" == "Name? Hello, Sam" ] || { echo "FAIL: built program printed: $actual"; fail=1; }

actual=$("$lucode" build "$tmp/broken.luc" -o "$tmp/broken" 2>&1)
[ "$actual" == "broken.luc, Line 1: Unknown command 'prnt'. Did you mean 'print'?" ] || { echo "FAIL: broken message: $actual"; fail=1; }
[ ! -e "$tmp/broken" ] || { echo "FAIL: a broken program was built"; fail=1; }

# The plain lucode still runs files.
actual=$("$lucode" "$tmp/hello.luc" <<< "Ann" 2>&1)
[ "$actual" == "Name? Hello, Ann" ] || { echo "FAIL: plain run printed: $actual"; fail=1; }

[ "$fail" -eq 0 ] && echo "build: passed"
exit $fail
