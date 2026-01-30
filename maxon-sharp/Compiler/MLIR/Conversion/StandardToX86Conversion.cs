using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

public static class StandardToX86Conversion {
	public static void Run(MlirModule module) {
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
					if (op is FuncCallOp callOp) {
						entryBlock.AddOp(new X86CallDirect(callOp.Callee));
						continue;
					}
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
