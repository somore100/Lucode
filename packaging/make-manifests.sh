#!/usr/bin/env bash
# Makes the Homebrew formula and the winget manifests for a release.
#   packaging/make-manifests.sh v0.16.1
# Output goes to dist/. Copy dist/homebrew/lucode.rb into your homebrew-lucode tap (Formula/lucode.rb);
# submit dist/winget/* to github.com/microsoft/winget-pkgs (for example with `wingetcreate` or `komac`).
set -euo pipefail
tag=${1:?usage: make-manifests.sh vX.Y.Z}
version=${tag#v}
repo=somore100/Lucode
base="https://github.com/$repo/releases/download/$tag"
cd "$(dirname "$0")/.."
mkdir -p dist/homebrew dist/winget

sha() { curl -fsSL "$1" | { sha256sum 2>/dev/null || shasum -a 256; } | cut -d' ' -f1; }
archive() { echo "$base/lucode-$tag-$1.tar.gz"; }

osx_arm=$(sha "$(archive osx-arm64)");   osx_x64=$(sha "$(archive osx-x64)")
lin_arm=$(sha "$(archive linux-arm64)"); lin_x64=$(sha "$(archive linux-x64)")
setup_url="$base/lucode-$version-setup.exe"
setup=$(sha "$setup_url")

cat > dist/homebrew/lucode.rb <<RUBY
class Lucode < Formula
  desc "Small, readable programming language for logic, math and live relationships"
  homepage "https://github.com/$repo"
  version "$version"
  license "MIT"

  on_macos do
    on_arm do
      url "$(archive osx-arm64)"
      sha256 "$osx_arm"
    end
    on_intel do
      url "$(archive osx-x64)"
      sha256 "$osx_x64"
    end
  end

  on_linux do
    on_arm do
      url "$(archive linux-arm64)"
      sha256 "$lin_arm"
    end
    on_intel do
      url "$(archive linux-x64)"
      sha256 "$lin_x64"
    end
  end

  def install
    bin.install "lucode"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/lucode --version")
  end
end
RUBY

id=somore100.Lucode
cat > "dist/winget/$id.yaml" <<YAML
PackageIdentifier: $id
PackageVersion: $version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.6.0
YAML
cat > "dist/winget/$id.locale.en-US.yaml" <<YAML
PackageIdentifier: $id
PackageVersion: $version
PackageLocale: en-US
Publisher: somore100
PackageName: Lucode
PackageUrl: https://github.com/$repo
License: MIT
ShortDescription: A small, readable programming language for logic, math and live relationships.
ManifestType: defaultLocale
ManifestVersion: 1.6.0
YAML
cat > "dist/winget/$id.installer.yaml" <<YAML
PackageIdentifier: $id
PackageVersion: $version
InstallerType: inno
Scope: user
InstallModes:
  - interactive
  - silent
  - silentWithProgress
Installers:
  - Architecture: x64
    InstallerUrl: $setup_url
    InstallerSha256: ${setup^^}
ManifestType: installer
ManifestVersion: 1.6.0
YAML
echo "Wrote dist/homebrew/lucode.rb and dist/winget/ for $tag"
