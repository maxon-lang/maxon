---
feature: regions-unique
status: experimental
keywords: [unique, is unique, move, clone, E3102, E3214]
category: semantics
---

# `type X is unique`

## Documentation

A type declared `is unique`, spelled after its name, is never cloned or aliased: `let b = a` moves it, so
reading `a` afterwards is E3102, and `clone()` is refused. A type with a unique field is unique too, with no
declaration, and gets no generated `clone()`. The standard library's `Promise` is unique.

## Tests

<!-- test: unique-let-moves -->
`let second = first` moves the unique ticket, so the read of `first` that follows is a use of a moved value.

```maxon
typealias Serial = int(0 to u64.max)

type Ticket is unique
	export var serial as Serial

	static function create(serial Serial) returns Self
		return Self{serial: serial}
	end 'create'
end 'Ticket'

function main() returns ExitCode
	var first = Ticket.create(7)
	first.serial = 8
	let second = first
	let again = first.serial
	print("{second.serial} {again}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:16:14: use of moved value 'first': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: unique-clone-refused -->
A copy of a unique value would be a second owner, so `clone()` is refused.

```maxon
typealias Serial = int(0 to u64.max)

type Ticket is unique
	export var serial as Serial

	static function create(serial Serial) returns Self
		return Self{serial: serial}
	end 'create'
end 'Ticket'

function main() returns ExitCode
	let ticket = Ticket.create(7)
	let copy = ticket.clone()
	print("{copy.serial}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:14:13: 'Ticket' is unique and cannot be cloned
```

<!-- test: holder-of-unique-is-unique -->
`Wallet` declares nothing, but its field holds a unique `Ticket`, so `Wallet` is unique and has no `clone()`.

```maxon
typealias Serial = int(0 to u64.max)

type Ticket is unique
	export var serial as Serial

	static function create(serial Serial) returns Self
		return Self{serial: serial}
	end 'create'
end 'Ticket'

type Wallet
	var ticket as Ticket

	static function create(serial Serial) returns Self
		return Self{ticket: Ticket.create(serial)}
	end 'create'

	function serial() returns Serial
		return self.ticket.serial
	end 'serial'
end 'Wallet'

function main() returns ExitCode
	let wallet = Wallet.create(7)
	let copy = wallet.clone()
	print("{copy.serial()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:26:13: 'Wallet' is unique (its field 'ticket' holds a unique 'Ticket') and cannot be cloned
```


<!-- test: a-unique-union-kept-by-a-call-while-its-match-binding-is-read-is-refused -->
The `filled` arm lends `slot` to its `token` binding, so handing `slot` to `keep`, which keeps it, is refused whatever the call reads of `token`.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	filled(token Token, n Tally)
end 'Slot'

typealias SlotArray = Array with Slot

function keep(slot Slot, k Tally) returns Tally
	let kept = SlotArray.create(0)
	kept.push(slot)

	return k
end 'keep'

function main() returns ExitCode
	let slot = Slot.filled(Token.create(1), n: 2)

	match slot 'which'
		empty then print("empty\n")
		filled(token, n) then print("{keep(slot, k: token.n)} {n}\n")
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:31:33: cannot pass 'slot' to parameter 'slot' of 'keep': it keeps a unique value, and 'slot' is matched by the arm this call is in, which lends it to its bindings; move it before or after the `match`
```

<!-- test: a-match-binding-read-after-its-subject-is-moved-is-refused -->
The `filled` arm lends `slot` to its `token` binding, so handing `slot` to `keep`, which keeps it, is refused whatever the call reads of the payload binding `n`.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	filled(token Token, n Tally)
end 'Slot'

typealias SlotArray = Array with Slot

function keep(slot Slot) returns Tally
	let kept = SlotArray.create(0)
	kept.push(slot)

	return 1
end 'keep'

function main() returns ExitCode
	let slot = Slot.filled(Token.create(1), n: 2)

	match slot 'which'
		empty then print("empty\n")
		filled(token, n) then print("{keep(slot)} {n} {token.n}\n")
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:31:33: cannot pass 'slot' to parameter 'slot' of 'keep': it keeps a unique value, and 'slot' is matched by the arm this call is in, which lends it to its bindings; move it before or after the `match`
```

<!-- test: a-unique-local-kept-by-a-call-is-refused-when-read-afterwards -->
`keep` stores its argument, which moves `ticket`, so reading it afterwards is a use of a moved value.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenArray = Array with Token

function keep(token Token) returns Tally
	let kept = TokenArray.create(0)
	kept.push(token)

	return 1
end 'keep'

function main() returns ExitCode
	let ticket = Token.create(1)
	let moved = keep(ticket)
	print("{moved} {ticket.n}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:24:18: use of moved value 'ticket': its ownership moved to the parameter 'token' of 'keep', which keeps it
```

<!-- test: a-unique-parameter-kept-by-a-call-is-refused-when-read-afterwards -->
A parameter is moved by a keeping call like any other name.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenArray = Array with Token

function keep(token Token) returns Tally
	let kept = TokenArray.create(0)
	kept.push(token)

	return 1
end 'keep'

function forward(token Token) returns Tally
	let moved = keep(token)

	return moved + token.n
end 'forward'

function main() returns ExitCode
	let ticket = Token.create(1)
	print("{forward(ticket)}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:24:17: use of moved value 'token': its ownership moved to the parameter 'token' of 'keep', which keeps it
```

<!-- test: a-unique-parameter-pushed-twice-is-refused -->
A parameter the function itself keeps is moved by the first `push`, so the second reads a moved value.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenArray = Array with Token

function keepTwice(token Token, shelf TokenArray)
	shelf.push(token)
	shelf.push(token)
end 'keepTwice'

function main() returns ExitCode
	let shelf = TokenArray.create(0)
	keepTwice(Token.create(1), shelf: shelf)

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:16:13: use of moved value 'token': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-unique-parameter-bound-to-a-second-name-twice-is-refused -->
`let first = token` moves a unique parameter exactly as it moves a local, so `let second = token` reads a moved value.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

function rebind(token Token) returns Tally
	let first = token
	let second = token

	return 1
end 'rebind'

function main() returns ExitCode
	let total = rebind(Token.create(1))
	print("{total}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:14:15: use of moved value 'token': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-lent-unique-parameter-rebound-reads-through-the-new-name -->
`let b = a` hands a lent unique parameter to `b`, so `b` is the live name and reading it is legal.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function rebind(a Tk)
	let b = a
	print("{b.n}\n")
end 'rebind'

function main() returns ExitCode
	rebind(Tk.create(7))

	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: a-lent-unique-parameter-rebound-is-pushed-through-the-new-name -->
The new name of a rebound unique parameter can be handed on, which moves it once more.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

typealias TkArray = Array with Tk

function rebind(a Tk, shelf TkArray)
	let b = a
	shelf.push(b)
end 'rebind'

function main() returns ExitCode
	let shelf = TkArray.create(0)
	rebind(Tk.create(7), shelf: shelf)
	print("{shelf.count()}\n")

	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: a-lent-unique-parameter-read-after-its-rebind-is-refused -->
The new name is live, and the parameter it was taken from is not.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function rebind(a Tk)
	let b = a
	print("{b.n} {a.n}\n")
end 'rebind'

function main() returns ExitCode
	rebind(Tk.create(7))

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:14:16: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-lent-unique-parameter-rebound-twice-reads-through-the-last-name -->
Each rebind hands the value on, so the last name is the live one.

```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function rebind(a Tk)
	let b = a
	let c = b
	print("{c.n}\n")
end 'rebind'

function main() returns ExitCode
	rebind(Tk.create(7))

	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: a-unique-parameter-handed-to-a-mutable-parameter-stays-readable -->
Handing a unique parameter to a `mutable` parameter lends it for the call, so the parameter is read again afterwards, including in the handler of the `try` that made the call.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'

	function peek() returns Tally
		return self.n
	end 'peek'
end 'Token'

enum Fault implements Error
	broken
end 'Fault'

interface Handler
	function handle(token mutable Token) throws Fault
end 'Handler'

type Quiet implements Handler
	static function create() returns Self
		return Self{}
	end 'create'

	function handle(token mutable Token) throws Fault
		print("{token.peek()}\n")
	end 'handle'
end 'Quiet'

function report(token Token, fault Fault) returns Tally
	print("{fault.name}\n")

	return token.peek()
end 'report'

function dispatch(token Token, handler Handler) returns Tally
	try handler.handle(token) otherwise (fault) 'threw'
		return report(token, fault: fault)
	end 'threw'

	return token.peek()
end 'dispatch'

function main() returns ExitCode
	let quiet = Quiet.create()
	let total = dispatch(Token.create(3), handler: quiet)
	print("{total}\n")

	return 0
end 'main'
```
```stdout
3
3
```

<!-- test: a-unique-local-kept-by-a-call-in-a-loop-is-refused-on-the-next-lap -->
The second lap hands `ticket` over again after the first lap moved it.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenArray = Array with Token

function keep(token Token) returns Tally
	let kept = TokenArray.create(0)
	kept.push(token)

	return 1
end 'keep'

function main() returns ExitCode
	let ticket = Token.create(1)
	var lap = 0 as Tally

	while lap < 2 'laps'
		let moved = keep(ticket)
		print("{moved}\n")
		lap = lap + 1
	end 'laps'

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:26:20: use of moved value 'ticket': it was moved in an earlier iteration of this loop
```

<!-- test: a-let-unique-handed-to-a-keeping-parameter-moves -->
A `let` unique handed to a keeping parameter moves into the call, and nothing reads it afterwards.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenArray = Array with Token

function keep(token Token) returns Tally
	let kept = TokenArray.create(0)
	kept.push(token)

	return 1
end 'keep'

function main() returns ExitCode
	let ticket = Token.create(1)
	let moved = keep(ticket)
	print("{moved}\n")

	return 0
end 'main'
```
```stdout
1
```
```exitcode
0
```

<!-- test: a-unique-local-lent-to-a-reading-parameter-stays-readable -->
`peek` only reads its parameter, so the call lends `ticket` and it is still readable.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

function peek(token Token) returns Tally
	return token.n
end 'peek'

function main() returns ExitCode
	let ticket = Token.create(1)
	let seen = peek(ticket)
	print("{seen} {ticket.n}\n")

	return 0
end 'main'
```
```stdout
1 1
```
```exitcode
0
```

<!-- test: a-unique-local-assigned-again-after-a-keeping-call-is-readable -->
The write gives `ticket` a new value, so the read after it is not of the moved one.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenArray = Array with Token

function keep(token Token) returns Tally
	let kept = TokenArray.create(0)
	kept.push(token)

	return 1
end 'keep'

function main() returns ExitCode
	var ticket = Token.create(1)
	let moved = keep(ticket)
	ticket = Token.create(2)
	print("{moved} {ticket.n}\n")

	return 0
end 'main'
```
```stdout
1 2
```
```exitcode
0
```

<!-- test: a-unique-union-arm-lent-to-a-reading-parameter-stays-readable -->
`peek` only reads, so the call moves nothing and the arm's `token` stays readable.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	filled(token Token, n Tally)
end 'Slot'

function peek(slot Slot, k Tally) returns Tally
	match slot 'which'
		empty then return k
		filled(token, n) then return token.n + n + k
	end 'which'
end 'peek'

function main() returns ExitCode
	let slot = Slot.filled(Token.create(1), n: 2)

	match slot 'which'
		empty then print("empty\n")
		filled(token, n) then print("{peek(slot, k: token.n)} {n} {token.n}\n")
	end 'which'

	return 0
end 'main'
```
```stdout
4 2 1
```
```exitcode
0
```

<!-- test: a-unique-union-kept-by-a-call-is-refused-when-matched-afterwards -->
`keep` stores the union, which moves `slot`, so matching it afterwards is a use of a moved value.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	filled(token Token, n Tally)
end 'Slot'

typealias SlotArray = Array with Slot

function keep(slot Slot) returns Tally
	let kept = SlotArray.create(0)
	kept.push(slot)

	return 1
end 'keep'

function main() returns ExitCode
	let slot = Slot.filled(Token.create(1), n: 2)
	let moved = keep(slot)

	match slot 'which'
		empty then print("empty {moved}\n")
		filled(token, n) then print("{moved} {n} {token.n}\n")
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:8: use of moved value 'slot': its ownership moved to the parameter 'slot' of 'keep', which keeps it
```

<!-- test: a-unique-field-read-handed-to-a-keeping-parameter-is-refused -->
`keep` stores its argument, which would move the unique `token` out of `holder`'s field, and a field cannot be moved out.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

type Holder
	export var token as Token

	static function create(n Tally) returns Self
		return Self{token: Token.create(n)}
	end 'create'
end 'Holder'

typealias TokenArray = Array with Token

function keep(token Token) returns Tally
	let kept = TokenArray.create(0)
	kept.push(token)

	return 1
end 'keep'

function main() returns ExitCode
	let holder = Holder.create(1)
	holder.token.n = 2
	let moved = keep(holder.token)
	print("{moved}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:32:14: cannot pass 'holder.token' to parameter 'token' of 'keep': it keeps a unique value, and a unique value cannot be moved out of a field or an element it was only read from; move it out with `pop` or `remove`, or `swap` it out of an isolated field
```

<!-- test: a-unique-element-read-handed-to-a-keeping-parameter-is-refused -->
An element read from an array names a slot the array goes on owning, so a keeping parameter cannot take it; `pop` moves one out.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenArray = Array with Token

function keep(token Token) returns Tally
	let kept = TokenArray.create(0)
	kept.push(token)

	return 1
end 'keep'

function main() returns ExitCode
	let tokens = TokenArray.create(0)
	tokens.push(Token.create(1))
	let moved = keep(try tokens.get(0) otherwise panic("one element"))
	print("{moved}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:24:14: cannot pass 'try tokens.get(0) otherwise panic("one element")' to parameter 'token' of 'keep': it keeps a unique value, and a unique value cannot be moved out of a field or an element it was only read from; move it out with `pop` or `remove`, or `swap` it out of an isolated field
```

<!-- test: a-unique-popped-from-an-array-is-handed-to-a-keeping-parameter -->
`pop` moves the element out of the array, so the value is the caller's own and `keep` may take it.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenArray = Array with Token

function keep(token Token) returns Tally
	let kept = TokenArray.create(0)
	kept.push(token)

	return 1
end 'keep'

function main() returns ExitCode
	let tokens = TokenArray.create(0)
	tokens.push(Token.create(1))
	let moved = keep(try tokens.pop() otherwise panic("one element"))
	print("{moved} {tokens.count()}\n")

	return 0
end 'main'
```
```stdout
1 0
```
```exitcode
0
```

<!-- test: a-unique-field-read-lent-to-a-reading-parameter-stays-readable -->
`peek` only reads its parameter, so `holder.token` is lent and stays readable.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

type Holder
	export var token as Token

	static function create(n Tally) returns Self
		return Self{token: Token.create(n)}
	end 'create'
end 'Holder'

function peek(token Token) returns Tally
	return token.n
end 'peek'

function main() returns ExitCode
	let holder = Holder.create(3)
	print("{peek(holder.token)} {holder.token.n}\n")

	return 0
end 'main'
```
```stdout
3 3
```
```exitcode
0
```

<!-- test: an-array-of-uniques-kept-by-a-call-is-refused-when-read-afterwards -->
An array holding a unique is unique-holding, so `keep` storing it moves `tokens`.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenArray = Array with Token
typealias TokenArrays = Array with TokenArray

function keep(tokens TokenArray) returns Tally
	let kept = TokenArrays.create(0)
	kept.push(tokens)

	return 1
end 'keep'

function main() returns ExitCode
	let tokens = TokenArray.create(0)
	tokens.push(Token.create(1))
	let moved = keep(tokens)
	print("{moved} {tokens.count()}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:26:18: use of moved value 'tokens': its ownership moved to the parameter 'tokens' of 'keep', which keeps it
```

<!-- test: an-array-of-uniques-lent-to-a-reading-parameter-stays-readable -->
`count` only reads the array, so the call lends it.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenArray = Array with Token

function empty(tokens TokenArray) returns bool
	return tokens.isEmpty()
end 'empty'

function main() returns ExitCode
	let tokens = TokenArray.create(0)
	tokens.push(Token.create(1))
	print("{empty(tokens)} {tokens.count()}\n")

	return 0
end 'main'
```
```stdout
false 1
```
```exitcode
0
```

<!-- test: a-tuple-holding-a-unique-kept-by-a-call-is-refused-when-read-afterwards -->
A tuple holding a unique is unique-holding, so `keep` storing it moves `pair`.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias Pair = (Token, Tally)
typealias Pairs = Array with Pair

function keep(pair Pair) returns Tally
	let kept = Pairs.create(0)
	kept.push(pair)

	return 1
end 'keep'

function main() returns ExitCode
	let pair = (Token.create(1), 2 as Tally)
	let moved = keep(pair)
	print("{moved} {pair.1}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:25:18: use of moved value 'pair': its ownership moved to the parameter 'pair' of 'keep', which keeps it
```

<!-- test: error.a-unique-payload-of-a-var-union-kept-by-a-call -->
The `filled` arm moves the payload of `slot` into `token`, so handing `slot` to `tally` after `keep(token)` in the same arm is a use of a moved value.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	filled(token Token, n Tally)
end 'Slot'

typealias TokenArray = Array with Token

function keep(token Token) returns Tally
	let kept = TokenArray.create(0)
	kept.push(token)

	return 1
end 'keep'

function tally(slot Slot) returns Tally
	match slot 'which'
		empty then return 0
		filled(token, n) then return n + token.n
	end 'which'
end 'tally'

function main() returns ExitCode
	let slot = Slot.filled(Token.create(1), n: 2)

	match slot 'which'
		empty then print("empty\n")
		filled(token, n) then print("{keep(token)} {n} {tally(slot)}\n")
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:38:57: use of moved value 'token': its ownership moved to the parameter 'token' of 'keep', which keeps it
```

<!-- test: a-payload-binding-read-after-its-subject-is-handed-to-a-consuming-call-is-refused -->
The `filled` arm moves the payload of `slot` into `token`, so handing the whole union to `Box.wrap` makes the later read of `token` in the same statement a use of a moved value.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	filled(token Token, n Tally)
end 'Slot'

type Box
	export var slot as Slot

	static function wrap(slot Slot) returns Self
		return Self{slot: slot}
	end 'wrap'

	function size() returns Tally
		return 1
	end 'size'
end 'Box'

function main() returns ExitCode
	let slot = Slot.filled(Token.create(1), n: 2)

	match slot 'which'
		empty then print("empty\n")
		filled(token, n) then print("{n} {Box.wrap(slot).size()} {token.n}\n")
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:34:61: use of moved value 'token': its ownership moved to the parameter 'slot' of 'Box.wrap', which keeps it
```

<!-- test: a-var-bound-to-a-unique-field-read-is-refused -->
`alias` would name the unique `token` while `holder` still holds it, and a unique value has one name.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

type Holder
	export var token as Token

	static function create(n Tally) returns Self
		return Self{token: Token.create(n)}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create(1)
	var alias = holder.token
	alias.n = 5
	print("{holder.token.n}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:22:6: cannot bind 'alias' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: a-let-bound-to-a-unique-element-read-is-refused -->
An element read from an array names a slot the array goes on owning, so a name cannot alias it.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenArray = Array with Token

function main() returns ExitCode
	var tokens = TokenArray.create(0)
	tokens.push(Token.create(1))
	let alias = try tokens.get(0) otherwise panic("one element")
	print("{alias.n}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:17:6: cannot bind 'alias' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: a-conditional-with-a-unique-field-read-arm-is-refused -->
The conditional hands back the field's unique, which would then have two names.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

type Holder
	export var token as Token

	static function create(n Tally) returns Self
		return Self{token: Token.create(n)}
	end 'create'
end 'Holder'

function pick(holder Holder, other Token, flag bool) returns Tally
	let chosen = holder.token if flag else other

	return chosen.n
end 'pick'

function main() returns ExitCode
	var holder = Holder.create(1)
	var other = Token.create(2)
	holder.token.n = 3
	print("{pick(holder, other: other, flag: true)}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:21:28: cannot join a unique value read out of a field or an element with another value: the field or element goes on owning it, and a unique value has one name; apply the use inside each arm instead, as `f(t) if c else f(fresh)` does
```

<!-- test: a-let-bound-to-a-unique-tuple-element-read-is-refused -->
A tuple element read is a read out of a slot the tuple goes on owning.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

function main() returns ExitCode
	var pair = (Token.create(1), 2 as Tally)
	let alias = pair.0
	print("{alias.n} {pair.1}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:14:6: cannot bind 'alias' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: a-unique-field-is-read-through-without-an-alias -->
`holder.token.n` reads a scalar through the field, which names no unique.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

type Holder
	export var token as Token

	static function create(n Tally) returns Self
		return Self{token: Token.create(n)}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let holder = Holder.create(4)
	holder.token.n = 6
	let seen = holder.token.n
	print("{seen} {holder.token.n}\n")

	return 0
end 'main'
```
```stdout
6 6
```
```exitcode
0
```

<!-- test: error.a-unique-field-bound-to-a-view-is-refused -->
`view` is another spelling of `let`, and a `let` names the unique it is bound to, so the field is no longer the only name for it.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

type Holder
	export var token as Token

	static function create(n Tally) returns Self
		return Self{token: Token.create(n)}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var holder = Holder.create(4)
	let lent = holder.token
	print("{lent.n}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:22:6: cannot bind 'lent' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: a-for-binding-over-uniques-handed-to-a-keeping-parameter-is-refused -->
The loop lends each element of `tokens`, so `keep` cannot take it.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenArray = Array with Token

function keep(token Token) returns Tally
	let kept = TokenArray.create(0)
	kept.push(token)

	return 1
end 'keep'

function main() returns ExitCode
	let tokens = TokenArray.create(0)
	tokens.push(Token.create(1))

	for token in tokens 'each'
		print("{keep(token)}\n")
	end 'each'

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:26:11: cannot pass 'token' to parameter 'token' of 'keep': it keeps a unique value, and 'token' is only lent by the loop; move it out with `pop` or `remove`
```

<!-- test: a-for-binding-over-uniques-handed-to-a-reading-parameter-is-accepted -->
`peek` only reads its parameter, so lending the loop's element is fine.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenArray = Array with Token

function peek(token Token) returns Tally
	return token.n
end 'peek'

function main() returns ExitCode
	let tokens = TokenArray.create(0)
	tokens.push(Token.create(3))

	for token in tokens 'each'
		print("{peek(token)}\n")
	end 'each'

	return 0
end 'main'
```
```stdout
3
```
```exitcode
0
```

<!-- test: a-tuple-element-binding-handed-to-a-keeping-parameter-is-refused -->
Destructuring lends the tuple's element, so `keep` cannot take it.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenArray = Array with Token

function keep(token Token) returns Tally
	let kept = TokenArray.create(0)
	kept.push(token)

	return 1
end 'keep'

function main() returns ExitCode
	let pair = (Token.create(1), 2 as Tally)
	let (token, n) = pair
	print("{keep(token)} {n}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:24:10: cannot pass 'token' to parameter 'token' of 'keep': it keeps a unique value, and 'token' is only lent by the tuple it was destructured from; move it out with `pop` or `remove`
```

<!-- test: a-record-holding-an-array-of-uniques-is-unique-and-cannot-be-cloned -->
`Wallet`'s only field is an `Array with Ticket`, an instance rather than a named record, and the one walk that decides uniqueness reaches the `Ticket` inside it, so `clone()` is refused.

```maxon
typealias Serial = int(0 to u64.max)
typealias Tickets = Array with Ticket

type Ticket is unique
	export var serial as Serial

	static function create(serial Serial) returns Self
		return Self{serial: serial}
	end 'create'
end 'Ticket'

type Wallet
	var tickets as Tickets

	static function create(serial Serial) returns Self
		var tickets = Tickets.create(0)
		tickets.push(Ticket.create(serial))
		return Self{tickets: tickets}
	end 'create'

	function count() returns Serial
		return self.tickets.count()
	end 'count'
end 'Wallet'

function main() returns ExitCode
	let wallet = Wallet.create(7)
	let copy = wallet.clone()
	print("{copy.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:29:13: 'Wallet' is unique (its field 'tickets' holds a unique 'Tickets') and cannot be cloned
```

<!-- test: a-record-holding-an-array-of-uniques-is-unique-and-cannot-be-frozen -->
The same `Wallet` is unique, so `freeze` is refused for the reason `clone()` is.

```maxon
typealias Serial = int(0 to u64.max)
typealias Tickets = Array with Ticket

type Ticket is unique
	export var serial as Serial

	static function create(serial Serial) returns Self
		return Self{serial: serial}
	end 'create'
end 'Ticket'

type Wallet
	var tickets as Tickets

	static function create(serial Serial) returns Self
		var tickets = Tickets.create(0)
		tickets.push(Ticket.create(serial))
		return Self{tickets: tickets}
	end 'create'

	function count() returns Serial
		return self.tickets.count()
	end 'count'
end 'Wallet'

function main() returns ExitCode
	let wallet = freeze Wallet.create(7)
	print("{wallet.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:28:22: 'Wallet' is unique (its field 'tickets' holds a unique 'Tickets') and cannot be frozen
```

<!-- test: a-for-binding-over-uniques-assigned-to-a-field-is-refused -->
The loop only lends each element of `tokens`, so storing it in `holder.token` would give a unique value two owners.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenArray = Array with Token

type Holder
	export var token as Token

	static function create() returns Self
		return Self{token: Token.create(0)}
	end 'create'
end 'Holder'

function main() returns ExitCode
	var tokens = TokenArray.create(0)
	tokens.push(Token.create(1))
	var holder = Holder.create()

	for token in tokens 'each'
		holder.token = token
	end 'each'

	print("{holder.token.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:28:18: cannot store into 'token' a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: a-for-binding-over-uniques-returned-from-the-function-is-refused -->
Returning the lent element would hand a unique value the array still owns to the caller.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenArray = Array with Token

function firstOf(tokens TokenArray) returns Token
	for token in tokens 'each'
		return token
	end 'each'

	return Token.create(0)
end 'firstOf'

function main() returns ExitCode
	var tokens = TokenArray.create(0)
	tokens.push(Token.create(1))
	let first = firstOf(tokens)
	print("{first.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:16:10: cannot return a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: a-for-binding-over-uniques-pushed-into-another-array-is-refused -->
Pushing the lent element into `kept` would give a unique value to a second array.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenArray = Array with Token

function main() returns ExitCode
	var tokens = TokenArray.create(0)
	tokens.push(Token.create(1))
	var kept = TokenArray.create(0)

	for token in tokens 'each'
		kept.push(token)
	end 'each'

	print("{kept.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:20:13: cannot store into 'an element of the container push writes' a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: a-for-binding-over-uniques-bound-to-a-var-is-refused -->
Binding the lent element to a `var` would make a second name for a unique value.

```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenArray = Array with Token

function main() returns ExitCode
	var tokens = TokenArray.create(0)
	tokens.push(Token.create(1))

	for token in tokens 'each'
		var second = token
		print("{second.n}\n")
	end 'each'

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:19:7: cannot bind 'second' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
```

<!-- test: a-for-over-an-array-of-unique-payload-unions-may-not-write-the-array -->
Each element is a union holding a unique `Ticket`, so the loop drains `slots` and the body may not write it, as it may not write an array of uniques it drains.

```maxon
typealias Tally = int(0 to 1024)

type Ticket is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Ticket'

union Slot
	held(ticket Ticket)
	empty
end 'Slot'

typealias SlotArray = Array with Slot

function main() returns ExitCode
	var slots = SlotArray.create(0)
	slots.push(Slot.held(Ticket.create(1)))

	for slot in slots 'each'
		match slot 'which'
			held(ticket) then print("{ticket.n}\n")
			empty then print("empty\n")
		end 'which'

		slots.push(Slot.empty)
	end 'each'

	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:29:9: cannot write through a read-only value while 'each' drains it
```

<!-- test: freezing-an-array-of-uniques-is-refused -->
An array holding `Ticket`s is itself unique, so it cannot be frozen, as a record holding the same array cannot.

```maxon
typealias Tally = int(0 to 1024)

type Ticket is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Ticket'

typealias TicketArray = Array with Ticket

function make() returns TicketArray
	var tickets = TicketArray.create(0)
	tickets.push(Ticket.create(1))
	return tickets
end 'make'

function main() returns ExitCode
	let sealed = freeze make()
	print("{sealed.count()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:21:22: 'TicketArray' is unique (its field 'element' holds a unique 'Ticket') and cannot be frozen
```

<!-- test: a-receiver-holding-uniques-read-by-a-builtin-twice-is-accepted -->
`count` only reads its receiver, so asking it twice moves nothing and the second read of `tickets` is legal.

```maxon
typealias Tally = int(0 to 1024)

type Ticket is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Ticket'

typealias TicketArray = Array with Ticket

function main() returns ExitCode
	let tickets = TicketArray.create(0)
	tickets.push(Ticket.create(1))
	let first = tickets.count()
	let second = tickets.count()
	print("{first} {second}\n")
	return 0
end 'main'
```
```stdout
1 1
```
```exitcode
0
```



<!-- test: a-let-rebind-of-a-let-unique-value-moves-it -->
`first` is a `let` holding a unique value, and `let second = first` moves it, exactly as it does from a `var`: the read of `first` that follows is a use of a moved value.
```maxon
typealias Number = int(0 to 999)

type Ticket is unique
	var n as Number

	static function create(n Number) returns Self
		return Self{n: n}
	end 'create'

	function show() returns Number
		return self.n
	end 'show'
end 'Ticket'

function main() returns ExitCode
	let first = Ticket.create(1)
	let second = first
	print("{first.show()} {second.show()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:19:10: use of moved value 'first': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-let-rebind-of-a-reply-moves-it -->
A `Reply` is unique, so `let moved = pending` moves it out of `pending` and the later `pending.answer(4)` is a use of a moved value.
```maxon
typealias Number = int(0 to 999)

type Desk
	var n as Number

	static function create() returns Self
		return Self{n: 0}
	end 'create'

	export function ask() returns Number throws ReplyStopped
		let pending = reply.later()
		let moved = pending
		moved.answer(3)
		pending.answer(4)
		return
	end 'ask'
end 'Desk'

function main() returns ExitCode
	let desk = spawn Desk.create()
	let asked = try desk.ask() otherwise panic("sent")
	let answer = try await asked otherwise panic("answered")
	print("{answer}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:15:3: use of moved value 'pending': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-unique-union-copied-in-the-arm-that-lends-it-is-refused -->
`match slot` lends the unique union to its arm, whose `token` binding views it, so `let copy = slot` in that arm gives the union a second name while it is lent.
```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	filled(token Token)
end 'Slot'

function main() returns ExitCode
	let slot = Slot.filled(Token.create(1))

	match slot 'which'
		empty then print("empty\n")
		filled(token) then let copy = slot
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:22:33: cannot move or copy the unique value matched by 'slot' inside the arm that lends it: the arm's bindings view it, and a unique value has one name; move or copy it before or after the `match`
```

<!-- test: a-unique-union-kept-by-a-call-in-the-arm-that-lends-it-is-refused -->
`keep` stores its argument, which moves `slot` out from under the `token` binding the arm reads, so the call is refused even though `token` is read before it.
```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	filled(token Token)
end 'Slot'

typealias SlotArray = Array with Slot

function keep(slot Slot) returns Tally
	let kept = SlotArray.create(0)
	kept.push(slot)

	return 1
end 'keep'

function main() returns ExitCode
	let slot = Slot.filled(Token.create(1))

	match slot 'which'
		empty then print("empty\n")
		filled(token) then print("{token.n} {keep(slot)}\n")
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:31:40: cannot pass 'slot' to parameter 'slot' of 'keep': it keeps a unique value, and 'slot' is matched by the arm this call is in, which lends it to its bindings; move it before or after the `match`
```

<!-- test: a-unique-union-stored-in-a-field-in-the-arm-that-lends-it-is-refused -->
Storing `slot` into a field in its own arm moves it, which the arm refuses.
```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	filled(token Token)
end 'Slot'

type Holder is unique
	export var slot as Slot

	static function create(slot Slot) returns Self
		return Self{slot: slot}
	end 'create'
end 'Holder'

function main() returns ExitCode
	let slot = Slot.filled(Token.create(1))
	let holder = Holder.create(Slot.empty)

	match slot 'which'
		empty then print("empty\n")
		filled(token) then holder.slot = slot
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:31:36: cannot move or copy the unique value matched by 'slot' inside the arm that lends it: the arm's bindings view it, and a unique value has one name; move or copy it before or after the `match`
```

<!-- test: a-closure-capturing-the-matched-unique-union-in-its-arm-is-refused -->
A closure made in the arm captures `slot`, which moves it into the closure.
```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	filled(token Token)
end 'Slot'

function peek(slot Slot) returns Tally
	return match slot 'which'
		empty gives 0
		filled(token) gives token.n
	end 'which'
end 'peek'

typealias Probe = function() returns Tally

function run(f Probe) returns Tally
	return f()
end 'run'

function main() returns ExitCode
	let slot = Slot.filled(Token.create(1))

	match slot 'which'
		empty then print("empty\n")
		filled(token) then print("{run(function() gives peek(slot))}\n")
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:35:60: cannot move or copy the unique value matched by 'slot' inside the arm that lends it: the arm's bindings view it, and a unique value has one name; move or copy it before or after the `match`
```

<!-- test: a-record-holding-the-matched-unique-union-kept-in-the-arm-is-refused -->
The arm of `match holder.slot` lends the record `holder` that owns the union, so a call that keeps `holder` is refused.
```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	filled(token Token)
end 'Slot'

type Holder is unique
	export var slot as Slot

	static function create(slot Slot) returns Self
		return Self{slot: slot}
	end 'create'
end 'Holder'

typealias HolderArray = Array with Holder

function keepHolder(holder Holder) returns Tally
	let kept = HolderArray.create(0)
	kept.push(holder)

	return 1
end 'keepHolder'

function main() returns ExitCode
	let holder = Holder.create(Slot.filled(Token.create(1)))

	match holder.slot 'which'
		empty then print("empty\n")
		filled(token) then print("{token.n} {keepHolder(holder)}\n")
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:39:40: cannot pass 'holder' to parameter 'holder' of 'keepHolder': it keeps a unique value, and 'holder' is matched by the arm this call is in, which lends it to its bindings; move it before or after the `match`
error E3207: <fragment>:39:51: 'keepHolder' needs a writable parameter 'holder' at line 29; 'holder' is read-only while 'which' is open
```

<!-- test: an-operand-of-a-cond-scrutinee-kept-in-the-arm-is-refused -->
Either operand of a `cond` scrutinee may be the union the arm views, so keeping `other` in the arm is refused as keeping `slot` is.
```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	filled(token Token)
end 'Slot'

typealias SlotArray = Array with Slot

function keep(slot Slot) returns Tally
	let kept = SlotArray.create(0)
	kept.push(slot)

	return 1
end 'keep'

function main() returns ExitCode
	let flag = true
	let slot = Slot.filled(Token.create(1))
	let other = Slot.empty

	match slot if flag else other 'which'
		empty then print("empty\n")
		filled(token) then print("{token.n} {keep(other)}\n")
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:33:40: cannot pass 'other' to parameter 'slot' of 'keep': it keeps a unique value, and 'other' is matched by the arm this call is in, which lends it to its bindings; move it before or after the `match`
error E3207: <fragment>:33:45: 'keep' needs a writable parameter 'slot' at line 21; 'other' is read-only while 'which' is open
```

<!-- test: a-by-reference-unique-union-kept-in-its-own-arm-is-refused -->
A `mutable` parameter that is matched is lent to its arms the same way a local is.
```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	filled(token Token)
end 'Slot'

typealias SlotArray = Array with Slot

function keep(slot Slot) returns Tally
	let kept = SlotArray.create(0)
	kept.push(slot)

	return 1
end 'keep'

function consume(slot mutable Slot)
	match slot 'which'
		empty then print("empty\n")
		filled(token) then print("{token.n} {keep(slot)}\n")
	end 'which'
end 'consume'

function main() returns ExitCode
	var slot = Slot.filled(Token.create(1))
	consume(slot)
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:29:40: cannot pass 'slot' to parameter 'slot' of 'keep': it keeps a unique value, and 'slot' is matched by the arm this call is in, which lends it to its bindings; move it before or after the `match`
```

<!-- test: a-unique-union-lent-to-a-reading-call-in-its-own-arm-runs -->
A call that only reads the union lends it again: the arm may read `slot` through `peek` and `token` together.
```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	filled(token Token)
end 'Slot'

function peek(slot Slot) returns Tally
	return match slot 'which'
		empty gives 0
		filled(token) gives token.n
	end 'which'
end 'peek'

function main() returns ExitCode
	let slot = Slot.filled(Token.create(1))

	match slot 'which'
		empty then print("empty\n")
		filled(token) then print("{token.n} {peek(slot)}\n")
	end 'which'

	return 0
end 'main'
```
```stdout
1 1
```
```exitcode
0
```

<!-- test: a-unique-union-kept-after-the-match-ends-runs -->
The refusal covers the arm only: after the `match` the union is moved as any unique value is.
```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	filled(token Token)
end 'Slot'

typealias SlotArray = Array with Slot

function keep(slot Slot) returns Tally
	let kept = SlotArray.create(0)
	kept.push(slot)

	return 1
end 'keep'

function main() returns ExitCode
	let slot = Slot.filled(Token.create(1))

	match slot 'which'
		empty then print("empty\n")
		filled(token) then print("{token.n}\n")
	end 'which'

	print("{keep(slot)}\n")
	return 0
end 'main'
```
```stdout
1
1
```
```exitcode
0
```

<!-- test: another-unique-union-kept-in-the-arm-runs -->
Only the union the `match` lends is refused: a second unique variable is kept in the arm as it would be anywhere.
```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	filled(token Token)
end 'Slot'

typealias SlotArray = Array with Slot

function keep(slot Slot) returns Tally
	let kept = SlotArray.create(0)
	kept.push(slot)

	return 1
end 'keep'

function main() returns ExitCode
	let slot = Slot.filled(Token.create(1))
	let other = Slot.filled(Token.create(2))

	match slot 'which'
		empty then print("empty\n")
		filled(token) then print("{token.n} {keep(other)}\n")
	end 'which'

	return 0
end 'main'
```
```stdout
1 1
```
```exitcode
0
```

<!-- test: a-unique-parameter-discarded-with-let-underscore-runs -->
A function that discards a unique parameter with `let _ = a` takes nothing from its caller.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function keepX(a Tk)
	let _ = a
end 'keepX'

function main() returns ExitCode
	let a = Tk.create(7)
	keepX(a)

	return 0
end 'main'
```
```exitcode
0
```

<!-- test: the-caller-keeps-a-unique-value-its-callee-discards -->
The discard moves nothing out of the caller, so the caller still reads the value it passed.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function keepX(a Tk)
	let _ = a
end 'keepX'

function main() returns ExitCode
	let a = Tk.create(7)
	keepX(a)
	print("{a.n}\n")

	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: a-unique-parameter-discarded-in-a-branch-runs -->
A discard that ends a branch is judged like one that ends the function.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function discardWhen(a Tk, flag bool)
	if flag 'whether'
		let _ = a
	end 'whether'
end 'discardWhen'

function main() returns ExitCode
	let a = Tk.create(7)
	discardWhen(a, flag: true)
	print("{a.n}\n")

	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: a-unique-parameter-assigned-to-the-discard-runs -->
`_ = a` discards a unique parameter as `let _ = a` does.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function dropIt(a Tk)
	_ = a
end 'dropIt'

function main() returns ExitCode
	let a = Tk.create(7)
	dropIt(a)
	print("{a.n}\n")

	return 0
end 'main'
```
```stdout
7
```
```exitcode
0
```

<!-- test: a-unique-parameter-read-after-its-discard-is-refused -->
The discard ends the function's use of the parameter.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function keepX(a Tk) returns Num
	let _ = a

	return a.n
end 'keepX'

function main() returns ExitCode
	let a = Tk.create(7)
	let n = keepX(a)
	print("{n}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:15:9: use of moved value 'a': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-unique-parameter-discarded-in-a-loop-is-refused -->
The second lap discards a parameter the first lap already discarded.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function discardTwice(a Tk)
	var i = 2 as Num

	while i != 0 'laps'
		let _ = a
		i = i - 1
	end 'laps'
end 'discardTwice'

function main() returns ExitCode
	let a = Tk.create(7)
	discardTwice(a)

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:16:11: use of moved value 'a': it was moved in an earlier iteration of this loop
```

<!-- test: a-unique-value-rebound-as-isolated-reads-through-the-new-name -->
`as isolated` on a bare local hands the value to the new name, as a plain rebind does.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let r = Tk.create(5) as isolated Tk
	let a = r as isolated Tk
	print("{a.n}\n")

	return 0
end 'main'
```
```stdout
5
```
```exitcode
0
```

<!-- test: a-unique-value-read-through-its-source-after-an-isolated-rebind-is-refused -->
The source of an `as isolated` rebind no longer holds the value.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function main() returns ExitCode
	let r = Tk.create(5) as isolated Tk
	let a = r as isolated Tk
	print("{a.n} {r.n}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:15:16: use of moved value 'r': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-union-holding-a-unique-cannot-be-cloned -->
A union whose payload is unique holds a unique, so its `clone()` would give the payload a second owner.
```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	none
	have(t Token)
end 'Slot'

function main() returns ExitCode
	let slot = Slot.have(Token.create(1))
	let copy = slot.clone()

	match copy 'x'
		none then break
		have(t) then print("{t.n}\n")
	end 'x'

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:19:13: 'Slot' is unique (its field 't' holds a unique 'Token') and cannot be cloned
```

<!-- test: a-unique-read-from-a-field-is-not-handed-to-a-service -->
A send hands its argument to the service's green thread, so a unique value only read out of a record's field, which the record keeps, cannot be sent.
```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

type Holder
	var token as Token

	static function create(token Token) returns Self
		return Self{token: token}
	end 'create'
end 'Holder'

type Shelf
	var token as Token

	static function create(token isolated Token) returns Self
		return Self{token: merge token}
	end 'create'

	export function put(held Token)
		self.token = held
	end 'put'
end 'Shelf'

function main() returns ExitCode
	let shelf = spawn Shelf.create(Token.create(0))
	let holder = Holder.create(Token.create(1))
	try shelf.put(holder.token) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:35:16: cannot hand 'holder.token' to argument `held` of the message `Shelf.put`: it keeps a unique value, and a unique value cannot be moved out of a field or an element it was only read from; move it out with `pop` or `remove`, or `swap` it out of an isolated field
```

<!-- test: a-unique-payload-lent-by-a-match-is-not-handed-to-a-service -->
The arm's binding views the unique union it matched, so sending the payload would give the service a second owner while the union still holds it.
```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	none
	have(t Token)
end 'Slot'

type Shelf
	var token as Token

	static function create(token isolated Token) returns Self
		return Self{token: merge token}
	end 'create'

	export function put(held Token)
		self.token = held
	end 'put'
end 'Shelf'

function main() returns ExitCode
	let shelf = spawn Shelf.create(Token.create(0))
	let slot = Slot.have(Token.create(1))

	match slot 'x'
		none then break
		have(t) then try shelf.put(t) otherwise ignore
	end 'x'

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:35:30: cannot hand 't' to argument `held` of the message `Shelf.put`: it keeps a unique value, and 't' is only borrowed by this code, and only the code that owns a unique value hands it to another green thread; move it in from its owner
```

<!-- test: a-unique-element-of-an-array-field-is-not-handed-to-a-service -->
Sending the array a record's field holds is a read of storage too: the record keeps the array and every unique in it.
```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias TokenArray = Array with Token

type Holder
	var tokens as TokenArray

	static function create(tokens TokenArray) returns Self
		return Self{tokens: tokens}
	end 'create'
end 'Holder'

type Sink
	var count as Tally

	static function create() returns Self
		return Self{count: 0}
	end 'create'

	export function take(tokens TokenArray)
		self.count = tokens.count() as Tally
	end 'take'
end 'Sink'

function main() returns ExitCode
	let sink = spawn Sink.create()
	let holder = Holder.create(TokenArray.create(0))
	holder.tokens.push(Token.create(1))
	try sink.take(holder.tokens) otherwise ignore
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:38:16: cannot hand 'holder.tokens' to argument `tokens` of the message `Sink.take`: it keeps a unique value, and a unique value cannot be moved out of a field or an element it was only read from; move it out with `pop` or `remove`, or `swap` it out of an isolated field
```

<!-- test: an-owned-unique-is-handed-to-a-service -->
A local the sender owns is moved into the message, and the service's own slot takes it.
```maxon
typealias Tally = int(0 to 1024)

type Token is unique
	export var n as Tally

	static function create(n Tally) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

type Shelf
	var token as Token

	static function create(token isolated Token) returns Self
		return Self{token: merge token}
	end 'create'

	export function put(held Token)
		self.token = held
	end 'put'
end 'Shelf'

function main() returns ExitCode
	let shelf = spawn Shelf.create(Token.create(0))
	let fresh = Token.create(1)
	try shelf.put(fresh) otherwise ignore
	try shelf.put(Token.create(2)) otherwise ignore
	return 0
end 'main'
```
```exitcode
0
```

<!-- test: a-write-through-the-holder-of-an-array-of-uniques-in-a-for-over-it-is-refused -->
`for t in h.xs` lends elements that `h` holds, so `h` is read-only in the body.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

typealias TkArray = Array with Tk

type H
	var xs as TkArray
	var n as Num

	static function create() returns Self
		let xs = TkArray.create(0)
		xs.push(Tk.create(1))
		return Self{xs: xs, n: 0}
	end 'create'
end 'H'

function main() returns ExitCode
	let h = H.create()

	for t in h.xs 'each'
		h.n = t.n
	end 'each'

	print("{h.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3197: <fragment>:29:3: cannot write 'n' through 'h', which is read-only while 'each' drains it
```

<!-- test: the-holder-of-an-array-of-uniques-passed-to-a-writing-parameter-in-a-for-over-it-is-refused -->
`bump` writes `h`, which is read-only in the body of a `for` over `h.xs`.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

typealias TkArray = Array with Tk

type H
	var xs as TkArray
	var n as Num

	static function create() returns Self
		let xs = TkArray.create(0)
		xs.push(Tk.create(1))
		return Self{xs: xs, n: 0}
	end 'create'
end 'H'

function bump(h H)
	h.n = h.n + 1
end 'bump'

function main() returns ExitCode
	let h = H.create()

	for t in h.xs 'each'
		print("{t.n}\n")
		bump(h)
	end 'each'

	print("{h.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:34:8: 'bump' needs a writable parameter 'h' at line 26; 'h' is read-only while 'each' is open
```

<!-- test: a-cond-of-lent-parameters-stored-keeps-the-then-arm -->
`stash` stores a `cond` of its two unique parameters, so it keeps both, and the caller's `t` moves into the call.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Kp
	var k as Tk

	static function create(k Tk) returns Self
		return Self{k: k}
	end 'create'
end 'Kp'

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function stash(t Tk, t2 Tk, h Kp)
	let u = (t if chosen(1) else t2)
	h.k = u
end 'stash'

function main() returns ExitCode
	let h = Kp.create(Tk.create(0))
	let t = Tk.create(1)
	stash(t, t2: Tk.create(2), h: h)
	h.k.n = 5
	return 0 if t.n == 5 else 1
end 'main'
```
```maxoncstderr
error E3102: <fragment>:34:14: use of moved value 't': its ownership moved to the parameter 't' of 'stash', which keeps it
```

<!-- test: a-cond-of-lent-parameters-stored-keeps-the-else-arm -->
The `else` arm of the stored `cond` is kept as the `then` arm is, so the caller's `t` passed at `t2` moves into the call.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Kp
	var k as Tk

	static function create(k Tk) returns Self
		return Self{k: k}
	end 'create'
end 'Kp'

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function stash(t Tk, t2 Tk, h Kp)
	h.k = t if chosen(0) else t2
end 'stash'

function main() returns ExitCode
	let h = Kp.create(Tk.create(0))
	let t = Tk.create(1)
	stash(Tk.create(2), t2: t, h: h)
	h.k.n = 5
	return 0 if t.n == 5 else 1
end 'main'
```
```maxoncstderr
error E3102: <fragment>:33:14: use of moved value 't': its ownership moved to the parameter 't2' of 'stash', which keeps it
```

<!-- test: a-cond-of-unique-locals-passed-to-a-call-lends-them -->
A `cond` handed straight to a call lends its operands for the call, so both stay usable afterwards.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function readN(t Tk) returns Num
	return t.n
end 'readN'

function bump(t Tk)
	t.n = t.n + 1
end 'bump'

function main() returns ExitCode
	let q = Tk.create(1)
	let q2 = Tk.create(2)
	let z = readN(q if chosen(1) else q2)
	bump(q)
	bump(q2)
	print("{z} {q.n} {q2.n}\n")
	return 0
end 'main'
```
```stdout
1 2 3
```

<!-- test: a-cond-of-one-unique-local-passed-to-a-call-lends-it -->
Both arms may name the same local: the call borrows it once.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function readN(t Tk) returns Num
	return t.n
end 'readN'

function main() returns ExitCode
	let q = Tk.create(4)
	let z = readN(q if chosen(1) else q)
	q.n = 5
	print("{z} {q.n}\n")
	return 0
end 'main'
```
```stdout
4 5
```

<!-- test: a-cond-of-unique-locals-as-a-receiver-lends-them -->
A `cond` that is the receiver of a call lends its operands for the call.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function doubled() returns Num
		return self.n * 2
	end 'doubled'
end 'Tk'

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function main() returns ExitCode
	let q = Tk.create(1)
	let q2 = Tk.create(2)
	let z = (q if chosen(0) else q2).doubled()
	q.n = 7
	print("{z} {q.n} {q2.n}\n")
	return 0
end 'main'
```
```stdout
4 7 2
```

<!-- test: a-cond-of-unique-unions-as-a-match-subject-lends-them -->
A `cond` that is a `match` subject lends its operands to the `match`, so both unions are matched again afterwards.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

union Slot
	empty
	filled(token Tk)
end 'Slot'

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function show(slot Slot)
	match slot 'which'
		empty then print("empty\n")
		filled(token) then print("{token.n}\n")
	end 'which'
end 'show'

function main() returns ExitCode
	let u = Slot.filled(Tk.create(1))
	let w = Slot.filled(Tk.create(2))

	match (u if chosen(1) else w) 'picked'
		empty then print("none\n")
		filled(token) then print("picked {token.n}\n")
	end 'picked'

	show(u)
	show(w)
	return 0
end 'main'
```
```stdout
picked 1
1
2
```

<!-- test: a-cond-of-unique-locals-bound-moves-both -->
Binding a `cond` moves every unique local it names, whichever arm ran, so `q2` is moved.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function main() returns ExitCode
	let q = Tk.create(1)
	let q2 = Tk.create(2)
	let r = q if chosen(1) else q2
	print("{r.n} {q2.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:20:16: use of moved value 'q2': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-cond-of-one-unique-local-bound-moves-it -->
A `cond` that names one unique local in both arms moves it when bound: `r` would otherwise be a second name for `q`.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function main() returns ExitCode
	let q = Tk.create(1)
	let r = q if chosen(1) else q
	r.n = 5
	print("{q.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:20:10: use of moved value 'q': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-bound-cond-of-unique-locals-releases-the-other-arm -->
The bound `cond` owns the chosen value and the other local is released, on either path.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Kp
	var k as Tk

	static function create(k Tk) returns Self
		return Self{k: k}
	end 'create'
end 'Kp'

function bound(flag bool) returns Num
	let q = Tk.create(1)
	let q2 = Tk.create(2)
	let r = q if flag else q2
	return r.n
end 'bound'

function stored(flag bool) returns Num
	let h = Kp.create(Tk.create(0))
	let q = Tk.create(3)
	let q2 = Tk.create(4)
	h.k = q if flag else q2
	return h.k.n
end 'stored'

function main() returns ExitCode
	print("{bound(true)} {bound(false)} {stored(true)} {stored(false)}\n")
	return 0
end 'main'
```
```stdout
1 2 3 4
```

<!-- test: a-cond-of-unique-locals-stored-moves-them -->
Storing a `cond` into a field moves the unique locals it names.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Kp
	var k as Tk

	static function create(k Tk) returns Self
		return Self{k: k}
	end 'create'
end 'Kp'

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function main() returns ExitCode
	let h = Kp.create(Tk.create(0))
	let q = Tk.create(1)
	let q2 = Tk.create(2)
	h.k = q if chosen(1) else q2
	h.k.n = 5
	print("{q.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:30:10: use of moved value 'q': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-cond-of-unique-locals-passed-to-a-keeping-callee-moves-them -->
A callee that keeps its parameter takes every unique local of the `cond` passed there.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

typealias TkArray = Array with Tk

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function keep(t Tk, into TkArray)
	into.push(t)
end 'keep'

function main() returns ExitCode
	let kept = TkArray.create(0)
	let q = Tk.create(1)
	let q2 = Tk.create(2)
	keep(q if chosen(1) else q2, into: kept)
	print("{q2.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:27:10: use of moved value 'q2': its ownership moved to the parameter 't' of 'keep', which keeps it
```

<!-- test: a-unique-local-passed-beside-a-cond-of-it-is-refused -->
The `cond` may be `q` itself, so passing `q` beside it would hand the callee two names for one unique value.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function total(a Tk, b Tk) returns Num
	b.n = b.n + 1
	return a.n + b.n
end 'total'

function main() returns ExitCode
	let q = Tk.create(1)
	let q2 = Tk.create(2)
	print("{total(q if chosen(1) else q2, b: q)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:24:43: 'q' is passed at two arguments of this call, and a unique value has one name; pass it once
```

<!-- test: a-nested-cond-of-unique-locals-passed-to-a-call-lends-them -->
A `cond` in an arm of another is looked through: the call borrows all three locals.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function readN(t Tk) returns Num
	return t.n
end 'readN'

function main() returns ExitCode
	let q = Tk.create(1)
	let q2 = Tk.create(2)
	let q3 = Tk.create(3)
	let z = readN(q if chosen(0) else (q2 if chosen(0) else q3))
	q.n = 4
	q2.n = 5
	q3.n = 6
	print("{z} {q.n} {q2.n} {q3.n}\n")
	return 0
end 'main'
```
```stdout
3 4 5 6
```

<!-- test: a-nested-cond-of-unique-locals-bound-moves-them-all -->
Binding a `cond` whose arm is another `cond` moves every local either one names.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function pick(a bool, b bool) returns Num
	let q = Tk.create(1)
	let q2 = Tk.create(2)
	let q3 = Tk.create(3)
	let r = q if a else (q2 if b else q3)
	return r.n
end 'pick'

function main() returns ExitCode
	print("{pick(true, b: true)} {pick(false, b: true)} {pick(false, b: false)}\n")
	return 0
end 'main'
```
```stdout
1 2 3
```

<!-- test: a-nested-cond-bound-leaves-no-local-usable -->
`q3` is named only by the inner `cond`, and binding the outer one moves it too.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function main() returns ExitCode
	let q = Tk.create(1)
	let q2 = Tk.create(2)
	let q3 = Tk.create(3)
	let r = q if chosen(1) else (q2 if chosen(1) else q3)
	print("{r.n} {q3.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:21:16: use of moved value 'q3': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-try-fallback-of-a-unique-local-bound-moves-it -->
Binding a value `try` whose fallback is a unique local moves the local, whichever edge ran: `r` would otherwise be a second name for `q`.
```maxon
typealias Num = int(i64.min to i64.max)

enum MkError implements Error
	none
end 'MkError'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function mk(n Num) returns Tk throws MkError
	if n == 0 'absent'
		throw MkError.none
	end 'absent'

	return Tk.create(n)
end 'mk'

function main() returns ExitCode
	let q = Tk.create(1)
	let r = try mk(0) otherwise q
	r.n = 5
	print("{q.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:28:10: use of moved value 'q': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-try-fallback-of-a-unique-local-passed-to-a-call-lends-it -->
A value `try` handed straight to a call lends its fallback for the call, so the local stays usable afterwards.
```maxon
typealias Num = int(i64.min to i64.max)

enum MkError implements Error
	none
end 'MkError'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function mk(n Num) returns Tk throws MkError
	if n == 0 'absent'
		throw MkError.none
	end 'absent'

	return Tk.create(n)
end 'mk'

function readN(t Tk) returns Num
	return t.n
end 'readN'

function main() returns ExitCode
	let q = Tk.create(1)
	let z = readN(try mk(0) otherwise q)
	let w = readN(try mk(7) otherwise q)
	q.n = 5
	print("{z} {w} {q.n}\n")
	return 0
end 'main'
```
```stdout
1 7 5
```

<!-- test: a-match-expression-naming-one-unique-local-in-every-arm-moves-it -->
A `match` expression whose every arm names `q` moves it once when bound, nested or not: `r` would otherwise be a second name for `q`.
```maxon
typealias Num = int(i64.min to i64.max)

enum Pick
	first
	second
end 'Pick'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function viaMatch(p Pick) returns Num
	let q = Tk.create(1)
	let r = match p 'k'
		first gives q
		second gives q
	end 'k'
	r.n = 5
	return q.n
end 'viaMatch'

function viaNestedMatch(p Pick, inner bool) returns Num
	let q = Tk.create(2)
	let r = match p 'k'
		first gives q
		second gives q if inner else q
	end 'k'
	r.n = 5
	return q.n
end 'viaNestedMatch'

function main() returns ExitCode
	print("{viaMatch(Pick.first)} {viaNestedMatch(Pick.second, inner: true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:24:9: use of moved value 'q': its ownership moved to another binding at an earlier bind or assignment
error E3102: <fragment>:34:9: use of moved value 'q': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-try-fallback-in-a-cond-of-a-unique-local-bound-moves-it -->
A conditional whose arms are a value `try` falling back to `q` and `q` itself moves `q` when bound, whichever edge ran.
```maxon
typealias Num = int(i64.min to i64.max)

enum MkError implements Error
	none
end 'MkError'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function mk(n Num) returns Tk throws MkError
	if n == 0 'absent'
		throw MkError.none
	end 'absent'

	return Tk.create(n)
end 'mk'

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function main() returns ExitCode
	let q = Tk.create(1)
	let r = (try mk(0) otherwise q) if chosen(1) else q
	r.n = 5
	print("{q.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:32:10: use of moved value 'q': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-merge-naming-one-unique-local-in-every-arm-hands-it-on-once -->
A conditional, a `match` expression and a value `try` that name `q` in every arm that can name it, alone or nested in another merge, hand the one value on: each binding reads what `q` held, and nothing is released twice.
```maxon
typealias Num = int(i64.min to i64.max)

enum Pick
	first
	second
end 'Pick'

enum MkError implements Error
	none
end 'MkError'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function mk(n Num) returns Tk throws MkError
	if n == 0 'absent'
		throw MkError.none
	end 'absent'

	return Tk.create(n)
end 'mk'

function viaCond(flag bool, inner bool) returns Num
	let q = Tk.create(1)
	let q2 = Tk.create(2)
	let r = q if flag else q
	let s = (q2 if inner else q2) if flag else q2
	return r.n * 10 + s.n
end 'viaCond'

function viaMatch(p Pick, inner bool) returns Num
	let q = Tk.create(3)
	let q2 = Tk.create(4)
	let r = match p 'k'
		first gives q
		second gives q
	end 'k'
	let s = match p 'k'
		first gives q2
		second gives q2 if inner else q2
	end 'k'
	return r.n * 10 + s.n
end 'viaMatch'

function viaTry(code Num, flag bool) returns Num
	let q = Tk.create(5)
	let q2 = Tk.create(6)
	let r = try mk(code) otherwise q
	let s = (try mk(code) otherwise q2) if flag else q2
	return r.n * 10 + s.n
end 'viaTry'

function main() returns ExitCode
	print("{viaCond(true, inner: false)} {viaCond(false, inner: true)}\n")
	print("{viaMatch(Pick.first, inner: true)} {viaMatch(Pick.second, inner: false)}\n")
	print("{viaTry(0, flag: true)} {viaTry(7, flag: false)}\n")
	return 0
end 'main'
```
```stdout
12 12
34 34
56 76
```
```exitcode
0
```

<!-- test: a-bound-try-fallback-of-a-unique-local-releases-it-on-either-edge -->
The bound value `try` owns whichever value reached it, and the fallback local is released on both edges.
```maxon
typealias Num = int(i64.min to i64.max)

enum MkError implements Error
	none
end 'MkError'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function mk(n Num) returns Tk throws MkError
	if n == 0 'absent'
		throw MkError.none
	end 'absent'

	return Tk.create(n)
end 'mk'

function bound(n Num) returns Num
	let q = Tk.create(1)
	let r = try mk(n) otherwise q
	return r.n
end 'bound'

function main() returns ExitCode
	print("{bound(0)} {bound(7)}\n")
	return 0
end 'main'
```
```stdout
1 7
```

<!-- test: a-unique-parameter-passed-beside-a-cond-of-it-is-refused -->
A `cond` of two unique parameters may be `t` itself, so passing `t` beside it hands the callee two names for one unique value.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function total(a Tk, b Tk) returns Num
	b.n = b.n + 1
	return a.n + b.n
end 'total'

function g(t Tk, t2 Tk, c bool) returns Num
	return total(t, b: t if c else t2)
end 'g'

function main() returns ExitCode
	print("{g(Tk.create(1), t2: Tk.create(2), c: true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:18:21: 't' is passed at two arguments of this call, and a unique value has one name; pass it once
```

<!-- test: a-bound-cond-of-unique-parameters-moves-them -->
Binding a `cond` of unique parameters moves them as `let u = t` moves `t`, so `t` is not read afterwards.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

function g(t Tk, t2 Tk, c bool) returns Num
	let u = (t if c else t2)
	t.n = 3
	return u.n
end 'g'

function main() returns ExitCode
	print("{g(Tk.create(1), t2: Tk.create(2), c: true)}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:14:2: use of moved value 't': its ownership moved to another binding at an earlier bind or assignment
```

<!-- test: a-literal-naming-a-unique-local-after-a-cond-of-it-is-refused -->
A tuple, a struct literal and a union case that name `q` after a `cond` that may be `q` would hold two names for one unique value.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Two
	var a as Tk
	var b as Tk

	static function of(q Tk, q2 Tk, c bool) returns Self
		return Self{a: q if c else q2, b: q}
	end 'of'
end 'Two'

union Both
	none
	both(a Tk, b Tk)
end 'Both'

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function tuple() returns Num
	let q = Tk.create(1)
	let q2 = Tk.create(2)
	let t = (q if chosen(1) else q2, q)
	return t.0.n + t.1.n
end 'tuple'

function payloads() returns Num
	let q = Tk.create(1)
	let q2 = Tk.create(2)

	match Both.both(q if chosen(1) else q2, b: q) 'made'
		none then return 0
		both(a, b) then return a.n + b.n
	end 'made'
end 'payloads'

function main() returns ExitCode
	let two = Two.of(Tk.create(1), q2: Tk.create(2), c: true)
	print("{tuple()} {payloads()} {two.a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:17:37: 'q' is named at two fields of this literal, and a unique value has one name; name it once
error E3102: <fragment>:33:35: 'q' is named at two elements of this tuple, and a unique value has one name; name it once
error E3102: <fragment>:41:45: 'q' is named at two payloads of this case, and a unique value has one name; name it once
```

<!-- test: a-literal-naming-a-unique-local-before-a-cond-of-it-is-refused -->
The order does not matter: naming `q` first and the `cond` second is refused as well.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Two
	var a as Tk
	var b as Tk

	static function of(q Tk, q2 Tk, c bool) returns Self
		return Self{a: q, b: q if c else q2}
	end 'of'
end 'Two'

union Both
	none
	both(a Tk, b Tk)
end 'Both'

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function tuple() returns Num
	let q = Tk.create(1)
	let q2 = Tk.create(2)
	let t = (q, q if chosen(1) else q2)
	return t.0.n + t.1.n
end 'tuple'

function payloads() returns Num
	let q = Tk.create(1)
	let q2 = Tk.create(2)

	match Both.both(q, b: q if chosen(1) else q2) 'made'
		none then return 0
		both(a, b) then return a.n + b.n
	end 'made'
end 'payloads'

function main() returns ExitCode
	let two = Two.of(Tk.create(1), q2: Tk.create(2), c: true)
	print("{tuple()} {payloads()} {two.a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:17:24: 'q' is named at two fields of this literal, and a unique value has one name; name it once
error E3102: <fragment>:33:14: 'q' is named at two elements of this tuple, and a unique value has one name; name it once
error E3102: <fragment>:41:24: 'q' is named at two payloads of this case, and a unique value has one name; name it once
```

<!-- test: an-interpolated-cond-of-unique-locals-leaves-them-usable -->
Interpolating a `cond` of unique locals reads them; it does not move them.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique implements Stringable
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function toString() returns String
		return "tk{self.n}"
	end 'toString'
end 'Tk'

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function main() returns ExitCode
	let q = Tk.create(1)
	let q2 = Tk.create(2)
	print("{q if chosen(1) else q2} and {q2 if chosen(1) else q} then {q.n}\n")
	q.n = 3
	q2.n = 4
	print("{q} {q2}\n")
	return 0
end 'main'
```
```stdout
tk1 and tk2 then 1
tk3 tk4
```

<!-- test: a-fresh-result-holding-a-unique-crosses-whole -->
`fresh` builds the `Conn` it returns, so its result is the whole of its region and crosses to the service.
```maxon
typealias Integer = int(i64.min to i64.max)

type Tk is unique
	var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Data
	var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Data'

type Conn
	var tk as Tk
	var data as Data

	static function create(tk Tk, data Data) returns Self
		return Self{tk: tk, data: data}
	end 'create'
end 'Conn'

type Sink
	var total as Integer

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function take(c Conn) returns Integer
		c.data.n = c.data.n + 100
		self.total = self.total + c.tk.n + c.data.n
		return self.total
	end 'take'
end 'Sink'

function fresh(n Integer) returns Conn
	let c = Conn.create(Tk.create(n), data: Data.create(n))
	c.data.n = c.data.n + 1
	return c
end 'fresh'

function main() returns ExitCode
	let svc = spawn Sink.create()
	let got = try await svc.take(fresh(1)) otherwise 0
	let c = fresh(2)
	let more = try await svc.take(c) otherwise 0
	print("{got} {more}\n")
	return 0
end 'main'
```
```stdout
103 208
```

<!-- test: a-result-sharing-a-part-with-the-sender-does-not-cross -->
`sharing` returns a `Conn` holding the caller's `d`, so the local bound from it is not the whole of its region and does not cross.
```maxon
typealias Integer = int(i64.min to i64.max)

type Tk is unique
	var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type Data
	var n as Integer

	static function create(n Integer) returns Self
		return Self{n: n}
	end 'create'
end 'Data'

type Conn
	var tk as Tk
	var data as Data

	static function create(tk Tk, data Data) returns Self
		return Self{tk: tk, data: data}
	end 'create'
end 'Conn'

type Sink
	var total as Integer

	static function create() returns Self
		return Self{total: 0}
	end 'create'

	export function take(c Conn) returns Integer
		c.data.n = c.data.n + 100
		self.total = self.total + c.tk.n + c.data.n
		return self.total
	end 'take'
end 'Sink'

function sharing(d Data) returns Conn
	let c = Conn.create(Tk.create(1), data: d)
	c.data.n = c.data.n + 1
	return c
end 'sharing'

function main() returns ExitCode
	let svc = spawn Sink.create()
	let d = Data.create(5)
	let c = sharing(d)
	let got = try await svc.take(c) otherwise 0
	d.n = 7
	print("{got}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3200: <fragment>:53:31: 'c' crosses to the service it is sent to only as the whole of its region, and 'Conn' is not closed: 'Conn.data' is a plain 'Data' that other code may still name. Declare 'data' `isolated`, pass a fresh value, or consume the value here
```

<!-- test: a-try-fallback-of-a-loop-element-is-refused -->
A loop element stays owned by its array, so a value `try` whose fallback is one would give a second name to a unique value the array goes on owning, as `let r = t` would.
```maxon
typealias Num = int(i64.min to i64.max)

enum MkError implements Error
	none
end 'MkError'

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

typealias TkArray = Array with Tk

function mk(n Num) returns Tk throws MkError
	if n == 0 'absent'
		throw MkError.none
	end 'absent'

	return Tk.create(n)
end 'mk'

function main() returns ExitCode
	let tokens = TkArray.create(0)
	tokens.push(Tk.create(1))

	for t in tokens 'each'
		let r = try mk(0) otherwise t
		r.n = 9
	end 'each'

	for t in tokens 'again'
		print("{t.n}\n")
	end 'again'

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:31:11: cannot join a unique value read out of a field or an element with another value: the field or element goes on owning it, and a unique value has one name; apply the use inside each arm instead, as `f(t) if c else f(fresh)` does
```

<!-- test: a-cond-of-a-loop-element-and-a-fresh-value-is-refused -->
The fresh arm makes the `cond` own its result, and the loop element's arm is still a second name for a value the array owns.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

typealias TkArray = Array with Tk

function main() returns ExitCode
	let tokens = TkArray.create(0)
	tokens.push(Tk.create(1))

	for t in tokens 'each'
		let r = t if t.n == 1 else Tk.create(5)
		r.n = 9
	end 'each'

	for t in tokens 'again'
		print("{t.n}\n")
	end 'again'

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:19:13: cannot join a unique value read out of a field or an element with another value: the field or element goes on owning it, and a unique value has one name; apply the use inside each arm instead, as `f(t) if c else f(fresh)` does
```

<!-- test: a-unique-operand-a-later-operand-moves-is-refused -->
A later argument that moves a unique value an earlier one already passed — through a nested tuple, a call that keeps it, a closure that captures it, or a `cond` — would hand the callee two names for one unique value.
```maxon
typealias Num = int(i64.min to i64.max)
typealias Reader = function() returns Num

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'

	function plus(o One) returns Num
		return self.n + o.x.n
	end 'plus'
end 'Tk'

type One
	var x as Tk

	static function of(x Tk) returns Self
		return Self{x: x}
	end 'of'
end 'One'

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function total(a Tk, b (Tk, Num)) returns Num
	return a.n + b.0.n + b.1
end 'total'

function sum(a Tk, b One) returns Num
	return a.n + b.x.n
end 'sum'

function run(t Tk, f Reader) returns Num
	return t.n + f()
end 'run'

function tupleArgument() returns Num
	let q = Tk.create(1)
	let q2 = Tk.create(2)
	return total(q, b: (q if chosen(1) else q2, 1))
end 'tupleArgument'

function keptArgument() returns Num
	let q = Tk.create(1)
	return sum(q, b: One.of(q))
end 'keptArgument'

function keptCond() returns Num
	let q = Tk.create(1)
	let q2 = Tk.create(2)
	return sum(q, b: One.of(q if chosen(1) else q2))
end 'keptCond'

function condBesideKept() returns Num
	let q = Tk.create(1)
	let q2 = Tk.create(2)
	return sum(q if chosen(1) else q2, b: One.of(q))
end 'condBesideKept'

function captured() returns Num
	let q = Tk.create(1)
	return run(q, f: function() gives q.n)
end 'captured'

function receiver() returns Num
	let q = Tk.create(1)
	return q.plus(One.of(q))
end 'receiver'

function parameterTuple(p Tk, p2 Tk) returns Num
	return total(p, b: (p if chosen(1) else p2, 1))
end 'parameterTuple'

function parameterKept(p Tk) returns Num
	return sum(p, b: One.of(p))
end 'parameterKept'

function main() returns ExitCode
	let z = tupleArgument() + keptArgument() + keptCond() + condBesideKept() + captured() + receiver() + parameterTuple(Tk.create(1), p2: Tk.create(2)) + parameterKept(Tk.create(1))
	print("{z}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:44:22: 'q' is passed at two arguments of this call, and a unique value has one name; pass it once
error E3102: <fragment>:49:26: 'q' is passed at two arguments of this call, and a unique value has one name; pass it once
error E3102: <fragment>:55:26: 'q' is passed at two arguments of this call, and a unique value has one name; pass it once
error E3102: <fragment>:61:47: 'q' is passed at two arguments of this call, and a unique value has one name; pass it once
error E3102: <fragment>:66:36: 'q' is passed at two arguments of this call, and a unique value has one name; pass it once
error E3102: <fragment>:71:23: 'q' is passed at two arguments of this call, and a unique value has one name; pass it once
error E3102: <fragment>:75:22: 'p' is passed at two arguments of this call, and a unique value has one name; pass it once
error E3102: <fragment>:79:26: 'p' is passed at two arguments of this call, and a unique value has one name; pass it once
```

<!-- test: a-literal-whose-nested-operand-moves-a-unique-operand-is-refused -->
A field, an element or a payload naming `q` beside a nested literal or call that moves `q` is refused where the nested one names it.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

type One
	var x as Tk

	static function of(x Tk) returns Self
		return Self{x: x}
	end 'of'
end 'One'

type Two
	var a as Tk
	var b as One

	static function of(q Tk, q2 Tk, c bool) returns Self
		return Self{a: q, b: One.of(q if c else q2)}
	end 'of'
end 'Two'

union Both
	none
	both(a Tk, b (Tk, Num))
end 'Both'

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function nestedCond() returns Num
	let q = Tk.create(1)
	let q2 = Tk.create(2)
	let t = (q, (q if chosen(1) else q2, 1))
	return t.0.n + t.1.0.n
end 'nestedCond'

function nestedTuple() returns Num
	let q = Tk.create(1)
	let t = (q, (q, 1))
	return t.0.n + t.1.0.n
end 'nestedTuple'

function payloads() returns Num
	let q = Tk.create(1)
	let q2 = Tk.create(2)

	match Both.both(q, b: (q if chosen(1) else q2, 1)) 'made'
		none then return 0
		both(a, b) then return a.n + b.0.n
	end 'made'
end 'payloads'

function main() returns ExitCode
	let two = Two.of(Tk.create(1), q2: Tk.create(2), c: true)
	print("{nestedCond()} {nestedTuple()} {payloads()} {two.a.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:25:31: 'q' is named at two fields of this literal, and a unique value has one name; name it once
error E3102: <fragment>:41:15: 'q' is named at two elements of this tuple, and a unique value has one name; name it once
error E3102: <fragment>:47:15: 'q' is named at two elements of this tuple, and a unique value has one name; name it once
error E3102: <fragment>:55:25: 'q' is named at two payloads of this case, and a unique value has one name; name it once
```

<!-- test: a-cond-of-unique-unions-as-a-match-subject-lends-each-to-the-arms -->
A `match` over a `cond` lends every union the `cond` names to its arms, as a `match` over one of them does, so an arm cannot hand `u` to a callee that keeps it.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

union Slot
	empty
	filled(token Tk)
end 'Slot'

type Keeper
	var held as Slot

	static function create() returns Self
		return Self{held: Slot.empty}
	end 'create'

	function keep(s Slot)
		self.held = s
	end 'keep'
end 'Keeper'

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function main() returns ExitCode
	let k = Keeper.create()
	let u = Slot.filled(Tk.create(1))
	let w = Slot.filled(Tk.create(2))

	match (u if chosen(1) else w) 'picked'
		empty then print("empty\n")
		filled(token) then k.keep(u)
	end 'picked'

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:40:24: cannot pass 'u' to parameter 's' of 'Keeper.keep': it keeps a unique value, and 'u' is matched by the arm this call is in, which lends it to its bindings; move it before or after the `match`
error E3207: <fragment>:40:29: 'keep' needs a writable parameter 's' at line 25; 'u' is read-only while 'picked' is open
```

<!-- test: a-cond-of-unique-arrays-as-a-loop-source-keeps-each-read-only -->
A loop over a `cond` holds every array the `cond` names, as a loop over one of them does, so the body cannot clear `xs`.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

typealias Tks = Array with Tk

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function filled(n Num) returns Tks
	let xs = Tks.create(0)
	xs.push(Tk.create(n))
	return xs
end 'filled'

function main() returns ExitCode
	let xs = filled(1)
	let ys = filled(2)

	for e in (xs if chosen(1) else ys) 'each'
		print("{e.n}\n")
		xs.clear()
	end 'each'

	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:30:3: 'clear' needs a writable receiver at line 306; 'xs' is read-only while 'each' is open
```

<!-- test: a-try-fallback-beside-a-unique-element-read-is-refused -->
A `try` whose result is an element read out of an array of unique values and whose fallback is fresh would give the merge a second name for the element the array still owns.
```maxon
typealias Num = int(i64.min to i64.max)

type Token is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

typealias Tokens = Array with Token

function main() returns ExitCode
	let xs = Tokens.create(0)
	xs.push(Token.create(1))
	let k = try xs.get(0) otherwise Token.create(9)
	k.n = 4
	print("{k.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:17:10: cannot join a unique value read out of a field or an element with another value: the field or element goes on owning it, and a unique value has one name; apply the use inside each arm instead, as `f(t) if c else f(fresh)` does
```

<!-- test: a-try-fallback-of-a-matched-payload-handed-to-a-keeping-call-moves-it -->
A `try` whose fallback is a payload the arm binds, handed to a call that keeps it, moves the payload out of the union as handing over the payload itself does, so the union is not read again.
```maxon
typealias Num = int(i64.min to i64.max)

type Token is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

union Slot
	empty
	filled(token Token)
end 'Slot'

enum MakeError implements Error
	none
end 'MakeError'

type Keeper
	var held as Token

	static function create() returns Self
		return Self{held: Token.create(0)}
	end 'create'

	function keep(t Token)
		self.held = t
	end 'keep'
end 'Keeper'

function mk(n Num) returns Token throws MakeError
	if n == 0 'absent'
		throw MakeError.none
	end 'absent'

	return Token.create(n)
end 'mk'

function show(s Slot)
	match s 'which'
		empty then print("empty\n")
		filled(token) then print("{token.n}\n")
	end 'which'
end 'show'

function main() returns ExitCode
	let k = Keeper.create()
	let s = Slot.filled(Token.create(3))

	match s 'which'
		empty then print("empty\n")
		filled(t) then k.keep(try mk(0) otherwise t)
	end 'which'

	show(s)
	print("{k.held.n}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:57:7: use of moved value 't': its ownership moved to the parameter 't' of 'Keeper.keep', which keeps it
```

<!-- test: a-field-path-over-a-cond-lends-every-holder-the-cond-names -->
A loop over a field read through a `cond`, and a `match` on one, hold every holder the `cond` names read-only, whichever arm ran, as reading the field through one holder does.
```maxon
typealias Num = int(i64.min to i64.max)

type Tk is unique
	var n as Num

	static function create(n Num) returns Self
		return Self{n: n}
	end 'create'
end 'Tk'

typealias Tks = Array with Tk

union Slot
	empty
	filled(token Tk)
end 'Slot'

type Holder
	var xs as Tks
	var u as Slot

	static function create(n Num) returns Self
		let xs = Tks.create(0)
		xs.push(Tk.create(n))
		return Self{xs: xs, u: Slot.filled(Tk.create(n))}
	end 'create'
end 'Holder'

function chosen(n Num) returns bool
	return n != 0
end 'chosen'

function reset(h Holder)
	h.u = Slot.empty
end 'reset'

function loopThen() returns Num
	let h = Holder.create(1)
	let g = Holder.create(2)

	for e in (h if chosen(1) else g).xs 'each'
		let o = try h.xs.pop() otherwise Tk.create(0)
		e.n = o.n + 2
	end 'each'

	return 0
end 'loopThen'

function loopElse() returns Num
	let h = Holder.create(1)
	let g = Holder.create(2)

	for e in (h if chosen(0) else g).xs 'each'
		let o = try g.xs.pop() otherwise Tk.create(0)
		e.n = o.n + 2
	end 'each'

	return 0
end 'loopElse'

function matchThen() returns Num
	let h = Holder.create(1)
	let g = Holder.create(2)

	match (h if chosen(1) else g).u 'which'
		empty then return 0
		filled(token) then reset(h)
	end 'which'

	return 1
end 'matchThen'

function matchElse() returns Num
	let h = Holder.create(1)
	let g = Holder.create(2)

	match (h if chosen(0) else g).u 'which'
		empty then return 0
		filled(token) then g.u = Slot.empty
	end 'which'

	return 1
end 'matchElse'

function main() returns ExitCode
	print("{loopThen()} {loopElse()} {matchThen()} {matchElse()}\n")
	return 0
end 'main'
```
```maxoncstderr
error E3207: <fragment>:43:19: 'pop' needs a writable receiver at line 246; the argument is read-only while 'each' is open
error E3207: <fragment>:55:19: 'pop' needs a writable receiver at line 246; the argument is read-only while 'each' is open
error E3207: <fragment>:68:28: 'reset' needs a writable parameter 'h' at line 35; 'h' is read-only while 'which' is open
error E3197: <fragment>:80:22: cannot write 'u' through 'g', which is read-only while 'which' lends a unique value out of it
```

<!-- test: error.a-unique-slot-path-beside-its-root-in-one-call-is-refused -->
`h.t` and `h` reach one unique value, and a callee is checked alone (ruling 18).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

type Holder is unique
	var t as Token

	static function create(t Token) returns Self
		return Self{t: t}
	end 'create'
end 'Holder'

function both(t Token, h Holder)
	h.t = Token.create(9)
	t.n = 3
end 'both'

function main() returns ExitCode
	let h = Holder.create(Token.create(1))
	both(h.t, h: h)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:27:15: 'h' is passed at two arguments of this call, and a unique value has one name; pass it once
```

<!-- test: error.a-container-reassigned-inside-a-for-that-lends-from-it-is-refused -->
The loop lends `tokens`' unique elements, so the variable holding the container stays put in the body.
```maxon
typealias Count = int(0 to 1000)
typealias Tokens = Array with Token

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

function main() returns ExitCode
	var tokens = Tokens.create(0)
	tokens.push(Token.create(1))

	for t in tokens 'each'
		tokens = Tokens.create(0)
		t.n = 6
	end 'each'

	return 0
end 'main'
```
```maxoncstderr
error E3203: <fragment>:18:3: 'tokens' roots what 'each' lends or iterates and cannot be reassigned or passed by reference inside it
```

<!-- test: a-plain-part-beside-its-unique-root-in-one-call -->
`h.data` is a plain part of `h`, not a second name for the unique value, so `h` is lent once in either order (ruling 44).
```maxon
typealias Count = int(0 to 1000)

type Box
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Box'

type Holder is unique
	var data as Box
	var n as Count

	static function create(data Box, n Count) returns Self
		return Self{data: data, n: n}
	end 'create'
end 'Holder'

function partFirst(d Box, h Holder)
	h.n = h.n + d.n
end 'partFirst'

function rootFirst(h Holder, d Box)
	h.n = h.n + d.n
end 'rootFirst'

function main() returns ExitCode
	let h = Holder.create(Box.create(2), n: 0)
	partFirst(h.data, h: h)
	rootFirst(h, d: h.data)
	print("{h.n}\n")
	return 0
end 'main'
```
```exitcode
0
```
```stdout
4
```

<!-- test: error.a-unique-slot-path-beside-its-root-is-refused-in-either-order -->
`h.t` and `h` reach one unique value whichever argument comes first (ruling 18).
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

type Holder is unique
	var t as Token

	static function create(t Token) returns Self
		return Self{t: t}
	end 'create'
end 'Holder'

function both(h Holder, t Token)
	h.t = Token.create(9)
	t.n = 3
end 'both'

function main() returns ExitCode
	let h = Holder.create(Token.create(1))
	both(h, t: h.t)
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:27:13: 'h' is passed at two arguments of this call, and a unique value has one name; pass it once
```

<!-- test: error.a-unique-slot-path-beside-its-receiver-names-self -->
`self.t` and `self` reach one unique value, and the refusal names the receiver as it is written.
```maxon
typealias Count = int(0 to 1000)

type Token is unique
	var n as Count

	static function create(n Count) returns Self
		return Self{n: n}
	end 'create'
end 'Token'

type Holder is unique
	var t as Token

	static function create(t Token) returns Self
		return Self{t: t}
	end 'create'

	function lendBoth()
		both(self.t, h: self)
	end 'lendBoth'
end 'Holder'

function both(t Token, h Holder)
	h.t = Token.create(9)
	t.n = 3
end 'both'

function main() returns ExitCode
	let h = Holder.create(Token.create(1))
	h.lendBoth()
	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:20:19: 'self' is passed at two arguments of this call, and a unique value has one name; pass it once
```
