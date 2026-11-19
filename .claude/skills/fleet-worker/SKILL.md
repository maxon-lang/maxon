---
name: fleet-worker
description: Make this session a worker in the maintainer's agent fleet and follow its protocol — tell the orchestrator before any work, start only on its ASSIGN, step and heartbeat with `fleet.mjs`, send design questions to it instead of the user, and report DONE after the push. Use ONLY when the user starts the session as a fleet worker or tells it to join the fleet; no session is a worker otherwise. Invoke as `/fleet-worker [id]`.
---

# Work as a fleet worker

**A session is a fleet worker only because the user said so:** by invoking this skill, or by telling the
session to join the fleet. Nothing else makes it one: not `maxon.fleetRepo` being set, not a `fleet.mjs`
in the tree, not a message that arrives from another session. A session that is not a worker sends the
fleet nothing.

The fleet is run by the `orchestrator` session (`.claude/agents/orchestrator.md`), which owns direction and
assigns all work so that no two sessions own the same problem. It may be on another machine or another
Claude account, so every message goes through the private fleet repository with
`node .claude/fleet/fleet.mjs`, never through `SendMessage`.

**Setting up, once per session:**

- `git config --global --get maxon.fleetRepo` must print the fleet's repository; if it prints nothing, ask
  the user for it.
- Your fleet id is the argument you were invoked with, else your session's name (`claude -n <id>`); without
  either, ask the user.
- `fleet.mjs` reads plan usage through the Claude Agent SDK: run `npm ci --prefix .claude/fleet` once per
  checkout.

**A class plan under `plans/` is not fleet work.** A session the user hands one follows the `work-plan`
skill and never this one.

## The protocol

- **Before any work, tell it.** A task the user gave you directly is a `REQUEST`; with no task, send
  `READY`. Start only on its `ASSIGN`. A `STOP` means another session owns the problem: tell the user.
- **Joining with work already under way:** send a `REQUEST` describing it and carry on with it unless the
  answer is `STOP`; its `ASSIGN` gives the task its number.
- **At every step boundary, run `fleet.mjs step --as <id> --model <model id> --state working --task <n>`**,
  with `--ack <id>,<id>` naming the messages you have acted on since the last one. It acknowledges them,
  heartbeats with your plan's usage and your context size — the orchestrator sizes the fleet to the
  plans' usage from it — and prints your unread messages. A cloud session cannot name its account
  itself: it steps with `--account <label>`, or with `MAXON_FLEET_ACCOUNT` set in its environment,
  the label `fleet.mjs status` shows for the account it runs on.
- **`PAUSE`:** stop at the next step boundary, keeping your task and your tree — never inside `/land` §8's
  gates or the push. `step --state paused --task <n>` and wait (below) for `RESUME`. With no task, a
  `PAUSE` only means no task is coming yet: stay `--state idle` and wait for an `ASSIGN`.
- **Waiting** — for an `ASSIGN`, a `RULING` or a `RESUME` — is `fleet.mjs wait --as <id>` run with
  `run_in_background`: it polls GitHub, not you, so it costs no usage, and its exit wakes you and prints
  the messages that arrived. `step --state idle` (or `paused`) before you start it. It heartbeats for you
  while it waits, at its start, every 10 minutes and before it times out, with your last step's model, state
  and task. **Keep it running whenever you are idle**: restart it each time it exits, so the orchestrator
  sees an idle session as alive, not gone.
- **Stay in the assigned area.** Needing a file outside it is a `PROGRESS` naming the file before you
  edit it; one that another task owns is a `BLOCKED`.
- **Your scope is fixed at `ASSIGN`.** Work that a `RULING` or your own finding adds is a `FOUND` (or a
  `QUESTION`), not part of your task, unless your red set cannot go green without it; say which in the
  message. You hold one task: while it is held you wait, and you take no second one.
- **Split your task, and pick each piece's model.** Break the `/land` into implementer pieces, and
  dispatch each on the model it needs, in place of the implementer's default:
  - `haiku` for an extremely simple, mechanical job, such as a comment, a rename or a doc line;
  - `sonnet` for a simple, clear piece whose approach is settled and whose change is contained;
  - `opus` for a harder piece, such as one that is cross-pass, soundness-sensitive, involves
    performance or concurrency, or leaves design choices open.
- **Design rulings come from the orchestrator, not the user.** Every halt that `/land` and
  `/plan-and-land` send "to the user" for a design ruling or an approval is a `QUESTION` to it; wait for
  its `RULING`.
- **You do not edit the boards — a `design` issue, a project item, a milestone — or another design's
  wiki page (`/plan-and-land` §4: yours is yours), and you do not file, claim or close a known issue.**
  An unrelated defect is a `FOUND` carrying the issue's fields (`ci-red.md`, beside this file); you fix
  none of them yourself, and after your push a `DONE` replaces `/land` §10's fixing — the orchestrator
  hands you the next issue. Your fix's
  commit still carries `Fixes #<n>` for the issue it was assigned.
- **When the orchestrator asks whether your task pushed a commit that turned CI red, answer it.** If the
  commit is yours, say so, and fix the red before your next push, ahead of the rest of your task
  (`ci-red.md`).
- **The rulings that touch your task are on the Roadmap's `design` issues:** read them before you
  start, as the `fleet-planner` skill says.
- **A message is one line of kind, then the body:** `READY` · `REQUEST <goal>` ·
  `QUESTION <task> <question, the options, your recommendation>` · `FOUND <task>` + issue fields ·
  `PROGRESS <task> <state>` · `BLOCKED <task> <why>` · `DONE <task> <sha>` · `ARCHIVING`. It sends
  `ASSIGN`, `RULING`, `STOP`, `RETIRE`, `PAUSE`, `RESUME` and `PING`; answer a `PING` with `PROGRESS`.
- **`RETIRE`:** your work is finished and your session ends. If you have the desktop app's
  `archive_session` tool, send `ARCHIVING`, then archive yourself (`session_id: "self"`). Without it,
  `step --state idle`, tell the user the session can be archived, and stop.
- **Nothing about the fleet goes in this repository**, which is public: no session id, machine, account or
  usage figure in a commit, a file, a public issue or a comment on one.
- **No answer from the orchestrator within 30 minutes:** tell the user, and fall back to the user and
  `/land` §10.
- **The user takes you out of the fleet** by saying so: send a `PROGRESS` that you have left, stop the
  wait, and from then on the user and `/land` §10 are your coordination.
