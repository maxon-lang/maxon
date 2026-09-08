## What this changes

<!-- What behaviour is different afterwards, in a sentence or two. -->

## The spec

<!--
⛔ A BEHAVIOUR CHANGE CARRIES A SPEC CHANGE. `specs/` is executable: each case is a Maxon program with
its output pinned, and it is how this project states what the language DOES. A change with no spec is
a change nothing will notice when it regresses.

Name the case(s) you added or changed. If this genuinely changes no behaviour — a comment, a script,
a doc — say that instead.
-->

## Did you watch it fail first?

<!--
A test that has never been red is a test that has never been shown to test anything. Say what the new
case did BEFORE the fix, and what it does now.
-->

## Checks

- [ ] `./maxon-bin/.maxon/maxon spec-test` — 0 failed
- [ ] `./scripts/fixpoint.sh` — the compiler still reproduces itself byte-for-byte
- [ ] New or changed comments describe the PRESENT, with no history in them
