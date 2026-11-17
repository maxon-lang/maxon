# Every operation on a held program's lifecycle answers a typed outcome: a release is refused by name while a stop the session sent is uncollected, and no wait for a program's end expires in silence

Class issue: #313 · Priority: P2

## Stage order

One stage is one local commit, which ticks its box. A stage starts when every stage it waits on is ticked
here and every issue it waits on is closed. A `deferred` stage is skipped. The plan lands once, after its
last stage.

| Done | Stage | Rule it establishes | Waits on | Not beside | Needs a ruling |
|---|---|---|---|---|---|
| [ ] | 1 | The released program's end is awaited in one place, and its expiry is a reply that says the program has not ended |  |  |  |
| [ ] | 2 | A release answers a typed refusal that names its cause, and the ptrace lane refuses to release while a stop it sent is owed | #281, #292 |  |  |
| [ ] | 3 | A held program's end is the session's own reap: a stub's exit packet is a claim, and a stub that exits without handing the program back is a typed outcome | 2, #292 |  |  |

## Problem

An operation on a held program's lifecycle (stop it, release it, end it, reap it, wait for its end) cannot say why it failed. The backends answer `bool`, the one detach answer that can fail carries no payload, and each layer above fills the gap with a budget that expires in silence.

- `PtraceBackend.detach`, `WindowsBackend.detach` and `GdbRemoteBackend.killTheProgram` return `bool`. `backendDetachAnswer` (`DebugStopEvent`) turns the bool into `BackendDetach.accepted` or `BackendDetach.refused`.
- `BackendDetach.refused` has no payload. `GdbRemoteBackend` answers it from eight places with different causes (an unanswered probe, a stub that is gone, a lost connection, a program that ended while `D` was awaited, a reply that was not `OK`, a passed deadline). `DebugEngine.detachSettled` renders every one as the single text `detach-refused`.
- `PtraceBackend.drainOwedStops` returns nothing. At its `GroupStopBudgetMs` deadline it returns with threads still owing a SIGSTOP, and `detach` releases them (#286).
- `DebugConversation.awaitDetachedEnd` and `letItFinish` (`DebugCommands`) each leave their own loop after `DetachRoundCap` rounds and push no reply (#285).
- `GdbRemoteBackend.probedForAnEnd` takes any exit packet as the program's end (`acknowledgedForAnEndedProgram`), and `DebugEngine.endAcknowledgedStub` then reports a program the stub never handed back as `the-stub-still-holds-the-program` after `DebugTarget.awaitHandOver`'s one-second budget, when the stub has already exited (#298).

Because the cause is dropped at the backend, a failure that appears only under load reaches the reader, and a test log, as a generic refusal or as nothing.

## Design

**Every lifecycle operation answers a typed outcome that names its cause, and no wait on a program's lifecycle ends without a reply.**

1. **One refusal vocabulary, owned by the backend interface.** `BackendDetach.refused` carries a `ReleaseRefusal` (the name is the worker's): stops the session sent and did not collect, with the threads; a thread the kernel would not release; a stub that did not answer, with the request; a stub that is gone; a stub that exited still holding the program; a program that ended during the release. `detach` on the ptrace and debug-port lanes and `killTheProgram` answer in the same vocabulary. `backendDetachAnswer` is deleted. `DebugEngine` renders the reason from one table, and the reasons are documented where `detach-refused` is today.
2. **A release is legal only when every stop the session sent is collected.** On the ptrace lane, `drainOwedStops` answers the threads still owing; `detach` refuses, with their ids, before it releases any thread, so the program stays held and the reader can retry or end it. On the debug-stub lane the same precondition is #292's predicate in `beginDetach`. No lane releases a thread with a stop of the session's pending.
3. **One owner for "wait for the program's end within a cap".** `awaitDetachedEnd` and `letItFinish` go through it. On expiry it replies that the program has not ended, adds that it is stopped where the lane can tell, and returns to the prompt. It never resumes or ends a program on expiry.
4. **A program's end is what the session's own reap answers.** An exit packet from a stub is a claim. The status reported is the one the session reaped. A program whose stub exited without handing it back is its own typed outcome, not `the-stub-still-holds-the-program`.

**Prior art.** Delve's process interface returns an error from every lifecycle call and has a typed `ErrProcessExited` carrying the status; its debugserver lane continues a process it finds stopped after a detach. gdb and LLDB collect every stop they sent before detaching, and LLDB funnels every state change through one door (`Process::SetPrivateState`). Maxon adopts the typed outcome and the collect-before-release rule. It does not adopt a single state type in this plan (open question 1).

**Existing rulings.** #281's rulings stand: the SIGCONT backstop in `endAcknowledgedStub`, and no `stopped` answer from the child poll deciding the wait. #292's ruling stands: `D` is sent only from a SIGSTOP stop the session collected. This plan adds no behaviour either refuses; it gives their failure arms names.

### Open questions

1. **Fold the lifecycle state into one machine?** It is spread over `EngineAttachment`, the engine's `stopped`, `pausePending` and `closed`, `DebugTarget.settled` and `DebugConversation.outcome`. No member is shown to come from two of these disagreeing. *Recommended: not in this plan; revisit when first-class state machines (#34) exist.*
2. **#286: refuse the detach, or show the drain cannot expire?** *Recommended: refuse before releasing any thread. A proof that no thread can owe a stop past the budget is not available: a thread that cannot take signals owes it for as long as it likes.*
3. **#298: can the session still reap a program whose stub exited without detaching?** If it can, the real status (137) is reported. If macOS hands the zombie to launchd for good, the outcome is the typed "status lost" and the plan must instead prevent the stub from exiting first. *Recommended: settle it in stage 3's survey, after #292, which replaces the probe step that produces the `$W00`.*

## Stages

1. **The released program's end is awaited in one place, and its expiry is a reply.**
   - Changes: design point 3; a reply kind for "has not ended".
   - Area: `maxon-bin/Debug/Debugger/DebugCommands.maxon` (`awaitDetachedEnd`, `letItFinish`, `finishedARound`), `DebugReply.maxon`, the debugger's `docs/` source.
   - Depends on: none. It shares no file with #281 or #292.
   - Discharges: #285.
   - Shown by: a `tests/debug` case whose released program is stopped by the test and never ends: the session must reply, within the cap, that the program has not ended. Red first (today: silence, then a prompt).
2. **A release answers a typed refusal, and the ptrace lane refuses while a stop is owed.**
   - Changes: design points 1 and 2.
   - Area: `maxon-bin/Debug/Backend/DebugStopEvent.maxon`, `DebugBackendUnion.maxon`, `PtraceBackend.maxon` (`drainOwedStops`, `detach`, `release`), `WindowsBackend.maxon` (`detach`), `GdbRemoteBackend.maxon` (the refusal sites of `beginDetach`, `awaitDetach`, `detachAnswered`, `detachAcknowledged`, `killTheProgram`), `Debugger/DebugEngine.maxon` (`detachSettled`, `askTheKernelToLetGo`, `endAcknowledgedStub`), the reason table's `docs/` source.
   - Depends on: #281's fix and #292's fix, which own `DebugEngine.endAcknowledgedStub` and `GdbRemoteBackend.beginDetach` until they land.
   - Discharges: #286.
   - Shown by: a ptrace-lane case in which a thread cannot take the SIGSTOP within the budget (a candidate: a thread held uninterruptible in a `vfork`), where the detach must be refused naming the thread and the program must stay held. Red first. If no such thread can be built from a test, a backend-level case drives `drainOwedStops` past its deadline. Each existing refusal text keeps a case that reads its new reason.
3. **A held program's end is the session's own reap.** `/plan-and-land`: the survey settles open question 3 with a logged stub.
   - Changes: design point 4. Also settles the second, unverified mechanism: an exit report that arrives during a pre-detach request (`z0`, `M`) is set aside by `sortedPacket` and consulted only by `oneReply`, so a later write fails and the detach is refused without the ending being noticed.
   - Area: `maxon-bin/Debug/Backend/GdbRemoteBackend.maxon` (`probedForAnEnd`, `acknowledgedForAnEndedProgram`, `sortedPacket`), `Debugger/DebugEngine.maxon` (`endAcknowledgedStub`), `Debugger/DebugTarget.maxon` (`awaitHandOver`), `tests/debug`.
   - Depends on: stage 2; #292.
   - Discharges: #298.
   - Shown by: `tests/debug/a-detach-right-after-a-kill-of-a-held-program-reports-its-end.maxtest` under load, and a deterministic stub-proxy case that answers the probe step with `$W00` and ends the stub: the session must report the reaped status or the typed lost-status outcome, and leave no zombie.

## Members

- #285 — a session that gives up waiting for a released program's end says nothing. Stage 1.
- #286 — a ptrace detach that cannot collect a stop it sent releases the thread anyway. Stage 2. No repro exists; the pending SIGSTOP lands after the detach and stops the program, the Linux form of #281's end state.
- #298 — on arm64-macos, a detach right after a kill of a held program is refused after about 11 s. Stage 3. Carried from it:
  - The probe step comes back as `thread_suspend ... object terminated`; debugserver sets the state exited without reaping, sends `$W00` (not the real status) and exits; `awaitHandOver` gets ECHILD for its whole budget; `quit` then reports os error 5.
  - Seen: `tests/debug/a-detach-right-after-a-kill-of-a-held-program-reports-its-end.maxtest` failed at line 34 after about 11 s in 3 of 66 runs under 12 busy loops on 8 cores; each failure left a zombie under launchd. About 1 run in 22.
  - Expected: the real status (137), reaped by the session, no zombie.

## Related, not folded

- #281 — a live red lane with its own ruling (the SIGCONT backstop). It lands on its own. This plan absorbs only its follow-up: its failure arm gets a name in stage 2.
- #292 — P0, a flaky failure of a CI case, with its own ruling and plan (`D` only from a SIGSTOP stop the session collected). It lands first; stages 2 and 3 depend on it.
- #300 — the test stub proxy matches a gdb-remote reply within one read. A framing fault in a test fixture (`Relay.step`), with its own fix.
- #301 — the debug test harness hard-codes Linux's SIGSTOP. A duplicated constant; its fix is one call into the stdlib entry #281's fix adds.
- #294 — which hosts provide a debug-host entry is decided by the stdlib's `#if os` gates and by the compiler's facility table. It belongs with #220 and #153, which edit the same table.
- #6 — the Windows process listing once took over 30 s in the VS Code suite. No mechanism is known; nothing ties it to the debug host's lifecycle.

## Assumed, not read

- Delve's, gdb's and LLDB's behaviour is stated from their documentation and from #281's ruling, not re-read in source.
- `WindowsBackend.detach`'s failure causes.
- That a thread in `vfork` is reachable from a `tests/debug` fixture.
