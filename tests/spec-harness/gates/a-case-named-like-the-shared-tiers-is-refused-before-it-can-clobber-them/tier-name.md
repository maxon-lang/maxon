---
feature: harness-gate-a-case-named-like-the-shared-tiers-is-refused-before-it-can-clobber-them
---
# The shared-tiers-name gate

The library-tier cases of a spec share one staged copy of the tiers, in a directory named for no case. A case that stages private tiers of its own (it carries a `// --- stdlib-overlay:` section) puts them in a directory named for the case, so a case named `__shared__` would put them where the shared copy lives and rewrite it under the other cases. The harness must refuse that case by name instead of staging it.

**Its expected stdout is DELIBERATELY WRONG on the second case, and it must stay wrong**: the gate reads the refusal out of that case's FAIL line.

## Tests

<!-- test: tier-name.shares-the-tiers -->
<!-- tier: stdlib -->

```maxon
function main() returns ExitCode
	return 4
end 'main'
```

```exitcode
4
```

<!-- test: __shared__ -->

```maxon
// --- stdlib-overlay: Clock.maxon
public typealias OverlayTick = int(0 to 9)
// --- file: main.maxon
function main() returns ExitCode
	return 0
end 'main'
```

```stdout
this expectation is deliberately wrong — see this fixture's header
```
