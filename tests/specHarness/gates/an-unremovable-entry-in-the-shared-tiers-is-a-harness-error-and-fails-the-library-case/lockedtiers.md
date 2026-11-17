---
feature: harness-gate-an-unremovable-entry-in-the-shared-tiers-is-a-harness-error-and-fails-the-library-case
---
# The lockedtiers gate

A library-tier run case that would pass. The gate plants, in the directory where the spec's library-tier
cases share their staged copy of `stdlib/` and `runtime/`, a file the host refuses to remove (read-only on
Windows, in an unwritable directory elsewhere). The run clears that directory before the spec's cases and
must report the file as a harness error naming it and this spec; staging the tiers for the case empties it
again and must fail the case naming the file, so the case never compiles against what was left there.

## Tests

<!-- test: lockedtiers.library -->
<!-- tier: stdlib -->

```maxon
function main() returns ExitCode
	print("the library-tier case compiled and ran\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
the library-tier case compiled and ran
```
