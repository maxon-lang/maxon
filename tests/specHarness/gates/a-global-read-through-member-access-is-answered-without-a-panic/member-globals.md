---
feature: harness-gate-a-global-read-through-member-access-is-answered-without-a-panic
---
# The a-global-read-through-member-access-is-answered-without-a-panic gate

A run case declares module-level `let`s read through member access (an enum case and a range bound) and a `static let`, beside a plain case. The front-end question the spec runner asks before it batches runs without the library, so a global whose initializer names a library type's member must be answered, never crash the worker that asked.

## Tests

<!-- test: member-globals.plain -->

```maxon
function main() returns ExitCode
	print("plain\n")
	return 0
end 'main'
```

```exitcode
0
```

```stdout
plain
```

<!-- test: member-globals.member-reads -->

```maxon
typealias Count = int(0 to 1000)

enum Colour
	red
	green
end 'Colour'

type Config
	static let limit = 5 as Count
end 'Config'

let chosen = Colour.green
let top = u8.max

function main() returns ExitCode
	if chosen == Colour.green 'isGreen'
		print("members {Config.limit} {top}\n")
	end 'isGreen'

	return 0
end 'main'
```

```exitcode
0
```

```stdout
members 5 255
```
