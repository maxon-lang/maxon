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
`keep` stores its `slot` argument, so the call moves `slot`; the `filled` arm then reads `token`, a view of the moved union's payload, in the same call.

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
	var kept = SlotArray.create(0)
	kept.push(slot)

	return k
end 'keep'

function main() returns ExitCode
	var slot = Slot.filled(Token.create(1), n: 2)

	match slot 'which'
		empty then print("empty\n")
		filled(token, n) then print("{keep(slot, k: token.n)} {n}\n")
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:31:47: use of moved value 'slot': its ownership moved to the parameter 'slot' of 'keep', which keeps it
```

<!-- test: a-match-binding-read-after-its-subject-is-moved-is-refused -->
The call moves `slot`, so the `token` binding of its payload is dead for the rest of the arm.

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
	var kept = SlotArray.create(0)
	kept.push(slot)

	return 1
end 'keep'

function main() returns ExitCode
	var slot = Slot.filled(Token.create(1), n: 2)

	match slot 'which'
		empty then print("empty\n")
		filled(token, n) then print("{keep(slot)} {n} {token.n}\n")
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:31:50: use of moved value 'slot': its ownership moved to the parameter 'slot' of 'keep', which keeps it
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
	var kept = TokenArray.create(0)
	kept.push(token)

	return 1
end 'keep'

function main() returns ExitCode
	var ticket = Token.create(1)
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
	var kept = TokenArray.create(0)
	kept.push(token)

	return 1
end 'keep'

function forward(token Token) returns Tally
	let moved = keep(token)

	return moved + token.n
end 'forward'

function main() returns ExitCode
	var ticket = Token.create(1)
	print("{forward(ticket)}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:24:17: use of moved value 'token': its ownership moved to the parameter 'token' of 'keep', which keeps it
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
	var kept = TokenArray.create(0)
	kept.push(token)

	return 1
end 'keep'

function main() returns ExitCode
	var ticket = Token.create(1)
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

<!-- test: a-let-unique-handed-to-a-keeping-parameter-is-refused -->
A keeping parameter takes the caller's mutable data, and a `let` is not.

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
	var kept = TokenArray.create(0)
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
```maxoncstderr
error E3078: <fragment>:23:14: cannot pass 'ticket' to parameter 'token' of 'keep': it keeps a unique value, which moves out of the caller's mutable data, and 'ticket' is not a `var`
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
	var ticket = Token.create(1)
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
	var kept = TokenArray.create(0)
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
	var slot = Slot.filled(Token.create(1), n: 2)

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
	var kept = SlotArray.create(0)
	kept.push(slot)

	return 1
end 'keep'

function main() returns ExitCode
	var slot = Slot.filled(Token.create(1), n: 2)
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
	var kept = TokenArray.create(0)
	kept.push(token)

	return 1
end 'keep'

function main() returns ExitCode
	var holder = Holder.create(1)
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
	var kept = TokenArray.create(0)
	kept.push(token)

	return 1
end 'keep'

function main() returns ExitCode
	var tokens = TokenArray.create(0)
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
	var kept = TokenArray.create(0)
	kept.push(token)

	return 1
end 'keep'

function main() returns ExitCode
	var tokens = TokenArray.create(0)
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
	var kept = TokenArrays.create(0)
	kept.push(tokens)

	return 1
end 'keep'

function main() returns ExitCode
	var tokens = TokenArray.create(0)
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
	var tokens = TokenArray.create(0)
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
	var kept = Pairs.create(0)
	kept.push(pair)

	return 1
end 'keep'

function main() returns ExitCode
	var pair = (Token.create(1), 2 as Tally)
	let moved = keep(pair)
	print("{moved} {pair.1}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:25:18: use of moved value 'pair': its ownership moved to the parameter 'pair' of 'keep', which keeps it
```

<!-- test: a-payload-binding-kept-by-a-call-moves-its-subject -->
`keep` stores the `token` payload, so the call moves the union it was bound out of and `slot` is dead afterwards.

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
	var kept = TokenArray.create(0)
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
	var slot = Slot.filled(Token.create(1), n: 2)

	match slot 'which'
		empty then print("empty\n")
		filled(token, n) then print("{keep(token)} {n} {tally(slot)}\n")
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:38:57: use of moved value 'slot': its ownership moved to the parameter 'token' of 'keep', which keeps it
```

<!-- test: a-payload-binding-read-after-its-subject-is-handed-to-a-consuming-call-is-refused -->
`Box.wrap` stores the whole union in a field, which moves `slot`, so the `token` binding of its payload is dead for the rest of the statement.

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
	var slot = Slot.filled(Token.create(1), n: 2)

	match slot 'which'
		empty then print("empty\n")
		filled(token, n) then print("{n} {Box.wrap(slot).size()} {token.n}\n")
	end 'which'

	return 0
end 'main'
```
```maxoncstderr
error E3102: <fragment>:34:61: use of moved value 'slot': its ownership moved to the parameter 'slot' of 'Box.wrap', which keeps it
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
error E3214: <fragment>:21:28: cannot give a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
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
	var holder = Holder.create(4)
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
	view lent = holder.token
	print("{lent.n}\n")

	return 0
end 'main'
```
```maxoncstderr
error E3214: <fragment>:22:7: cannot bind 'lent' to a unique value read out of a field or an element: the field or element goes on owning it, and a unique value has one name; read what is needed through the field without binding it, or move it out with `pop` or `remove`
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
	var kept = TokenArray.create(0)
	kept.push(token)

	return 1
end 'keep'

function main() returns ExitCode
	var tokens = TokenArray.create(0)
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
	var tokens = TokenArray.create(0)
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
	var kept = TokenArray.create(0)
	kept.push(token)

	return 1
end 'keep'

function main() returns ExitCode
	var pair = (Token.create(1), 2 as Tally)
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
	var tickets = TicketArray.create(0)
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
