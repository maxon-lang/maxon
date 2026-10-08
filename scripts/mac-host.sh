#!/usr/bin/env bash
#
# Share one arm64 Mac, the host of the arm64-macos lane, between several agents at once.
#
# Isolation: each agent builds and runs only in its own clone, `~/Dev/agents/<session>/maxon` on the
# Mac, never in the shared `~/Dev/maxon`, which may hold a person's uncommitted work.
#
# One host lock: timing-sensitive specs (the monitor, preemption, the debug agent) flake when two
# suites share the host, so a job runs only under the lock.
#
# The Mac is named by the environment or the repository's gitignored `.env` (see `.env.example`).
# When neither names one, every command prints a `skipped` line and exits 0. Run with no arguments
# for usage.

set -euo pipefail

readonly ConnectTimeoutSeconds=10
readonly WaitStepSeconds=30
readonly HeartbeatSeconds=60
readonly StaleHeartbeatSeconds=600
readonly JobLogLines=40
readonly TokenBytes=16
readonly TimestampFormat='%Y-%m-%dT%H:%M:%SZ'
readonly JobIdFormat='%Y%m%dT%H%M%SZ'
readonly MacTarget=arm64-macos
readonly OpStaleSeconds=30
readonly OpWaitSeconds=$((2 * OpStaleSeconds))
readonly OpPollSeconds=0.1
readonly ConstantNamePattern='^[A-Z][a-z][A-Za-z0-9]*$'
readonly ReadonlyDeclarationPattern='^declare -[a-z]*r'
readonly EnvLinePattern='^MAXON_MAC_HOST[[:space:]]*=[[:space:]]*(.*)$'
readonly SessionPattern='^[A-Za-z0-9_][A-Za-z0-9._-]*$'
readonly SecretPattern="^[0-9a-f]{$((TokenBytes * 2))}\$"
readonly SeedTagPattern='^v[0-9][A-Za-z0-9.+-]*$'
readonly LoginUserPattern='^[A-Za-z0-9._-]+$'
readonly LoginHostPattern='^[A-Za-z0-9.:%-]+$'
readonly Ipv4Pattern='^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$'
readonly ResolveIpv4Script='(Resolve-DnsName $env:MAXON_MAC_RESOLVE_NAME -ErrorAction Stop | Where-Object { $_.IPAddress -match "^\d+\.\d+\.\d+\.\d+$" } | Select-Object -First 1).IPAddress'
readonly ExitSuccess=0
readonly ExitFailure=1
readonly ExitUsage=2
readonly ExitHeld=3
readonly ExitTerminated=143
readonly ExitSshFailed=255

usage() {
	printf '%s\n' \
		"Usage: scripts/mac-host.sh <command> [arguments]" \
		"" \
		"  status                             the lock, its holder, its heartbeat's age (and STALE when it is)," \
		"                                     its job, and the agent sessions" \
		"  lock <session> [--wait=SECONDS] <purpose...>" \
		"                                     take the host lock and print token=<TOKEN>, the proof of" \
		"                                     ownership that heartbeat, unlock and run ask for; exits $ExitHeld" \
		"                                     at once if the lock is held; with --wait it breaks a STALE lock and" \
		"                                     takes it, and retries every ${WaitStepSeconds} s until SECONDS pass on one that is not" \
		"  heartbeat <session> <token>        mark the lock as still in use" \
		"  unlock <session> <token>           release the lock; refused while its job is alive" \
		"  break [--force] <reason...>        release a STALE lock, logging who held it, since when, and the" \
		"                                     reason in ~/.maxon-host.lock.log on the Mac; --force also releases" \
		"                                     one that is not stale, but never one whose job is alive" \
		"  clone <session> <commit> [--seed[=TAG]]" \
		"                                     a fresh clone at <commit> in ~/Dev/agents/<session>/maxon, which" \
		"                                     must not exist yet; prints session-key=<KEY>, which drop asks for;" \
		"                                     a failed clone leaves nothing behind. --seed" \
		"                                     also places the $MacTarget compiler of release TAG (default: the" \
		"                                     latest release, the one CI seeds from) at .bootstrap/maxon, fetched" \
		"                                     here with gh into a temporary directory" \
		"  run <session> <token> <program> [arguments...]" \
		"                                     run <program> detached in the session's clone under a login bash;" \
		"                                     one job per lock, and the lock is released when it exits. Every" \
		"                                     argument reaches <program> exactly as given and no shell re-reads" \
		"                                     it, so a shell line is passed as: bash -c '<line>'." \
		"                                     Prints job=<ID>, pid=<PID> and log=<PATH>" \
		"  job <session>                      the session's newest job (alive, finished with its exit code, or" \
		"                                     gone) and the last $JobLogLines lines of its log" \
		"  drop <session> <key>               delete ~/Dev/agents/<session>; <key> is its session key, or the" \
		"                                     token of the lock it holds; refused while any of its jobs is alive;" \
		"                                     a lock the session holds is released with it" \
		"" \
		"  A session name is letters, digits, '.', '_' and '-', and starts with neither '.' nor '-'." \
		"  Sessions live only in ~/Dev/agents/ on the Mac, never in the shared ~/Dev/maxon." \
		"" \
		"  A lock is STALE when it has no live job and its heartbeat is older than ${StaleHeartbeatSeconds} s. lock sets" \
		"  the heartbeat and a running job refreshes it every ${HeartbeatSeconds} s, so a job-held lock is never stale;" \
		"  a holder with no running job must call heartbeat at least every ${StaleHeartbeatSeconds} s (its watcher" \
		"  poll is the natural place), or any agent may break the lock. clone --seed refreshes the" \
		"  heartbeat of a lock its session holds while it places the seed; a long clone does not." \
		"" \
		"Environment:" \
		"  MAXON_MAC_HOST   user@host of the Mac; host is a name, an IPv4 or an IPv6 address. On Windows" \
		"                   a .local name is resolved to its IPv4 address with PowerShell." \
		"" \
		"  It is read from the environment, or else from a MAXON_MAC_HOST=VALUE line in the repository's" \
		"  .env (see .env.example; an export prefix and one pair of quotes are allowed, and every other" \
		"  line is ignored). When MAXON_MAC_HOST is set in neither, every command whose arguments are" \
		"  valid prints" \
		"  \"mac-host.sh: skipped — MAXON_MAC_HOST is not set (see .env.example)\" and exits" \
		"  $ExitSuccess without touching the Mac, so a caller sees that line instead of token=, job= or" \
		"  session-key= lines." \
		"" \
		"Exit status: $ExitSuccess success, $ExitFailure failure or refusal, $ExitUsage usage, $ExitHeld the lock is held," \
		"$ExitSshFailed ssh could not reach the Mac." >&2
	exit "$ExitUsage"
}

die() {
	echo "mac-host.sh: $*" >&2
	exit "$ExitFailure"
}

check_session() {
	[[ "$1" =~ $SessionPattern ]] || die "a session name is letters, digits, '.', '_' and '-', and starts with neither '.' nor '-'; got '$1'"
}

check_secret() {
	[[ "$1" =~ $SecretPattern ]] || die "a token or session key is the $((TokenBytes * 2)) hex digits that lock or clone printed; got '$1'"
}

marked_value() {
	local value

	value="$(printf '%s\n' "$2" | sed -n "s/^$1=//p")"
	[[ -n "$value" && "$value" != *[[:cntrl:]]* ]] || die "the Mac printed no single $1= line"
	printf '%s\n' "$value"
}

check_single_line() {
	[[ "$2" != *[[:cntrl:]]* ]] || die "$1 must be one line without control characters"
}

resolve_address() {
	local address

	case "$mac_name" in
		*.local)
			if command -v powershell.exe >/dev/null 2>&1; then
				address="$(MAXON_MAC_RESOLVE_NAME="$mac_name" powershell.exe -NoProfile -NonInteractive -Command "$ResolveIpv4Script" 2>/dev/null | tr -d '\r')" || address=""
				[[ "$address" =~ $Ipv4Pattern ]] || die "cannot resolve $mac_name to an IPv4 address (is the Mac awake?)"
				printf '%s\n' "$address"
				return
			fi
			;;
	esac

	printf '%s\n' "$mac_name"
}

# Parsed, never sourced, so a config file cannot execute code.
load_env() {
	local file="$repo_root/.env" line value number=0

	[ -f "$file" ] || return 0

	while IFS= read -r line || [ -n "$line" ]; do
		number=$((number + 1))
		line="${line%$'\r'}"

		if [ "$number" -eq 1 ]; then
			line="${line#$'\xef\xbb\xbf'}"
		fi

		line="${line#"${line%%[![:space:]]*}"}"

		case "$line" in
			export[[:space:]]*)
				line="${line#export}"
				line="${line#"${line%%[![:space:]]*}"}"
				;;
		esac

		[[ "$line" =~ $EnvLinePattern ]] || continue
		value="${BASH_REMATCH[1]}"
		value="${value%"${value##*[![:space:]]}"}"

		case "$value" in
			\"*\") value="${value#\"}"; value="${value%\"}" ;;
			\'*\') value="${value#\'}"; value="${value%\'}" ;;
		esac

		if [ -z "${MAXON_MAC_HOST:-}" ]; then
			MAXON_MAC_HOST="$value"
		fi
	done < "$file"
}

connect() {
	load_env

	if [ -z "${MAXON_MAC_HOST:-}" ]; then
		echo "mac-host.sh: skipped — MAXON_MAC_HOST is not set (see .env.example)"
		exit "$ExitSuccess"
	fi

	mac_user="${MAXON_MAC_HOST%@*}"
	mac_name="${MAXON_MAC_HOST##*@}"
	[ "$mac_user" != "$MAXON_MAC_HOST" ] || die "MAXON_MAC_HOST must be user@host, got '$MAXON_MAC_HOST'"
	[[ "$mac_user" =~ $LoginUserPattern ]] || die "MAXON_MAC_HOST has an unusable user '$mac_user'"
	[[ "$mac_name" =~ $LoginHostPattern ]] || die "MAXON_MAC_HOST has an unusable host '$mac_name'"

	mac_address="$(resolve_address)"
	mac_login="$mac_user@$mac_address"

	case "$mac_address" in
		*:*) scp_login="$mac_user@[$mac_address]" ;;
		*)   scp_login="$mac_login" ;;
	esac

	ssh_options=(-o BatchMode=yes -o "ConnectTimeout=$ConnectTimeoutSeconds" -o "HostKeyAlias=$mac_name")
}

r_init() {
	lock_dir="$HOME/.maxon-host.lock"
	op_dir="$HOME/.maxon-host.lock.op"
	lock_log="$HOME/.maxon-host.lock.log"
	agents_dir="$HOME/Dev/agents"
	shared_checkout="$HOME/Dev/maxon"
	op_marker=""
	op_term_action="-"
	trap 'r_op_leave' EXIT
}

# Remote code is every `r_*` function by `declare -f` plus every readonly constant whose name
# matches `ConstantNamePattern`, so nothing is expanded locally inside remote code and a new
# constant ships without being listed. A constant named outside that pattern is unset on the Mac.
r_program() {
	local name

	for name in $(compgen -A variable); do
		if [[ "$name" =~ $ConstantNamePattern && "$(declare -p "$name")" =~ $ReadonlyDeclarationPattern ]]; then
			printf 'readonly %s=%q\n' "$name" "${!name}"
		fi
	done

	declare -f $(compgen -A function r_)
	printf 'r_init\n'
	printf '%q ' "$@"
	printf '\n'
}

r_warn() {
	echo "mac-host.sh: $*" >&2
}

r_fail() {
	r_warn "$@"
	exit "$ExitFailure"
}

r_timestamp() {
	date -u -r "$1" +"$TimestampFormat"
}

r_now() {
	date -u +%s
}

r_write_atomic() {
	printf '%s\n' "$2" > "$1.tmp.$$" && mv -f "$1.tmp.$$" "$1"
}

r_seconds_since() {
	case "$1" in
		''|*[!0-9]*) ;;
		*) echo $(( $(r_now) - $1 )) ;;
	esac
}

r_log() {
	if ! printf '%s\n' "$@" 2>/dev/null >> "$lock_log"; then
		r_warn "cannot append to $lock_log"
	fi
}

# The operation mutex serialises every read-then-change of lock state, so a check followed by an act
# inside one section is sound. Sections stay short (no clone, seed or wait inside one): a section
# older than `OpStaleSeconds` is broken as stale.
r_op_enter() {
	local deadline age grave marker

	deadline=$(( $(r_now) + OpWaitSeconds ))
	marker="$op_dir/holder.$$.$(r_new_token)"

	until r_op_claim "$marker"; do
		age="$(r_seconds_since "$(stat -f %m "$op_dir" 2>/dev/null || true)")"

		if [ -n "$age" ] && [ "$age" -ge "$OpStaleSeconds" ]; then
			grave="$(mktemp -d "$op_dir.broken.XXXXXX")" || r_fail "cannot break the stale $op_dir"

			if mv "$op_dir" "$grave/op" 2>/dev/null; then
				r_log "=== operation mutex broken $(r_timestamp "$(r_now)") by pid $$: $op_dir was ${age}s old"
			fi

			rm -rf "$grave"
			continue
		fi

		[ "$(r_now)" -lt "$deadline" ] || r_fail "another lock operation has held $op_dir for too long"
		sleep "$OpPollSeconds"
	done
}

r_op_claim() {
	local outcome=contended term_pending=""

	# TERM is recorded, not ignored: a TERM between the `mkdir` and `op_marker` would leave a mutex
	# nobody releases, and an ignored one would be lost. The recorded one is honoured once the marker
	# exists.
	trap 'term_pending=yes' TERM

	if mkdir "$op_dir" 2>/dev/null; then
		if : 2>/dev/null > "$1"; then
			op_marker="$1"
			outcome=claimed
		else
			rmdir "$op_dir"
			outcome=unmarked
		fi
	fi

	trap "$op_term_action" TERM

	if [ -n "$term_pending" ]; then
		if [ "$op_term_action" = "-" ]; then
			exit "$ExitTerminated"
		fi

		eval "$op_term_action"
	fi

	case "$outcome" in
		claimed) return 0 ;;
		contended) return 1 ;;
		unmarked) r_fail "cannot create the holder marker $1" ;;
		*) r_fail "unknown mutex claim outcome '$outcome'" ;;
	esac
}

# Removes only this holder's marker, and the directory only when that succeeded: a holder paused
# past `OpStaleSeconds` (the Mac dozing) may find its mutex broken and re-taken, and must not
# release the new holder's. Never `rm -rf` the op directory.
r_op_leave() {
	local marker="$op_marker"

	if [ -z "$marker" ]; then
		return 0
	fi

	op_marker=""

	if rm "$marker" 2>/dev/null; then
		rmdir "$op_dir" 2>/dev/null || r_warn "cannot remove $op_dir"
	fi
}

r_owner_field() {
	local line

	while IFS= read -r line; do
		case "$line" in
			"$1="*)
				printf '%s\n' "${line#"$1="}"
				return 0
				;;
		esac
	done 2>/dev/null < "$lock_dir/owner" || true
}

r_lock_token() {
	local path

	for path in "$lock_dir"/token.*; do
		if [ -e "$path" ]; then
			printf '%s\n' "${path##*/token.}"
			return 0
		fi
	done
}

r_heartbeat_age() {
	r_seconds_since "$(cat "$lock_dir/heartbeat" 2>/dev/null || true)"
}

r_beat() {
	[ -e "$lock_dir/token.$1" ] || return 1
	r_write_atomic "$lock_dir/heartbeat" "$(r_now)"
}

# Renamed to a grave before deleting: a rename cannot fail on the lock's contents, while `rm -rf`
# racing a writer can fail with ENOTEMPTY and leave a half-deleted lock.
r_discard_lock() {
	local grave

	grave="$(mktemp -d "$lock_dir.released.XXXXXX")" || return 1

	if ! mv "$lock_dir" "$grave/lock"; then
		rmdir "$grave"
		return 1
	fi

	rm -rf "$grave"
}

# Published complete, by renaming the staging directory onto the absent path, so a lock without a
# token can only be a relic and `r_judge_lock` judges it stale. The `token.<token>` file is the
# proof of ownership, and the job slot is `job.<token>` so a job cannot land in another holder's
# lock.
r_take_lock() {
	local session="$1" purpose="$2" from="$3" staging epoch

	lock_token="$(r_new_token)"
	staging="$(mktemp -d "$lock_dir.new.XXXXXX")" || r_fail "cannot stage a lock beside $lock_dir"
	epoch="$(r_now)"

	if ! printf 'session=%s\npurpose=%s\nstarted=%s\nstarted_epoch=%s\nfrom=%s\n' \
			"$session" "$purpose" "$(r_timestamp "$epoch")" "$epoch" "$from" > "$staging/owner" ||
		! : > "$staging/token.$lock_token" ||
		! printf '%s\n' "$epoch" > "$staging/heartbeat"; then
		rm -rf "$staging"
		r_fail "cannot stage the lock in $staging"
	fi

	if [ -e "$lock_dir" ]; then
		rm -rf "$staging"
		r_fail "refusing to take $lock_dir: it exists although this operation holds $op_dir"
	fi

	if ! mv "$staging" "$lock_dir"; then
		rm -rf "$staging"
		r_fail "cannot create $lock_dir"
	fi
}

r_judge_lock() {
	local status=0

	judged_verdict=free
	judged_token=""
	judged_session=""
	judged_owner=""
	judged_started=""
	judged_age=""
	judged_job=""
	judged_why=""

	if [ ! -e "$lock_dir" ]; then
		return 0
	fi

	judged_token="$(r_lock_token)"
	judged_session="$(r_owner_field session)"
	judged_started="$(r_owner_field started_epoch)"
	judged_owner="$(cat "$lock_dir/owner" 2>/dev/null)" || judged_owner="(no owner file)"
	judged_age="$(r_heartbeat_age)"

	if [ -z "$judged_token" ]; then
		judged_job="no token: the relic of an interrupted operation"
		judged_verdict=stale
		judged_why="no token"
		return 0
	fi

	judged_job="$(r_lock_job_alive "$judged_token")" || status=$?

	if [ "$status" -eq 0 ]; then
		judged_verdict=live
	elif [ -n "$judged_age" ] && [ "$judged_age" -ge "$StaleHeartbeatSeconds" ]; then
		judged_verdict=stale
		judged_why="heartbeat ${judged_age}s old, no live job"
	else
		judged_verdict=held
	fi
}

r_log_break() {
	r_log "=== broken $(r_timestamp "$(r_now)") from $1 ($2): $3" "$judged_owner" "$judged_job"
}

r_require_holder() {
	[ -e "$lock_dir" ] || r_fail "the lock is not held"
	[ -e "$lock_dir/token.$2" ] || r_fail "the token does not match the lock, which '$(r_owner_field session)' holds"

	local holder
	holder="$(r_owner_field session)"
	[ "$holder" = "$1" ] || r_fail "the token's lock is held by '$holder', not '$1'"
}

r_require_session_key() {
	[ -f "$session_root/key" ] && [ "$(cat "$session_root/key")" = "$1" ] || r_fail "that is not the session key of $session_root"
}

r_beat_for_session() {
	local token

	r_op_enter
	token="$(r_lock_token)"

	if [ -n "$token" ] && [ "$(r_owner_field session)" = "$1" ]; then
		r_beat "$token" || true
	fi

	r_op_leave
}

r_session_paths() {
	local name="$1" agents_real real shared_real path

	case "$name" in
		''|.*|-*|*/*)
			r_warn "invalid session name '$name'"
			return 1
			;;
	esac

	if [ ! -d "$agents_dir" ]; then
		r_warn "there is no $agents_dir"
		return 1
	fi

	agents_real="$(cd "$agents_dir" && pwd -P)" || return 1
	session_root="$agents_real/$name"

	if [ -L "$session_root" ]; then
		r_warn "refusing $session_root: it is a symbolic link"
		return 1
	fi

	if [ -e "$session_root" ]; then
		real="$(cd "$session_root" && pwd -P)" || return 1

		if [ "$real" != "$session_root" ]; then
			r_warn "refusing $session_root: it resolves to $real"
			return 1
		fi
	fi

	if [ -d "$shared_checkout" ]; then
		shared_real="$(cd "$shared_checkout" && pwd -P)" || return 1

		case "$session_root/" in
			"$shared_real/"*)
				r_warn "refusing $session_root: it is inside the shared checkout $shared_real"
				return 1
				;;
		esac

		case "$shared_real/" in
			"$session_root/"*)
				r_warn "refusing $session_root: it contains the shared checkout $shared_real"
				return 1
				;;
		esac
	fi

	clone_dir="$session_root/maxon"
	logs_dir="$session_root/logs"

	for path in "$clone_dir" "$logs_dir"; do
		if [ -L "$path" ]; then
			r_warn "refusing $path: it is a symbolic link"
			return 1
		fi
	done
}

r_new_token() {
	local token

	token="$(od -An -N"$TokenBytes" -tx1 /dev/urandom | tr -d ' \n')"

	case "$token" in
		''|*[!0-9a-f]*) r_fail "cannot read a token from /dev/urandom" ;;
	esac

	[ "${#token}" -eq $((TokenBytes * 2)) ] || r_fail "cannot read a token from /dev/urandom"
	printf '%s\n' "$token"
}

r_job_state() {
	local base="$1" pid

	# `.exit` is read before `kill -0`: macOS reuses pids, so a finished job's pid can name a live
	# stranger.
	if [ -f "$base.exit" ]; then
		echo "finished (exit $(cat "$base.exit"))"
		return 1
	fi

	if [ ! -f "$base.pid" ]; then
		echo "not started (no pid recorded)"
		return 1
	fi

	pid="$(cat "$base.pid")"

	if kill -0 "$pid" 2>/dev/null; then
		echo "alive (pid $pid)"
		return 0
	fi

	echo "gone (pid $pid, no exit code recorded)"
	return 1
}

r_lock_job_alive() {
	local id state

	if [ ! -f "$lock_dir/job.$1" ]; then
		echo "no job was started under the lock"
		return 1
	fi

	id="$(cat "$lock_dir/job.$1")"

	case "$id" in
		''|.*|*/*)
			echo "the lock records an invalid job id '$id'"
			return 1
			;;
	esac

	if ! r_session_paths "$(r_owner_field session)" 2>/dev/null; then
		echo "job $id: the holder's session cannot be resolved"
		return 1
	fi

	if state="$(r_job_state "$logs_dir/$id")"; then
		echo "job $id: $state"
		return 0
	fi

	echo "job $id: $state"
	return 1
}

r_seed_paths() {
	seed_path="$clone_dir/.bootstrap/maxon"
	seed_staging="$seed_path.partial"
}

r_job_main() {
	job_token="$1"
	job_base="$2"
	job_heartbeat=""
	local clone="$3"
	shift 3
	r_write_atomic "$job_base.pid" "$$"
	trap 'r_job_finish "$?"' EXIT
	(
		op_term_action="exit 0"
		trap 'r_op_leave' EXIT
		trap "$op_term_action" TERM

		# The parent check stops a SIGKILLed job, whose EXIT trap never runs, from keeping its lock
		# fresh forever.
		while sleep "$HeartbeatSeconds"; do
			kill -0 "$$" 2>/dev/null || exit 0
			r_op_enter

			if ! kill -0 "$$" 2>/dev/null || ! r_beat "$job_token"; then
				exit 0
			fi

			r_op_leave
		done
	) &
	job_heartbeat=$!
	cd "$clone" || exit
	"$@"
}

r_job_finish() {
	if [ -n "$job_heartbeat" ]; then
		kill "$job_heartbeat" 2>/dev/null
	fi

	r_write_atomic "$job_base.exit" "$1"
	r_op_enter

	if [ -e "$lock_dir/token.$job_token" ]; then
		r_discard_lock
	fi

	r_op_leave
}

r_cmd_status() {
	local path age

	r_op_enter
	r_judge_lock
	r_op_leave

	if [ "$judged_verdict" = free ]; then
		echo "LOCK free"
	else
		echo "LOCK held:"
		printf '%s\n' "$judged_owner" | sed 's/^/  /'
		age="$(r_seconds_since "$judged_started")"
		echo "  age=${age:-unknown}s"
		echo "  heartbeat=${judged_age:-none}${judged_age:+s ago}"
		echo "  $judged_job"

		if [ "$judged_verdict" = stale ]; then
			echo "  STALE: $judged_why"
		fi
	fi

	echo "agent sessions:"

	for path in "$agents_dir"/*; do
		if [ -d "$path" ]; then
			echo "  ${path##*/}"
		fi
	done
}

r_cmd_lock() {
	local session="$1" purpose="$2" from="$3" break_stale="$4"

	r_op_enter
	r_judge_lock

	case "$judged_verdict" in
		free) ;;
		stale)
			if [ "$break_stale" != yes ]; then
				exit "$ExitHeld"
			fi

			r_log_break "$from" "stale, $judged_why" "stale: $judged_why"
			r_discard_lock || r_fail "cannot release the stale $lock_dir"
			;;
		*) exit "$ExitHeld" ;;
	esac

	r_take_lock "$session" "$purpose" "$from"
	r_op_leave
	echo "token=$lock_token"
}

r_cmd_heartbeat() {
	r_op_enter
	r_require_holder "$1" "$2"
	r_beat "$2" || r_fail "cannot refresh the heartbeat of $lock_dir"
	r_op_leave
}

r_cmd_unlock() {
	r_op_enter
	r_require_holder "$1" "$2"
	r_judge_lock

	if [ "$judged_verdict" = live ]; then
		r_fail "refusing: the lock's $judged_job"
	fi

	r_discard_lock || r_fail "cannot release $lock_dir"
	r_op_leave
	echo "mac-host.sh: $1 released the lock"
}

r_cmd_break() {
	local force="$1" from="$2" reason="$3" how

	r_op_enter
	r_judge_lock

	case "$judged_verdict" in
		free) r_fail "the lock is not held" ;;
		live) r_fail "refusing: the lock's $judged_job" ;;
		stale) how="stale, $judged_why" ;;
		held)
			if [ "$force" != force ]; then
				r_fail "the lock is not stale: its heartbeat is ${judged_age:-missing}${judged_age:+s old}, and it goes stale after ${StaleHeartbeatSeconds}s with no live job; pass --force to break it"
			fi

			how="forced, heartbeat ${judged_age:-missing}${judged_age:+s old}"
			;;
		*) r_fail "unknown lock verdict '$judged_verdict'" ;;
	esac

	r_log_break "$from" "$how" "$reason"
	r_discard_lock || r_fail "cannot release $lock_dir"
	r_op_leave
	r_warn "lock broken (logged in $lock_log)"
}

r_cmd_clone() {
	local session="$1" commit="$2" origin="$3" key head

	mkdir -p "$agents_dir"
	r_session_paths "$session" || exit "$ExitFailure"
	[ ! -e "$session_root" ] || r_fail "$session_root exists; 'drop' the session first"
	key="$(r_new_token)"
	mkdir "$session_root"

	if ! r_write_atomic "$session_root/key" "$key" ||
		! git clone --quiet "$origin" "$clone_dir" ||
		! git -C "$clone_dir" checkout --quiet --detach "$commit" ||
		! head="$(git -C "$clone_dir" log --oneline -1)"; then
		rm -rf "$session_root"
		r_fail "cannot clone $origin at $commit; removed $session_root"
	fi

	echo "session-key=$key"
	echo "mac-host.sh: $clone_dir at $head"
}

r_cmd_seed_staging() {
	r_session_paths "$1" || exit "$ExitFailure"
	[ -d "$clone_dir" ] || r_fail "no clone at $clone_dir"
	r_require_session_key "$2"
	r_beat_for_session "$1"
	r_seed_paths
	mkdir -p "${seed_path%/*}"
	printf 'staging=%s\n' "$seed_staging"
}

r_cmd_seed_install() {
	local version

	r_session_paths "$1" || exit "$ExitFailure"
	r_require_session_key "$2"
	r_beat_for_session "$1"
	r_seed_paths
	[ -f "$seed_staging" ] || r_fail "no seed was copied to $seed_staging"
	chmod +x "$seed_staging"
	version="$("$seed_staging" version)" || r_fail "the seed at $seed_staging does not run"
	mv -f "$seed_staging" "$seed_path"
	echo "mac-host.sh: $seed_path is $version"
}

r_cmd_run() {
	local session="$1" token="$2" id base pid job_program
	shift 2

	r_session_paths "$session" || exit "$ExitFailure"
	[ -d "$clone_dir" ] || r_fail "no clone at $clone_dir ('clone' first)"
	mkdir -p "$logs_dir"

	r_op_enter
	r_require_holder "$session" "$token"
	[ ! -e "$lock_dir/job.$token" ] || r_fail "a job was already started under this lock; a lock runs one job, so 'lock' again"
	r_beat "$token"
	id="$(date -u +"$JobIdFormat")-$$"
	base="$logs_dir/$id"
	(set -C; : > "$base.log") || r_fail "cannot create a fresh log at $base.log"
	printf '%s\n' "$id" > "$lock_dir/job.$token"
	job_program="$(r_program r_job_main "$token" "$base" "$clone_dir" "$@")"
	nohup bash -l -c "$job_program" mac-host-job > "$base.log" 2>&1 < /dev/null &
	pid=$!
	r_write_atomic "$base.pid" "$pid"
	r_op_leave
	printf 'job=%s\npid=%s\nlog=%s\n' "$id" "$pid" "$base.log"
}

r_cmd_job() {
	local session="$1" path newest="" base state

	r_session_paths "$session" || exit "$ExitFailure"

	for path in "$logs_dir"/*.log; do
		if [ -e "$path" ] && { [ -z "$newest" ] || [ "$path" -nt "$newest" ]; }; then
			newest="$path"
		fi
	done

	[ -n "$newest" ] || r_fail "no job log for $session"
	base="${newest%.log}"
	state="$(r_job_state "$base")" || true
	echo "JOB ${base##*/}: $state"
	echo "log: $newest"
	tail -n "$JobLogLines" "$newest"
}

r_cmd_drop() {
	local session="$1" credential="$2" path state key="" holds=""

	r_session_paths "$session" || exit "$ExitFailure"
	[ -d "$session_root" ] || r_fail "there is no $session_root"

	if [ -f "$session_root/key" ]; then
		key="$(cat "$session_root/key")"
	fi

	r_op_enter

	for path in "$logs_dir"/*.pid; do
		if [ -e "$path" ] && state="$(r_job_state "${path%.pid}")"; then
			r_fail "refusing: job $(basename "${path%.pid}") is $state"
		fi
	done

	r_judge_lock

	if [ "$judged_verdict" != free ] && [ "$judged_session" = "$session" ]; then
		holds=yes
	fi

	if ! { [ -n "$key" ] && [ "$credential" = "$key" ]; } &&
		! { [ -n "$holds" ] && [ -n "$judged_token" ] && [ "$credential" = "$judged_token" ]; }; then
		r_fail "refusing: that is neither $session's session key nor the token of a lock it holds"
	fi

	if [ -n "$holds" ]; then
		r_discard_lock || r_fail "cannot release $lock_dir"
		echo "mac-host.sh: $session released the lock"
	fi

	r_op_leave
	rm -rf "$session_root"
	echo "mac-host.sh: removed $session_root"
}

remote_script() {
	printf 'set -euo pipefail\n'
	r_program "$@"
}

remote() {
	local status=0

	remote_script "$@" | ssh "${ssh_options[@]}" "$mac_login" 'bash -s' || status=$?

	if [ "$status" -eq "$ExitSshFailed" ]; then
		echo "mac-host.sh: ssh to $mac_login failed (is the Mac awake?)" >&2
		exit "$ExitSshFailed"
	fi

	return "$status"
}

cmd_status() {
	[ $# -eq 0 ] || usage
	connect
	remote r_cmd_status
}

cmd_lock() {
	[ $# -ge 1 ] || usage
	local session="$1" wait=0 break_stale="" argument seconds purpose from deadline status
	local purpose_words=()
	shift
	check_session "$session"

	for argument in "$@"; do
		case "$argument" in
			--wait=*)
				seconds="${argument#--wait=}"
				[[ "$seconds" =~ ^[0-9]+$ ]] || die "--wait takes whole seconds; got '$seconds'"
				wait=$((10#$seconds))
				break_stale=yes
				;;
			*) purpose_words+=("$argument") ;;
		esac
	done

	[ ${#purpose_words[@]} -gt 0 ] || die "lock needs a purpose"
	purpose="${purpose_words[*]}"
	check_single_line "the purpose" "$purpose"
	from="$(hostname)"
	check_single_line "this host's name" "$from"
	connect
	deadline=$(( $(date +%s) + wait ))

	while true; do
		status=0
		remote r_cmd_lock "$session" "$purpose" "$from" "$break_stale" || status=$?

		case "$status" in
			"$ExitSuccess")
				echo "mac-host.sh: $session holds the lock" >&2
				return
				;;
			"$ExitHeld") ;;
			*) exit "$status" ;;
		esac

		if [ "$(date +%s)" -ge "$deadline" ]; then
			echo "mac-host.sh: the lock is held:" >&2
			remote r_cmd_status >&2
			exit "$ExitHeld"
		fi

		sleep "$WaitStepSeconds"
	done
}

cmd_holder() {
	[ $# -eq 3 ] || usage
	check_session "$2"
	check_secret "$3"
	connect
	remote "$1" "$2" "$3"
}

cmd_break() {
	local force=""

	if [ "${1:-}" = "--force" ]; then
		force=force
		shift
	fi

	[ $# -gt 0 ] || die "break needs a reason"
	local reason="$*" from
	check_single_line "the reason" "$reason"
	from="$(hostname)"
	check_single_line "this host's name" "$from"
	connect
	remote r_cmd_break "$force" "$from" "$reason"
}

cmd_clone() {
	[ $# -eq 2 ] || [ $# -eq 3 ] || usage
	local session="$1" commit="$2" option="${3:-}" seeding="" tag="" origin output key staging
	check_session "$session"
	[[ "$commit" != -* && "$commit" != *[[:space:][:cntrl:]]* && -n "$commit" ]] || die "not a commit: '$commit'"

	case "$option" in
		"") ;;
		--seed) seeding=yes ;;
		--seed=*)
			seeding=yes
			tag="${option#--seed=}"
			[[ "$tag" =~ $SeedTagPattern ]] || die "a seed is a release tag such as v0.2.2; got '$tag'"
			;;
		*) usage ;;
	esac

	connect
	origin="$(git -C "$repo_root" remote get-url origin)"

	if [ -n "$seeding" ]; then
		seed_dir="$(mktemp -d)"
		trap 'rm -rf "$seed_dir"' EXIT
		local fetch_arguments=("$MacTarget" "--output=$seed_dir/maxon")
		[ -z "$tag" ] || fetch_arguments+=("$tag")
		"$repo_root/scripts/fetch-seed.sh" "${fetch_arguments[@]}"
	fi

	output="$(remote r_cmd_clone "$session" "$commit" "$origin")"
	printf '%s\n' "$output"

	if [ -n "$seeding" ]; then
		key="$(marked_value session-key "$output")"
		staging="$(marked_value staging "$(remote r_cmd_seed_staging "$session" "$key")")"
		(cd "$seed_dir" && MSYS_NO_PATHCONV=1 scp -q "${ssh_options[@]}" maxon "$scp_login:$staging")
		remote r_cmd_seed_install "$session" "$key"
	fi
}

cmd_run() {
	[ $# -ge 3 ] || usage
	check_session "$1"
	check_secret "$2"
	connect
	remote r_cmd_run "$@"
}

cmd_job() {
	[ $# -eq 1 ] || usage
	check_session "$1"
	connect
	remote r_cmd_job "$1"
}

cmd_drop() {
	[ $# -eq 2 ] || usage
	check_session "$1"
	check_secret "$2"
	connect
	remote r_cmd_drop "$1" "$2"
}

repo_root="$(cd "$(dirname "$0")/.." && pwd)"

[ $# -gt 0 ] || usage
command="$1"
shift

case "$command" in
	status)    cmd_status "$@" ;;
	lock)      cmd_lock "$@" ;;
	heartbeat) cmd_holder r_cmd_heartbeat "$@" ;;
	unlock)    cmd_holder r_cmd_unlock "$@" ;;
	break)     cmd_break "$@" ;;
	clone)     cmd_clone "$@" ;;
	run)       cmd_run "$@" ;;
	job)       cmd_job "$@" ;;
	drop)      cmd_drop "$@" ;;
	*)         usage ;;
esac
