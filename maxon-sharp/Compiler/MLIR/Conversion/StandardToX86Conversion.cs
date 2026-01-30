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

		// Pre-scan: compute last use index for each StdValue (for dead-value freeing)
		var lastUseOfValue = new Dictionary<StdValue, int>();
		int scanIdx = 0;
		foreach (var block in func.Body.Blocks) {
			foreach (var op in block.Operations) {
				foreach (var val in GetReadValues(op)) {
					lastUseOfValue[val] = scanIdx;
				}
				scanIdx++;
			}
		}

		// Track float constants for rdata deduplication
		var floatConstants = new Dictionary<double, string>();

		var regManager = new RegisterManager();
		var sourceBlocks = func.Body.Blocks.ToList();
		int currentOpIndex = 0;

		for (int blockIdx = 0; blockIdx < sourceBlocks.Count; blockIdx++) {
			var srcBlock = sourceBlocks[blockIdx];
			var x86Block = newFunc.Body.AddBlock(srcBlock.Name);

			regManager.Reset();

			// Prologue only in entry block
			if (blockIdx == 0) {
				x86Block.AddOp(new X86PushRegOp(X86Register.Rbp));
				x86Block.AddOp(new X86MovRegRegOp(X86Register.Rbp, X86Register.Rsp));
				if (stackSize > 0)
					x86Block.AddOp(new X86SubRegImmOp(X86Register.Rsp, stackSize));
			}

			foreach (var op in srcBlock.Operations) {
				switch (op) {
					case StdConstI64Op constOp:
						regManager.EmitLoadImmediate(constOp.Result, (int)constOp.Value, x86Block);
						break;

					case StdAddI64Op addOp:
						regManager.EmitBinaryRegReg(addOp.Lhs, addOp.Rhs, addOp.Result, x86Block,
							(l, r) => new X86AddRegRegOp(l, r));
						break;

					case StdSubI64Op subOp:
						regManager.EmitBinaryRegReg(subOp.Lhs, subOp.Rhs, subOp.Result, x86Block,
							(l, r) => new X86SubRegRegOp(l, r));
						break;

					case StdMulI64Op mulOp:
						regManager.EmitMultiply(mulOp.Lhs, mulOp.Rhs, mulOp.Result, x86Block);
						break;

					case StdDivI64Op divOp:
						regManager.EmitDivision(divOp.Lhs, divOp.Rhs, divOp.Result, x86Block);
						break;

					case StdRemI64Op remOp:
						regManager.EmitRemainder(remOp.Lhs, remOp.Rhs, remOp.Result, x86Block);
						break;

					case StdConstF64Op floatOp: {
							var label = GetOrCreateFloatLabel(floatOp.Value, outputModule, floatConstants);
							regManager.EmitXmmLoadFromRipRelative(floatOp.Result, label, x86Block);
							break;
						}

					case StdStoreI64Op storeOp:
						regManager.EmitStoreToStack(storeOp.Value, varOffsets[storeOp.VarName], x86Block);
						break;

					case StdStoreF64Op storeOp:
						regManager.EmitXmmStoreToStack(storeOp.Value, varOffsets[storeOp.VarName], x86Block);
						break;

					case StdLoadI64Op loadOp:
						regManager.EmitLoadFromStack(loadOp.Result, varOffsets[loadOp.VarName], x86Block);
						break;

					case StdLoadF64Op loadOp:
						regManager.EmitXmmLoadFromStack(loadOp.Result, varOffsets[loadOp.VarName], x86Block);
						break;

					case StdCmpF64Op cmpOp:
						regManager.EmitXmmCompare(cmpOp.Lhs, cmpOp.Rhs, x86Block);
						break;

					case StdCondBrOp condBr: {
							var scopedElse = $"{func.Name}.{condBr.ElseBlock}";
							x86Block.AddOp(new X86JccOp("ne", scopedElse));
							x86Block.AddOp(new X86JccOp("p", scopedElse));
							break;
						}

					case StdCallOp callOp:
						regManager.EmitCall(callOp.Callee, callOp.Result, x86Block);
						break;

					case StdReturnOp retOp: {
							if (retOp.ReturnValue != null)
								regManager.EnsureInSpecificRegister(retOp.ReturnValue, X86Register.Eax, x86Block);
							if (stackSize > 0)
								x86Block.AddOp(new X86AddRegImmOp(X86Register.Rsp, stackSize));
							x86Block.AddOp(new X86PopRegOp(X86Register.Rbp));
							x86Block.AddOp(new X86RetOp());
							break;
						}

					default:
						throw new InvalidOperationException($"No StandardToX86 conversion for: {op.GetType().Name} ({op.Mnemonic})");
				}

				// Free registers for values whose last use was this op
				FreeDeadValues(regManager, lastUseOfValue, currentOpIndex, GetReadValues(op));
				regManager.AdvanceOp();
				currentOpIndex++;
			}
		}

		return newFunc;
	}

	private static List<StdValue> GetReadValues(StandardOp op) => op switch {
		StdStoreI64Op s => [s.Value],
		StdStoreF64Op s => [s.Value],
		StdAddI64Op a => [a.Lhs, a.Rhs],
		StdAddI32Op a => [a.Lhs, a.Rhs],
		StdSubI64Op s => [s.Lhs, s.Rhs],
		StdSubI32Op s => [s.Lhs, s.Rhs],
		StdMulI64Op m => [m.Lhs, m.Rhs],
		StdDivI64Op d => [d.Lhs, d.Rhs],
		StdRemI64Op r => [r.Lhs, r.Rhs],
		StdCmpF64Op c => [c.Lhs, c.Rhs],
		StdReturnOp r when r.ReturnValue != null => [r.ReturnValue],
		StdCallOp c => c.Args,
		_ => []
	};

	private static void FreeDeadValues(
		RegisterManager regManager,
		Dictionary<StdValue, int> lastUseOfValue,
		int currentOpIndex,
		IEnumerable<StdValue> readValues) {
		foreach (var val in readValues) {
			if (lastUseOfValue.TryGetValue(val, out var lastUse) && lastUse == currentOpIndex) {
				regManager.NoteValueDead(val);
			}
		}
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
