using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Allocates virtual registers to physical x86-64 registers.
/// Uses linear scan with liveness analysis to reuse registers.
/// </summary>
public sealed class RegisterAllocationPass : FunctionPass {
	public override string Name => "register-allocation";
	public override string Description => "Allocates virtual registers to physical x86-64 registers";

	protected override bool RunOnFunction(MlirFunction func) {
		bool changed = false;

		Logger.Debug(LogCategory.RegAlloc, $"register-allocation: processing {func.Name}");

		// Step 1: Compute liveness info for all vregs
		var livenessInfo = ComputeLiveness(func);

		// Step 2: Analyze which vregs are live across function calls
		var liveAcrossCalls = AnalyzeLiveAcrossCalls(func);

		// Step 3: Allocate registers with liveness-based reuse
		var allocation = new Dictionary<int, X86Register>();
		var usedNonVolatileGpr = new HashSet<X86Register>();
		var usedNonVolatileXmm = new HashSet<X86Register>();
		var ctx = new AllocationContext(allocation, liveAcrossCalls, usedNonVolatileGpr, usedNonVolatileXmm, livenessInfo, func.Body.Blocks.Count);

		for (int blockIdx = 0; blockIdx < func.Body.Blocks.Count; blockIdx++) {
			var (blockChanged, _) = AllocateBlock(func.Body.Blocks[blockIdx], ctx, blockIdx);
			changed |= blockChanged;
		}

		// Step 4: Insert save/restore of callee-saved registers in prologue/epilogue
		if (ctx.UsedNonVolatileGpr.Count > 0 || ctx.UsedNonVolatileXmm.Count > 0) {
			InsertCalleeSavedSaveRestore(func, ctx.UsedNonVolatileGpr, ctx.UsedNonVolatileXmm);
		}

		if (allocation.Count > 0) {
			Logger.Debug(LogCategory.RegAlloc, $"  {func.Name}: allocated {allocation.Count} virtual registers ({ctx.UsedNonVolatileGpr.Count} callee-saved GPRs, {ctx.UsedNonVolatileXmm.Count} callee-saved XMMs)");
		}
		return changed;
	}

	/// <summary>
	/// Liveness information for a function.
	/// </summary>
	private sealed class LivenessInfo {
		/// <summary>
		/// For each vreg, the last (blockIdx, opIdx) where it's used.
		/// </summary>
		public Dictionary<int, (int blockIdx, int opIdx)> LastUse { get; } = [];

		/// <summary>
		/// For each block, which vregs are used in that block.
		/// </summary>
		public Dictionary<int, HashSet<int>> VregsUsedInBlock { get; } = [];

		/// <summary>
		/// For each vreg, which blocks use it.
		/// </summary>
		public Dictionary<int, HashSet<int>> BlocksUsingVreg { get; } = [];

		/// <summary>
		/// Set of vregs that are used inside a loop (between back-edge source and target).
		/// These cannot be freed until after the loop exits.
		/// </summary>
		public HashSet<int> LiveThroughLoop { get; } = [];
	}

	/// <summary>
	/// Computes liveness information: for each vreg, record where it's used
	/// and which blocks use it. Also detects back-edges and marks vregs that
	/// are live through loops.
	/// </summary>
	private static LivenessInfo ComputeLiveness(MlirFunction func) {
		var info = new LivenessInfo();
		var blockNames = new Dictionary<string, int>();

		// First pass: collect vreg uses and build block name -> index mapping
		for (int blockIdx = 0; blockIdx < func.Body.Blocks.Count; blockIdx++) {
			var block = func.Body.Blocks[blockIdx];
			blockNames[block.Name] = blockIdx;
			info.VregsUsedInBlock[blockIdx] = [];

			for (int opIdx = 0; opIdx < block.Operations.Count; opIdx++) {
				if (block.Operations[opIdx] is X86Op x86Op) {
					foreach (var operand in x86Op.X86Operands) {
						CollectVregUses(operand, blockIdx, opIdx, info);
					}
				}
			}
		}

		// Collect vreg definitions (which block defines each vreg - approximated as first use)
		var vregDefBlock = new Dictionary<int, int>();
		for (int blockIdx = 0; blockIdx < func.Body.Blocks.Count; blockIdx++) {
			if (info.VregsUsedInBlock.TryGetValue(blockIdx, out var usedVregs)) {
				foreach (var vregId in usedVregs) {
					// First occurrence is considered the definition
					if (!vregDefBlock.ContainsKey(vregId)) {
						vregDefBlock[vregId] = blockIdx;
					}
				}
			}
		}

		// Second pass: detect back-edges and mark loop-live vregs
		// A vreg is loop-live if it's defined BEFORE the loop header AND used inside the loop
		for (int blockIdx = 0; blockIdx < func.Body.Blocks.Count; blockIdx++) {
			var block = func.Body.Blocks[blockIdx];
			var terminator = block.Operations.LastOrDefault();

			// Find successor block indices
			var successorNames = terminator switch {
				JmpOp jmp => [jmp.Target],
				JccOp jcc => [jcc.TrueTarget, jcc.FalseTarget],
				_ => Array.Empty<string>()
			};

			foreach (var succName in successorNames) {
				if (blockNames.TryGetValue(succName, out var succIdx) && succIdx <= blockIdx) {
					// Back-edge found: blockIdx -> succIdx where succIdx <= blockIdx
					// Loop header is at succIdx, loop body is [succIdx, blockIdx]
					Logger.Trace(LogCategory.RegAlloc, $"  Back-edge: block {blockIdx} -> {succIdx}");

					// Find vregs that are defined BEFORE the loop header but used inside the loop
					for (int loopBlockIdx = succIdx; loopBlockIdx <= blockIdx; loopBlockIdx++) {
						if (info.VregsUsedInBlock.TryGetValue(loopBlockIdx, out var usedVregs)) {
							foreach (var vregId in usedVregs) {
								// Check if vreg is defined before the loop header
								if (vregDefBlock.TryGetValue(vregId, out var defBlock) && defBlock < succIdx) {
									info.LiveThroughLoop.Add(vregId);
								}
							}
						}
					}
				}
			}
		}

		if (info.LiveThroughLoop.Count > 0) {
			Logger.Trace(LogCategory.RegAlloc, $"  Loop-live vregs: {string.Join(", ", info.LiveThroughLoop.Select(v => $"v{v}"))}");
		}

		return info;
	}

	private static void CollectVregUses(X86Operand operand, int blockIdx, int opIdx, LivenessInfo info) {
		if (operand is VRegOperand vreg) {
			info.LastUse[vreg.Id] = (blockIdx, opIdx);
			info.VregsUsedInBlock[blockIdx].Add(vreg.Id);

			if (!info.BlocksUsingVreg.TryGetValue(vreg.Id, out var blocks)) {
				blocks = [];
				info.BlocksUsingVreg[vreg.Id] = blocks;
			}
			blocks.Add(blockIdx);
		} else if (operand is MemOperand mem) {
			if (mem.Base is not null) CollectVregUses(mem.Base, blockIdx, opIdx, info);
			if (mem.Index is not null) CollectVregUses(mem.Index, blockIdx, opIdx, info);
		}
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
		AllocationContext ctx,
		int blockIdx = 0) {

		var startCount = ctx.AllocationCount;

		// Replace operations with allocated versions
		var newOps = new List<MlirOperation>();
		bool changed = false;

		for (int opIdx = 0; opIdx < block.Operations.Count; opIdx++) {
			var op = block.Operations[opIdx];

			// Free dead registers before processing this operation
			ctx.BeforeOp(blockIdx, opIdx);

			if (op is X86Op x86Op) {
				// Handle register clobbering: save any live vregs in clobbered registers
				var clobbered = x86Op.ClobberedRegisters;
				var toSave = new List<(int vregId, X86Register reg)>();
				if (clobbered.Count > 0) {
					toSave = ctx.GetLiveVregsInRegisters(clobbered);
					foreach (var (vregId, reg) in toSave) {
						newOps.Add(new PushOp(new RegOperand(reg)));
						Logger.Trace(LogCategory.RegAlloc, $"  Save v{vregId} ({reg}) before {x86Op.Mnemonic}");
					}
				}

				var allocated = AllocateOp(x86Op, ctx);
				newOps.Add(allocated);
				changed = true;

				// Restore saved vregs after the clobbering operation (in reverse order)
				for (int i = toSave.Count - 1; i >= 0; i--) {
					var (vregId, reg) = toSave[i];
					newOps.Add(new PopOp(new RegOperand(reg)));
					Logger.Trace(LogCategory.RegAlloc, $"  Restore v{vregId} ({reg}) after {x86Op.Mnemonic}");
				}
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
		HashSet<X86Register> usedNonVolatileXmm,
		LivenessInfo livenessInfo,
		int totalBlocks) {

		// Track which physical registers are currently free for reuse
		private readonly HashSet<X86Register> _freeVolatileGprs = [.. WindowsX64Abi.VolatileGprs];
		private readonly HashSet<X86Register> _freeNonVolatileGprs = [.. WindowsX64Abi.NonVolatileGprs];
		private readonly HashSet<X86Register> _freeVolatileXmms = [.. WindowsX64Abi.VolatileXmm];
		private readonly HashSet<X86Register> _freeNonVolatileXmms = [.. WindowsX64Abi.NonVolatileXmm];

		// Track which vregs are currently allocated (for freeing when dead)
		private readonly Dictionary<int, X86Register> _activeVregs = [];
		private readonly Dictionary<int, bool> _vregIsFloat = [];
		private readonly Dictionary<int, bool> _vregIsNonVolatile = [];

		// Total number of blocks in this function
		private readonly int _totalBlocks = totalBlocks;

		// Current position in the function
		public int CurrentBlockIdx;
		public int CurrentOpIdx;

		public int AllocationCount => allocation.Count;
		public Dictionary<int, X86Register> GetAllocations() => allocation;
		public HashSet<X86Register> UsedNonVolatileGpr => usedNonVolatileGpr;
		public HashSet<X86Register> UsedNonVolatileXmm => usedNonVolatileXmm;

		/// <summary>
		/// Called before processing each operation to free dead registers.
		/// </summary>
		public void BeforeOp(int blockIdx, int opIdx) {
			CurrentBlockIdx = blockIdx;
			CurrentOpIdx = opIdx;
			FreeDeadRegisters();
		}

		/// <summary>
		/// Free registers for vregs whose last use was before the current position.
		/// A vreg can be freed if:
		/// 1. It's not live through a loop (would be reused across iterations)
		/// 2. Its last use is in the current block and before the current operation, AND
		/// 3. It's only used in blocks with index <= current block (no future uses in later blocks)
		/// </summary>
		private void FreeDeadRegisters() {
			var toFree = new List<int>();
			foreach (var (vregId, physReg) in _activeVregs) {
				// Never free vregs that are live through a loop
				if (livenessInfo.LiveThroughLoop.Contains(vregId)) continue;

				if (!livenessInfo.LastUse.TryGetValue(vregId, out var lastUse)) continue;

				// Check if last use is in current block and before current position
				if (lastUse.blockIdx == CurrentBlockIdx && lastUse.opIdx < CurrentOpIdx) {
					// Also verify no uses in later blocks (index > current)
					if (livenessInfo.BlocksUsingVreg.TryGetValue(vregId, out var blocks)) {
						bool hasLaterUse = blocks.Any(b => b > CurrentBlockIdx);
						if (!hasLaterUse) {
							toFree.Add(vregId);
						}
					} else {
						// No block info means only used in current block
						toFree.Add(vregId);
					}
				}
			}

			foreach (var vregId in toFree) {
				var physReg = _activeVregs[vregId];
				_activeVregs.Remove(vregId);

				var isFloat = _vregIsFloat.GetValueOrDefault(vregId);
				var isNonVolatile = _vregIsNonVolatile.GetValueOrDefault(vregId);

				if (isFloat) {
					if (isNonVolatile) _freeNonVolatileXmms.Add(physReg);
					else _freeVolatileXmms.Add(physReg);
				} else {
					if (isNonVolatile) _freeNonVolatileGprs.Add(physReg);
					else _freeVolatileGprs.Add(physReg);
				}

				Logger.Trace(LogCategory.RegAlloc, $"  Freed {physReg} (v{vregId} is dead at block {CurrentBlockIdx} op {CurrentOpIdx})");
			}
		}

		/// <summary>
		/// Returns list of live vregs that are allocated to any of the specified registers.
		/// Used for save/restore around operations that clobber specific registers.
		/// Only returns vregs that are used AFTER the current operation (not consumed by it).
		/// </summary>
		public List<(int vregId, X86Register reg)> GetLiveVregsInRegisters(IReadOnlyList<X86Register> registers) {
			var result = new List<(int vregId, X86Register reg)>();
			foreach (var (vregId, physReg) in _activeVregs) {
				if (registers.Contains(physReg)) {
					// Check if this vreg is used AFTER current operation (not just at current)
					if (livenessInfo.LastUse.TryGetValue(vregId, out var lastUse)) {
						bool isUsedAfter = lastUse.blockIdx > CurrentBlockIdx ||
							(lastUse.blockIdx == CurrentBlockIdx && lastUse.opIdx > CurrentOpIdx);
						if (isUsedAfter) {
							result.Add((vregId, physReg));
						}
					}
				}
			}
			return result;
		}

		public X86Operand Alloc(X86Operand operand) {
			if (operand is VRegOperand vreg) {
				if (!allocation.TryGetValue(vreg.Id, out var physReg)) {
					physReg = vreg.IsFloat
						? AllocXmm(vreg.Id)
						: AllocGpr(vreg.Id);
					allocation[vreg.Id] = physReg;
					_activeVregs[vreg.Id] = physReg;
					_vregIsFloat[vreg.Id] = vreg.IsFloat;
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
			bool needsNonVolatile = liveAcrossCalls.Contains(vregId);
			_vregIsNonVolatile[vregId] = needsNonVolatile;

			if (needsNonVolatile) {
				return AllocFromFreeSet(
					_freeNonVolatileGprs,
					usedNonVolatileGpr,
					$"callee-saved GPRs for v{vregId}");
			}

			// For loop-live vregs, avoid RAX/RDX which are clobbered by idiv
			// These registers can be reused mid-loop but a loop-live vreg must survive the entire loop
			if (livenessInfo.LiveThroughLoop.Contains(vregId)) {
				// Try volatile registers except RAX/RDX
				var safeVolatile = _freeVolatileGprs.Where(r => r != X86Register.RAX && r != X86Register.RDX).ToList();
				if (safeVolatile.Count > 0) {
					var reg = safeVolatile.First();
					_freeVolatileGprs.Remove(reg);
					return reg;
				}

				// Fall back to callee-saved for loop-live vregs
				_vregIsNonVolatile[vregId] = true;
				return AllocFromFreeSet(
					_freeNonVolatileGprs,
					usedNonVolatileGpr,
					$"GPRs for loop-live v{vregId} (avoiding RAX/RDX)");
			}

			// Try volatile first, then fall back to non-volatile
			if (_freeVolatileGprs.Count > 0) {
				return AllocFromFreeSet(_freeVolatileGprs, null, $"volatile GPRs for v{vregId}");
			}

			// Fall back to callee-saved registers (will be saved/restored in prologue/epilogue)
			_vregIsNonVolatile[vregId] = true;
			return AllocFromFreeSet(
				_freeNonVolatileGprs,
				usedNonVolatileGpr,
				$"GPRs for v{vregId} (spilled to callee-saved)");
		}

		private X86Register AllocXmm(int vregId) {
			bool needsNonVolatile = liveAcrossCalls.Contains(vregId);
			_vregIsNonVolatile[vregId] = needsNonVolatile;

			if (needsNonVolatile) {
				return AllocFromFreeSet(
					_freeNonVolatileXmms,
					usedNonVolatileXmm,
					$"callee-saved XMM registers for vf{vregId}");
			}
			return AllocFromFreeSet(
				_freeVolatileXmms,
				null,
				$"volatile XMM registers for vf{vregId}");
		}

		private static X86Register AllocFromFreeSet(
			HashSet<X86Register> freeSet,
			HashSet<X86Register>? usedSet,
			string description) {
			if (freeSet.Count == 0) {
				throw new InvalidOperationException($"Ran out of {description}. Spilling not yet implemented.");
			}
			var reg = freeSet.First();
			freeSet.Remove(reg);
			usedSet?.Add(reg);
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

