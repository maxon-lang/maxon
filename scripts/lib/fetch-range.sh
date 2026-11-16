# Sourced by a workflow step whose checkout is one commit deep; bash only. `fetch_range <base> <head>`
# fetches the history from <head> back to <base> and sets `RangeBase`, so that `$RangeBase..<head>` names
# the commits the push or pull request brought; its `::notice::` lines are the step's own output.
#
# With no usable base (empty, all zeros on a branch's first push, or one the remote no longer has) the
# range is <head> alone. A root <head> then has no parent to compare it with, so `RangeBase` is <head>
# itself and the range is empty. A complete clone cannot be deepened, so a base on unrelated history ends
# the search there.

# Deep enough for <head>'s parent, which is the base when the event names none.
RangeFirstDepth=2
RangeDepthGrowth=4
# Past this depth the rest of the history is fetched in one go.
RangeDeepestStep=4096

fetch_range() {
	range_base="$1"
	range_head="$2"
	range_depth="$RangeFirstDepth"
	git fetch --no-tags --depth="$range_depth" origin "$range_head"

	if [ -z "$range_base" ] || [ -z "${range_base//0/}" ] || ! git fetch --no-tags --depth=1 origin "$range_base"; then
		range_head_name="$(git rev-parse --short "$range_head")"

		if ! range_base="$(git rev-parse --verify --quiet "$range_head^")"; then
			echo "::notice::no base commit to measure from, and $range_head_name is a root commit with no parent to compare its rows with; no commit is checked"
			RangeBase="$range_head"
			return 0
		fi

		echo "::notice::no base commit to measure from; checking $range_head_name alone"
	fi

	until git merge-base "$range_base" "$range_head" > /dev/null; do
		if [ "$(git rev-parse --is-shallow-repository)" = false ]; then
			break
		fi

		range_depth=$((range_depth * RangeDepthGrowth))

		if [ "$range_depth" -gt "$RangeDeepestStep" ]; then
			git fetch --no-tags --unshallow origin
			break
		fi

		git fetch --no-tags --depth="$range_depth" origin "$range_head" "$range_base"
	done

	RangeBase="$range_base"
}
