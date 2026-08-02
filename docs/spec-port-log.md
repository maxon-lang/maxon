# Spec-port log

The dated trend of the `/spec-port` loop — one row per tick, read downwards. It records **what the loop
did**, the way `docs/optimization-log.md` records what the compiler's cost did. It is a TREND, not a
backlog: it never holds work. Deferred *work* goes to `maxon-shv2/PLAN.md` like every other deferral in
this project.

**The one thing it is load-bearing for:** a `DEFERRED` row is what stops the next tick from picking that
spec up again. The selector in `.claude/skills/spec-port/SKILL.md` §"the ordering" skips any spec named
in the first column, so a spec that needs a rung is retired from the loop until that rung lands — at
which point you delete its row and the loop takes it on the next tick.

The order the loop walks is `maxon-selfhosted/Testing/SpecTestRunner.maxon`'s `whitelist` — ~265 names
ordered easiest-first by the v1 loop that earned the ordering.

`cases` is *active cases passing / `<!-- test:` markers in the file*. The two differ exactly by the
shelved (`disabled-test:`) cases, and every shelved case's reason lives in the commit that shelved it.

| spec | date | outcome | cases | note |
|------|------|---------|-------|------|
| print-function | 2026-08-01 | PORTED | 4/4 | No compiler change: string interpolation already handled every case (`{x}`, `{10 + 5}`, repeated calls, two holes + literal text in one string). Suite 3258 → 3262. Goldens read and balanced; the ```stdout gate falsified by sabotage before the tick was closed. |
| panic | 2026-08-01 | PORTED | 4/4 | A user `panic("…")` dropped its message and exited 134 — while shv2's OWN range-check panic already printed a located message + stack trace and exited 1. One runtime, one door unwired. Now routed through the same `mrt_panic` (`StdOp.osRangePanic` → `osPanic`, two producers now), message baked at parse time; `RuntimeAbort.userPanic = 134` deleted as a dead claim. 338 goldens moved — the panic block went 4 instructions → 2 wherever stdlib panics appear. Suite 3262 → 3266. All 4 ported cases are literals and all 4 pass — nothing diverged. Interpolated holes still render `{}`; that is pinned by `panic-interpolation` (a separate unported spec), taken as tick 3. |
| function-declaration | 2026-08-01 | PORTED | 6/6 | A missing `returns` keyword (`function foo() int`) was not caught at the signature: `parseOptionalReturnType` reads "no `returns`" as VOID, so the header ended at `)` and the `int` fell into the body, dying as `E2015 Unsupported: int statement` instead of the spec's `E2001 unexpected token: 'int'`. New `requireSignatureLineEnd` at the real-parse site (not the shared helper, which runs before `throws`, and not the tolerant sweep, which would swallow it). Existing `matchExpressionStrayToken` generalised to `strayToken` — same E2001, same sentence, two raisers now — and the token-display fact deduplicated into `Lexer.tokenDisplayText` (3 open-coded readers → 1). Suite 3266 → 3272, exe −4,608 B against a measured control. Nothing shelved. Review found 5 code-quality issues, all fixed before the commit; the same-shaped hole in `parseInterfaceMethod` is left to the spec that covers it (§3b). |
