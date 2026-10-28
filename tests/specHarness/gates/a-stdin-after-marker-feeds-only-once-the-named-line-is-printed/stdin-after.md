---
feature: harness-gate-stdin-after
---
# The stdin-after gate

`<!-- stdin: after "go" -->` feeds the program one line when its stdout shows the line `go`, and not before.

## Tests

<!-- test: stdin-after.fed-only-after-the-line -->
<!-- procs: 2 -->
<!-- stdin: after "go" -->

The reader blocks in the kernel on stdin from the start. Main prints `going`, which only CONTAINS the anchor, naps
for a third of a second, and prints `go`. A feed clocked from the spawn, or from the first byte, or from a line that
merely contains the anchor, lets the reader print `fed` ahead of `go`.

```maxon
typealias Integer = int(i64.min to i64.max)

let napMs = 300

type Reader
	var id as Integer

	static function create() returns Self
		return Self{id: 0}
	end 'create'

	export function read() returns Integer
		let line = try Console.stdin().readLine() otherwise ""
		print("fed\n")

		return line.count()
	end 'read'
end 'Reader'

function main() returns ExitCode
	let reader = spawn Reader.create()
	let pending = try reader.read() otherwise panic("an open mailbox with room takes the send")

	print("going\n")
	sleep(napMs)
	print("go\n")

	let n = await pending
	print("read={n}\n")

	return 0
end 'main'
```

```exitcode
0
```

```stdout
going
go
fed
read=5
```

<!-- test: stdin-after.line-never-printed -->
<!-- stdin: after "never printed" -->

A program that exits without printing the named line is not waited on for ever and is fed nothing.

```maxon
function main() returns ExitCode
	print("done\n")

	return 0
end 'main'
```

```exitcode
0
```

```stdout
done
```
