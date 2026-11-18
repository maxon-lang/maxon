# Every region-model corpus program has one checked-in fate: an agreeing spec case, an owned ledger row or a counted skip, and the translator writes only what the model judged

Class issue: #248 · Priority: P2

## Goal

The region model and the compiler stay two independent judges of one corpus, and the tree records every program's fate: an agreeing spec case, a disagreement with an owner, or a counted skip. The model lives in this tree, so a ruling and the model's switch for it land together.

- **Done when:** the generator fails on an unowned disagreement; the translator writes only what the model judged, or skips by name; the ledger has no `translator` rows; every faithful disagreement is also a spec case.
- **Not the goal:** making either judge follow the other by construction, or generating one from the other: the model is worth having because it is built differently.
- **Between two designs that both satisfy a stage, take** a named skip over a translation the model did not judge, and a failing generator over a row with no owner.

## Stage order

One stage is one local commit, which ticks its box. A stage starts when every stage it waits on is ticked
here and every issue it waits on is closed. A `deferred` stage is skipped. The plan lands once, after its
last stage.

| Done | Stage | Rule it establishes | Waits on | Not beside | Needs a ruling |
|---|---|---|---|---|---|
| [ ] | 1 | The generator records every disagreement in a checked-in ledger and fails on an unowned, stale or unmapped one |  |  |  |
| [ ] | 2 | The translator has one door each for expressibility, binding kind and place, so it never writes a program the model did not judge | 1 |  |  |
| [ ] | 3 | The translator's send has one door that never changes what the model judged, and a translation bucket entry or run-time failure fails the generator (`/plan-and-land`) | 2 |  |  |
| [ ] | 5 | The model's checker, oracle and hand corpora live in this tree, and the generator reads them from there | 3 |  |  |
| [ ] | 6 | Every faithful disagreement is a spec case the generator writes from the ledger, pinned to the compiler's current answer | 3 |  |  |
| [ ] | 4 | Model round fa26: the model follows the rulings the compiler already follows, and the specs and ledger are regenerated from it | 5, 6 |  | Open question 3: #253's diverging handler, whether the model gains a handler that diverges (recommended, as a switch in this round) or the shape is left to a compiler spec |

## Rulings

- Open question 1: the region model moves in-tree: its checker, its oracle and its hand corpora live in this repository (stage 5).
- Open question 2: every faithful disagreement is also a spec case, in `specs/model-open.md` (stage 6).

## Problem

The region design (#30) is encoded four times, and nothing checked in says where the encodings disagree:

1. the rulings, as prose on #30 and the wiki's `Design-verona-regions`;
2. the compiler;
3. **capmodel**, the executable model: a Python repository outside this tree, whose rule set (`core-m3w-fa25`) is named here only by `DEFAULT_RULESET` in `scripts/model-specs/model_corpus.py` and by the prose of each `specs/model-*.md` case;
4. the model-spec generator, which carries two further hand encodings: `rules.py`'s `RULE_CODES` (model rule to diagnostic) and `mx_translate.py`'s `Translator` (model program to Maxon text).

`scripts/model-specs/generate.py` is the only place the encodings meet. Its `classify` sorts every translated corpus program into a bucket, and `write_specs` writes only the `AGREEING` buckets to `specs/model-*.md` (2,240 cases). Every other bucket goes to `report.md` and `entries.json` in a scratch directory (`report`), and `main`'s exit status counts only the written cases. So a disagreement is recorded nowhere in the tree, the run that finds one passes, and each one becomes an issue only if someone restates it by hand. No workflow and no skill runs the generator.

Three shapes follow from that one gap:

- **The model lags a ruling** (#253 to #259, #267). A ruling or a reading changes what is accepted, the compiler follows, and the model's switch is not written. The programs sit in "model refuses, compiler accepts" or "model accepts, compiler refuses" with no row saying which side owes the change.
- **The translator writes a different program from the one the model judged** (#260 to #266). `Translator` builds Maxon by string templates, and each language rule it must honour is applied where a construct happens to be spelled:
  - a send is spelled in three places that disagree on failure (`send_text`: `otherwise ignore` for a valueless send, `let _ = try … otherwise panic("send failed")` for a dropped reply; `send_promise_text`: `otherwise panic`; `send_with_handler`: a match on the failure), and none passes `sendTimeout:`;
  - a binding's `let` or `var` is chosen in `bind` from the model's own keyword, and a `match` payload in `arm_text` is always a `let`;
  - `expr` renders a `Cond` as a parenthesised ternary wherever it stands, including the head of a `modify` (`stmt`, `C.Modify`) and the target of a field store (`C.AssignField`);
  - `check_supported` refuses generic types, interfaces and name collisions, and has no test for a type that reaches itself.

  The compiler's refusal of such a translation lands in the `translation` bucket, or, when the code is in `CAPABILITY_CODES` (E4014 is), in `wrong-refusal`, where it reads as a compiler defect.
- **A silent default hides a missing row.** `rules.codes_of` answers the empty set for a model rule `RULE_CODES` does not list, so an unmapped rule is pinned as "agree, other code" instead of failing. `docs/SPECS.md` spells the generator's command with `--ruleset core-m3w-fa24` while the default is fa25.

## Design

**What is one source and what is not.** The model and the compiler stay two independent implementations: the model's value is that it is built differently and has a run-time oracle. The single checked source is the corpus's verdict record. Every corpus program has exactly one checked-in fate: an agreeing case in `specs/model-*.md`, a row in a ledger, or a counted skip. The generator fails when a program has none, or two.

This is how Go holds its two type checkers together (`cmd/compile/internal/types2` and `go/types`): both run one corpus, `internal/types/testdata`, whose programs carry their expected errors, and one list of error codes, `internal/types/errors`. Maxon adopts the shared corpus with one verdict per program. It does not adopt Go's generation of one checker from the other (`go/types/generate_test.go`), because that would remove the independence the model exists for.

1. **The ledger.** `scripts/model-specs/ledger.tsv`, checked in. A header records the rule set and the model's stamp (`model_corpus.model_stamp`). One row per translated program that is not an agreeing case:
   - the program (`family`, `origin`, `ident`);
   - its bucket;
   - the side that owes the change: `model`, `compiler`, `translator`, `corpus` or `by-construction`;
   - the ruling that decides it (ruling 36 where none names the shape);
   - the issue that owns it.

   A second section holds the skip counts by reason, so a translator change that starts skipping programs shows in the diff.
2. **The gate.** `generate.py` exits non-zero, naming each program, when:
   - a disagreeing program has no row;
   - a row's program now agrees, or sits in another bucket;
   - a row names a program the corpus does not hold;
   - a skip count differs from the ledger's;
   - a model rule has no `RULE_CODES` row;
   - the model checkout's stamp differs from the header's, unless the run is told it is a new round.

   `--update-ledger` rewrites rows whose fate moved and leaves the owner columns of new rows empty, which the gate then refuses: a new disagreement cannot land without an owner.
3. **Two row kinds from #248's follow-up.** A program that never terminates by construction (a service that re-sends to its own handle from every handler: hand-AC `sp-handle-in-mailbox-forever`, hand-AD `j-send-to-self-handle`) is a `by-construction` row and is not run, so the generator does not wait its 120 s timeout. A model accept whose oracle flag is a ruling-family verdict (O-MOVEIN or O-DETACH with readonly-write, which #30 ruling 34 permits) is a row naming ruling 34, apart from "the model flags its own accept".
4. **The translator never changes what the model judged.** A structural rewrite is a canonicalisation of the model program, which the model judges again, as `judged` already does for `unbind_unread_arms`. Where Maxon has no spelling for the construct, the program is skipped with a named reason. Each language rule the translator must honour has one door:
   - **send**: one function decides the spelling from where the send stands (a service body, or `main`, a function or a coroutine) and what becomes of its result (no reply, a dropped reply, a bound promise, an awaited one, a hand-back);
   - **binding kind**: one function decides `let` or `var` for a local, a payload and a hand-back name from the name's uses;
   - **place**: one function renders the head of a `modify`, the target of a store and a by-reference argument, and refuses an expression that is not a path;
   - **expressibility**: `check_supported` owns every reason a program's declarations cannot be written, a recursive type among them.
5. **Once the doors exist, `translator` is not a ledger side.** A translation the compiler refuses for a reason that is not a capability rule, or that fails at run time because of its spelling, fails the generator. The translator emits Maxon the compiler judges on capability grounds, or skips.

### Open questions

1. **Where the model lives.** Every stage below needs a capmodel checkout, and the model round of stage 4 is not a commit in this repository.
   - (a) It stays outside the tree. The gate runs only when someone with the checkout regenerates.
   - (b) Its checker, oracle and hand corpora (what `model_corpus.py` imports) move into this tree. A round becomes a reviewable commit, a ruling and its switch can land together, and CI can run the gate against cached verdicts. Cost: Python in the code lanes, and the model's size, which is not measured here.
   - (c) It becomes its own public repository, pinned here by revision.

   Recommendation: (b), with the campaign reports left out. Stage 1's stamp in the ledger header holds under any answer.
2. **Whether a disagreement is also a spec case.** A ledger row is checked only on a regeneration. Writing each faithful disagreement as a case pinned to the compiler's current answer, its prose naming the owing side and the issue (Rust's `known-bug` tests), would turn the case red in the commit that closes the disagreement. Cost: a known wrong acceptance is pinned green until then. Recommendation: yes, after stage 3, in a `specs/model-open.md` the generator writes from the ledger; not staged below until decided.
3. **#253's diverging handler.** The model program has no handler; `otherwise panic` is the translator's spelling. With stage 3 the send propagates, so the program is expected to be refused by both sides. Whether the model gains a handler that diverges (accepting more: a narrow round with its own switch) or the shape is left to a compiler spec is undecided. Recommendation: add the switch to stage 4's round, since Maxon programs write `otherwise panic` on a send and no ruling covers it.

## Stages

Each stage regenerates `specs/model-*.md`, so the stages share an area and land in order.

1. **The ledger and its gate.**
   - Changes: design points 1 to 3; `codes_of` fails on an unmapped rule; `docs/SPECS.md`'s "Model-derived specs" describes the ledger and names no rule set in its command line.
   - Area: `scripts/model-specs/generate.py`, `rules.py`, new `ledger.tsv`, `docs/SPECS.md`.
   - Depends on: none.
   - Discharges: #248.
   - Shown by: a self-test over synthetic entries (no model needed) that is red for an unowned disagreement, a stale row, an unknown program and an unmapped rule; then a run against fa25 with an empty ledger, which fails naming every disagreement. The first ledger gives every row an owner: this issue for the model, corpus and translator rows below, the compiler-agreement issues for the rest. A disagreement no issue owns is reported for filing, not ledgered without an owner.

2. **Expressibility, binding kind and place doors.**
   - Changes: a type that reaches itself through fields or payloads is skipped as "recursive type" (a cycle through service handles stays, since the model judges it as R-HANDLE-CYCLE); a name passed to a `mutable` parameter is a `var`, and a payload passed by reference is moved into a `var` by a canonicalisation the model re-judges, or skipped where an arm cannot hold it; a path over a `cond` at a `modify` head or a store target is skipped as "a path over a cond has no Maxon spelling".
   - Area: `scripts/model-specs/mx_translate.py`, `model_prune.py`, `ledger.tsv`, `specs/model-*.md`, `docs/SPECS.md` (its list of skip reasons).
   - Depends on: stage 1.
   - Discharges: #262, #264, #265.
   - Shown by: the ledger gate: the rows for hand-AP `g1-recursive-closed`; hand-FA25 `c-clear-field`, `a-clear-field`, `c-fill-field`, `a-fill-field`; hand-AB `s-slot-param-payload`; hand-AO `f1-modify-cond-path-iso` and `s1-stmt-open-cond-iso-field-write` are stale after the change, and the skip counts move by exactly the skipped programs. `s-slot-param-payload` must end as an agreeing refusal (E3224).

3. **The send door, and no translator rows.**
   - Changes: design points 4 (send) and 5.
     - A send in `main`, a function or a coroutine passes `sendTimeout: ForeverMs`, because the model's send to a full mailbox does not fail. A send in a service body or a factory keeps `sendTimeout: 0` (E3221).
     - An unhandled send propagates its failure, so it stays a throw point (#30 rulings 35 and 36): a bare `try` in a function that declares the send error. Where the position cannot propagate, the program is skipped by name. `otherwise ignore` and `otherwise panic` are no longer written for a model send that has no handler.
     - A dropped reply is bound and cancelled. It is never awaited: an `await` adds a wait the model program does not have and changes the waiting rules' verdicts.
     - A `translation` bucket entry, and a run-time failure, fail the generator.
   - Area: `scripts/model-specs/mx_translate.py`, `generate.py`, `ledger.tsv`, `specs/model-*.md`, `docs/SPECS.md`.
   - Depends on: stage 2.
   - Discharges: #260, #261, #266.
   - Shown by: the ledger gate. Hand-FA14 `s-nofactory-send-live` and `a-sp-factory-sends` become agreeing refusals (R-LIN-DROP); finding-AA `send-full-await` runs to exit 0; hand-AP `g1-service-field` and hand-FA15 `c6-factory-param` leave the `translation` bucket. The verdict diff of the whole regeneration is read: every case whose expectation flips is explained by one of the three send changes. Hand-AC `wc-send-let-promise-linear` (#253) is re-read under open question 3.
   - This stage's approach needs a survey (which positions can propagate a send error, and what `.cancel()` does to a reply promise), so it is planned before it is landed.

5. **The model moves in-tree.** Made before stage 4.
   - Changes: the model's checker, its run-time oracle and its hand corpora (everything `model_corpus.py` imports, and the finding families it reads) are copied into this tree; the campaign reports stay out. The generator finds the model in the tree and takes no checkout argument. The model's own tests run from the tree. `docs/SPECS.md`'s "Model-derived specs" gives the one command.
   - Area: a new directory for the model beside `scripts/model-specs/`, `model_corpus.py`, `generate.py`, `docs/SPECS.md`.
   - Depends on: stage 3.
   - Needs: the capmodel checkout stages 1 to 3 were run against. A session without one parks this stage and stage 4.
   - Shown by: a regeneration from the in-tree model writes `specs/model-*.md` and the ledger byte for byte as the checkout did, and the ledger's stamp is then the tree's.

6. **A disagreement is a spec case.**
   - Changes: the generator writes each ledger row whose side is `model`, `compiler` or `corpus` as a case in `specs/model-open.md`, pinned to the compiler's current answer, its prose naming the owing side, the ruling and the issue. A `by-construction` row is not written: it is never run.
   - Area: `scripts/model-specs/generate.py`, `spec_writer.py`, new `specs/model-open.md`, `docs/SPECS.md`.
   - Depends on: stage 3, so no row is a translator's.
   - Shown by: `spec-test --filter=model-open` is green; a row removed from the ledger removes its case; the commit that closes a disagreement turns its case red until the regeneration moves it to an agreeing file.

4. **Model round fa26, and the specs follow it.**
   - Changes, in the model, which is in this tree after stage 5:
     - accepting more, each behind its own switch: an I3 recovery that does not count a read reaching nothing of the result (#254); an I4 recovery that counts plain payloads only (#255); a renamed owned message parameter stored as a move (#256); MERGE-ARMS-ALT over call arms, with the R-LIN-COND lift for linear arms (#257, #30 readings R-a and R-f); the diverging handler, if open question 3 says so (#253);
     - refusing only: an unmarked linear-union message parameter is outer data to a window (#258, rulings 45.7, 41 part 1, 42 item 3); a call given a unique argument whose type has a plain part is not a clean call, whatever the argument's freshness (#259, ruling 44 point 1); a field store compares record types (#267);
     - the hand program `bm-nowin-keep` stores `y.p` (#267).
   - Evidence the round owes: for each switch, its tests and a mutation gate; a hand corpus of the programs the members name and their variants; one gadget per accepting switch at 2,000 programs per oracle; a verdict diff over the corpus programs the switches can touch; the standing tests; one red team on the changed surface. The refusing switches owe their tests and the verdict diff. #257's own list stands: the ten corpus programs; a call arm in each of the `cond`, `match` and `try … otherwise` merges and nested; `m1-arm-moves-arm-names` and `-rev`; a consuming-call arm against a bare arm of one linear variable; refused controls (a call arm that only reads, a linear variable moved in one arm only, two different linear variables).
   - Changes, in this tree: `DEFAULT_RULESET` and `HAND_FAMILIES` in `model_corpus.py`, the ledger's header, the regenerated `specs/model-*.md`, and the removal of every `model` and `corpus` row the round closes.
   - Area: the in-tree model; `scripts/model-specs/model_corpus.py`, `ledger.tsv`, `specs/model-*.md`.
   - Depends on: stages 5 and 6 (so the round is a commit here, and its verdict diff is not mixed with translator churn), and open question 3.
   - Discharges: #253, #254, #255, #256, #257, #258, #259, #267.
   - Shown by: the ledger gate, which fails on the fa25 ledger once the model accepts or refuses the named programs, and passes when their rows are gone.

## Members

- #248 — the generator records no disagreement in the tree. Stage 1; this issue is #248 rewritten.
- #253 — the model refuses hand-AC `wc-send-let-promise-linear` (`let p = send a.take(new Resp(0))`, written by the translator with `otherwise panic`); the compiler accepts. Stage 3 changes the translation; stage 4 under open question 3.
- #254 — hand-AH `i3-value-read-in-init`: `let u: isolated = mk Mp.m1(new Bx(peekBx(w)))`, then `w.n = 3`, with `peekBx(b Bx) returns Num`; the model gives E3102, the rulings (17, 36 amendment (a), 45) accept. Stage 4.
- #255 — hand-AH `i4-sibling-immutable`: `match u { m1(y, c) then takeBx(y) }` with `m1(var y Bx, var c immutable Cfg)`; the model gives C-RECOVER, rulings 34 I4 and 45.2 accept. Stage 4.
- #256 — hand-AJ `message-body-store`: `behave tk(t Tk) { let u = t; self.k = u }`; the model gives C-STORE, ruling 42 items 1 (addendum iii) and 3 accept. Stage 4.
- #257 — MERGE-ARMS-ALT and R-LIN-COND count only bare-variable arms. Programs: hand-AP `m1-cond-lend-leaf-b`, `m1-cond-stash-ret` and `-b`, `-c`, `-d`, `k1-cond-arm-reads-other-moves` and `-b`, `-c`; hand-AR `l6-cond-arm-seq-call`; hand-FA25 `a-cond-arm-reads-arm-moves-bare`; hand-AP `m1-cond-lend-leaf-iso` (`isoUT((idUT(u) if c else u))`, accepted by the compiler from `ownership` W1); and `let c = (r.me() if t else r); c.fin(1)`. Ruling 49.3, readings R-a and R-f. Stage 4.
- #258 — the model lets a behaviour consume its unmarked linear-union message parameter inside a window; the compiler gives E3207. Programs: hand-AG `f1-behaviour-param`, `beh-win-mr-modify-keep`, `beh-win-mr-modify-keepfn`; hand-AH `m-svc-w`, `w-beh-store`, `w-beh-keepfn`, `w-beh-write`. Stage 4.
- #259 — hand-AM `un-wrap-noalias`: `let k isolated = partU(mk UQ.have(q))`, then `send s.takeB(k)`; the model accepts, the compiler gives E3200 (ruling 44 point 1). Stage 4.
- #260 — an unhandled send is written `try … otherwise ignore`, so the model's throw point is gone (hand-FA14 `s-nofactory-send-live`, `a-sp-factory-sends`). Stage 3.
- #261 — an unhandled send to a full one-slot mailbox panics "send failed" (finding-AA `send-full-await`: `spawn Rv(1) capacity 1`, three sends from `main`, three awaits; expected exit 0). Stage 3.
- #262 — a recursive unique type is emitted and refused with E4014 (hand-AP `g1-recursive-closed`). Stage 2.
- #264 — a local or payload passed to a `mutable` parameter is a `let` (E2013; the five programs under stage 2). Stage 2.
- #265 — a path over a `cond` is written at a `modify` head or a statement head (E2001; hand-AO, the two programs under stage 2). Stage 2.
- #266 — a dropped reply is written `let _ = try … otherwise panic`, which drops the promise (E3213; hand-AP `g1-service-field`, hand-FA15 `c6-factory-param`). Stage 3.
- #267 — hand-AG `bm-nowin-keep` stores an `Hq` into a `Bx` field (`self.item.keep = y`, meant `y.p`), and the model's field store does not compare types; the compiler gives E3005. Stage 4.

## Related, not folded

- #130, #177 — ladder generators under `tests/ladders/` that write programs the language refuses. The same kind of gap (no gate compiles a generator's output: `tests/ladders/index.maxtest` checks only that each script has an index row), but another tool with no code in common with the model specs. They want their own fix: every ladder script generates and compiles at a smoke rung.
- #117 — minted `mm-trace` and `log-trace` cases may lose their subject on a re-mint. Its mechanism is the re-mint, and it follows #115.
- #195 — spec-test batches a case whose verdict depends on whether the whole program starts a service. The generator compiles candidates unbatched and verifies the written files batched, so it can meet this, but the defect is spec-test's.
- #232, #233, #235, #245, #246, #249 to #252 and the other compiler-agreement issues — the compiler side of the same disagreements. Each is a `compiler` row in the ledger and stays its own issue.
- #193 — a type argument cannot be written `isolated` as ruling 22 spells it: the compiler against a ruling, with no model or translator part.
