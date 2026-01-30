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

		// Pre-scan: calculate stack frame from store ops
		var varOffsets = new Dictionary<string, int>();
		int stackSize = 0;
		foreach (var block in func.Body.Blocks) {
			foreach (var op in block.Operations) {
				var varName = op switch {
					StdStoreI64Op s => s.VarName,
					StdStoreF64Op s => s.VarName,
					_ => null
				};
				if (varName != null && !varOffsets.ContainsKey(varName)) {
					int size = op is StdStoreF64Op ? 8 : 8;
					stackSize += size;
					varOffsets[varName] = -stackSize;
				}
			}
		}
		// Align to 16 bytes
		if (stackSize > 0)
			stackSize = (stackSize + 15) & ~15;

		// Track float constants for rdata deduplication
		var floatConstants = new Dictionary<double, string>();

		// Track which StdValue maps to which XMM register
		var valueToXmm = new Dictionary<StdValue, X86XmmRegister>();
		int nextXmm = 0;

		// Track which StdValue maps to which GPR
		var valueToGpr = new Dictionary<StdValue, X86Register>();
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
					case StdConstI64Op constOp: {
							var gpr = gprPool[nextGpr];
							x86Block.AddOp(new X86MovRegImmOp(gpr, (int)constOp.Value));
							valueToGpr[constOp.Result] = gpr;
							nextGpr++;
							break;
						}

					case StdAddI64Op addOp: {
							var lhsReg = valueToGpr[addOp.Lhs];
							var rhsReg = valueToGpr[addOp.Rhs];
							x86Block.AddOp(new X86AddRegRegOp(lhsReg, rhsReg));
							valueToGpr[addOp.Result] = lhsReg;
							break;
						}

					case StdSubI64Op subOp: {
							var lhsReg = valueToGpr[subOp.Lhs];
							var rhsReg = valueToGpr[subOp.Rhs];
							x86Block.AddOp(new X86SubRegRegOp(lhsReg, rhsReg));
							valueToGpr[subOp.Result] = lhsReg;
							break;
						}

					case StdConstF64Op floatOp: {
							var label = GetOrCreateFloatLabel(floatOp.Value, outputModule, floatConstants);
							var xmmReg = (X86XmmRegister)nextXmm;
							x86Block.AddOp(new X86MovSdXmmRipRelOp(xmmReg, label));
							valueToXmm[floatOp.Result] = xmmReg;
							nextXmm++;
							break;
						}

					case StdStoreI64Op storeOp: {
							var offset = varOffsets[storeOp.VarName];
							var srcReg = valueToGpr[storeOp.Value];
							x86Block.AddOp(new X86MovMemRegOp(offset, srcReg));
							break;
						}

					case StdStoreF64Op storeOp: {
							var offset = varOffsets[storeOp.VarName];
							var srcXmm = valueToXmm[storeOp.Value];
							x86Block.AddOp(new X86MovSdMemXmmOp(offset, srcXmm));
							break;
						}

					case StdLoadI64Op loadOp: {
							var offset = varOffsets[loadOp.VarName];
							var gpr = gprPool[nextGpr];
							x86Block.AddOp(new X86MovRegMemOp(gpr, offset));
							valueToGpr[loadOp.Result] = gpr;
							nextGpr++;
							break;
						}

					case StdLoadF64Op loadOp: {
							var offset = varOffsets[loadOp.VarName];
							var xmmReg = (X86XmmRegister)nextXmm;
							x86Block.AddOp(new X86MovSdXmmMemOp(xmmReg, offset));
							valueToXmm[loadOp.Result] = xmmReg;
							nextXmm++;
							break;
						}

					case StdCmpF64Op cmpOp: {
							var lhsReg = valueToXmm.GetValueOrDefault(cmpOp.Lhs, X86XmmRegister.Xmm0);
							var rhsReg = valueToXmm.GetValueOrDefault(cmpOp.Rhs, X86XmmRegister.Xmm1);
							x86Block.AddOp(new X86UcomisdOp(lhsReg, rhsReg));
							break;
						}

					case StdCondBrOp condBr: {
							var scopedElse = $"{func.Name}.{condBr.ElseBlock}";
							x86Block.AddOp(new X86JccOp("ne", scopedElse));
							x86Block.AddOp(new X86JccOp("p", scopedElse));
							break;
						}

					case StdCallOp callOp:
						x86Block.AddOp(new X86CallDirectOp(callOp.Callee));
						break;

					case StdReturnOp retOp: {
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
