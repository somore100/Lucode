#!/bin/sh
# Installs the latest Lucode for your user (no sudo needed):
#   curl -fsSL https://raw.githubusercontent.com/somore100/Lucode/main/install.sh | sh
# Options (environment variables):
#   LUCODE_VERSION      a version such as 0.16.1 instead of the latest
#   LUCODE_INSTALL_DIR  where to put it (default: ~/.local/bin)
set -eu

repo=somore100/Lucode

case "$(uname -s)" in
    Linux) os=linux ;;
    Darwin) os=osx ;;
    *) echo "Unsupported system: $(uname -s). On Windows, use the setup program from https://github.com/$repo/releases" >&2; exit 1 ;;
esac
case "$(uname -m)" in
    x86_64 | amd64) arch=x64 ;;
    aarch64 | arm64) arch=arm64 ;;
    *) echo "Unsupported processor: $(uname -m)" >&2; exit 1 ;;
esac
rid="$os-$arch"

if [ -n "${LUCODE_VERSION:-}" ]; then
    tag="v${LUCODE_VERSION#v}"
else
    # /releases/latest redirects to /releases/tag/<tag>
    tag=$(curl -fsSLI -o /dev/null -w '%{url_effective}' "https://github.com/$repo/releases/latest")
    tag=${tag##*/}
fi
case "$tag" in
    v[0-9]*) ;;
    *) echo "Couldn't find the latest Lucode version." >&2; exit 1 ;;
esac

dir=${LUCODE_INSTALL_DIR:-$HOME/.local/bin}
name="lucode-$tag-$rid"
tmp=$(mktemp -d)
trap 'rm -rf "$tmp"' EXIT

echo "Downloading Lucode $tag for $rid..."
base="https://github.com/$repo/releases/download/$tag"
curl -fsSL -o "$tmp/$name.tar.gz" "$base/$name.tar.gz"

# Releases publish SHA256SUMS; check the download against it (older releases have none).
if curl -fsSL -o "$tmp/SHA256SUMS" "$base/SHA256SUMS" 2>/dev/null; then
    expected=$(grep "  $name.tar.gz\$" "$tmp/SHA256SUMS" | cut -d' ' -f1)
    if command -v sha256sum >/dev/null 2>&1; then actual=$(sha256sum "$tmp/$name.tar.gz" | cut -d' ' -f1)
    elif command -v shasum >/dev/null 2>&1; then actual=$(shasum -a 256 "$tmp/$name.tar.gz" | cut -d' ' -f1)
    else actual=""; fi
    if [ -z "$expected" ] || [ -z "$actual" ]; then
        echo "Couldn't check the download against SHA256SUMS; stopping to be safe." >&2; exit 1
    fi
    if [ "$expected" != "$actual" ]; then
        echo "The download does not match SHA256SUMS (expected $expected, got $actual). Not installing." >&2; exit 1
    fi
    echo "Checksum OK."
else
    echo "Note: this release has no SHA256SUMS, so the download was not checked." >&2
fi
tar xzf "$tmp/$name.tar.gz" -C "$tmp"

mkdir -p "$dir"
install -m 755 "$tmp/$name/lucode" "$dir/lucode"
# macOS only runs signed programs; sign the copy on this Mac (ad hoc, no account needed).
if [ "$os" = osx ] && command -v codesign >/dev/null 2>&1; then
    codesign --force --sign - "$dir/lucode" >/dev/null 2>&1 || true
fi

echo "Installed: $("$dir/lucode" --version) in $dir"
case ":$PATH:" in
    *":$dir:"*) ;;
    *) echo "Note: $dir is not on your PATH yet. Add this to your shell profile:  export PATH=\"$dir:\$PATH\"" ;;
esac
