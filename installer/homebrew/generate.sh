#!/usr/bin/env bash
#
# Generate the Homebrew formula for a built release, into dist/homebrew/, for every target whose
# `channels` cell in maxon-bin/targets.tsv holds `homebrew`.
#
# ⭐ HOMEBREW MAKES MAXON USABLE IN THE SHELL THAT INSTALLED IT. `brew install` links the binary into
# Homebrew's `bin` — `/opt/homebrew/bin` on macOS, `/home/linuxbrew/.linuxbrew/bin` on Linux — which is
# already on PATH, so there is no PATH edit and no new terminal. On macOS it also strips the quarantine
# attribute, so the Gatekeeper warning a downloaded archive earns does not appear.
#
# ⛔ THE BINARY IS LINKED INTO `bin` AS A SYMLINK, AND THAT IS SAFE — MEASURED, NOT ASSUMED. The
# compiler finds `stdlib/` and its sibling `runtime/` by walking UP from its own executable, so a symlink
# whose directory has neither beside it would break every compile if the walk started at the LINK. It
# does not: `Process.executablePath()` resolves the link (`/proc/self/exe` on Linux), and a `maxon` reached
# through a symlink on PATH compiles against the stdlib and runtime in its real prefix. `homebrew.yml`
# tests exactly that on every platform before a formula is published.
#
# ⛔ THE TAP PREFIX IS REQUIRED, NOT A CONVENTION — `brew install maxon` INSTALLS SOMETHING ELSE.
# `maxon` in homebrew-cask is **Maxon App**, Maxon Computer's Cinema 4D / ZBrush installer, and a bare
# `brew install maxon` resolves to it. MEASURED: the attempt reported "Purging files for version
# 2026.5.0 of Cask maxon". So the fully-qualified `maxon-lang/tap/maxon` is the ONLY spelling that
# installs the language.
#
# ⚠ `brew tap` + a bare name is not a shortcut either: a third-party tap now needs `brew trust` before
# its formulae load, and the bare name would still hit the cask.
#
# ⚠ GENERATED, NOT COMMITTED: the formula carries the SHA256 of each archive, and a formula checked in
# beside the source is stale the moment a release is rebuilt.
#
# Usage:
#   installer/homebrew/generate.sh [<version>]
#
# Publish by committing the result to maxon-lang/homebrew-tap as Formula/maxon.rb.

set -euo pipefail

repo_root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$repo_root"

# shellcheck source=scripts/lib/host-binaries.sh
. scripts/lib/host-binaries.sh

DIST="dist"
OUT="$DIST/homebrew"
REPO="maxon-lang/maxon"

archive_for() {
	local archive_ext matches found

	archive_ext="$(maxon_target_archive_ext "$1")" || { echo "homebrew: $1 has no archive extension in $MAXON_TARGETS_TABLE" >&2; exit 1; }
	matches="$(find "$DIST" -maxdepth 1 -type f -name "maxon-*-$1.$archive_ext" | sort)" || { echo "homebrew: could not search $DIST for the $1 archive" >&2; exit 1; }
	found="${matches%%$'\n'*}"
	[ -n "$found" ] || { echo "homebrew: no $1 archive (maxon-*-$1.$archive_ext) in $DIST — download the release's archives first" >&2; exit 1; }
	echo "$found"
}

cpu_of() { echo "${1%%-*}"; }
os_of() { echo "${1#*-}"; }

formula_os_block() {
	case "$1" in
		macos) echo "on_macos" ;;
		linux) echo "on_linux" ;;
		*) echo "homebrew: the formula has no block for the operating system '$1'" >&2; exit 1 ;;
	esac
}

formula_cpu_block() {
	case "$1" in
		x64)   echo "on_intel" ;;
		arm64) echo "on_arm" ;;
		*) echo "homebrew: the formula has no block for the cpu '$1'" >&2; exit 1 ;;
	esac
}

formula_cpu_symbol() {
	case "$1" in
		x64)   echo "x86_64" ;;
		arm64) echo "arm64" ;;
		*) echo "homebrew: the formula has no architecture symbol for the cpu '$1'" >&2; exit 1 ;;
	esac
}

homebrew_rows="$(maxon_table_select target channels=homebrew)"
[ -n "$homebrew_rows" ] || { echo "homebrew: $MAXON_TARGETS_TABLE lists no target in the homebrew channel" >&2; exit 1; }

FormulaOperatingSystems="macos linux"
FormulaCpus="x64 arm64"

for target in $homebrew_rows; do
	formula_os_block "$(os_of "$target")" > /dev/null
	formula_cpu_block "$(cpu_of "$target")" > /dev/null
	formula_cpu_symbol "$(cpu_of "$target")" > /dev/null
done

targets=()
archives=()
operating_systems=()

homebrew_targets=" $(printf '%s' "$homebrew_rows" | tr '\n' ' ') "

for operating_system in $FormulaOperatingSystems; do
	for cpu in $FormulaCpus; do
		case "$homebrew_targets" in
			*" $cpu-$operating_system "*) ;;
			*) continue ;;
		esac

		archive="$(archive_for "$cpu-$operating_system")"
		targets+=("$cpu-$operating_system")
		archives+=("$archive")

		case " ${operating_systems[*]-} " in
			*" $operating_system "*) ;;
			*) operating_systems+=("$operating_system") ;;
		esac
	done
done

version="${1:-}"
if [ -z "$version" ]; then
	version="$(basename "${archives[0]}")"
	version="${version#maxon-}"
	version="${version%-"${targets[0]}".*}"
fi

url_of() { echo "https://github.com/$REPO/releases/download/v$version/$(basename "$1")"; }
sha_of() { sha256sum "$1" | cut -d' ' -f1; }

emit_os_block() {
	local operating_system="$1" index cpu_count=0

	for index in "${!targets[@]}"; do
		if [ "$(os_of "${targets[$index]}")" = "$operating_system" ]; then
			cpu_count=$((cpu_count + 1))
		fi
	done

	echo "  $(formula_os_block "$operating_system") do"

	# An operating system served on one cpu only pins it, so Homebrew refuses the other cpu there rather
	# than resolving a formula with no download.
	if [ "$cpu_count" -eq 1 ]; then
		for index in "${!targets[@]}"; do
			if [ "$(os_of "${targets[$index]}")" = "$operating_system" ]; then
				echo "    depends_on arch: :$(formula_cpu_symbol "$(cpu_of "${targets[$index]}")")"
			fi
		done
	fi

	for index in "${!targets[@]}"; do
		if [ "$(os_of "${targets[$index]}")" = "$operating_system" ]; then
			echo "    $(formula_cpu_block "$(cpu_of "${targets[$index]}")") do"
			echo "      url \"$(url_of "${archives[$index]}")\""
			echo "      sha256 \"$(sha_of "${archives[$index]}")\""
			echo "    end"
		fi
	done

	echo "  end"
	echo
}

mkdir -p "$OUT"
formula="$OUT/maxon.rb"

{
	cat <<EOF
# Generated by installer/homebrew/generate.sh — do not edit by hand.
class Maxon < Formula
  desc "Systems language whose compiler is written in itself"
  homepage "https://maxon.dev"
  license any_of: ["MIT", "Apache-2.0"]

EOF

	for operating_system in "${operating_systems[@]}"; do
		emit_os_block "$operating_system"
	done

	cat <<'EOF'
  def install
    # ⛔ THE COMPILER, ITS STANDARD LIBRARY AND ITS RUNTIME MUST STAY SIBLINGS. `maxon` finds `stdlib/`
    # and `runtime/` by walking up from its own executable, so installing the binary into `bin` on its
    # own would leave it unable to compile at all. The whole tree goes into libexec, and `bin/maxon` is
    # a symlink to it — the compiler resolves that link, so the walk starts in the real prefix.
    libexec.install Dir["*"]
    bin.install_symlink libexec/"maxon"
  end

  test do
    (testpath/"hello.maxon").write <<~MAXON
      typealias ExitCode = int(0 to 255)

      function main() returns ExitCode
      \tprint("hello\\n")
      \treturn 0
      end 'main'
    MAXON

    assert_match version.to_s, shell_output("#{bin}/maxon version")
    system bin/"maxon", "build", testpath/"hello.maxon", "--output=#{testpath}/hello"
    assert_equal "hello\n", shell_output(testpath/"hello")
  end
end
EOF
} > "$formula"

echo "homebrew: wrote $formula for $version"
for index in "${!targets[@]}"; do
	printf '  %-12s %s\n' "${targets[$index]}:" "$(basename "${archives[$index]}")"
done
echo
echo "⚠ The release must be live before the formula resolves. To publish it:"
echo "    commit this file to maxon-lang/homebrew-tap as Formula/maxon.rb"
echo "  then it installs with:"
echo "    brew install maxon-lang/tap/maxon"
