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

# A .deb: check its parts, unpack the program from it and run it. dpkg-deb, when there, must accept it too.
if command -v ar >/dev/null; then
    "$lucode" build "$tmp/hello.luc" --deb -o "$tmp/hello.deb" --version 2.1 --description "Says hello" >/dev/null \
        || { echo "FAIL: deb build"; fail=1; }
    members=$(ar t "$tmp/hello.deb" | tr '\n' ' ')
    [ "$members" == "debian-binary control.tar.gz data.tar.gz " ] || { echo "FAIL: deb members: $members"; fail=1; }
    control=$(ar p "$tmp/hello.deb" control.tar.gz | tar xzO ./control)
    for line in "Package: hello" "Version: 2.1" "Description: Says hello"; do
        grep -qF "$line" <<<"$control" || { echo "FAIL: deb control lacks: $line"; fail=1; }
    done
    mkdir "$tmp/root"
    ar p "$tmp/hello.deb" data.tar.gz | tar xz -C "$tmp/root"
    actual=$(echo Eve | "$tmp/root/usr/bin/hello" 2>&1)
    [ "$actual" == "Name? Hello, Eve" ] || { echo "FAIL: program from deb printed: $actual"; fail=1; }
    if command -v dpkg-deb >/dev/null; then
        dpkg-deb --info "$tmp/hello.deb" >/dev/null && dpkg-deb --contents "$tmp/hello.deb" >/dev/null \
            || { echo "FAIL: dpkg-deb rejects the package"; fail=1; }
    fi
else
    echo "note: 'ar' not found, skipped the .deb check"
fi

# An .rpm needs rpmbuild; check it with rpm itself and run the program unpacked from it.
if command -v rpmbuild >/dev/null && command -v rpm >/dev/null && command -v rpm2cpio >/dev/null && command -v cpio >/dev/null; then
    "$lucode" build "$tmp/hello.luc" --rpm -o "$tmp/hello.rpm" --version 2.1 --description "Says hello" >/dev/null \
        || { echo "FAIL: rpm build"; fail=1; }
    info=$(rpm -qp --queryformat '%{NAME} %{VERSION} %{SUMMARY}' "$tmp/hello.rpm" 2>&1)
    [ "$info" == "hello 2.1 Says hello" ] || { echo "FAIL: rpm info: $info"; fail=1; }
    files=$(rpm -qpl "$tmp/hello.rpm" 2>&1)
    [ "$files" == "/usr/bin/hello" ] || { echo "FAIL: rpm files: $files"; fail=1; }
    mkdir "$tmp/rpmroot"
    (cd "$tmp/rpmroot" && rpm2cpio "$tmp/hello.rpm" | cpio -idm --quiet)
    actual=$(echo Eve | "$tmp/rpmroot/usr/bin/hello" 2>&1)
    [ "$actual" == "Name? Hello, Eve" ] || { echo "FAIL: program from rpm printed: $actual"; fail=1; }
else
    echo "note: rpmbuild/rpm2cpio/cpio not all found, skipped the .rpm check"
fi

# An AppImage needs appimagetool; run the result without FUSE.
if command -v appimagetool >/dev/null; then
    "$lucode" build "$tmp/hello.luc" --appimage -o "$tmp/hello.AppImage" >/dev/null \
        || { echo "FAIL: appimage build"; fail=1; }
    actual=$(echo Eve | APPIMAGE_EXTRACT_AND_RUN=1 "$tmp/hello.AppImage" 2>&1)
    [ "$actual" == "Name? Hello, Eve" ] || { echo "FAIL: AppImage printed: $actual"; fail=1; }
else
    echo "note: appimagetool not found, skipped the AppImage check"
fi

[ "$fail" -eq 0 ] && echo "build: passed"
exit $fail
