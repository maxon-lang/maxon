using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Allocates virtual registers to physical x86-64 registers.
/// Uses a simple linear scan approach for now.
/// </summary>
public sealed class RegisterAllocationPass : FunctionPass {
	public override string Name => "register-allocation";
	public override string Description => "Allocates virtual registers to physical x86-64 registers";

	protected override bool RunOnFunction(MlirFunction func) {
		bool changed = false;

		Logger.Debug(LogCategory.RegAlloc, $"register-allocation: processing {func.Name}");

		// Step 1: Analyze which vregs are live across function calls
		var liveAcrossCalls = AnalyzeLiveAcrossCalls(func);

		// Step 2: Allocate registers
		// - vregs live across calls get callee-saved (non-volatile) registers
		// - other vregs get caller-saved (volatile) registers
		var allocation = new Dictionary<int, X86Register>();
		var usedNonVolatileGpr = new HashSet<X86Register>();
		var usedNonVolatileXmm = new HashSet<X86Register>();
		var ctx = new AllocationContext(allocation, liveAcrossCalls, usedNonVolatileGpr, usedNonVolatileXmm);

		foreach (var block in func.Body.Blocks) {
			var (blockChanged, _) = AllocateBlock(block, ctx);
			changed |= blockChanged;
		}

		// Step 3: Insert save/restore of callee-saved registers in prologue/epilogue
		if (ctx.UsedNonVolatileGpr.Count > 0 || ctx.UsedNonVolatileXmm.Count > 0) {
			InsertCalleeSavedSaveRestore(func, ctx.UsedNonVolatileGpr, ctx.UsedNonVolatileXmm);
		}

		if (allocation.Count > 0) {
			Logger.Debug(LogCategory.RegAlloc, $"  {func.Name}: allocated {allocation.Count} virtual registers ({ctx.UsedNonVolatileGpr.Count} callee-saved GPRs, {ctx.UsedNonVolatileXmm.Count} callee-saved XMMs)");
		}
		return changed;
	}

	/// <summary>
	/// Analyzes which virtual registers are live across function calls.
	/// A vreg is live across a call if it's defined before the call and used after.
	/// </summary>
	private static HashSet<int> AnalyzeLiveAcrossCalls(MlirFunction func) {
		var liveAcrossCalls = new HashSet<int>();

		// Collect all vreg definitions and uses per block
		var allDefs = new Dictionary<int, (int blockIndex, int opIndex)>();
		var allUses = new List<(int vregId, int blockIndex, int opIndex)>();
		var callLocations = new List<(int blockIndex, int opIndex)>();

		for (int blockIdx = 0; blockIdx < func.Body.Blocks.Count; blockIdx++) {
			var block = func.Body.Blocks[blockIdx];
			for (int opIdx = 0; opIdx < block.Operations.Count; opIdx++) {
				var op = block.Operations[opIdx];

				if (op is X86CallOp) {
					callLocations.Add((blockIdx, opIdx));
				}

				if (op is X86Op x86Op) {
					// Record definitions (first operand for most ops)
					foreach (var operand in x86Op.X86Operands) {
						if (operand is VRegOperand vreg) {
							// First occurrence is a definition
							if (!allDefs.ContainsKey(vreg.Id)) {
								allDefs[vreg.Id] = (blockIdx, opIdx);
							}
							allUses.Add((vreg.Id, blockIdx, opIdx));
						}
					}
				}
			}
		}

		// For each call, find vregs that are defined before and used after
		foreach (var (callBlockIdx, callOpIdx) in callLocations) {
			foreach (var kvp in allDefs) {
				var vregId = kvp.Key;
				var (defBlockIdx, defOpIdx) = kvp.Value;
				// Is this vreg defined before the call?
				bool definedBefore = defBlockIdx < callBlockIdx ||
					(defBlockIdx == callBlockIdx && defOpIdx < callOpIdx);

				if (!definedBefore) continue;

				// Is this vreg used after the call?
				bool usedAfter = allUses.Any(u =>
					u.vregId == vregId &&
					(u.blockIndex > callBlockIdx ||
					(u.blockIndex == callBlockIdx && u.opIndex > callOpIdx)));

				if (usedAfter) {
					liveAcrossCalls.Add(vregId);
					Logger.Trace(LogCategory.RegAlloc, $"  v{vregId} is live across call at block {callBlockIdx}");
				}
			}
		}

		return liveAcrossCalls;
	}

	/// <summary>
	/// Inserts push/pop of callee-saved registers in prologue/epilogue.
	/// </summary>
	private static void InsertCalleeSavedSaveRestore(
		MlirFunction func,
		HashSet<X86Register> usedNonVolatileGpr,
		HashSet<X86Register> usedNonVolatileXmm) {

		var gprsToSave = usedNonVolatileGpr.OrderBy(r => r).ToList();
		var xmmsToSave = usedNonVolatileXmm.OrderBy(r => r).ToList();

		// Find prologue in entry block and insert pushes after it
		var entryBlock = func.Body.Blocks[0];
		int prologueIdx = -1;
		for (int i = 0; i < entryBlock.Operations.Count; i++) {
			if (entryBlock.Operations[i] is PrologueOp) {
				prologueIdx = i;
				break;
			}
		}

		if (prologueIdx >= 0) {
			// Insert pushes after prologue (in order)
			int insertIdx = prologueIdx + 1;
			foreach (var reg in gprsToSave) {
				entryBlock.Operations.Insert(insertIdx++, new PushOp(new RegOperand(reg)));
			}

			// For XMM registers, we would need to save to stack with movaps/movups
			// For now, just log that they're used (full implementation would require stack space)
			if (xmmsToSave.Count > 0) {
				Logger.Trace(LogCategory.RegAlloc, $"  XMM save/restore not yet implemented for: {string.Join(", ", xmmsToSave)}");
			}
		}

		// Find epilogues in all blocks and insert pops before them
		foreach (var block in func.Body.Blocks) {
			for (int i = 0; i < block.Operations.Count; i++) {
				if (block.Operations[i] is EpilogueOp) {
					// Insert pops before epilogue (in reverse order)
					foreach (var reg in gprsToSave.AsEnumerable().Reverse()) {
						block.Operations.Insert(i++, new PopOp(new RegOperand(reg)));
					}
					break;
				}
			}
		}
	}

	private static (bool changed, int allocationCount) AllocateBlock(
		MlirBlock block,
		AllocationContext ctx) {

		var startCount = ctx.AllocationCount;

		// Replace operations with allocated versions
		var newOps = new List<MlirOperation>();
		bool changed = false;

		foreach (var op in block.Operations) {
			if (op is X86Op x86Op) {
				var allocated = AllocateOp(x86Op, ctx);
				newOps.Add(allocated);
				changed = true;
			} else {
				newOps.Add(op);
			}
		}

		// Log new allocations at trace level
		var newAllocCount = ctx.AllocationCount - startCount;
		if (newAllocCount > 0) {
			foreach (var (vregId, physReg) in ctx.GetAllocations().Skip(startCount)) {
				Logger.Trace(LogCategory.RegAlloc, $"  v{vregId} -> {physReg}");
			}
		}

		// Replace block operations
		block.Operations.Clear();
		foreach (var op in newOps) {
			block.Operations.Add(op);
		}
		return (changed, newAllocCount);
	}

	private sealed class AllocationContext(
		Dictionary<int, X86Register> allocation,
		HashSet<int> liveAcrossCalls,
		HashSet<X86Register> usedNonVolatileGpr,
		HashSet<X86Register> usedNonVolatileXmm) {

		public int NextVolatileGpr;
		public int NextNonVolatileGpr;
		public int NextVolatileXmm;
		public int NextNonVolatileXmm;

		public int AllocationCount => allocation.Count;
		public Dictionary<int, X86Register> GetAllocations() => allocation;
		public HashSet<X86Register> UsedNonVolatileGpr => usedNonVolatileGpr;
		public HashSet<X86Register> UsedNonVolatileXmm => usedNonVolatileXmm;

		public X86Operand Alloc(X86Operand operand) {
			if (operand is VRegOperand vreg) {
				if (!allocation.TryGetValue(vreg.Id, out var physReg)) {
					physReg = vreg.IsFloat
						? AllocXmm(vreg.Id)
						: AllocGpr(vreg.Id);
					allocation[vreg.Id] = physReg;
				}
				return new RegOperand(physReg);
			}

			if (operand is MemOperand mem) {
				return new MemOperand(
					mem.Base is not null ? Alloc(mem.Base) : null,
					mem.Index is not null ? Alloc(mem.Index) : null,
					mem.Scale,
					mem.Displacement,
					mem.Size
				);
			}

			return operand;
		}

		private X86Register AllocGpr(int vregId) {
			if (liveAcrossCalls.Contains(vregId)) {
				return AllocFromPool(
					ref NextNonVolatileGpr,
					WindowsX64Abi.NonVolatileGprs,
					usedNonVolatileGpr,
					$"callee-saved GPRs for v{vregId}");
			}
			return AllocFromPool(
				ref NextVolatileGpr,
				WindowsX64Abi.VolatileGprs,
				usedRegs: null,
				$"volatile GPRs for v{vregId}");
		}

		private X86Register AllocXmm(int vregId) {
			if (liveAcrossCalls.Contains(vregId)) {
				return AllocFromPool(
					ref NextNonVolatileXmm,
					WindowsX64Abi.NonVolatileXmm,
					usedNonVolatileXmm,
					$"callee-saved XMM registers for vf{vregId}");
			}
			return AllocFromPool(
				ref NextVolatileXmm,
				WindowsX64Abi.VolatileXmm,
				usedRegs: null,
				$"volatile XMM registers for vf{vregId}");
		}

		private static X86Register AllocFromPool(
			ref int nextIndex,
			X86Register[] pool,
			HashSet<X86Register>? usedRegs,
			string description) {
			if (nextIndex >= pool.Length) {
				throw new InvalidOperationException($"Ran out of {description}. Spilling not yet implemented.");
			}
			var reg = pool[nextIndex++];
			usedRegs?.Add(reg);
			return reg;
		}
	}

	private static X86Op AllocateOp(X86Op op, AllocationContext ctx) {
		return op switch {
			MovOp mov => new MovOp(ctx.Alloc(mov.Dst), ctx.Alloc(mov.Src)),
			AddOp add => new AddOp(ctx.Alloc(add.Dst), ctx.Alloc(add.Src)),
			SubOp sub => new SubOp(ctx.Alloc(sub.Dst), ctx.Alloc(sub.Src)),
			ImulOp imul => imul.Src2 is not null
				? new ImulOp(ctx.Alloc(imul.Dst), ctx.Alloc(imul.Src1), ctx.Alloc(imul.Src2))
				: new ImulOp(ctx.Alloc(imul.Dst), ctx.Alloc(imul.Src1)),
			IdivOp idiv => new IdivOp(ctx.Alloc(idiv.Divisor)),
			CmpOp cmp => new CmpOp(ctx.Alloc(cmp.Left), ctx.Alloc(cmp.Right)),
			TestOp test => new TestOp(ctx.Alloc(test.Left), ctx.Alloc(test.Right)),
			SetccOp setcc => new SetccOp(setcc.Condition, ctx.Alloc(setcc.Dst)),
			PushOp push => new PushOp(ctx.Alloc(push.Src)),
			PopOp pop => new PopOp(ctx.Alloc(pop.Dst)),
			LeaOp lea => new LeaOp(ctx.Alloc(lea.Dst), ctx.Alloc(lea.Src)),
			LeaGlobalOp leaGlobal => new LeaGlobalOp(ctx.Alloc(leaGlobal.Dst), leaGlobal.GlobalName),
			// Shift ops
			ShlOp shl => new ShlOp(ctx.Alloc(shl.Dst), ctx.Alloc(shl.Count)),
			ShrOp shr => new ShrOp(ctx.Alloc(shr.Dst), ctx.Alloc(shr.Count)),
			SarOp sar => new SarOp(ctx.Alloc(sar.Dst), ctx.Alloc(sar.Count)),
			// Bitwise ops
			AndOp and => new AndOp(ctx.Alloc(and.Dst), ctx.Alloc(and.Src)),
			OrOp or => new OrOp(ctx.Alloc(or.Dst), ctx.Alloc(or.Src)),
			XorOp xor => new XorOp(ctx.Alloc(xor.Dst), ctx.Alloc(xor.Src)),
			NotOp not => new NotOp(ctx.Alloc(not.Dst)),
			// Control flow and misc
			X86CallOp call => call,
			JmpOp jmp => jmp,
			JccOp jcc => jcc,
			RetOp ret => ret,
			LabelOp label => label,
			CdqOp cdq => cdq,
			PrologueOp prologue => prologue,
			EpilogueOp epilogue => epilogue,
			// SSE instructions
			MovqOp movq => new MovqOp(ctx.Alloc(movq.Dst), ctx.Alloc(movq.Src)),
			MovsdOp movsd => new MovsdOp(ctx.Alloc(movsd.Dst), ctx.Alloc(movsd.Src)),
			AddsdOp addsd => new AddsdOp(ctx.Alloc(addsd.Dst), ctx.Alloc(addsd.Src)),
			SubsdOp subsd => new SubsdOp(ctx.Alloc(subsd.Dst), ctx.Alloc(subsd.Src)),
			MulsdOp mulsd => new MulsdOp(ctx.Alloc(mulsd.Dst), ctx.Alloc(mulsd.Src)),
			DivsdOp divsd => new DivsdOp(ctx.Alloc(divsd.Dst), ctx.Alloc(divsd.Src)),
			CvttsdOp cvttsd => new CvttsdOp(ctx.Alloc(cvttsd.Dst), ctx.Alloc(cvttsd.Src)),
			CvtsiOp cvtsi => new CvtsiOp(ctx.Alloc(cvtsi.Dst), ctx.Alloc(cvtsi.Src)),
			ComiOp comi => new ComiOp(ctx.Alloc(comi.Left), ctx.Alloc(comi.Right)),
			// SSE math instructions
			SqrtsdOp sqrtsd => new SqrtsdOp(ctx.Alloc(sqrtsd.Dst), ctx.Alloc(sqrtsd.Src)),
			RoundsdOp roundsd => new RoundsdOp(ctx.Alloc(roundsd.Dst), ctx.Alloc(roundsd.Src), roundsd.Mode),
			MinsdOp minsd => new MinsdOp(ctx.Alloc(minsd.Dst), ctx.Alloc(minsd.Src)),
			MaxsdOp maxsd => new MaxsdOp(ctx.Alloc(maxsd.Dst), ctx.Alloc(maxsd.Src)),
			AndpdOp andpd => new AndpdOp(ctx.Alloc(andpd.Dst), ctx.Alloc(andpd.Src)),
			XorpdOp xorpd => new XorpdOp(ctx.Alloc(xorpd.Dst), ctx.Alloc(xorpd.Src)),
			_ => op
		};
	}
}

