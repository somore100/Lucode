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
curl -fsSL "https://github.com/$repo/releases/download/$tag/$name.tar.gz" | tar xz -C "$tmp"

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
