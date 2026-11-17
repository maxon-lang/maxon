# What inspired each part of Maxon

The planner reads this before it designs a fix. Each row names the language or system a part of Maxon
was taken from, and where the repository says so. Before ruling, find how that source solved the same
problem — its design documents, its source, the papers behind it — and say in the ruling what was
learned, what is adopted and why Maxon departs where it does.

Rows marked *candidate* have no stated inspiration in the repository; they name the prior art most worth
reading. Cite them as references, never as decisions already made.

## Stated inspirations

| Part of Maxon | Source | What was taken | Where the repository says so |
|---|---|---|---|
| Memory model, regions | Project Verona (Reggio regions, OOPSLA'23) | static region types; `isolated` / `immutable`; types carry no capability; closures as objects with fields | wiki `Design-verona-regions`; #30 |
| Services, `acquire` | Verona behaviour-oriented concurrency | a service is a cown, a message a behaviour; `acquire` is Verona's `when` | `Design-verona-regions`; `docs/internals/cown-runtime.md` |
| The cown queue | MCS lock | the behaviour chain | `docs/internals/cown-runtime.md` |
| Capabilities (first design, withdrawn) | Pony | kept: automatic recovery of fresh regions, right-hand-side-first evaluation, ownership transfer | `Design-verona-regions` |
| Scheduler, green threads | Go runtime (GMP) | ported function by function (`runqget`, `findRunnable`, `sysmon`, `wakep`, `checkdead`); `MAXON_MAX_PROCS` is `GOMAXPROCS` | `Runtime/SchedRuntime.maxon`; `specs/sched-runqueue.md` |
| Green-thread stacks | Go | 8 KiB seed, `morestack`, `shrinkstack`, `stackPreempt`, `gopark`, `nosplit` | #148; `Runtime/GtRuntime.maxon`; `specs/stack-guard-pages.md` |
| `awaitAny`, sends, deadlock check | Go | `awaitAny` is `selectgo`; a send with no timeout is a blocking send; `checkdead` | `specs/await-any.md`; `specs/services.md` |
| Cancellation | Go | cooperative cancellation; a `SharedValue` read stands in for an atomic or `context` | `Design-verona-regions` |
| Network poller | Go netpoll | `netpollinit`, `netpollBreak`, `netpoll` | `IR/Std/StdDialect.maxon` |
| Allocator | Go mheap / mcentral | 68 size classes, 8 KiB chunks, `lfstack`, per-processor span caches | `specs/slab-allocator.md`; `docs/internals/memory-allocator.md` |
| Reference counting | Swift, Perceus, Lobster | reference counting with elision, not a borrow checker | `.claude/skills/maxon-coder/compiler.md` |
| Hashing | Go `hash/maphash` | `Hasher` in maphash's shape; per-process seeds; `==` on a Map or Set refused | `stdlib/Hasher.maxon`; #78; wiki `Design-hashing` |
| Array growth | Go `nextslicecap` | double below 256 elements, then ease toward 1.25× | `docs/optimization-log.md` |
| Sorting | Rust driftsort (Peters and Bergdoll, 2024) | the same family | `stdlib/helpers/sort/driftsort.maxon` |
| Packages, name scope | Go, Rust | a directory is a namespace as Go's package block; a package is a marked subtree as a crate; no overloading, as in Go | `Design-verona-regions`; wiki `Design-packages`; `specs/top-level-name-scope.md` |
| Shifts, integers | Go | the left operand's type decides the fill; a negative count panics | `IR/Maxon/TypeRules.maxon`; `specs/bitwise-operators.md` |
| Float printing | Python, JavaScript, Rust, Go, Swift, Java | shortest round-trip; `%g` from C and Python | `specs/sin.md`; `stdlib/Builtins.maxon` |
| Literal protocols, ordering | Swift | the `ExpressibleBy…Literal` pattern; `isTotallyOrdered` | `stdlib/Builtins.maxon`; `specs/primitive-comparable.md` |
| Stdlib shapes | Swift (`Subprocess`, `CharacterSet`); Go (`log/slog`, `net.Error.Timeout`, `bufio`, `strconv`) | API shapes | `stdlib/Subprocess.maxon`; `stdlib/CharacterSet.maxon`; `specs/logger.md` |
| Generics | Go | per-instantiation bodies (#106); `Map` measured against Go (#82) | #106; #82 |
| Inlining budget | Go's inline cost model | studied for #114; the depth cap was kept, and one cost budget is on the Backlog (#120) | #114; #120 |
| Build tooling | Go | `--define` as `-ldflags -X`; `lint` apart from `build`, as `go vet` from `go build` | `docs/RELEASING.md`; `docs/optimization-log.md` |
| Profiler | Go | per-thread `SIGPROF` CPU periods | `Profile/ProfileSampler.maxon` |
| Context parameters (proposed) | Dagger | compile-time wiring as the comparison point | wiki `Design-context-parameters`; #33 |

The website's slogans ("the safety of Rust, the concurrency of Go, the ease of C#, Swift's errors") are
marketing, not design evidence; where they disagree with a design page, the design page wins.

## Candidates: parts with no stated inspiration

| Part of Maxon | Prior art worth reading |
|---|---|
| Inliner and optimizer | Go's inliner and its cost budget; LLVM's inline cost model; the Swift SIL optimizer |
| Register allocator (SSA, chordal colouring) | Hack's SSA-based allocation (Hack, Grund and Goos); Go's SSA `regalloc`; LLVM's greedy allocator |
| Interfaces and witness tables | Swift's protocol witness tables and specialization; Go's itabs |
| Pattern matching, `union` | Rust and Swift exhaustiveness checking (Maranget, "Warnings for pattern matching") |
| Build cache, query spine | rust-analyzer and Salsa; Go's build cache; Bazel's action cache |
| Formatter | gofmt; rustfmt |
| Language server | gopls; rust-analyzer |
| Debug info, debugger | Go's DWARF generation and Delve; LLDB |
| Ranged integers | Ada's range types; Pascal subranges |
| Strings, `Character` | Swift's grapheme-cluster `Character`; Go's byte strings with UTF-8 |
| Testing | Go's `testing` package (`t.TempDir`, subtests); Rust's test harness |
| Maps (open addressing) | Go's Swiss-table `map` (Go 1.24); Abseil's Swiss tables; Rust's hashbrown |

## Where the sources are

Go's source can be staged at `vendor/go` (the `compiler-workflow` skill); it is the reference for
every runtime row. For anything else, read the project's own design documents and source online, and cite
what was read.
