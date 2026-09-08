#!/usr/bin/env bash
#
# Build the Windows MSI from a packaged x64-windows archive.
#
# ⭐ IT BUILDS FROM THE ARCHIVE, NOT FROM THE TREE. The zip is what `release.sh --package` produced and
# what a person downloading the release gets; building the installer from the same bytes means the MSI
# cannot contain a different compiler from the one published beside it.
#
# ⚠ WiX IS THE ONE REMAINING .NET DEPENDENCY, AND IT IS ON THE RELEASE MACHINE ONLY. Nothing about
# BUILDING Maxon needs it — `dotnet tool install --global wix` is asked of whoever cuts a release, and
# of the `msi` job in the release workflow. It is not a contributor prerequisite.
#
# Usage:
#   installer/windows/build.sh [<archive.zip>]
#
# With no argument it takes the single x64-windows zip in dist/.

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$repo_root"

DIST="dist"
here="installer/windows"

command -v wix >/dev/null 2>&1 || {
	cat >&2 <<'EOF'
installer: the WiX toolset is not on PATH.

  dotnet tool install --global wix

  It is needed only to build the installer, never to build Maxon. If `dotnet tool install` succeeded
  and `wix` is still not found, add ~/.dotnet/tools to PATH.
EOF
	exit 1
}

archive="${1:-}"
if [ -z "$archive" ]; then
	archive="$(find "$DIST" -maxdepth 1 -type f -name 'maxon-*-x64-windows.zip' | head -n1 || true)"
fi
[ -n "$archive" ] && [ -f "$archive" ] || {
	echo "installer: no x64-windows archive — run scripts/release.sh --package first" >&2
	exit 1
}

# ⛔ THE VERSION COMES OUT OF THE ARCHIVE'S NAME, which `release.sh` built from the COMPILER's own
# `--version`. Reading it from anywhere else — the source, a tag, a variable here — reintroduces the
# disagreement the whole scheme exists to prevent: an installer whose ProductVersion names a release
# its payload is not.
base="$(basename "$archive" .zip)"
version="${base#maxon-}"
version="${version%-x64-windows}"
case "$version" in
	[0-9]*.[0-9]*.[0-9]*) ;;
	*) echo "installer: cannot read a version out of '$base'" >&2; exit 1 ;;
esac

work="$(mktemp -d)"
payload="$here/payload"
rm -rf "$payload"
mkdir -p "$payload"

# The archive holds ONE top-level directory; its CONTENTS are the payload, so that `maxon.exe` and
# `stdlib/` land directly in the install directory rather than one level under it.
if command -v unzip >/dev/null 2>&1; then
	unzip -q "$archive" -d "$work"
else
	powershell -NoProfile -Command "Expand-Archive -Path '$(cygpath -w "$archive")' -DestinationPath '$(cygpath -w "$work")' -Force"
fi

staged="$(find "$work" -mindepth 1 -maxdepth 1 -type d | head -n1)"
[ -n "$staged" ] || { echo "installer: $archive holds no directory to install" >&2; exit 1; }
cp -r "$staged"/. "$payload"/

[ -f "$payload/maxon.exe" ] || { echo "installer: no maxon.exe in $archive" >&2; exit 1; }
[ -d "$payload/stdlib" ] || { echo "installer: no stdlib/ in $archive" >&2; exit 1; }

# ⭐ THE LICENCE DIALOG IS GENERATED FROM THE LICENCE FILES, so it cannot show terms the package does
# not ship. WiX wants RTF and the repo holds plain text, which is a mechanical conversion: three
# characters are special to RTF and every line ends a paragraph.
write_license_rtf() {
	local out="$1"

	{
		printf '{\\rtf1\\ansi\\ansicpg1252\\deff0{\\fonttbl{\\f0\\fnil\\fcharset0 Segoe UI;}}\n'
		printf '\\viewkind4\\uc1\\pard\\f0\\fs18\n'
		printf 'Maxon is distributed under EITHER the MIT licence OR the Apache License 2.0, at your option.\\par\n'
		printf '\\par\n'
		local f
		for f in LICENSE-MIT LICENSE-APACHE; do
			printf '%s\\par\n\\par\n' "$f"
			# ⚠ THE ORDER OF THE THREE SUBSTITUTIONS MATTERS. Backslash must be escaped FIRST, or the
			# backslashes introduced by escaping the braces would themselves be escaped again.
			sed -e 's/\\/\\\\/g' -e 's/{/\\{/g' -e 's/}/\\}/g' -e 's/$/\\par/' "$f"
			printf '\\par\n\\par\n'
		done
		printf '}\n'
	} > "$out"
}

license="$here/license.rtf"
write_license_rtf "$license"

msi="$DIST/maxon-$version-x64.msi"
rm -f "$msi"

echo "installer: building $msi from $(basename "$archive")"
# ⚠ `WixToolset.UI.wixext` IS A SEPARATE INSTALL: `wix extension add -g WixToolset.UI.wixext`. The
# installer needs it for the directory-chooser and for the exit-dialog checkbox that offers the VS
# Code extension.
( cd "$here" && wix build maxon.wxs -arch x64 -d "Version=$version" -ext WixToolset.UI.wixext -o "../../$msi" )

# ⚠ THE `.wixpdb` IS NOT A RELEASE ASSET. WiX writes it beside the MSI, and `--publish` uploads
# everything in `dist/` that matches its patterns — a debugging artifact would ride along into the
# release without anyone deciding it should.
rm -f "${msi%.msi}.wixpdb" "$license"
rm -rf "$payload" "$work"
echo "installer: wrote $msi"
