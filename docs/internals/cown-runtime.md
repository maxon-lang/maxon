---
title: Cown Runtime
description: How a service runs on a cown - the cown record, the behaviour chain, the gate word, the batched runner, waiting senders, close, the multi-cown acquire and how the scheduler sees an idle cown.
sidebar:
  order: 9
---

What a service means for a user is in [Services](../LANGUAGE_REFERENCE.md#services--spawn) and
[Mailboxes and failed sends](../LANGUAGE_REFERENCE.md#mailboxes-and-failed-sends). This page is the map of
the runtime underneath: which record holds a service, how a send reaches it, who runs its messages and when
it stops.

## What a service is

A service is a **cown**: a runtime record that owns the service's state and a queue of **behaviours**. No
green thread belongs to a service. A send appends a behaviour, and when the cown is idle that send starts a
**runner**, an ordinary green thread that drains the queue and ends. Two sends to different cowns run on
different processors; two sends to one cown run one after the other, in the order they were appended.

| File | What it holds |
| --- | --- |
| `Compiler/Runtime/CownRuntime.maxon` | The emitted runtime: `__cown_spawn`, `__cown_send`, `__cown_send_wait`, `__cown_run`, `__cown_close`, the handle drop and clone, and the multi-cown acquire |
| `Compiler/Runtime/CownInterface.maxon` | The interface handle: a box holding a cown and a witness table, and the per-requirement send stubs |
| `Compiler/Runtime/CownRegistry.maxon` | The service slots of `default` keys, read and written under the registry's lock |
| `Compiler/Runtime/ServiceBehaviour.maxon` | `<T>.__behave` and `<T>.__finish`, synthesized per service at the Maxon tier |
| `Compiler/ServiceCompanions.maxon` | The `<T>.request` union and the `<T>.handle` type |
| `Compiler/Runtime/GtRuntime.maxon`, `SchedRuntime.maxon` | The green-thread scheduler a runner is scheduled on |

Only the context switch is per target (`Compiler/Targets/X64/X64GtRuntime.maxon`,
`Compiler/Targets/Arm64/Arm64GtRuntime.maxon`). The queue, gate and runner logic is shared.

## The cown record

The record starts with the words below, in this order, followed by the lock.

| Word | Holds |
| --- | --- |
| `CownOffLast` | The tail of the behaviour chain, or 0 when no behaviour is queued or running |
| `CownOffGate` | The gate word (below) |
| `CownOffShares` | Handle count in the high half, reference count in the low half. Dropping the last handle closes the cown, and the cown is freed when its last reference is given back |
| `CownOffCapacity` | The mailbox capacity, from `spawn(capacity:)` |
| `CownOffSlotBytes` | The byte size of a message behaviour record, set at spawn |
| `CownOffState` | A pointer to the service's state record, 0 until the start behaviour stores it |
| `CownOffBehave`, `CownOffFinish` | `<T>.__behave` and `<T>.__finish` |
| `CownOffSendNext`, `CownOffSendPrev` | The queue of senders parked for room |
| `CownOffLock` | The lock that guards the sender queue |

A **behaviour** is a record of a next pointer, a code word, a kind and a payload. The kinds are `message`,
`start` (the factory), `finish` (drop the state) and `acquire` (a slot of a multi-cown acquire).

## Sending: the behaviour chain

The chain is a queue in the style of an MCS lock. A sender swaps itself in as the new tail
(`CownOffLast`); if the old tail was 0 the cown was idle and the sender schedules a runner, and otherwise it
stores its record into the old tail's next pointer. The runner follows next pointers.

- The swap is a load and compare-and-swap loop, and the next-pointer store is a store-release that pairs with
  the runner's load-acquire of the same word. Every cross-thread edge in the runtime is one of
  compare-and-swap, store-release and load-acquire.
- Between the tail swap and the next-pointer store the publisher cannot be stopped, so a runner spinning on a
  next pointer waits a bounded time. Every publisher is a symbol that asynchronous preemption never stops: an
  unguarded function, or one that pins its thread. The runner's own finish claim publishes through
  `__cown_enqueue_finish` rather than inline in `__cown_run`, which is stoppable, and the builder refuses to
  emit a publish into a function that could be stopped. `__cown_acquire_release` and `__cown_acquire_many` spin on
  these links and must never gain a park point.
- A behaviour record is freed only after the runner has decided to release or hand on, so a late reader of a
  next pointer never reads freed memory. The compare-and-swap that sets `CownOffLast` from the current
  behaviour to 0 can succeed on an address that was reused, and that is harmless: success means that address
  is the current tail.
- The handler runs `<T>.__behave(cown, request)`, which loads the state from `CownOffState`, switches on the
  request's tag and calls the handler.
- The start behaviour stores `CownOffState` last. A send or a close from the awaiting spawner links behind the
  running start behaviour, so no behaviour reads the state before the factory has returned. A factory that
  throws stores nothing, `__finish` then finds the state word 0, and the runner skips it.

## The gate word

`CownOffGate` packs the cown's admission state into one word, so a decision and its update are one
compare-and-swap.

| Bits | Name | Meaning |
| --- | --- | --- |
| 62 | Closed | `shutdown()` or the last handle drop closed the cown; later sends fail `serviceStopped` |
| 61 | FinishClaimed | One caller owns enqueueing the finish behaviour |
| 60 | Waiters | At least one sender is parked for room |
| 0 to 59 | queued | Messages reserved and not yet started |

`queued` counts a message from its reservation until its handler starts, so a capacity of N admits exactly N
queued sends and the message being handled does not count. The start and finish behaviours are outside the
count.

- A send reserves with a compare-and-swap that refuses at `queued == capacity`; the caller then fails
  `mailboxFull` or, with a timeout, parks.
- Admitting a waiting sender hands it the slot the runner just freed: the gate is not decremented, so a later
  send that does not wait sees a full gate and cannot overtake the waiters.
- Waiters is set only under the cown lock, after a compare-and-swap that proved the gate full. A runner that
  finds no waiter clears the bit in the same compare-and-swap as its decrement.
- Claim and decrement share one compare-and-swap. A close with `Closed` and `queued == 0`, and a message start
  with `Closed` and `queued == 1`, set FinishClaimed in the swap that closes or decrements, so there is no
  second claim step.

## Finish

The finish behaviour is always enqueued at the tail and never run inline, so an `acquire` slot already queued
behind the last message still runs against live state. It runs `<T>.__finish`, which drops the state, and the
runner stores 0 into `CownOffState`. A sticky `finished` flag in the runner records that the finish ran: after
the final compare-and-swap of `CownOffLast` from the finished cown's last behaviour to 0, the runner gives back
the cown's own reference and frees the cown if that was the last.

## The runner

`__cown_run` runs one cown's queue.

- It takes at most 64 messages per turn (`CownBatchMessages`), then calls `__gt_resched` on the same green
  thread, still holding the cown, so every other runnable service runs before it continues. When the queue is
  empty it ends.
- It ends with `__gt_owner_done` on its own green thread: a runner is its own consumer, so nothing else joins it.
- A handler that sleeps or does I/O parks the runner's green thread with the cown held. Messages queue behind it
  up to the capacity. No handler waits on another party: the compiler refuses a message that may (E3221).
- A runner that reaches an `acquire` behaviour decrements the group's count and ends without releasing the
  cown; the party that brings the count to 0 runs the group's code, and the group's cowns stay held until
  `__cown_acquire_release`.

## Parked senders

`__cown_send_wait` is the send given a positive `sendTimeout:`. It re-tries the reservation, and on a full gate
takes the cown lock, appends the sending green thread to the cown's sender queue, sets Waiters and parks with
`GtParkKind.cownSend`. A runner that frees a slot takes the first waiter, delivers its message into that slot
and readies it. A timeout, a dropped promise or a close removes the waiter and wakes it with a status
(`CownSendWake`: `delivered`, `cancelled`, `stopped`, `timedOut`).

A parked sender is on no timer or I/O store, which is sound: it parks only while `queued == capacity > 0`, so
a runner exists that will free a slot. Cowns that fill each other and wait on each other form a real deadlock,
and the scheduler reports it as exit 92 or, when green threads leak, 75.

## Close

`__cown_close` runs when the last handle drops or on `shutdown()`. It sets Closed, wakes every parked sender
with `stopped`. When no message is queued it enqueues the finish behaviour itself; otherwise the runner that
starts the last queued message does, so queued messages run first.

## Stack guards

A green thread's stack grows on demand, so a runtime function that runs on one carries a stack guard
(`needsGreenThreadStackGuard`). `__cown_run` is guarded and may be stopped and resumed on another machine,
because it holds no runtime lock at any program counter of its own. `__cown_close`, `__cown_send_wait`,
`__cown_reply_refused` and the green-thread spawn are guarded **and pinned** (`pinsItsThread`): runtime code
may reach them while it holds a lock, so their guard never parks and the body is never stopped asynchronously.
They are guarded rather than left unguarded because their inlined enqueue, schedule and ready frames exceed the
stack margin an unguarded runtime function may use, which the compiler reports as E5004.

## The multi-cown acquire

`__cown_acquire(cowns, n)` is what an `acquire` block calls. It answers the group once every named cown is held,
or `AcquireStoppedBit` with the position of a stopped cown in the low bits; a caller that was cancelled while
parked answers stopped too. It parks the calling green thread (`GtParkKind.cownAcquire`) until the group is
granted, so the thread's machine runs other work meanwhile. After the grant it checks each cown's state again:
a cown whose state has been finished releases the group and answers stopped.

`__cown_acquire_many(cowns, n, code)` takes several cowns for one group so that `code(group)` runs with all of
them.

1. **Phase 0** sorts the cowns by address and collapses duplicates.
2. **Lock** takes each cown's exclusive lock in ascending address order.
3. **Phase 1** swaps the group's slot into each cown's tail. The locks make a group's slots appear in every
   cown's queue together, so two groups that need the same two cowns never enqueue in opposite orders. Each
   lock is released once the slots are in.
4. **Phase 2** subtracts the granted count plus the bias of 1 from the group's count. The party that brings the
   count to 0 runs `code(group)`.

For an `acquire` block `code` is `__cown_acquire_grant`: it hands the group to the parked acquirer, or, for an
acquirer that has abandoned the group, releases it. The block ends with `__cown_acquire_release(group)`, which
hands each cown to its next behaviour; every cown of the group stays held until then. The acquire-exit path does
not give back the cown's reference, so an acquire is correct only on a cown that is not closed.

`CownTorture.violations(cownCount, workers:, rounds:)` in `stdlib/Testing.maxon` drives the same door an
`acquire` block uses, with sends and a stopped service in the mix
([STDLIB_REFERENCE](../STDLIB_REFERENCE.md#cowntorture)). `cownCount` must be a power of two.

## How the scheduler sees a cown

The scheduler needs no cown-specific state. An idle cown owns no green thread, and a cown with queued work
always has one of: a runner that is running, runnable or parked on a store, or a sender between its tail swap
and `__gt_ready`, which is itself running or runnable. Each is already a term of the scheduler's exit
condition, so `__gt_drain_at_exit` waits for queued messages without knowing cowns exist.

`__gt_drain_at_exit` releases the registry and the global cleanup after the drain, which can close an idle cown.
The close spawns a finish runner from the main thread's scheduler context, and the spawn's trace-id mint is safe
there because it needs no processor.

`GtParkKind.cownSend` is the park kind of a parked sender, and `GtParkKind.cownAcquire` of a parked acquirer.

## Where to look when it breaks

- A message handled twice, or never: the gate's `queued` arithmetic and the compare-and-swap that sets
  FinishClaimed. `scripts/multicore-stress/cown-close-race-torture.maxon` races `shutdown()` against a burst.
- A send delivered out of order: the tail swap and next-pointer pairing.
  `cown-handoff-torture.maxon` checks per-sender order across a handoff.
- One service starving another at one processor: the batch of 64 and the `__gt_resched` after it.
  `cown-batch-fairness-torture.maxon` pins it.
- A waiting sender that is lost, doubled or overtaken: the Waiters bit and the slot transfer.
  `cown-send-wait-torture.maxon` pins delivered plus refused equals attempted.
- Two groups in one cown at once: the address order and the cown locks. `cown-multi-torture.maxon`.
