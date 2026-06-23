export const meta = {
  name: 'selfhost-loop',
  description: 'Drive the Maxon self-hosting loop: enable the next deferred spec (error-directed when possible), fix the full suite to green, code-review, commit; repeat until blocked.',
  whenToUse: 'When you want to autonomously advance the self-hosted compiler by enabling and fixing deferred spec tests one at a time, committing each, until an iteration cannot reach green.',
  phases: [
    { title: 'Scout' },
    { title: 'Fix' },
    { title: 'Verify' },
    { title: 'Review' },
    { title: 'Commit' },
  ],
}

// ----------------------------------------------------------------------------
// Maxon self-hosting development loop.
//
// One iteration = one spec. Each iteration:
//   1. Scout  — try to self-compile, map the blocker to the best next DEFERRED
//               (commented-out) spec in the whitelist, and return the exact edit
//               that uncomments it. Hybrid: error-directed if the self-compile
//               error implicates a deferred spec, else topmost non-hang deferred.
//   2. Fix    — apply the edit, rebuild the self-hosted compiler, and implement
//               compiler changes until the FULL suite is green on both targets.
//   3. Verify — an independent agent re-runs the full suite (x64-windows AND
//               wasm32-wasi) and returns a strict verdict. The fixer cannot
//               self-certify.
//   4. Review — run the code-review quality gate on the diff.
//   5. Commit — commit the spec + fix.
//
// Stop conditions (run-until-blocked):
//   * Verify reports not-green  -> revert the whitelist enable, STOP, report.
//   * Scout finds no enable-able deferred spec -> STOP (done / all enabled).
//   * Token budget exhausted    -> STOP gracefully.
//
// EVERY deferred spec is in scope — NOTHING is skipped or permanently deferred,
// including the specs whose notes say they HANG the runner (with-iterator,
// generic-module-merge-*, memory-safety, refcount) or that are docs-only /
// async. A "hang" is a real compiler bug: the runner enforces a per-test
// wall-clock timeout, so an enabled hang spec surfaces as a failing/timed-out
// fragment that the fixer must ROOT-CAUSE and fix (e.g. an infinite loop in the
// compiler or a fragment that never returns), not route around. The loop
// processes deferred specs in file order until every one is enabled and green.
// ----------------------------------------------------------------------------

const WHITELIST = 'maxon-selfhosted/Testing/SpecTestRunner.maxon'

// Specs whose deferral notes flag them as hanging / slow / docs-only. These are
// NOT skipped — they are flagged so the fixer knows to expect a hang/timeout and
// fix its root cause (infinite loop, non-returning worker, missing fragments)
// rather than be surprised by it. The scout still selects them in file order.
const KNOWN_HANG_OR_SPECIAL = [
  'with-iterator',
  'generic-module-merge-pattern',
  'generic-module-merge-crash',
  'memory-safety',
  'refcount',
  'code-review-timings',
  'async-filesystem',
]

const SCOUT_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['done', 'spec', 'reason', 'oldString', 'newString'],
  properties: {
    done: {
      type: 'boolean',
      description: 'true if there is no enable-able deferred spec left (loop should stop).',
    },
    spec: {
      type: 'string',
      description: 'The spec name selected to enable this iteration (empty string if done).',
    },
    reason: {
      type: 'string',
      description: 'Why this spec was picked: the self-compile error it unblocks, or "topmost deferred" fallback.',
    },
    selfCompileError: {
      type: 'string',
      description: 'The first/most-relevant self-compile error observed (error code + message), or "" if self-compile succeeded.',
    },
    oldString: {
      type: 'string',
      description: 'Exact current text in SpecTestRunner.maxon that comments out the chosen spec (the `// "spec" ...` line, verbatim incl. indentation). Empty if done.',
    },
    newString: {
      type: 'string',
      description: 'Replacement text that uncomments the chosen spec as a live array entry (e.g. `\\t"spec",`). Must keep the surrounding note lines intact unless they belong to a different spec. Empty if done.',
    },
  },
}

const VERIFY_SCHEMA = {
  type: 'object',
  additionalProperties: false,
  required: ['green', 'x64Summary', 'wasmSummary', 'failingTests'],
  properties: {
    green: {
      type: 'boolean',
      description: 'true ONLY if the full self-hosted suite passes on BOTH x64-windows and wasm32-wasi with zero failures and no hang/timeout.',
    },
    x64Summary: { type: 'string', description: 'Pass/fail counts for x64-windows.' },
    wasmSummary: { type: 'string', description: 'Pass/fail counts for wasm32-wasi.' },
    failingTests: {
      type: 'array',
      items: { type: 'string' },
      description: 'Names of any failing fragments (empty if green).',
    },
  },
}

function scoutPrompt() {
  return `You are the SCOUT for one iteration of the Maxon self-hosting loop. Your job: pick the single best next DEFERRED spec to enable, error-directed when possible, and return the EXACT whitelist edit to uncomment it. Do NOT modify any files. Do NOT fix anything. Selection + edit text only.

CONTEXT
- The self-hosted compiler source is in \`maxon-selfhosted/\`. The spec whitelist is the \`whitelist = [ ... ]\` array in \`${WHITELIST}\`.
- Specs ABOVE the comment header "DEFERRED SPECS" and the live (uncommented) \`"name",\` entries after it are ALREADY enabled and passing — do NOT touch them.
- A spec is DEFERRED (not yet enabled) ONLY when it appears as a COMMENTED entry like \`// "spec-name" ... note\`. Those are the only candidates.
- NO spec is off-limits. EVERY commented deferred spec must eventually be enabled and fixed — including ones whose notes say they HANG the runner or are slow/docs-only (${KNOWN_HANG_OR_SPECIAL.map((s) => '"' + s + '"').join(', ')}). A hang is a real compiler bug, not a reason to skip. Select these in file order like any other.

STEPS
1. Read ${WHITELIST} and list EVERY commented-out (\`// "name"\`) deferred spec entry in file order. Do not exclude any.
2. Attempt the self-host self-compile to surface the real blocker. Use the maxon-dev MCP build tool to build target "selfhosted"; if it builds, try to have the self-hosted compiler compile its own source (maxon-selfhosted/). Capture the FIRST/most-relevant compiler error (4-digit code + message). If you cannot make it attempt self-compilation, record selfCompileError="" and proceed to step 3's fallback.
3. SELECT one spec:
   - HYBRID-PREFERRED: if the self-compile error clearly implicates a language feature exercised by one of the deferred specs, pick THAT spec. Read candidate spec files in specs/<name>.md to judge the match. Explain the link in "reason".
   - FALLBACK: otherwise pick the TOPMOST commented deferred spec in file order. Set reason="topmost deferred (no clean error mapping)".
4. Produce the EXACT edit that uncomments the chosen spec:
   - oldString = the verbatim current line(s) that comment it out, including leading tabs, e.g. \`\t// "enum-match-range" (8/9) — ...\` (only the part that must change; keep multi-line notes if they belong to this spec by uncommenting just the entry line and leaving the explanatory note as a comment, OR convert as appropriate so the spec becomes a live \`"name",\` array element).
   - newString = the same region with the spec as a LIVE array entry: a line \`\t"spec-name",\`. Preserve any explanatory note as a trailing/preceding comment if it still applies.
   - oldString MUST be unique in the file and copied exactly (tabs, em-dashes, parens). Verify by reading the surrounding lines.
5. If there are NO enable-able deferred specs left (all remaining are hang/skip or none exist), set done=true and leave spec/oldString/newString empty.

Return the structured object. The script applies your edit — so the strings must be exactly right.`
}

function fixPrompt(spec, scout, hangAware) {
  return `You are the FIXER for the Maxon self-hosting loop. The spec "${spec}" has just been enabled in the whitelist (${WHITELIST}). Your job: make the FULL self-hosted spec suite pass on BOTH x64-windows and wasm32-wasi, by implementing real compiler functionality. Follow the project's selfhosted-dev / incremental-dev conventions and CLAUDE.md.

WHY THIS SPEC: ${scout.reason}
${scout.selfCompileError ? 'SELF-COMPILE BLOCKER OBSERVED: ' + scout.selfCompileError : 'No self-compile error was captured this iteration.'}
${hangAware ? "HANG/SLOW/SPECIAL FLAG: this spec's deferral note flags it as HANGING or slow (the runner's worker never returns) or docs-only. Treat the hang as a REAL COMPILER BUG to root-cause — an infinite loop / non-terminating worker / fragment that never returns. Reproduce it under a filter, find why it doesn't terminate (dump_ir / dump_stages, narrow the fragment), and fix the underlying non-termination. Do NOT route around it, do NOT re-disable it. If the note says docs-only (no fragments), confirm that and ensure the runner handles a zero-fragment spec cleanly." : ''}

RULES (from CLAUDE.md — non-negotiable):
- Fix ROOT CAUSES, no workarounds, no sentinel returns, no silent unhandled match/else cases (use \`default throws\`/\`panic\`). Complexity and time do not matter.
- The bug may be in the self-hosted compiler (maxon-selfhosted/) OR the C# bootstrap (maxon-sharp/). Fix whichever is wrong.
- Cross-target consistency: any target-specific change (x64) needs the equivalent change in the other targets (arm64/wasm) where applicable.
- Valid ExitCode range is int(0 to 125). Fix any test returning outside it.
- If RequiredIR blocks fail, regenerate with the build/test tool's updateRequired flag.
- Format any Maxon files you touch with the maxon-dev fmt tool.

WORKFLOW (use the maxon-dev MCP tools, not raw Bash):
1. Read specs/${spec}.md to learn the expected behavior. Read maxon-selfhosted/ARCHITECTURE.md if you need the compiler map.
2. Rebuild: maxon-dev build with target "selfhosted" (use "both" if you change the C# bootstrap).
3. Run the suite: maxon-dev run_self_hosted_test, filtered to "${spec}" first, then UNFILTERED to catch regressions. Use spec_test_outcome (compiler "selfhosted") for per-test detail, lookup_error_code for 4-digit codes, dump_ir / dump_stages to inspect lowering.
4. Implement fixes, rebuild, re-test. Iterate until "${spec}" AND the full suite pass.
5. Run wasm too: run_self_hosted_test with target "wasm32-wasi" (unfiltered). Fix any wasm-only divergence. Use per-target Stdout:/RequiredIR: spec blocks if output legitimately differs by target.
6. Run the C# suite (maxon-dev run_spec_test, default compiler) if you touched the bootstrap.

Do NOT commit. Do NOT revert the whitelist enable. When the full suite is green on both targets, return a concise report: what was broken, the root cause, the files changed, and the final pass/fail counts for x64-windows and wasm32-wasi. If you genuinely cannot reach green, say so explicitly and report the remaining failing tests with the blocking reason — do NOT claim success.`
}

function verifyPrompt(spec) {
  return `You are the VERIFIER for the Maxon self-hosting loop. The fixer claims the suite is green after enabling spec "${spec}". Independently confirm. Do NOT change any code. Run tests only.

1. Rebuild the self-hosted compiler: maxon-dev build target "selfhosted".
2. Run the FULL self-hosted suite UNFILTERED on x64-windows: maxon-dev run_self_hosted_test (no filter, default target).
3. Run the FULL suite UNFILTERED on wasm32-wasi: maxon-dev run_self_hosted_test with target "wasm32-wasi".
4. Report exact pass/fail counts per target and list every failing fragment.

green = true ONLY if BOTH targets pass with zero failures and no hang/timeout. If either has any failure, or the runner hangs, green = false. Be strict — a regression anywhere in the suite means not green, even if "${spec}" itself passes.`
}

function reviewPrompt(spec) {
  return `Run the project's code-review quality gate on the uncommitted changes for enabling+fixing spec "${spec}" in the Maxon compiler. Invoke the code-review skill (SlashCommand /code-review or the Skill tool) against the current working-tree diff. Apply the CLAUDE.md Code Quality standards: no duplicated logic, no silent unhandled cases, no sentinel returns, narrow typed ranges, cross-target consistency, comments explain why. If you find quality issues, FIX them directly (then note them). Do NOT commit. Return a short summary of what was reviewed, any fixes you applied, and confirmation the diff is commit-ready.`
}

function commitPrompt(spec, scout, fixReport) {
  return `Commit the changes for the Maxon self-hosting iteration that enabled and fixed spec "${spec}". The full suite is verified green on x64-windows and wasm32-wasi and the code-review gate passed.

1. Stage the relevant changes (git add the whitelist + compiler source you changed; do not add unrelated files).
2. Write a commit message in the repo's style: \`feat(selfhosted): enable ${spec} — <one-line root-cause/what-was-fixed>\`. Base the one-liner on this fix report:
---
${fixReport}
---
3. End the commit message body with the required trailer:
Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
4. Commit (do NOT push). Return the commit hash and subject line.`
}

// ----------------------------------------------------------------------------

const MIN_BUDGET = 120_000 // need enough headroom for a full fix+verify cycle
const enabled = []
let iteration = 0

while (true) {
  if (budget.total && budget.remaining() < MIN_BUDGET) {
    log(`Stopping: token budget low (${Math.round(budget.remaining() / 1000)}k < ${MIN_BUDGET / 1000}k). Enabled this run: ${enabled.join(', ') || 'none'}.`)
    break
  }

  iteration++
  log(`=== Iteration ${iteration}: scouting next deferred spec ===`)

  phase('Scout')
  const scout = await agent(scoutPrompt(), {
    label: `scout #${iteration}`,
    phase: 'Scout',
    schema: SCOUT_SCHEMA,
    agentType: 'general-purpose',
  })

  if (!scout) {
    log('Scout agent failed to return. Stopping and reporting.')
    return { stopped: 'scout-failed', iteration, enabled }
  }
  if (scout.done || !scout.spec) {
    log('No enable-able deferred spec left. Every deferred spec has been enabled.')
    return { stopped: 'done', iteration, enabled, lastSelfCompileError: scout.selfCompileError || '' }
  }

  const hangAware = KNOWN_HANG_OR_SPECIAL.includes(scout.spec)
  log(`Iteration ${iteration}: enabling "${scout.spec}"${hangAware ? ' [flagged hang/slow/special — fixer must root-cause the hang]' : ''} — ${scout.reason}`)

  // Apply the whitelist edit deterministically (scout returned exact strings).
  let enableApplied = false
  const applier = await agent(
    `Apply EXACTLY this one edit and nothing else. In file ${WHITELIST}, replace the first exact occurrence of OLD with NEW. Verify OLD appears verbatim once; if it does not match exactly, report failure and do NOT guess. Do not touch any other file or line.\n\nOLD (verbatim):\n<<<OLD\n${scout.oldString}\nOLD\n\nNEW (verbatim):\n<<<NEW\n${scout.newString}\nNEW\n\nReturn "OK" if applied, or "MISMATCH: <reason>" if OLD did not match.`,
    { label: `enable ${scout.spec}`, phase: 'Scout', agentType: 'general-purpose' }
  )
  enableApplied = typeof applier === 'string' && applier.trim().startsWith('OK')
  if (!enableApplied) {
    log(`Failed to apply whitelist edit for "${scout.spec}": ${applier}. Stopping for inspection.`)
    return { stopped: 'enable-edit-failed', iteration, enabled, spec: scout.spec, applierSaid: applier }
  }

  // Fix the full suite to green.
  phase('Fix')
  const fixReport = await agent(fixPrompt(scout.spec, scout, hangAware), {
    label: `fix ${scout.spec}`,
    phase: 'Fix',
    agentType: 'core-implementation',
  })

  // Independent verification — the fixer cannot self-certify.
  phase('Verify')
  const verdict = await agent(verifyPrompt(scout.spec), {
    label: `verify ${scout.spec}`,
    phase: 'Verify',
    schema: VERIFY_SCHEMA,
    agentType: 'general-purpose',
  })

  if (!verdict || !verdict.green) {
    log(`Iteration ${iteration} NOT green for "${scout.spec}". Reverting whitelist enable and stopping.`)
    // Revert the enable so the suite/whitelist returns to its last-green state.
    await agent(
      `Revert exactly this one edit in ${WHITELIST}: replace the first exact occurrence of NEW with OLD (i.e. re-comment the spec back to its deferred form). Do not touch anything else.\n\nNEW (currently in file):\n<<<NEW\n${scout.newString}\nNEW\n\nOLD (restore this):\n<<<OLD\n${scout.oldString}\nOLD\n\nReturn "OK" or "MISMATCH: <reason>".`,
      { label: `revert ${scout.spec}`, phase: 'Verify', agentType: 'general-purpose' }
    )
    return {
      stopped: 'not-green',
      iteration,
      enabled,
      blockingSpec: scout.spec,
      reason: scout.reason,
      selfCompileError: scout.selfCompileError || '',
      x64: verdict ? verdict.x64Summary : 'verify-agent-failed',
      wasm: verdict ? verdict.wasmSummary : 'verify-agent-failed',
      failingTests: verdict ? verdict.failingTests : [],
      fixReport,
    }
  }

  log(`Iteration ${iteration}: "${scout.spec}" GREEN — x64: ${verdict.x64Summary} | wasm: ${verdict.wasmSummary}. Running review + commit.`)

  // Per-spec quality gate, then commit.
  phase('Review')
  await agent(reviewPrompt(scout.spec), {
    label: `review ${scout.spec}`,
    phase: 'Review',
    agentType: 'general-purpose',
  })

  phase('Commit')
  const commit = await agent(commitPrompt(scout.spec, scout, fixReport), {
    label: `commit ${scout.spec}`,
    phase: 'Commit',
    agentType: 'general-purpose',
  })

  enabled.push(scout.spec)
  log(`Iteration ${iteration} committed: ${commit}`)
}

return { stopped: 'budget', iteration, enabled }
