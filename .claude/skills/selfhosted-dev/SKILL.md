---
name: selfhosted-dev
description: Implement the self hosted compiler
---

We develop the self-hosted Maxon compiler by enabling deferred spec tests **one at a time** and fixing every failure to green before moving on. This keeps progress incremental and makes "what works" provable at each step.

Each iteration enables exactly **one** deferred spec, drives the full suite to green on both targets, passes a code-review gate, and commits — then repeats. This is the same Scout → Fix → Verify → Review → Commit process the `selfhost-loop` workflow runs autonomously; here you run it yourself in the conversation, one iteration per request (or repeatedly until blocked).

Prefer the `maxon-dev` MCP tools for all build/test commands (see CLAUDE.md for the full mapping). They are faster and return structured output. Only fall back to raw Bash when no MCP tool covers the case.

## The whitelist

The set of enabled specs is the `whitelist = [ ... ]` array in `maxon-selfhosted/Testing/SpecTestRunner.maxon`.

- Entries **above** the `DEFERRED SPECS` header and any live `"name",` entries after it are **already enabled and passing** — do not touch them.
- A spec is **deferred** only when it appears as a **commented** entry, e.g. `// "spec-name" (8/9) — note`. These commented entries (ordered roughly easiest-first) are the only candidates to enable.
- **No deferred spec is off-limits.** Every commented spec must eventually be enabled and fixed — including ones whose notes say they HANG the runner or are slow/docs-only (e.g. `with-iterator`, `generic-module-merge-*`, `memory-safety`, `refcount`, `async-filesystem`). A hang is a **real compiler bug** (infinite loop / non-terminating worker / fragment that never returns), not a reason to skip. The runner enforces a per-test wall-clock timeout, so a hang surfaces as a failing/timed-out fragment you must root-cause — never route around it, never re-disable it.

## Process — one iteration

Read `docs/WRITING_MAXON_CODE.md` first if you haven't this session.

### 1. Scout — pick the next spec (prefer what blocks self-hosting)

The goal of this whole effort is to make the self-hosted compiler compile **itself**. So the spec to enable is, by strong preference, **the one that unblocks self-compilation** — not just the next one in the list. Selection is therefore error-directed first:

1. **Surface the real blocker.** Build target `selfhosted` (`mcp__maxon-dev__build`), then have the self-hosted compiler compile its own source under `maxon-selfhosted/`. Capture the first/most-relevant compiler error (4-digit code + message); use `mcp__maxon-dev__lookup_error_code` to decode it.
2. **Map the blocker to a deferred spec and enable THAT one.** If the self-compile error implicates a language feature exercised by one of the deferred specs, that spec is the pick — it's directly on the critical path to self-hosting. Read candidate `specs/<name>.md` files to confirm the match, and record which self-compile error the spec unblocks (this becomes the commit one-liner). Prefer this even when the matching spec sits lower in the file than other untried entries; clearing the actual blocker matters more than file order.
3. **Fallback — only when nothing maps.** If self-compile succeeds, or its error maps to no deferred spec, fall back to the **topmost** commented deferred spec in file order. This is the genuine fallback, not the default: reach for it only after step 2 finds no blocking spec.

Enable the chosen spec by uncommenting its entry — turn `// "spec-name" — note` into a live `"spec-name",` array element. Preserve any explanatory note as a trailing/leading comment if it still applies.

### 2. Fix — drive the full suite to green

Make the **full** self-hosted suite pass on **both** `x64-windows` and `wasm32-wasi` by implementing real compiler functionality.

Many deferred specs are "simply untried" rather than known-broken, so **some pass the moment they're enabled** with no compiler change at all. That's a valid outcome, not a reason to be suspicious or to invent work — but it does **not** skip any later step. A no-fix iteration still runs the full unfiltered suite on both targets (step 2.3), still goes through independent Verify (step 3), still passes the Review gate (step 4 — even a one-line whitelist diff gets reviewed), and still commits (step 5). Build first, run the spec filtered, and only treat it as a no-fix pass once the **full** suite is also green on both targets. If you make zero source changes, note that explicitly in the fix report and the commit one-liner (e.g. `enable <spec> — passes as-is, no compiler change needed`).

1. Read `specs/<spec>.md` for the expected behavior. Read `maxon-selfhosted/ARCHITECTURE.md` if you need the compiler map.
2. Rebuild: `mcp__maxon-dev__build` with `target: "selfhosted"` (use `"both"` if you change the C# bootstrap).
3. Run the suite with `mcp__maxon-dev__run_self_hosted_test`, filtered to the spec first, then **unfiltered** to catch regressions. Use `mcp__maxon-dev__spec_test_outcome` (`compiler: "selfhosted"`) for per-test PASS/FAIL detail, and `mcp__maxon-dev__dump_ir` / `mcp__maxon-dev__dump_stages` to inspect lowering.
4. Implement fixes, rebuild, re-test. Iterate until the spec **and** the full suite pass.
5. Run wasm too: `mcp__maxon-dev__run_self_hosted_test` with `target: "wasm32-wasi"` (unfiltered). Fix any wasm-only divergence. Use per-target `Stdout:<target>` / `RequiredIR:<target>` spec blocks when output legitimately differs by target.
6. If you touched the C# bootstrap, run the C# suite with `mcp__maxon-dev__run_spec_test` (default compiler).

Rules (from CLAUDE.md — non-negotiable):
- Fix **root causes**, no workarounds, no sentinel returns, no silent unhandled `match`/`else` cases (use `default throws` / `panic`). Complexity and time do not matter.
- The bug may be in the self-hosted compiler (`maxon-selfhosted/`) **or** the C# bootstrap (`maxon-sharp/`). Fix whichever is wrong.
- Cross-target consistency: any target-specific change (x64) needs the equivalent change in the other targets (arm64/wasm) where applicable.
- Valid `ExitCode` range is `int(0 to 125)` (due to the wasm target). Fix any test returning outside it.
- If RequiredIR blocks fail, regenerate with `mcp__maxon-dev__run_self_hosted_test` and `updateRequired: true`. If anything in `register-allocator.md` changed, review the regenerated RequiredIR to confirm it's correct.

### 3. Verify — independent full-suite confirmation

Do **not** self-certify off your own filtered pass. The agent that wrote the fix is the worst judge of whether it's green. **Delegate verification to a fresh subagent** so confirmation comes from a context that never saw the fix being built.

Launch a subagent with the `Agent` tool (`subagent_type: "general-purpose"`) and this charge — it must change no code, run tests only:

> Independently verify the Maxon self-hosted suite is green after enabling spec `<spec>`. Do **not** change any code — run tests only.
> 1. Rebuild the self-hosted compiler: `mcp__maxon-dev__build` with `target: "selfhosted"`.
> 2. Run the **full** suite **unfiltered** on `x64-windows`: `mcp__maxon-dev__run_self_hosted_test` (no filter, default target).
> 3. Run the **full** suite **unfiltered** on `wasm32-wasi`: `mcp__maxon-dev__run_self_hosted_test` with `target: "wasm32-wasi"`.
> 4. Report exact pass/fail counts per target and list every failing fragment.
>
> Be strict: report green **only if both targets pass with zero failures and no hang/timeout**. A regression anywhere in the suite is not-green even if `<spec>` itself passes.

Treat the subagent's verdict as authoritative — do not override it with your own recollection of a passing run. **If it reports not green: revert the whitelist enable** (re-comment the spec back to its deferred form), stop, and report the failing fragments and blocking reason — do not claim success.

### 4. Review — quality gate before commit

Once green, run the `code-review` skill on the working-tree diff **before** committing (per-spec quality gate) — this runs every iteration, even when the diff is only the one-line whitelist uncomment. Apply the CLAUDE.md Code Quality standards: no duplicated logic (refactor shared logic into helpers, including pre-existing duplication), no silent unhandled cases, no sentinel returns, narrowest correct typed ranges, cross-target consistency, comments explain *why*. Fix any issues you find directly. Format every modified Maxon file with `mcp__maxon-dev__fmt`.

### 5. Commit

Stage the whitelist edit plus the compiler source you changed (not unrelated files). Update `ROADMAP.md` to reflect the new status and remaining work. Commit (do **not** push) with the repo's style:

```
feat(selfhosted): enable <spec> — <one-line root-cause / what was fixed>

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
```

## Stop conditions

- **Verify not green** → revert the enable, stop, report the failing fragments and blocking reason.
- **No enable-able deferred spec left** → every deferred spec is enabled; done.

## Guidelines
- Read the relevant `specs/` file to understand the expected behavior before fixing.
- Fix root causes, not symptoms. No workarounds.
- Bugs may live in the C# bootstrap (`maxon-sharp/`); fix it there when that's the real cause.
- The valid `ExitCode` range is `int(0 to 125)` (due to the wasm target). Fix any test returning outside it.
