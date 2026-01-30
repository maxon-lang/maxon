using System.Globalization;
using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

public static class StandardToX86Conversion {
	public static MlirModule<X86Op> Run(MlirModule<StandardOp> module) {
		var result = new MlirModule<X86Op>();
		result.Globals.AddRange(module.Globals);

		foreach (var func in module.Functions) {
			var newFunc = ConvertFunction(func, result);
			result.AddFunction(newFunc);
		}

		return result;
	}

	private static MlirFunction<X86Op> ConvertFunction(MlirFunction<StandardOp> func, MlirModule<X86Op> outputModule) {
		var newFunc = new MlirFunction<X86Op>(func.Name, func.ParamTypes, func.ReturnType);

		// Pre-scan: calculate stack frame for local variables
		var varOffsets = new Dictionary<string, int>();
		int stackSize = 0;
		foreach (var block in func.Body.Blocks) {
			foreach (var op in block.Operations) {
				if (op is StandardMemRefAllocaOp alloca) {
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

		// Track which SSA values map to which GPR
		var valueToGpr = new Dictionary<MlirValue, X86Register>();
		var gprPool = new[] { X86Register.Eax, X86Register.Ecx, X86Register.Edx, X86Register.Ebx, X86Register.Esi, X86Register.Edi };
		int nextGpr = 0;

		var sourceBlocks = func.Body.Blocks.ToList();

		for (int blockIdx = 0; blockIdx < sourceBlocks.Count; blockIdx++) {
			var srcBlock = sourceBlocks[blockIdx];
			var x86Block = newFunc.Body.AddBlock(srcBlock.Name);

			nextGpr = 0;
			nextXmm = 0;

			// Prologue only in entry block
			if (blockIdx == 0) {
				x86Block.AddOp(new X86PushRegOp(X86Register.Rbp));
				x86Block.AddOp(new X86MovRegRegOp(X86Register.Rbp, X86Register.Rsp));
				if (stackSize > 0)
					x86Block.AddOp(new X86SubRegImmOp(X86Register.Rsp, stackSize));
			}

			foreach (var op in srcBlock.Operations) {
				switch (op) {
					case StandardArithConstantOp constOp: {
							var gpr = gprPool[nextGpr];
							x86Block.AddOp(new X86MovRegImmOp(gpr, (int)constOp.IntValue));
							valueToGpr[constOp.Result] = gpr;
							nextGpr++;
							break;
						}

					case StandardArithAddIOp addOp: {
							var lhsReg = valueToGpr[addOp.Operands[0]];
							var rhsReg = valueToGpr[addOp.Operands[1]];
							x86Block.AddOp(new X86AddRegRegOp(lhsReg, rhsReg));
							// Result is in lhsReg
							valueToGpr[addOp.Result] = lhsReg;
							break;
						}

					case StandardArithSubIOp subOp: {
							var lhsReg = valueToGpr[subOp.Operands[0]];
							var rhsReg = valueToGpr[subOp.Operands[1]];
							x86Block.AddOp(new X86SubRegRegOp(lhsReg, rhsReg));
							valueToGpr[subOp.Result] = lhsReg;
							break;
						}

					case StandardArithFloatConstantOp floatOp: {
							var label = GetOrCreateFloatLabel(floatOp.FloatValue, outputModule, floatConstants);
							var xmmReg = (X86XmmRegister)nextXmm;
							x86Block.AddOp(new X86MovSdXmmRipRelOp(xmmReg, label));
							valueToXmm[floatOp.Result] = xmmReg;
							nextXmm++;
							break;
						}

					case StandardMemRefAllocaOp:
						break;

					case StandardMemRefStoreOp storeOp: {
							var offset = varOffsets[storeOp.VarName];
							x86Block.AddOp(new X86MovSdMemXmmOp(offset, X86XmmRegister.Xmm0));
							// After storing, xmm0 is free; reset allocation
							nextXmm = 0;
							break;
						}

					case StandardMemRefLoadOp loadOp: {
							var offset = varOffsets[loadOp.VarName];
							x86Block.AddOp(new X86MovSdXmmMemOp(X86XmmRegister.Xmm0, offset));
							valueToXmm[loadOp.Result] = X86XmmRegister.Xmm0;
							nextXmm = 1; // xmm0 is now occupied
							break;
						}

					case StandardArithCmpFOp cmpOp: {
							var lhsReg = valueToXmm.GetValueOrDefault(cmpOp.Operands[0], X86XmmRegister.Xmm0);
							var rhsReg = valueToXmm.GetValueOrDefault(cmpOp.Operands[1], X86XmmRegister.Xmm1);
							x86Block.AddOp(new X86UcomisdOp(lhsReg, rhsReg));
							break;
						}

					case StandardCfCondBrOp condBr: {
							var scopedElse = $"{func.Name}.{condBr.ElseBlock}";
							x86Block.AddOp(new X86JccOp("ne", scopedElse));
							x86Block.AddOp(new X86JccOp("p", scopedElse));
							break;
						}

					case StandardFuncCallOp callOp:
						x86Block.AddOp(new X86CallDirectOp(callOp.Callee));
						break;

					case StandardFuncReturnOp retOp: {
							if (retOp.ReturnValue != null && valueToGpr.TryGetValue(retOp.ReturnValue, out var retReg)) {
								if (retReg != X86Register.Eax) {
									x86Block.AddOp(new X86MovRegRegOp(X86Register.Eax, retReg));
								}
							}
							if (stackSize > 0)
								x86Block.AddOp(new X86AddRegImmOp(X86Register.Rsp, stackSize));
							x86Block.AddOp(new X86PopRegOp(X86Register.Rbp));
							x86Block.AddOp(new X86RetOp());
							break;
						}

					default:
						throw new InvalidOperationException($"No StandardToX86 conversion for: {op.GetType().Name} ({op.Mnemonic})");
				}
			}
		}

		return newFunc;
	}

	private static string GetOrCreateFloatLabel(double value, MlirModule<X86Op> module, Dictionary<double, string> floatConstants) {
		if (!floatConstants.TryGetValue(value, out var label)) {
			label = $"__float_{value.ToString(CultureInfo.InvariantCulture)}";
			floatConstants[value] = label;
			module.RdataEntries.Add((label, BitConverter.GetBytes(value)));
		}
		return label;
	}
}
