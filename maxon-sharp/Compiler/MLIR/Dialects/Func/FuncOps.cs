using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects.Builtin;

namespace MaxonSharp.Compiler.Mlir.Dialects.Func;

/// <summary>
/// Base class for func operations.
/// </summary>
public abstract class FuncOpBase : MlirOperation {
	public override string Dialect => "func";
}

// ============================================================================
// Function Definition
// ============================================================================

/// <summary>
/// Function definition: func.func @name(%args) -> type { body }
/// </summary>
public sealed class FuncOp : FuncOpBase {
	public override string Mnemonic => "func";
	public override bool HasSideEffects => false;

	public string Name { get; }
	public FunctionType FunctionType { get; }
	public MlirRegion Body { get; }
	public bool IsPublic { get; set; }
	public bool IsDeclaration => Body.IsEmpty;

	public FuncOp(string name, FunctionType functionType) {
		Name = name;
		FunctionType = functionType;
		Body = new MlirRegion { ParentOp = this };
		Regions.Add(Body);
		Attributes["sym_name"] = new StringAttr(name);
		Attributes["function_type"] = new TypeAttr(functionType);
	}

	/// <summary>
	/// Creates the entry block with parameters.
	/// </summary>
	public MlirBlock CreateEntryBlock() {
		var block = Body.CreateBlock("entry");
		foreach (var paramType in FunctionType.Inputs) {
			block.AddArgument(paramType);
		}
		return block;
	}

	/// <summary>
	/// Gets the entry block.
	/// </summary>
	public MlirBlock? EntryBlock => Body.EntryBlock;

	/// <summary>
	/// Gets parameter values from the entry block.
	/// </summary>
	public IReadOnlyList<MlirValue> GetArguments() =>
		EntryBlock?.Arguments.Select(a => a.Value).ToList() ?? [];

	public override void Print(MlirPrinter printer) {
		var visibility = IsPublic ? "public " : "";

		// Build parameter string from entry block arguments
		var paramStrs = new List<string>();
		if (EntryBlock is not null) {
			for (int i = 0; i < EntryBlock.Arguments.Count; i++) {
				var arg = EntryBlock.Arguments[i];
				paramStrs.Add($"{arg.Value}: {arg.Value.Type}");
			}
		} else {
			// For declarations, use function type
			for (int i = 0; i < FunctionType.Inputs.Count; i++) {
				paramStrs.Add($"%arg{i}: {FunctionType.Inputs[i]}");
			}
		}

		var resultStr = FunctionType.Results.Count switch {
			0 => "",
			1 => $" -> {FunctionType.Results[0]}",
			_ => $" -> ({string.Join(", ", FunctionType.Results)})"
		};

		printer.Print($"{visibility}func.func @{Name}({string.Join(", ", paramStrs)}){resultStr}");

		if (IsDeclaration) {
			printer.PrintLine();
		} else {
			printer.PrintLine(" {");
			foreach (var block in Body.Blocks) {
				block.Print(printer);
			}
			printer.PrintLine("}");
		}
	}
}

// ============================================================================
// Function Call
// ============================================================================

/// <summary>
/// Function call: %result = func.call @name(%args) : (types) -> type
/// </summary>
public sealed class CallOp : FuncOpBase {
	public override string Mnemonic => "call";
	public override bool HasSideEffects => true;

	public string Callee { get; }
	public MlirValue? Result => Results.Count > 0 ? Results[0] : null;

	public CallOp(string callee, IEnumerable<MlirValue> args, MlirType? returnType = null) {
		Callee = callee;
		foreach (var arg in args)
			Operands.Add(arg);
		Attributes["callee"] = new SymbolRefAttr(callee);
		if (returnType is not null && returnType is not NoneType)
			CreateResult(returnType);
	}

	public override void Print(MlirPrinter printer) {
		var argsStr = string.Join(", ", Operands);
		var resultStr = Result is not null ? $"{Result} = " : "";
		var typeStr = BuildTypeString();
		printer.PrintLine($"{resultStr}func.call @{Callee}({argsStr}){typeStr}");
	}

	private string BuildTypeString() {
		var inputTypes = Operands.Select(o => o.Type.ToString());
		var resultType = Result?.Type.ToString() ?? "()";
		return $" : ({string.Join(", ", inputTypes)}) -> {resultType}";
	}
}

// ============================================================================
// Return
// ============================================================================

/// <summary>
/// Function return: func.return %values...
/// </summary>
public sealed class ReturnOp : FuncOpBase {
	public override string Mnemonic => "return";
	public override bool IsTerminator => true;
	public override bool HasSideEffects => true;

	public IReadOnlyList<MlirValue> ReturnValues => Operands;

	public ReturnOp(params MlirValue[] values) {
		foreach (var v in values)
			Operands.Add(v);
	}

	public override void Print(MlirPrinter printer) {
		if (Operands.Count == 0) {
			printer.PrintLine("func.return");
		} else if (Operands.Count == 1) {
			printer.PrintLine($"func.return {Operands[0]} : {Operands[0].Type}");
		} else {
			var valuesStr = string.Join(", ", Operands);
			var typesStr = string.Join(", ", Operands.Select(o => o.Type));
			printer.PrintLine($"func.return {valuesStr} : {typesStr}");
		}
	}
}

// ============================================================================
// Indirect Call
// ============================================================================

/// <summary>
/// Indirect function call: %result = func.call_indirect %callee(%args) : type
/// </summary>
public sealed class CallIndirectOp : FuncOpBase {
	public override string Mnemonic => "call_indirect";
	public override bool HasSideEffects => true;

	public MlirValue Callee => Operands[0];
	public IReadOnlyList<MlirValue> Arguments => [.. Operands.Skip(1)];
	public MlirValue? Result => Results.Count > 0 ? Results[0] : null;

	public CallIndirectOp(MlirValue callee, IEnumerable<MlirValue> args, MlirType? returnType = null) {
		Operands.Add(callee);
		foreach (var arg in args)
			Operands.Add(arg);
		if (returnType is not null && returnType is not NoneType)
			CreateResult(returnType);
	}

	public override void Print(MlirPrinter printer) {
		var argsStr = string.Join(", ", Arguments);
		var resultStr = Result is not null ? $"{Result} = " : "";
		printer.PrintLine($"{resultStr}func.call_indirect {Callee}({argsStr}) : {Callee.Type}");
	}
}
