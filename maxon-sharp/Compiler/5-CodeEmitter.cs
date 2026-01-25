using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;
using MaxonSharp.Compiler.Mlir.Emit;

namespace MaxonSharp.Compiler;

/// <summary>
/// Result of code emission.
/// </summary>
public record CodeEmitResult(
	byte[] Code,
	byte[] Data
);

/// <summary>
/// Stage 5: Code emission.
/// Converts X86 dialect operations to machine code bytes.
/// </summary>
public class CodeEmitter {
	/// <summary>
	/// Emits machine code from an MLIR module containing X86 dialect operations.
	/// </summary>
	/// <param name="module">The MLIR module with X86 operations</param>
	public static CodeEmitResult Emit(MlirModule module) {
		Logger.Debug(LogCategory.Codegen, "Emitting machine code");

		var emitter = new X86CodeEmitter();

		// Emit globals (define them in the data section)
		foreach (var global in module.Globals) {
			var size = global.Type.SizeInBytes;
			long initValue = 0;
			if (global.InitValue is IntegerAttr intAttr) {
				initValue = intAttr.Value;
			}
			emitter.DefineGlobal(global.Name, size, initValue);
		}

		// Emit main first (entry point must be at start of code section)
		var mainFunc = module.Functions.FirstOrDefault(f => f.Name == "main") ?? throw new CompileError(ErrorCode.CodeEmitterNoMain, "No 'main' function found");
		EmitFunction(emitter, mainFunc);

		// Emit other functions
		foreach (var func in module.Functions.Where(f => f.Name != "main")) {
			EmitFunction(emitter, func);
		}

		emitter.ResolveLabels();

		// Resolve global references
		if (emitter.HasGlobals) {
			var codeSize = (uint)emitter.GetCode().Length;
			var codeSizeVirtual = AlignUp(codeSize, 0x1000);
			var dataRvaOffset = (int)(codeSizeVirtual - codeSize);
			emitter.ResolveGlobals(dataRvaOffset);
		}

		var code = emitter.GetCode();
		var data = emitter.GetData();

		Logger.Debug(LogCategory.Codegen, $"Emitted {code.Length} bytes code, {data.Length} bytes data");

		return new CodeEmitResult(code, data);
	}

	/// <summary>
	/// Emits machine code for a single function.
	/// </summary>
	private static void EmitFunction(X86CodeEmitter emitter, MlirFunction func) {
		emitter.DefineLabel(func.Name);

		foreach (var block in func.Body.Blocks) {
			if (block.Name != "entry") {
				emitter.DefineLabel(block.Name);
			}

			foreach (var op in block.Operations) {
				if (op is X86Op x86Op) {
					emitter.Emit(x86Op);
				}
			}
		}
	}

	private static uint AlignUp(uint value, uint alignment) {
		return (value + alignment - 1) & ~(alignment - 1);
	}
}
