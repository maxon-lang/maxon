#!/usr/bin/env bash
#
# Generate the winget manifests for a built MSI, into dist/winget/.
#
# ⛔ GENERATED, NOT COMMITTED, AND THAT IS THE POINT. A winget manifest carries the installer's SHA256
# and its `ProductCode` — and WiX mints a fresh `ProductCode` on every build, so a manifest checked in
# beside the source would be stale the first time anyone rebuilt the MSI. Nothing here is written down
# twice: the version, the hash and both GUIDs are read OUT of the artifact being published.
#
# ⚠ THE PACKAGE IDENTIFIER IS `MaxonLang.Maxon`, NOT `Maxon.Maxon`. `manifests/m/Maxon/` in
# winget-pkgs is Maxon Computer GmbH's publisher namespace — Cinema 4D, ZBrush, Cinebench — and a
# package added into another publisher's namespace is rejected on review. `maxon` remains the command
# people type, which is what the `Commands` entry below is for.
#
# Usage:
#   installer/winget/generate.sh [<version>]
#
# With no argument it takes the single MSI in dist/.

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$repo_root"
. scripts/lib/msi.sh

DIST="dist"
OUT="$DIST/winget"
REPO="maxon-lang/maxon"
PACKAGE="MaxonLang.Maxon"
MANIFEST_VERSION="1.6.0"

msi="$(find "$DIST" -maxdepth 1 -type f -name 'maxon-*-x64.msi' | head -n1 || true)"
[ -n "$msi" ] || { echo "winget: no MSI in $DIST — run installer/windows/build.sh first" >&2; exit 1; }

base="$(basename "$msi" .msi)"
version="${1:-}"
if [ -z "$version" ]; then
	version="${base#maxon-}"
	version="${version%-x64}"
fi

command -v wix >/dev/null 2>&1 || { echo "winget: WiX is needed to read the MSI's ProductCode" >&2; exit 1; }

sha="$(sha256sum "$msi" | cut -d' ' -f1 | tr 'a-f' 'A-F')"

# ⛔ BOTH GUIDS ARE READ OUT OF THE MSI. `ProductCode` is how winget recognises an existing install and
# `UpgradeCode` is how it recognises the product across versions; a manifest naming either wrongly
# reports the package as not-installed forever, and offers an upgrade that installs a second copy.
decompiled="$(mktemp -d)/decompiled.wxs"
wix msi decompile "$msi" -o "$decompiled" >/dev/null 2>&1 \
	|| { echo "winget: could not read $msi" >&2; exit 1; }
product_code="$(grep -oE 'ProductCode="\{[^}]+\}"' "$decompiled" | head -1 | sed 's/.*"{\(.*\)}"/{\1}/')"
upgrade_code="$(grep -oE 'UpgradeCode="\{[^}]+\}"' "$decompiled" | head -1 | sed 's/.*"{\(.*\)}"/{\1}/')"
[ -n "$product_code" ] && [ -n "$upgrade_code" ] || { echo "winget: $msi carries no ProductCode/UpgradeCode" >&2; exit 1; }

url="https://github.com/$REPO/releases/download/v$version/$(basename "$msi")"

unsigned_note=""
if ! msi_is_signed "$msi"; then
	unsigned_note="

  This installer is not code-signed, so Windows SmartScreen warns on first run. Choose More info, then
  Run anyway. The SHA256 published beside the installer is what confirms the file is the one built."
fi

mkdir -p "$OUT"

cat > "$OUT/$PACKAGE.installer.yaml" <<EOF
# yaml-language-server: \$schema=https://aka.ms/winget-manifest.installer.$MANIFEST_VERSION.schema.json
PackageIdentifier: $PACKAGE
PackageVersion: $version
InstallerType: wix
Scope: machine
InstallModes:
  - interactive
  - silent
  - silentWithProgress
UpgradeBehavior: install
# INSTALLDIR is the property name maxon.wxs declares; winget substitutes the user's
# --location into it, and the flag is silently ignored if the two ever disagree.
InstallerSwitches:
  InstallLocation: INSTALLDIR="<INSTALLPATH>"
ProductCode: '$product_code'
AppsAndFeaturesEntries:
  - ProductCode: '$product_code'
    UpgradeCode: '$upgrade_code'
    InstallerType: wix
Commands:
  - maxon
FileExtensions:
  - maxon
InstallationMetadata:
  DefaultInstallLocation: '%ProgramFiles%\\Maxon'
Installers:
  - Architecture: x64
    InstallerUrl: $url
    InstallerSha256: $sha
ManifestType: installer
ManifestVersion: $MANIFEST_VERSION
EOF

cat > "$OUT/$PACKAGE.locale.en-US.yaml" <<EOF
# yaml-language-server: \$schema=https://aka.ms/winget-manifest.defaultLocale.$MANIFEST_VERSION.schema.json
PackageIdentifier: $PACKAGE
PackageVersion: $version
PackageLocale: en-US
Publisher: Maxon Language
PublisherUrl: https://github.com/maxon-lang
PublisherSupportUrl: https://github.com/$REPO/issues
PackageName: Maxon
PackageUrl: https://github.com/$REPO
License: MIT OR Apache-2.0
LicenseUrl: https://github.com/$REPO/blob/main/LICENSE-MIT
ShortDescription: A systems language whose compiler is written in itself.
Description: |-
  Maxon is a systems language whose compiler is written in Maxon. It has a native backend and emits
  standalone PE, ELF, Mach-O and WebAssembly executables with no external runtime, no LLVM and no
  garbage collector, and it reproduces itself exactly: two successive self-compiles are byte-identical.${unsigned_note}
Tags:
  - compiler
  - language
  - systems-programming
ManifestType: defaultLocale
ManifestVersion: $MANIFEST_VERSION
EOF

cat > "$OUT/$PACKAGE.yaml" <<EOF
# yaml-language-server: \$schema=https://aka.ms/winget-manifest.version.$MANIFEST_VERSION.schema.json
PackageIdentifier: $PACKAGE
PackageVersion: $version
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $MANIFEST_VERSION
EOF

echo "winget: wrote $OUT/ for $PACKAGE $version"
echo "  installer: $(basename "$msi")"
echo "  sha256:    $sha"
echo "  product:   $product_code"
echo "  upgrade:   $upgrade_code"
echo "  url:       $url"
echo
echo "⚠ The URL must be live before submitting. First submission is by hand:"
echo "    wingetcreate submit --token <pat> $OUT"
