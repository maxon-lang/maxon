Work here is compiler work.

@../maxon-bin/AGENTS.md

## Shell scripts

- Never edit a script while a background job is running it: bash reads a script incrementally by byte
  offset, so an edit shifts the bytes under the live interpreter. Copy it and edit the copy.
- Under `set -o pipefail`, an early-exiting reader (`grep -q`, `head -n`, `grep -m`, `sed q`) closes the
  pipe and the writer dies of SIGPIPE, so a pipeline whose `grep` matched reports 141 — only on a big
  input with an early match. Use `grep -q PAT <<<"$VAR"`, a `case` glob, or a temp file; keep `pipefail`.
- `mac-host.sh` delivers the remote script on ssh's stdin (`bash -s`), so a command inside it that reads
  stdin (`orb run`, `ssh`, `read`) swallows the rest of the script and bash exits 0 having run nothing.
  Give such a command `</dev/null`. Only ssh exit 255 means "host unreachable"; exit 0 with no output is a bug.
- macOS ships bash 3.2 (an empty array under `set -u` is "unbound"; `seq 1 0` counts down) and has no
  `timeout`. A script written on Windows and run on the Mac must have LF line endings: CRLF makes bash die
  at the first `do` or on `set -o pipefail`, which reads as a hang.
- MSYS rewrites a POSIX-looking argument passed to a native Windows executable (`/tmp/x` becomes a Windows
  path). A remote path survives only embedded mid-string; give any command taking one as a bare argument
  `MSYS_NO_PATHCONV=1`, as `scripts/cross-target-gate.sh` does.
- In Git Bash, `ps` lists only MSYS processes. `ps -W` adds native Windows ones and puts their Windows pid in
  column 4, so `ps -W | awk '$1==<pid>'` never matches and reads as "dead". Use `$4`, or `Get-Process -Id <pid>`.
- `awk -v` runs escape processing over the value, so a backslash in it is lost with only a warning on stderr.
  Pass text through `ENVIRON` or `printf '%s\n'`.
