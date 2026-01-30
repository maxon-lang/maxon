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

		// Track which StdValue maps to which XMM register
		var valueToXmm = new Dictionary<StdValue, X86XmmRegister>();
		int nextXmm = 0;

		var regManager = new RegisterManager();
		var sourceBlocks = func.Body.Blocks.ToList();
		int currentOpIndex = 0;

		for (int blockIdx = 0; blockIdx < sourceBlocks.Count; blockIdx++) {
			var srcBlock = sourceBlocks[blockIdx];
			var x86Block = newFunc.Body.AddBlock(srcBlock.Name);

			regManager.Reset();
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
							var gpr = regManager.AllocateRegister(constOp.Result, x86Block);
							x86Block.AddOp(new X86MovRegImmOp(gpr, (int)constOp.Value));
							break;
						}

					case StdAddI64Op addOp: {
							var lhsReg = regManager.EnsureInRegister(addOp.Lhs, x86Block);
							var rhsReg = regManager.EnsureInRegister(addOp.Rhs, x86Block);
							x86Block.AddOp(new X86AddRegRegOp(lhsReg, rhsReg));
							regManager.TransferValue(addOp.Lhs, lhsReg, addOp.Result);
							break;
						}

					case StdSubI64Op subOp: {
							var lhsReg = regManager.EnsureInRegister(subOp.Lhs, x86Block);
							var rhsReg = regManager.EnsureInRegister(subOp.Rhs, x86Block);
							x86Block.AddOp(new X86SubRegRegOp(lhsReg, rhsReg));
							regManager.TransferValue(subOp.Lhs, lhsReg, subOp.Result);
							break;
						}

					case StdRemI64Op remOp: {
							// IDIV: RDX:RAX / divisor → quotient in RAX, remainder in RDX
							var lhsReg = regManager.EnsureInRegister(remOp.Lhs, x86Block);
							var rhsReg = regManager.EnsureInRegister(remOp.Rhs, x86Block);

							// Divisor must not be in RAX or RDX (IDIV clobbers both).
							// Move it out before we set up RAX.
							if (rhsReg == X86Register.Eax || rhsReg == X86Register.Edx) {
								var safeReg = lhsReg != X86Register.Ecx ? X86Register.Ecx : X86Register.Ebx;
								x86Block.AddOp(new X86MovRegRegOp(safeReg, rhsReg));
								rhsReg = safeReg;
							}

							// Move dividend to EAX
							if (lhsReg != X86Register.Eax) {
								x86Block.AddOp(new X86MovRegRegOp(X86Register.Eax, lhsReg));
							}

							// Sign-extend RAX into RDX:RAX
							x86Block.AddOp(new X86CqoOp());
							x86Block.AddOp(new X86IdivRegOp(rhsReg));

							// Remainder is in EDX
							regManager.NoteValueInRegister(remOp.Result, X86Register.Edx);
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
							var srcReg = regManager.EnsureInRegister(storeOp.Value, x86Block);
							x86Block.AddOp(new X86MovMemRegOp(offset, srcReg));
							regManager.NoteStoreToStack(storeOp.Value, offset);
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
							var gpr = regManager.AllocateRegister(loadOp.Result, x86Block);
							x86Block.AddOp(new X86MovRegMemOp(gpr, offset));
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
						if (callOp.Result != null) {
							regManager.NoteValueInRegister(callOp.Result, X86Register.Eax);
						}
						break;

					case StdReturnOp retOp: {
							if (retOp.ReturnValue != null) {
								var retReg = regManager.EnsureInRegister(retOp.ReturnValue, x86Block);
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
