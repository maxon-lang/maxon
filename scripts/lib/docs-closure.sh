DocsClosureShippedKind="shipped"
DocsClosureOutsideKind="outside"

docs_closure_normalize() {
	local rest="$1" part resolved=""

	while [ -n "$rest" ]; do
		case "$rest" in
			*/*) part="${rest%%/*}"; rest="${rest#*/}" ;;
			*) part="$rest"; rest="" ;;
		esac

		case "$part" in
			""|".") ;;
			"..")
				[ -n "$resolved" ] || return 1
				case "$resolved" in
					*/*) resolved="${resolved%/*}" ;;
					*) resolved="" ;;
				esac
				;;
			*) resolved="${resolved:+$resolved/}$part" ;;
		esac
	done

	printf '%s\n' "$resolved"
}

docs_closure_links() {
	local file="$1"

	{ grep -oE '\]\([^)#[:space:]]+' "$file" || true; } \
		| sed -E 's/^\]\(//' \
		| { grep -vE '^[A-Za-z][A-Za-z0-9+.-]*:' || true; } \
		| LC_ALL=C sort -u
}

docs_link_closure() {
	local docs_dir="$1" seed="$2"
	local tab=$'\t' newline=$'\n'
	local -a queue=("$seed")
	local queued="$newline$seed$newline" outside=""
	local index=0 current dir link joined target link_count link_index
	local -a links

	[ -f "$docs_dir/$seed" ] || { echo "release.sh: $docs_dir/$seed does not exist, so the documents it links cannot be gathered" >&2; return 1; }

	while [ "$index" -lt "${#queue[@]}" ]; do
		current="${queue[$index]}"
		index=$((index + 1))

		case "$current" in
			*.md) ;;
			*) continue ;;
		esac

		case "$current" in
			*/*) dir="${current%/*}" ;;
			*) dir="" ;;
		esac

		links=()
		while IFS= read -r link; do
			links+=("$link")
		done < <(docs_closure_links "$docs_dir/$current")

		link_count="${#links[@]}"
		link_index=0
		while [ "$link_index" -lt "$link_count" ]; do
			link="${links[$link_index]}"
			link_index=$((link_index + 1))

			case "$link" in
				/*)
					outside="$outside$current$tab$link$newline"
					continue ;;
			esac

			joined="${dir:+$dir/}$link"
			if ! target="$(docs_closure_normalize "$joined")"; then
				outside="$outside$current$tab$link$newline"
				continue
			fi

			if [ ! -f "$docs_dir/$target" ]; then
				echo "release.sh: $docs_dir/$current links to $link, which is not a file ($docs_dir/$target)" >&2
				return 1
			fi

			case "$queued" in
				*"$newline$target$newline"*) ;;
				*)
					queued="$queued$target$newline"
					queue+=("$target") ;;
			esac
		done
	done

	printf '%s\n' "${queue[@]}" | LC_ALL=C sort -u | while IFS= read -r target; do
		printf '%s%s%s\n' "$DocsClosureShippedKind" "$tab" "$target"
	done

	printf '%s' "$outside" | LC_ALL=C sort -u | while IFS="$tab" read -r current link; do
		printf '%s%s%s%s%s\n' "$DocsClosureOutsideKind" "$tab" "$current" "$tab" "$link"
	done
}

stage_docs_closure() {
	local docs_dir="$1" seed="$2" stage_docs="$3"
	local closure_file kind first second tab=$'\t'

	closure_file="$(mktemp)"
	if ! docs_link_closure "$docs_dir" "$seed" > "$closure_file"; then
		rm -f "$closure_file"
		return 1
	fi

	while IFS="$tab" read -r kind first second; do
		case "$kind" in
			"$DocsClosureShippedKind")
				mkdir -p "$stage_docs/$(dirname "$first")"
				cp "$docs_dir/$first" "$stage_docs/$first" \
					|| { echo "release.sh: $docs_dir/$first is reachable from $docs_dir/$seed and cannot be copied into the archive" >&2; rm -f "$closure_file"; return 1; }
				;;
			"$DocsClosureOutsideKind")
				echo "release.sh: $docs_dir/$first links outside $docs_dir/, which the archive does not carry: $second" >&2
				;;
			*)
				echo "release.sh: unknown docs closure line kind: $kind" >&2
				rm -f "$closure_file"
				return 1 ;;
		esac
	done < "$closure_file"

	rm -f "$closure_file"
}
