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
