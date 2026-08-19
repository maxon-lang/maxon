---
feature: short-circuit-block-lifetime
status: stable
keywords: [refcount, memory, ownership, consume, dominance, uaf, short-circuit]
category: memory-safety
---

# Consumed-Argument Lifetime Across a Dominating Block

## Documentation

When a managed value is passed as a CONSUMED argument to a callee (its `+1` moves
into the callee without a copy-incref), the caller must not release it again — the
reference is the callee's now. The refcount inserter suppresses the caller's
last-use decref for such a move.

That suppression used to be BLOCK-LOCAL: it only recognised a consume that sat in
the SAME basic block as the value's death point. But the inserter's interior-borrow
liveness extension deliberately treats every pointer-width field load of a value as
a possible interior pointer of it (an integer `.id`/length read is indistinguishable
from a real interior pointer at the load site), which keeps the value's SSA range
alive PAST its consume — often into a LATER block entirely. There the release
planner lands a last-use decref that the block-local guard cannot see, because the
consume and the death now live in different blocks. That decref over-releases: the
value's reference already left on the consume's success edge (or was released by the
caller's try-error cleanup on a throwing edge), so the object is freed with an owner
still pointing at it.

This is the reduced form of the self-hosted compiler's own short-circuit
over-release. Parsing `a or b` inside a `type` method (e.g. the stdlib
`Ascii.isAlphanumeric = return Ascii.isAlpha(c) or Ascii.isDigit(c)`) runs
`Parser.emitShortCircuit`: it allocates an `rhsBlock` (owned by `module.blocks`),
loads `rhsBlock.id`, then hands `rhsBlock` to `parseExpressionBP` — a THROWING call
that consumes it. `rhsBlock.id`, read before the call and fed into the `cond_br`
built several blocks later, keeps `rhsBlock` live to that later block, where a
spurious last-use decref frees a block `module.blocks` still references (surfacing
under `--rc-sanitize` as `INCREF of freed object … in Parser.emitShortCircuit`, and
in a leak build as a null-deref when the freed slab is recycled). The fix makes the
consumed-earlier suppression path-aware: a consume that DOMINATES the death point
(walking the dominator chain, with the tryCall's success edge required to dominate)
proves the `+1` is already gone on every path there.

These tests run under the suite's leak gate AND `--rc-sanitize`, so the recycled
over-release (`--rc-sanitize`) and any over-suppression it might introduce (a leak)
both fail them.

## Tests

<!-- test: consumed-arg-scalar-borrow-survives-throwing-call -->
A managed `Blk` owned by a `blocks` array, aliased into a `currentBlock` cursor
field, then handed to a THROWING recursive method (`parseExpressionBP`) that
consumes it. Its scalar `.id`, loaded before the call and used AFTER it, keeps the
block's SSA live into a later block; without the cross-block consumed suppression
the caller's last-use decref frees the block while `blocks` still owns it, and the
final `sumIds` walk reads a recycled slab (pre-fix: `INCREF of freed object … in
Parser.emitShortCircuit` under `--rc-sanitize`). The `sum != 205` guard also catches
the recycled read on a plain build when the freed slab was reused.

```maxon
typealias Num = int(0 to u64.max)

enum ParseErr implements Error
	bad
end 'ParseErr'

type Blk
	export var id as Num

	static function create(i Num) returns Blk
		return Blk{id: i}
	end 'create'

	function bump()
		id = id + 1
	end 'bump'
end 'Blk'

typealias BlkArray = Array with Blk

type Module
	var blocks as BlkArray

	static function create() returns Module
		return Module{blocks: BlkArray.create()}
	end 'create'

	function addBlock(i Num) returns Blk
		let b = Blk.create(i)
		blocks.push(b)
		return b
	end 'addBlock'

	function sumIds() returns Num
		var sum = 0
		for b in blocks 'each'
			sum = sum + b.id
		end 'each'
		return sum
	end 'sumIds'
end 'Module'

type Parser
	var currentBlock as Blk
	var module as Module

	static function create(m Module, entry Blk) returns Parser
		return Parser{currentBlock: entry, module: m}
	end 'create'

	function parseIdentifierExpr(block Blk, hasCall bool) returns Num throws ParseErr
		currentBlock = block
		var acc = block.id
		if hasCall 'callArgs'
			let args = try parseCallArgsRaw(block)
			acc = acc + args
		end 'callArgs'
		return acc + block.id
	end 'parseIdentifierExpr'

	function parseCallArgsRaw(block Blk) returns Num throws ParseErr
		currentBlock = block
		let arg = try parseExpressionBP(block, opCount: 0, hasCall: false)
		return arg + block.id
	end 'parseCallArgsRaw'

	function parsePrimary(block Blk, hasCall bool) returns Num throws ParseErr
		currentBlock = block
		let inner = try parseIdentifierExpr(block, hasCall: hasCall)
		return inner + block.id
	end 'parsePrimary'

	function parseUnary(block Blk, hasCall bool) returns Num throws ParseErr
		currentBlock = block
		let inner = try parsePrimary(block, hasCall: hasCall)
		return inner + block.id
	end 'parseUnary'

	function parseExpressionBP(block Blk, opCount Num, hasCall bool) returns Num throws ParseErr
		var blk = block
		currentBlock = blk
		var left = try parseUnary(blk, hasCall: hasCall)
		blk = currentBlock
		var remaining = opCount
		while remaining > 0 'prattLoop'
			let merge = try emitShortCircuit(blk)
			blk = currentBlock
			left = left + merge
			remaining = remaining - 1
		end 'prattLoop'
		return left
	end 'parseExpressionBP'

	function emitShortCircuit(entryBlock Blk) returns Num throws ParseErr
		let entryId = entryBlock.id
		let rhsBlock = module.addBlock(101)
		let rhsId = rhsBlock.id
		currentBlock = rhsBlock
		let rhsResult = try parseExpressionBP(rhsBlock, opCount: 0, hasCall: true)
		var mergeBlock = module.addBlock(102)
		mergeBlock.bump()
		let cond = rhsId + entryId
		currentBlock = mergeBlock
		return rhsResult + cond
	end 'emitShortCircuit'
end 'Parser'

function main() returns ExitCode
	var m = Module.create()
	let entry = m.addBlock(1)
	var p = Parser.create(m, entry: entry)
	let r = try p.parseExpressionBP(entry, opCount: 1, hasCall: true) otherwise return 3
	let sum = m.sumIds()
	if sum != 205 'corrupt'
		return 1
	end 'corrupt'
	if r == 0 'noResult'
		return 2
	end 'noResult'
	return 0
end 'main'
```
```exitcode
0
```
