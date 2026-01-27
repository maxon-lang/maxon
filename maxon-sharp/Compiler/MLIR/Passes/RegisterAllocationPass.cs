using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Passes;

/// <summary>
/// Allocates virtual registers to physical x86-64 registers.
/// Uses linear scan with liveness analysis to reuse registers.
///
/// Register clobbering is handled by two mechanisms:
///
/// 1. Constraint Analysis (for division): The division sequence writes to RAX/RDX
///    explicitly before idiv executes. We cannot push/pop these because idiv needs
///    them. Instead, we prevent other vregs from being allocated to RAX/RDX when
///    they're live across the division. IdivOp/CdqOp return empty ClobberedRegisters
///    because constraint analysis handles them.
///
/// 2. Push/Pop (for shifts and calls): Operations declare ClobberedRegisters and
///    the allocator saves/restores any live vregs in those registers.
///
/// Spilling: When all registers are exhausted, the allocator spills a vreg to memory
/// using the "furthest next use" heuristic (spill the vreg whose next use is furthest
/// away to minimize reloads). Spilled vregs are stored in the stack frame below local
/// variables and reloaded when needed.
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

		// Step 3: Analyze which vregs are live across explicit physical register writes
		// (e.g., division sequence uses mov rax, X; cdq; idiv which clobbers RAX/RDX)
		AnalyzePhysicalRegisterConstraints(func, livenessInfo);

		// Step 4: Allocate registers with liveness-based reuse
		var allocation = new Dictionary<int, X86Register>();
		var usedNonVolatileGpr = new HashSet<X86Register>();
		var usedNonVolatileXmm = new HashSet<X86Register>();
		var ctx = new AllocationContext(allocation, liveAcrossCalls, usedNonVolatileGpr, usedNonVolatileXmm, livenessInfo, func.Body.Blocks.Count);

		// Set the base offset for spill slots: after shadow space and locals
		// Get the current prologue stack size which includes shadow space and locals
		var entryBlock = func.Body.Blocks.FirstOrDefault();
		if (entryBlock is not null) {
			foreach (var op in entryBlock.Operations) {
				if (op is PrologueOp prologue) {
					// Spill area base is negative offset from RBP, starting after current stack frame
					ctx.SpillAreaBaseOffset = -prologue.StackSize;
					break;
				}
			}
		}

		for (int blockIdx = 0; blockIdx < func.Body.Blocks.Count; blockIdx++) {
			var (blockChanged, _) = AllocateBlock(func.Body.Blocks[blockIdx], ctx, blockIdx);
			changed |= blockChanged;
		}

		// Step 5: Insert save/restore of callee-saved registers in prologue/epilogue
		if (ctx.UsedNonVolatileGpr.Count > 0 || ctx.UsedNonVolatileXmm.Count > 0) {
			InsertCalleeSavedSaveRestore(func, ctx.UsedNonVolatileGpr, ctx.UsedNonVolatileXmm);
		}

		// Step 6: Update prologue stack size to include spill area and alignment padding
		// This must be done AFTER callee-saved insertion so we know the GPR push count
		UpdateFinalPrologueStackSize(func, ctx.TotalSpillSize, ctx.UsedNonVolatileGpr.Count);

		if (allocation.Count > 0) {
			Logger.Debug(LogCategory.RegAlloc, $"  {func.Name}: allocated {allocation.Count} virtual registers ({ctx.UsedNonVolatileGpr.Count} callee-saved GPRs, {ctx.UsedNonVolatileXmm.Count} callee-saved XMMs)");
		}
		return changed;
	}

	/// <summary>
	/// Updates the PrologueOp stack size to include spill area and alignment padding.
	///
	/// Stack layout after prologue (pushes happen BEFORE sub rsp):
	/// 1. call pushes return address: RSP - 8
	/// 2. push rbp: RSP - 16 (aligned)
	/// 3. push callee-saved GPRs: RSP - 16 - N*8 (where N = gprPushCount)
	/// 4. sub rsp, stackSize
	///
	/// For 16-byte alignment at call sites:
	/// - Even GPR pushes: stackSize must be multiple of 16
	/// - Odd GPR pushes: stackSize must be 8 mod 16 (to compensate for 8-byte misalignment)
	/// </summary>
	private static void UpdateFinalPrologueStackSize(MlirFunction func, int spillSize, int gprPushCount) {
		var entryBlock = func.Body.Blocks.FirstOrDefault();
		if (entryBlock is null) return;

		for (int i = 0; i < entryBlock.Operations.Count; i++) {
			if (entryBlock.Operations[i] is PrologueOp prologue) {
				int baseSize = prologue.StackSize + spillSize;

				// After push rbp + N GPR pushes, the stack is:
				// - 16-byte aligned if N is even (rbp + even pushes = 16 + even*8 = multiple of 16)
				// - 8-byte misaligned if N is odd (rbp + odd pushes = 16 + odd*8 = 8 mod 16)
				//
				// For sub rsp, stackSize to result in 16-byte alignment:
				// - Even pushes: stackSize must be 0 mod 16
				// - Odd pushes: stackSize must be 8 mod 16
				int targetMod = (gprPushCount % 2 == 0) ? 0 : 8;
				int currentMod = baseSize % 16;
				int alignmentPadding = (16 + targetMod - currentMod) % 16;
				int newStackSize = baseSize + alignmentPadding;

				if (newStackSize != prologue.StackSize || !prologue.CalleeSavedGprs.SequenceEqual(prologue.CalleeSavedGprs)) {
					entryBlock.Operations[i] = new PrologueOp(newStackSize, prologue.CalleeSavedGprs);
					Logger.Debug(LogCategory.RegAlloc, $"  Updated prologue: {prologue.StackSize} -> {newStackSize} (spills={spillSize}, gprPushes={gprPushCount}, alignPad={alignmentPadding})");
				}
				return;
			}
		}
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

		/// <summary>
		/// Maps physical registers to the set of vregs that cannot use that register.
		/// A vreg cannot use a physical register if:
		/// - The vreg is defined before an explicit write to that physical register
		/// - The vreg is used after that explicit write
		/// This handles patterns like division where RAX/RDX are explicitly used.
		/// </summary>
		public Dictionary<X86Register, HashSet<int>> VregsConstrainedFrom { get; } = [];
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
		// Note: we scan ALL control flow ops in each block, not just the terminator,
		// because inline .args blocks may contain additional JmpOps after a JccOp
		for (int blockIdx = 0; blockIdx < func.Body.Blocks.Count; blockIdx++) {
			var block = func.Body.Blocks[blockIdx];

			// Collect ALL successor names from ALL control flow ops in the block
			var successorNames = new List<string>();
			foreach (var op in block.Operations) {
				switch (op) {
					case JmpOp jmp:
						successorNames.Add(jmp.Target);
						break;
					case JccOp jcc:
						successorNames.Add(jcc.TrueTarget);
						successorNames.Add(jcc.FalseTarget);
						break;
				}
			}

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
					// Record definitions and uses for all operands
					foreach (var operand in x86Op.X86Operands) {
						CollectVregDefsAndUses(operand, blockIdx, opIdx, allDefs, allUses);
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
	/// Analyzes which vregs are live across explicit physical register writes.
	/// When code like "mov rax, vreg" explicitly writes to a physical register,
	/// any vreg that's defined before and used after cannot be allocated to that register.
	/// This is essential for patterns like division where RAX/RDX are explicitly used.
	/// </summary>
	private static void AnalyzePhysicalRegisterConstraints(MlirFunction func, LivenessInfo livenessInfo) {
		// Collect vreg definitions, uses, and physical register write locations
		var vregDefs = new Dictionary<int, (int blockIdx, int opIdx)>();
		var vregUses = new List<(int vregId, int blockIdx, int opIdx)>();
		var physRegWrites = new List<(X86Register reg, int blockIdx, int opIdx)>();

		for (int blockIdx = 0; blockIdx < func.Body.Blocks.Count; blockIdx++) {
			var block = func.Body.Blocks[blockIdx];
			for (int opIdx = 0; opIdx < block.Operations.Count; opIdx++) {
				var op = block.Operations[opIdx];
				if (op is not X86Op x86Op) continue;

				// Check for explicit physical register writes
				// MovOp where destination is a physical register (not vreg)
				if (op is MovOp mov && mov.Dst is RegOperand dstReg) {
					physRegWrites.Add((dstReg.Register, blockIdx, opIdx));
				}

				// CdqOp implicitly writes to RDX (sign extension)
				if (op is CdqOp) {
					physRegWrites.Add((X86Register.RDX, blockIdx, opIdx));
				}

				// IdivOp writes to both RAX (quotient) and RDX (remainder)
				if (op is IdivOp) {
					physRegWrites.Add((X86Register.RAX, blockIdx, opIdx));
					physRegWrites.Add((X86Register.RDX, blockIdx, opIdx));
				}

				// Collect vreg defs and uses
				foreach (var operand in x86Op.X86Operands) {
					CollectVregDefsAndUsesForConstraints(operand, blockIdx, opIdx, vregDefs, vregUses);
				}
			}
		}

		// For each physical register write, find vregs that are live across it
		foreach (var (physReg, writeBlockIdx, writeOpIdx) in physRegWrites) {
			if (!livenessInfo.VregsConstrainedFrom.ContainsKey(physReg)) {
				livenessInfo.VregsConstrainedFrom[physReg] = [];
			}

			foreach (var kvp in vregDefs) {
				var vregId = kvp.Key;
				var (defBlockIdx, defOpIdx) = kvp.Value;

				// Is vreg defined before the physical register write?
				bool definedBefore = defBlockIdx < writeBlockIdx ||
					(defBlockIdx == writeBlockIdx && defOpIdx < writeOpIdx);

				if (!definedBefore) continue;

				// Is vreg used after the physical register write?
				bool usedAfter = vregUses.Any(u =>
					u.vregId == vregId &&
					(u.blockIdx > writeBlockIdx ||
					(u.blockIdx == writeBlockIdx && u.opIdx > writeOpIdx)));

				if (usedAfter) {
					livenessInfo.VregsConstrainedFrom[physReg].Add(vregId);
				}
			}
		}

		// Log constraints
		foreach (var (physReg, constrainedVregs) in livenessInfo.VregsConstrainedFrom) {
			if (constrainedVregs.Count > 0) {
				Logger.Trace(LogCategory.RegAlloc, $"  Vregs constrained from {physReg}: {string.Join(", ", constrainedVregs.Select(v => $"v{v}"))}");
			}
		}
	}

	/// <summary>
	/// Collects vreg definitions and uses for constraint analysis.
	/// </summary>
	private static void CollectVregDefsAndUsesForConstraints(
		X86Operand operand,
		int blockIdx,
		int opIdx,
		Dictionary<int, (int blockIdx, int opIdx)> defs,
		List<(int vregId, int blockIdx, int opIdx)> uses) {

		if (operand is VRegOperand vreg) {
			if (!defs.ContainsKey(vreg.Id)) {
				defs[vreg.Id] = (blockIdx, opIdx);
			}
			uses.Add((vreg.Id, blockIdx, opIdx));
		} else if (operand is MemOperand mem) {
			if (mem.Base is not null) {
				CollectVregDefsAndUsesForConstraints(mem.Base, blockIdx, opIdx, defs, uses);
			}
			if (mem.Index is not null) {
				CollectVregDefsAndUsesForConstraints(mem.Index, blockIdx, opIdx, defs, uses);
			}
		}
	}

	/// <summary>
	/// Collects vreg definitions and uses from an operand, including vregs inside memory operands.
	/// </summary>
	private static void CollectVregDefsAndUses(
		X86Operand operand,
		int blockIdx,
		int opIdx,
		Dictionary<int, (int blockIndex, int opIndex)> allDefs,
		List<(int vregId, int blockIndex, int opIndex)> allUses) {

		if (operand is VRegOperand vreg) {
			// First occurrence is a definition
			if (!allDefs.ContainsKey(vreg.Id)) {
				allDefs[vreg.Id] = (blockIdx, opIdx);
			}
			allUses.Add((vreg.Id, blockIdx, opIdx));
		} else if (operand is MemOperand mem) {
			// Also check vregs used inside memory operands (base/index)
			if (mem.Base is not null) {
				CollectVregDefsAndUses(mem.Base, blockIdx, opIdx, allDefs, allUses);
			}
			if (mem.Index is not null) {
				CollectVregDefsAndUses(mem.Index, blockIdx, opIdx, allDefs, allUses);
			}
		}
	}

	/// <summary>
	/// Updates prologue/epilogue with callee-saved registers.
	/// GPR saves are handled by PrologueOp/EpilogueOp (pushes BEFORE sub rsp).
	/// XMM saves are handled by movsd to stack slots AFTER the prologue.
	/// </summary>
	private static void InsertCalleeSavedSaveRestore(
		MlirFunction func,
		HashSet<X86Register> usedNonVolatileGpr,
		HashSet<X86Register> usedNonVolatileXmm) {

		var gprsToSave = usedNonVolatileGpr.OrderBy(r => r).ToList();
		var xmmsToSave = usedNonVolatileXmm.OrderBy(r => r).ToList();

		// Find prologue in entry block and update it with callee-saved GPRs
		var entryBlock = func.Body.Blocks[0];
		int prologueIdx = -1;
		PrologueOp? prologue = null;
		for (int i = 0; i < entryBlock.Operations.Count; i++) {
			if (entryBlock.Operations[i] is PrologueOp p) {
				prologueIdx = i;
				prologue = p;
				break;
			}
		}

		if (prologueIdx >= 0 && prologue != null) {
			// Update prologue with callee-saved GPRs (they'll be pushed BEFORE sub rsp)
			int newStackSize = prologue.StackSize;

			// For XMM registers, add space to stack frame
			if (xmmsToSave.Count > 0) {
				int xmmSaveAreaOffset = prologue.StackSize;
				int xmmSaveSize = xmmsToSave.Count * 8;
				newStackSize = WindowsX64Abi.AlignTo(prologue.StackSize + xmmSaveSize, 16);

				// Store the XMM save info for use in epilogue
				func.SetMetadata("xmm_save_offset", xmmSaveAreaOffset);
				func.SetMetadata("xmm_save_count", xmmsToSave.Count);
			}

			// Replace prologue with one that includes callee-saved GPRs
			entryBlock.Operations[prologueIdx] = new PrologueOp(newStackSize, gprsToSave);

			// Insert XMM saves AFTER the prologue (they use RBP-relative addressing)
			if (xmmsToSave.Count > 0) {
				int xmmSaveAreaOffset = func.GetMetadataValue<int>("xmm_save_offset");
				int insertIdx = prologueIdx + 1;
				for (int i = 0; i < xmmsToSave.Count; i++) {
					var reg = xmmsToSave[i];
					int offset = -(xmmSaveAreaOffset + (i + 1) * 8);  // Negative offset from RBP
					var memOp = new MemOperand(
						Base: new RegOperand(X86Register.RBP),
						Displacement: offset,
						Size: 8
					);
					entryBlock.Operations.Insert(insertIdx++, new MovsdOp(memOp, new RegOperand(reg)));
				}

				Logger.Debug(LogCategory.RegAlloc, $"  Saving XMM registers: {string.Join(", ", xmmsToSave)} at offsets [{xmmSaveAreaOffset}..{xmmSaveAreaOffset + xmmsToSave.Count * 8})");
			}
		}

		// Find epilogues in all blocks and update them with callee-saved GPRs
		foreach (var block in func.Body.Blocks) {
			for (int i = 0; i < block.Operations.Count; i++) {
				if (block.Operations[i] is EpilogueOp) {
					// Restore XMM registers BEFORE epilogue (they use RBP-relative addressing)
					if (xmmsToSave.Count > 0) {
						int xmmSaveAreaOffset = func.GetMetadataValue<int>("xmm_save_offset");
						for (int j = xmmsToSave.Count - 1; j >= 0; j--) {
							var reg = xmmsToSave[j];
							int offset = -(xmmSaveAreaOffset + (j + 1) * 8);
							var memOp = new MemOperand(
								Base: new RegOperand(X86Register.RBP),
								Displacement: offset,
								Size: 8
							);
							block.Operations.Insert(i++, new MovsdOp(new RegOperand(reg), memOp));
						}
					}

					// Replace epilogue with one that includes callee-saved GPRs
					block.Operations[i] = new EpilogueOp(gprsToSave);
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

			// Defensive assertion: CdqOp must be immediately followed by IdivOp
			// This invariant is assumed by constraint analysis which handles RAX/RDX clobbering
			if (op is CdqOp) {
				var nextIdx = opIdx + 1;
				if (nextIdx >= block.Operations.Count || block.Operations[nextIdx] is not IdivOp) {
					throw new InvalidOperationException(
						$"CdqOp must be immediately followed by IdivOp (found at block {blockIdx} op {opIdx})");
				}
			}

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

				// Clear pending ops before allocation
				ctx.ClearPendingOps();

				var allocated = AllocateOp(x86Op, ctx);

				// Insert pending spills FIRST (to save register values before they're overwritten)
				if (ctx.PendingSpills.Count > 0) {
					var spillDescs = ctx.PendingSpills.Select(p => {
						if (p is MovsdOp movsd) return $"movsd {movsd.Dst} <- {movsd.Src}";
						if (p is MovOp mov) return $"mov {mov.Dst} <- {mov.Src}";
						return ((X86Op)p).Mnemonic;
					});
					Logger.Trace(LogCategory.RegAlloc, $"  Spills for {x86Op.Mnemonic}: {string.Join("; ", spillDescs)}");
				}
				foreach (var spillOp in ctx.PendingSpills) {
					newOps.Add(spillOp);
				}

				// Insert pending reloads AFTER spills (to load values after registers are freed)
				if (ctx.PendingReloads.Count > 0) {
					var reloadDescs = ctx.PendingReloads.Select(p => {
						if (p is MovsdOp movsd) return $"movsd {movsd.Dst} <- {movsd.Src}";
						if (p is MovOp mov) return $"mov {mov.Dst} <- {mov.Src}";
						return ((X86Op)p).Mnemonic;
					});
					Logger.Trace(LogCategory.RegAlloc, $"  Reloads for {x86Op.Mnemonic}: {string.Join("; ", reloadDescs)}");
				}
				foreach (var reloadOp in ctx.PendingReloads) {
					newOps.Add(reloadOp);
				}

				newOps.Add(allocated);
				changed = true;

				// After the instruction, all active vregs have values and are spill-safe
				ctx.MarkAllActiveSpillSafe();

				// Insert pending post-operations (stores) after the main op
				foreach (var postOp in ctx.PendingPostOps) {
					newOps.Add(postOp);
				}

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

		// Track which vregs have received a value and are safe to spill.
		// A vreg becomes spill-safe after it's defined (instruction completes) or reloaded.
		private readonly HashSet<int> _spillSafeVregs = [];

		// Spill slot tracking - maps vregId to frame offset (negative relative to RBP)
		private readonly Dictionary<int, int> _spillOffsets = [];
		private int _nextSpillOffset = 0;  // starts at 0, grows downward (more negative)

		// For loop-live vregs, track the original "home" register they were allocated to.
		// When spilled and reloaded, they must return to this register for loop correctness.
		private readonly Dictionary<int, X86Register> _loopLiveHomeRegs = [];

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
		/// Total size of spill slots allocated (positive value for stack allocation).
		/// </summary>
		public int TotalSpillSize => -_nextSpillOffset;

		/// <summary>
		/// Returns true if the given vreg is currently spilled to memory.
		/// </summary>
		public bool IsSpilled(int vregId) => _spillOffsets.ContainsKey(vregId);

		/// <summary>
		/// Gets the frame offset for a spilled vreg.
		/// </summary>
		public int GetSpillOffset(int vregId) => _spillOffsets[vregId];

		/// <summary>
		/// Allocates a spill slot for a vreg. Returns the frame offset (negative relative to RBP).
		/// The offset is computed relative to the spill area, which starts after shadow space and locals.
		/// </summary>
		public int AllocateSpillSlot(int vregId, int size = 8) {
			_nextSpillOffset -= WindowsX64Abi.AlignTo(size, 8);
			_spillOffsets[vregId] = _nextSpillOffset;
			Logger.Trace(LogCategory.RegAlloc, $"  Allocated spill slot for v{vregId} at offset {_nextSpillOffset}");
			return _nextSpillOffset;
		}

		/// <summary>
		/// Finds the best vreg to spill using the "furthest next use" heuristic.
		/// Returns the vreg whose next use is furthest away to minimize reloads.
		/// </summary>
		public int FindSpillCandidate(bool isFloat) {
			int bestVreg = -1;
			int bestDistance = int.MinValue;  // Use MinValue to allow negative distances

			Logger.Trace(LogCategory.RegAlloc, $"  FindSpillCandidate(isFloat={isFloat}) at block {CurrentBlockIdx} op {CurrentOpIdx}: {_activeVregs.Count} active vregs");
			foreach (var (vregId, physReg) in _activeVregs) {
				// Skip if wrong register type
				bool vregIsFloat = _vregIsFloat.GetValueOrDefault(vregId);
				if (vregIsFloat != isFloat) {
					Logger.Trace(LogCategory.RegAlloc, $"    v{vregId} in {physReg}: skip (isFloat={vregIsFloat}, wanted={isFloat})");
					continue;
				}

				// Skip vregs that haven't received a value yet (being defined by current instruction)
				if (!_spillSafeVregs.Contains(vregId)) {
					Logger.Trace(LogCategory.RegAlloc, $"    v{vregId} in {physReg}: skip (not spill-safe, no value yet)");
					continue;
				}

				// Calculate distance to next use (or use a fallback if no liveness info)
				int distance;
				if (livenessInfo.LastUse.TryGetValue(vregId, out var lastUse)) {
					// Distance = blocks away * 1000 + operations away
					distance = (lastUse.blockIdx - CurrentBlockIdx) * 1000 + (lastUse.opIdx - CurrentOpIdx);
				} else {
					// No liveness info - assume it's used soon (distance 0)
					distance = 0;
				}
				Logger.Trace(LogCategory.RegAlloc, $"    v{vregId} in {physReg}: distance={distance}");
				if (distance > bestDistance) {
					bestDistance = distance;
					bestVreg = vregId;
				}
			}

			if (bestVreg == -1) {
				throw new InvalidOperationException($"No vreg available to spill - all active vregs are of wrong type or not spill-safe. Looking for isFloat={isFloat}. Active: {string.Join(", ", _activeVregs.Select(kv => $"v{kv.Key}({kv.Value}, float={_vregIsFloat.GetValueOrDefault(kv.Key)}, safe={_spillSafeVregs.Contains(kv.Key)})"))}");
			}

			return bestVreg;
		}

		/// <summary>
		/// Spills a vreg: allocates a spill slot (or reuses existing), generates store instruction, and frees its register.
		/// Returns the physical register that was freed.
		/// </summary>
		public X86Register SpillVreg(int vregId) {
			if (!_activeVregs.TryGetValue(vregId, out var physReg)) {
				throw new InvalidOperationException($"Cannot spill v{vregId} - not currently in a register");
			}

			var isFloat = _vregIsFloat.GetValueOrDefault(vregId);
			var isNonVolatile = _vregIsNonVolatile.GetValueOrDefault(vregId);

			// Allocate spill slot (or reuse existing if this vreg was previously spilled and reloaded)
			if (_spillOffsets.TryGetValue(vregId, out int spillOffset)) {
				Logger.Trace(LogCategory.RegAlloc, $"  Re-spilling v{vregId} to existing slot at offset {spillOffset}");
			} else {
				spillOffset = AllocateSpillSlot(vregId);
			}

			// Generate store instruction to save current value to spill slot
			// IMPORTANT: Spills go to PendingSpills (not PendingReloads) to ensure they
			// execute BEFORE any reloads that might overwrite the register.
			var memOp = new MemOperand(
					Base: new RegOperand(X86Register.RBP),
					Displacement: SpillAreaBaseOffset + spillOffset,
					Size: 8
				);

			if (isFloat) {
				PendingSpills.Add(new MovsdOp(memOp, new RegOperand(physReg)));
			} else {
				PendingSpills.Add(new MovOp(memOp, new RegOperand(physReg)));
			}

			// Remove from active set and free the register
			_activeVregs.Remove(vregId);

			if (isFloat) {
				if (isNonVolatile) _freeNonVolatileXmms.Add(physReg);
				else _freeVolatileXmms.Add(physReg);
			} else {
				if (isNonVolatile) _freeNonVolatileGprs.Add(physReg);
				else _freeVolatileGprs.Add(physReg);
			}

			Logger.Debug(LogCategory.RegAlloc, $"  Spilled v{vregId} from {physReg} to [rbp{SpillAreaBaseOffset + spillOffset}]");
			return physReg;
		}

		/// <summary>
		/// Spill stores to insert before the current operation.
		/// These must be emitted FIRST to save register values before they're overwritten.
		/// </summary>
		public List<X86Op> PendingSpills { get; } = [];

		/// <summary>
		/// Reload operations to insert before the current operation.
		/// These are emitted AFTER spills to load values into registers.
		/// </summary>
		public List<X86Op> PendingReloads { get; } = [];

		/// <summary>
		/// Operations to insert after the current operation (e.g., stores for definitions).
		/// </summary>
		public List<X86Op> PendingPostOps { get; } = [];

		/// <summary>
		/// Clears pending operations after they have been inserted.
		/// </summary>
		public void ClearPendingOps() {
			PendingSpills.Clear();
			PendingReloads.Clear();
			PendingPostOps.Clear();
		}

		/// <summary>
		/// Marks all active vregs as spill-safe (called after an instruction completes).
		/// </summary>
		public void MarkAllActiveSpillSafe() {
			foreach (var vregId in _activeVregs.Keys) {
				_spillSafeVregs.Add(vregId);
			}
		}

		/// <summary>
		/// Gets the current spill area base offset. This will be combined with the
		/// function's local variable size to compute the actual frame offset.
		/// </summary>
		public int SpillAreaBaseOffset { get; set; } = 0;

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
				// Check if this vreg is currently spilled and needs to be reloaded
				if (IsSpilled(vreg.Id) && !_activeVregs.ContainsKey(vreg.Id)) {
					X86Register newReg;

					// For loop-live vregs, we must reload into the home register
					// to maintain correct values at the loop header
					if (_loopLiveHomeRegs.TryGetValue(vreg.Id, out var homeReg)) {
						newReg = EnsureRegisterFree(homeReg, vreg.IsFloat);
					} else {
						// Allocate a new register for the reloaded value
						newReg = vreg.IsFloat
							? AllocXmm(vreg.Id)
							: AllocGpr(vreg.Id);
					}

					// Generate reload instruction from spill slot
					// Reloads go to PendingReloads (after spills) to ensure register is freed first
					var spillOffset = GetSpillOffset(vreg.Id);
					var memOp = new MemOperand(
						Base: new RegOperand(X86Register.RBP),
						Displacement: SpillAreaBaseOffset + spillOffset,
						Size: 8
					);

					if (vreg.IsFloat) {
						PendingReloads.Add(new MovsdOp(new RegOperand(newReg), memOp));
					} else {
						PendingReloads.Add(new MovOp(new RegOperand(newReg), memOp));
					}

					// Update tracking - reloaded vregs are immediately spill-safe
					allocation[vreg.Id] = newReg;
					_activeVregs[vreg.Id] = newReg;
					_spillSafeVregs.Add(vreg.Id);
					Logger.Debug(LogCategory.RegAlloc, $"  Reload v{vreg.Id} from [rbp{SpillAreaBaseOffset + spillOffset}] to {newReg}");

					return new RegOperand(newReg);
				}

				if (!allocation.TryGetValue(vreg.Id, out var physReg)) {
					physReg = vreg.IsFloat
						? AllocXmm(vreg.Id)
						: AllocGpr(vreg.Id);
					allocation[vreg.Id] = physReg;
					_activeVregs[vreg.Id] = physReg;
					_vregIsFloat[vreg.Id] = vreg.IsFloat;

					// Record home register for loop-live vregs
					if (livenessInfo.LiveThroughLoop.Contains(vreg.Id)) {
						_loopLiveHomeRegs[vreg.Id] = physReg;
						Logger.Trace(LogCategory.RegAlloc, $"  Recorded home register for loop-live v{vreg.Id}: {physReg}");
					}
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

			// Collect registers this vreg cannot use due to explicit physical register writes
			var constrainedRegs = new HashSet<X86Register>();
			foreach (var (physReg, constrainedVregs) in livenessInfo.VregsConstrainedFrom) {
				if (constrainedVregs.Contains(vregId)) {
					constrainedRegs.Add(physReg);
				}
			}

			if (needsNonVolatile) {
				// Filter out constrained registers from non-volatile set
				var available = _freeNonVolatileGprs.Where(r => !constrainedRegs.Contains(r)).ToHashSet();
				if (available.Count > 0) {
					return AllocFromFreeSetFilteredWithSpilling(
						_freeNonVolatileGprs,
						available,
						usedNonVolatileGpr,
						isFloat: false);
				}
				// Fall back to any available non-volatile with spilling
				return AllocFromFreeSetWithSpilling(
					_freeNonVolatileGprs,
					usedNonVolatileGpr,
					isFloat: false);
			}

			// For loop-live vregs, avoid RAX/RDX which are clobbered by idiv
			// These registers can be reused mid-loop but a loop-live vreg must survive the entire loop
			if (livenessInfo.LiveThroughLoop.Contains(vregId)) {
				// Try volatile registers except RAX/RDX and any constrained registers
				var safeVolatile = _freeVolatileGprs
					.Where(r => r != X86Register.RAX && r != X86Register.RDX && !constrainedRegs.Contains(r))
					.ToList();
				if (safeVolatile.Count > 0) {
					var reg = safeVolatile.First();
					_freeVolatileGprs.Remove(reg);
					return reg;
				}

				// Fall back to callee-saved for loop-live vregs
				_vregIsNonVolatile[vregId] = true;
				var availableNonVol = _freeNonVolatileGprs.Where(r => !constrainedRegs.Contains(r)).ToHashSet();
				if (availableNonVol.Count > 0) {
					return AllocFromFreeSetFilteredWithSpilling(
						_freeNonVolatileGprs,
						availableNonVol,
						usedNonVolatileGpr,
						isFloat: false
						);
				}
				return AllocFromFreeSetWithSpilling(
					_freeNonVolatileGprs,
					usedNonVolatileGpr,
					isFloat: false);
			}

			// Check if this vreg is constrained from any registers
			if (constrainedRegs.Count > 0) {
				// Try volatile registers that aren't constrained
				var safeVolatile = _freeVolatileGprs.Where(r => !constrainedRegs.Contains(r)).ToList();
				if (safeVolatile.Count > 0) {
					var reg = safeVolatile.First();
					_freeVolatileGprs.Remove(reg);
					Logger.Trace(LogCategory.RegAlloc, $"  v{vregId} constrained from {string.Join(",", constrainedRegs)}, using {reg}");
					return reg;
				}

				// Try non-volatile that aren't constrained
				var safeNonVolatile = _freeNonVolatileGprs.Where(r => !constrainedRegs.Contains(r)).ToList();
				if (safeNonVolatile.Count > 0) {
					_vregIsNonVolatile[vregId] = true;
					var reg = safeNonVolatile.First();
					_freeNonVolatileGprs.Remove(reg);
					usedNonVolatileGpr.Add(reg);
					Logger.Trace(LogCategory.RegAlloc, $"  v{vregId} constrained from {string.Join(",", constrainedRegs)}, using callee-saved {reg}");
					return reg;
				}
			}

			// Try volatile first, then fall back to non-volatile
			if (_freeVolatileGprs.Count > 0) {
				return AllocFromFreeSetWithSpilling(_freeVolatileGprs, null, isFloat: false);
			}

			// Fall back to callee-saved registers (will be saved/restored in prologue/epilogue)
			_vregIsNonVolatile[vregId] = true;
			return AllocFromFreeSetWithSpilling(
				_freeNonVolatileGprs,
				usedNonVolatileGpr,
				isFloat: false);
		}

		/// <summary>
		/// Allocates from a filtered subset of the free set, spilling if necessary.
		/// </summary>
		private X86Register AllocFromFreeSetFilteredWithSpilling(
			HashSet<X86Register> freeSet,
			HashSet<X86Register> available,
			HashSet<X86Register>? usedSet,
			bool isFloat) {
			if (available.Count == 0) {
				// Need to spill - find the best candidate and spill it
				var victimVreg = FindSpillCandidate(isFloat);
				var freedReg = SpillVreg(victimVreg);
				// Update available set with the freed register if it's valid
				if (!available.Contains(freedReg)) {
					// The freed register might not match our filter criteria
					// In this case, just use the freed register anyway
					usedSet?.Add(freedReg);
					return freedReg;
				}
			}
			var reg = available.First();
			freeSet.Remove(reg);
			usedSet?.Add(reg);
			return reg;
		}

		private X86Register AllocXmm(int vregId) {
			bool needsNonVolatile = liveAcrossCalls.Contains(vregId);
			_vregIsNonVolatile[vregId] = needsNonVolatile;

			if (needsNonVolatile) {
				return AllocFromFreeSetWithSpilling(
					_freeNonVolatileXmms,
					usedNonVolatileXmm,
					isFloat: true);
			}
			return AllocFromFreeSetWithSpilling(
				_freeVolatileXmms,
				null,
				isFloat: true);
		}

		/// <summary>
		/// Allocates a register from the free set, spilling if necessary.
		/// </summary>
		private X86Register AllocFromFreeSetWithSpilling(
			HashSet<X86Register> freeSet,
			HashSet<X86Register>? usedSet,
			bool isFloat) {
			if (freeSet.Count == 0) {
				// Need to spill - find the best candidate and spill it
				var victimVreg = FindSpillCandidate(isFloat);
				_ = SpillVreg(victimVreg);
				// The freed register is now in freeSet, so fall through to allocate it
			}

			var reg = freeSet.First();
			freeSet.Remove(reg);
			usedSet?.Add(reg);
			return reg;
		}

		/// <summary>
		/// Ensures a specific register is free by spilling any vreg currently using it.
		/// Returns the register after ensuring it's free.
		/// </summary>
		private X86Register EnsureRegisterFree(X86Register targetReg, bool isFloat) {
			// Check if any vreg is currently using this register
			int? vregInTarget = null;
			foreach (var (vregId, physReg) in _activeVregs) {
				if (physReg == targetReg) {
					vregInTarget = vregId;
					break;
				}
			}

			if (vregInTarget.HasValue) {
				// Spill the vreg currently in the target register
				Logger.Trace(LogCategory.RegAlloc, $"  EnsureRegisterFree: spilling v{vregInTarget.Value} from {targetReg}");
				SpillVreg(vregInTarget.Value);
			}

			// Remove from free sets (it may already not be there if it was active)
			if (isFloat) {
				_freeVolatileXmms.Remove(targetReg);
				_freeNonVolatileXmms.Remove(targetReg);
			} else {
				_freeVolatileGprs.Remove(targetReg);
				_freeNonVolatileGprs.Remove(targetReg);
			}

			return targetReg;
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
			MovzxOp movzx => new MovzxOp(ctx.Alloc(movzx.Dst), ctx.Alloc(movzx.Src), movzx.IsByte),
			MovsxOp movsx => new MovsxOp(ctx.Alloc(movsx.Dst), ctx.Alloc(movsx.Src), movsx.IsByte),
			MovsxdOp movsxd => new MovsxdOp(ctx.Alloc(movsxd.Dst), ctx.Alloc(movsxd.Src)),
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
			JzOp jz => jz,
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
			CldOp cld => cld,
			RepMovsbOp repMovsb => repMovsb,
			_ => throw new NotSupportedException($"Unsupported X86Op type for allocation: {op.GetType()}")
		};
	}
}

