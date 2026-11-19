# Known issues and CI reds in the fleet

The orchestrator files, claims and assigns every known issue (the `file-issue` and `assign-task` skills).
A worker's part is the `FOUND` that reports one and the answer when a red is asked about.

## The fields a `FOUND` carries

An unrelated defect (`/land` §10's test) becomes an issue labelled `bug` and `accepted` plus its area
(`compiler`, `codegen`, `runtime`, `stdlib`, `tooling`, `performance`). The `FOUND` gives the orchestrator
what the issue needs:

- **Title:** the one-line symptom.
- **Where:** `file:line`.
- **Seen:** how, and by which change.
- **Repro:** a program that reproduces it: the fixing land's red set.

## CI reds — the `ci-red` label, and the culprit takes its own

**A red lane on main is an issue labelled `ci-red`, `bug`, `accepted` and `priority: high`**, assigned
ahead of every other. Its **Repro** is the run URL, the lane and the failing case; its **Suspects** are the
commits between the last green run and the first red one (`gh run list`, then `git log <green>..<red>`).

**The orchestrator asks the culprit** with one message to every live session: the lane, the failing case,
the run URL and the suspect shas, and *"Did your current task push one of these? If so, take this
issue."*

**A worker asked that answers it.** If the commit is yours, say so: the orchestrator assigns you the
issue, and you fix the red before your next push, ahead of the rest of your task. A worker that meets a
red on main itself sends a `FOUND` naming the run and the lane, and files and claims nothing.

**When every answer is "not mine", or none has come by the orchestrator's next wake,** it assigns the
issue to the next free worker, ahead of everything else.
