using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler;

public record MlirPipelineResult(MlirModule Module, string? X86Ir);

public class MlirPipeline {
	public MlirPipelineResult Run(MlirModule module, bool returnIr = false, string? dumpStagesBasePath = null) {
		Logger.Debug(LogCategory.Mlir, "Starting MLIR pipeline");

		// Semantic checks
		RunSemanticChecks(module);

		if (dumpStagesBasePath != null) {
			File.WriteAllText($"{dumpStagesBasePath}.1-maxon.mlir", MlirPrinter.Print(module));
		}

		// Maxon dialect -> Standard dialects
		LowerMaxonToStandard(module);
		Logger.Debug(LogCategory.Mlir, "Lowered Maxon to Standard");

		if (dumpStagesBasePath != null) {
			File.WriteAllText($"{dumpStagesBasePath}.2-standard.mlir", MlirPrinter.Print(module));
		}

		// Standard dialects -> X86 dialect
		LowerStandardToX86(module);
		Logger.Debug(LogCategory.Mlir, "Lowered Standard to X86");

		string? x86Ir = null;
		if (returnIr || dumpStagesBasePath != null) {
			x86Ir = MlirPrinter.Print(module);
			if (dumpStagesBasePath != null) {
				File.WriteAllText($"{dumpStagesBasePath}.3-x86.mlir", x86Ir);
			}
		}

		return new MlirPipelineResult(module, x86Ir);
	}

	public static void WriteMlirOutput(MlirModule module, string path) {
		File.WriteAllText(path, MlirPrinter.Print(module));
	}

	// ============================================================================
	// Semantic checks
	// ============================================================================

	private static void RunSemanticChecks(MlirModule module) {
		// E3001: main function must exist
		var mainFunc = module.Functions.FirstOrDefault(f => f.Name == "main");
		if (mainFunc == null) {
			throw new CompileError(ErrorCode.SemanticNoMain, "No 'main' function found");
		}

		// E3002: main must return int
		if (mainFunc.ReturnType == null || mainFunc.ReturnType != MlirType.I64) {
			throw new CompileError(ErrorCode.SemanticMainWrongReturnType, "Function 'main' must return int");
		}
	}

	// ============================================================================
	// Maxon dialect -> Standard dialects
	// ============================================================================

	private static void LowerMaxonToStandard(MlirModule module) {
		foreach (var func in module.Functions) {
			foreach (var block in func.Body.Blocks) {
				var newOps = new List<MlirOperation>();
				foreach (var op in block.Operations) {
					switch (op) {
						case MaxonConstantOp constOp:
							newOps.Add(new ArithConstantOp(constOp.Value, constOp.ResultType));
							break;
						case MaxonReturnOp retOp:
							// Map the return value to the new ArithConstantOp result
							MlirValue? newRetVal = null;
							if (retOp.ReturnValue != null) {
								// Find the ArithConstantOp that replaced the MaxonConstantOp
								var defOp = retOp.ReturnValue.DefiningOp;
								if (defOp is MaxonConstantOp) {
									// Find the replacement ArithConstantOp in newOps
									var replacement = newOps.OfType<ArithConstantOp>().LastOrDefault();
									newRetVal = replacement?.Result;
								} else {
									newRetVal = retOp.ReturnValue;
								}
							}
							newOps.Add(new FuncReturnOp(newRetVal));
							break;
						default:
							newOps.Add(op);
							break;
					}
				}
				block.Operations.Clear();
				block.Operations.AddRange(newOps);
			}
		}
	}

	// ============================================================================
	// Standard dialects -> X86 dialect
	// ============================================================================

	private static void LowerStandardToX86(MlirModule module) {
		foreach (var func in module.Functions) {
			var newBlocks = new List<MlirBlock>();
			var entryBlock = new MlirBlock("entry");
			newBlocks.Add(entryBlock);

			// Function prologue
			entryBlock.AddOp(new X86PushReg(X86Register.Rbp));
			entryBlock.AddOp(new X86MovRegReg(X86Register.Rbp, X86Register.Rsp));

			// Translate standard ops to X86
			foreach (var block in func.Body.Blocks) {
				foreach (var op in block.Operations) {
					switch (op) {
						case ArithConstantOp constOp:
							// Move constant into eax (return value register)
							entryBlock.AddOp(new X86MovRegImm(X86Register.Eax, (int)constOp.IntValue));
							break;
						case FuncReturnOp:
							// Function epilogue + ret
							entryBlock.AddOp(new X86PopReg(X86Register.Rbp));
							entryBlock.AddOp(new X86Ret());
							break;
					}
				}
			}

			// Replace function body
			func.Body.Blocks.Clear();
			func.Body.Blocks.AddRange(newBlocks);
		}
	}
}
