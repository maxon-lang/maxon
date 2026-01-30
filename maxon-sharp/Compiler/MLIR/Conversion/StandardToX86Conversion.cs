using System.Globalization;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

public static class StandardToX86Conversion {
	public static void Run(MlirModule module) {
		foreach (var func in module.Functions) {
			ConvertFunction(func, module);
		}
	}

	private static void ConvertFunction(MlirFunction func, MlirModule module) {
		// Pre-scan: calculate stack frame for local variables
		var varOffsets = new Dictionary<string, int>();
		int stackSize = 0;
		foreach (var block in func.Body.Blocks) {
			foreach (var op in block.Operations) {
				if (op is MemRefAllocaOp alloca) {
					stackSize += alloca.VarType.SizeInBytes;
					varOffsets[alloca.VarName] = -stackSize;
				}
			}
		}
		// Align to 16 bytes
		if (stackSize > 0)
			stackSize = (stackSize + 15) & ~15;

		// Track float constants for rdata deduplication
		var floatConstants = new Dictionary<double, string>();

		// Track which SSA values map to which XMM register
		var valueToXmm = new Dictionary<MlirValue, X86XmmRegister>();
		int nextXmm = 0;

		var sourceBlocks = func.Body.Blocks.ToList();
		var newBlocks = new List<MlirBlock>();

		for (int blockIdx = 0; blockIdx < sourceBlocks.Count; blockIdx++) {
			var srcBlock = sourceBlocks[blockIdx];
			var x86Block = new MlirBlock(srcBlock.Name);
			newBlocks.Add(x86Block);

			// Prologue only in entry block
			if (blockIdx == 0) {
				x86Block.AddOp(new X86PushReg(X86Register.Rbp));
				x86Block.AddOp(new X86MovRegReg(X86Register.Rbp, X86Register.Rsp));
				if (stackSize > 0)
					x86Block.AddOp(new X86SubRegImm(X86Register.Rsp, stackSize));
			}

			foreach (var op in srcBlock.Operations) {
				switch (op) {
					case ArithConstantOp constOp:
						x86Block.AddOp(new X86MovRegImm(X86Register.Eax, (int)constOp.IntValue));
						break;

					case ArithFloatConstantOp floatOp: {
						var label = GetOrCreateFloatLabel(floatOp.FloatValue, module, floatConstants);
						var xmmReg = (X86XmmRegister)nextXmm;
						x86Block.AddOp(new X86MovSdXmmRipRel(xmmReg, label));
						valueToXmm[floatOp.Result] = xmmReg;
						nextXmm++;
						break;
					}

					case MemRefAllocaOp:
						break;

					case MemRefStoreOp storeOp: {
						var offset = varOffsets[storeOp.VarName];
						x86Block.AddOp(new X86MovSdMemXmm(offset, X86XmmRegister.Xmm0));
						// After storing, xmm0 is free; reset allocation
						nextXmm = 0;
						break;
					}

					case MemRefLoadOp loadOp: {
						var offset = varOffsets[loadOp.VarName];
						x86Block.AddOp(new X86MovSdXmmMem(X86XmmRegister.Xmm0, offset));
						valueToXmm[loadOp.Result] = X86XmmRegister.Xmm0;
						nextXmm = 1; // xmm0 is now occupied
						break;
					}

					case ArithCmpFOp cmpOp: {
						var lhsReg = valueToXmm.GetValueOrDefault(cmpOp.Operands[0], X86XmmRegister.Xmm0);
						var rhsReg = valueToXmm.GetValueOrDefault(cmpOp.Operands[1], X86XmmRegister.Xmm1);
						x86Block.AddOp(new X86Ucomisd(lhsReg, rhsReg));
						break;
					}

					case CfCondBrOp condBr: {
						var scopedElse = $"{func.Name}.{condBr.ElseBlock}";
						x86Block.AddOp(new X86Jcc("ne", scopedElse));
						x86Block.AddOp(new X86Jcc("p", scopedElse));
						break;
					}

					case FuncCallOp callOp:
						x86Block.AddOp(new X86CallDirect(callOp.Callee));
						break;

					case FuncReturnOp:
						if (stackSize > 0)
							x86Block.AddOp(new X86AddRegImm(X86Register.Rsp, stackSize));
						x86Block.AddOp(new X86PopReg(X86Register.Rbp));
						x86Block.AddOp(new X86Ret());
						break;
				}
			}
		}

		func.Body.Blocks.Clear();
		func.Body.Blocks.AddRange(newBlocks);
	}

	private static string GetOrCreateFloatLabel(double value, MlirModule module, Dictionary<double, string> floatConstants) {
		if (!floatConstants.TryGetValue(value, out var label)) {
			label = $"__float_{value.ToString(CultureInfo.InvariantCulture)}";
			floatConstants[value] = label;
			module.RdataEntries.Add((label, BitConverter.GetBytes(value)));
		}
		return label;
	}
}
