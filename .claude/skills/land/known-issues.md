# `/land` §10 outside the fleet: filing, CI reds and draining

In the maintainer's fleet the orchestrator does all of this (`/land` §10). Everywhere else, the
coordinator does. Which defects are related, and the issue's fields, are in `/land` §10.

## Filing an unrelated defect

- **Search before filing:** `gh issue list -R maxon-lang/maxon --state open --search "<file or symptom>"`.
  An open issue that already records it gets at most a comment with the new sighting, never a twin.
- **You, the coordinator, file it** the moment a finding arrives. Implementers and reviewers report to
  you; they do not file.
- `gh issue create -R maxon-lang/maxon --label bug --label accepted --label <area> --title <symptom>
  --body-file <file>`, the body written in your scratchpad, then put it on the Roadmap as free to take:
  `node .claude/roadmap/status.mjs <n> Ready`. Nothing touches the tree.
- Then go on with the change: recording is the whole of what an unrelated defect costs it.

## CI reds — the `ci-red` label, and the culprit takes its own

**A red lane on main is an issue labelled `ci-red`, `bug`, `accepted` and `priority: high`**, drained
before every other. Its **Repro** is the run URL, the lane and the failing case; its **Suspects** are the
commits between the last green run and the first red one (`gh run list`, then `git log <green>..<red>`).

**Whenever you meet a red** — §9's glance, or one reported to you — look for its issue first
(`gh issue list -R maxon-lang/maxon --label ci-red --state open`):

- **Assigned to someone else:** it is theirs. Do not fix it; push.
- **Not filed:** file it, then ask the culprit before claiming it.
- **Filed and unassigned:** ask the culprit before claiming it.

**Ask the culprit.** `ListAgents`, then one `SendMessage` to every session that can answer (skip cloud
sessions, which cannot): the lane, the failing case, the run URL and the suspect shas, and *"Did your
current task push one of these? If so, take this issue."* An agent whose commit it is claims the issue,
answers that it has, and fixes the red before its next push. Read answers with `ReadNotifications` while
you go on with your own work — at §9's glance, that is your push. **When every answer is "not mine", or
none has come after 10 minutes, claim it yourself** unless someone has by then, and land it before
anything else in the drain.

## Draining — after the push, ONE issue at a time

Once your push has landed, drain the open issues labelled `accepted` and `bug` with no assignee:

1. **Pick ONE:** the oldest unassigned `ci-red`
   (`gh issue list -R maxon-lang/maxon --state open --label ci-red --search "no:assignee sort:created-asc"`),
   else the oldest unassigned `accepted` `bug`. An issue assigned more than 24 hours ago with no comment or
   commit since is abandoned: unassign it, set it back to `Ready`, and treat it as open.
2. **Claim it:** `gh issue edit <n> --add-assignee @me`, `node .claude/roadmap/status.mjs <n> "In progress"`
   (the project board is where everyone sees what is being implemented), then a comment
   `Claimed by <session>`. Every session on one machine shares one GitHub login, so re-read the comments:
   if an earlier `Claimed by` than yours stands since the last unassignment, someone moved first — delete
   your comment and go back to 1. A `ci-red` issue is claimed only after asking the culprit.
3. **Land it as its own `/land`**, its repro the red set, its commit message carrying `Fixes #<n>` so the
   push closes it. A defect found while landing it takes the same RELATED test: related to this issue,
   fixed in this land; unrelated, filed.
4. **Repeat until nothing is claimable.** Then the task is done; one report names every sha.

**Hold one claim at a time**: a slow issue then blocks only itself.

- An issue whose repro will not go red was fixed on the way: close it with a comment naming the sha that
  fixed it.
- ⛔ **The queue is not `todo.md` and not a memory note**: it is a work queue, and draining it is part of
  the task.
- **An issue that needs a design ruling is a question** to the user, asked when its turn comes — never a
  skipped issue.
