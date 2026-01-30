using System.Text;

namespace MaxonSharp.Compiler.Mlir.Core;

public static class MlirPrinter {
	public static string Print(MlirModule module) {
		var sb = new StringBuilder();
		sb.AppendLine("module {");
		foreach (var func in module.Functions) {
			PrintFunction(sb, func, "  ");
		}
		sb.AppendLine("}");
		return sb.ToString();
	}

	private static void PrintFunction(StringBuilder sb, MlirFunction func, string indent) {
		sb.Append($"{indent}func @{func.Name}(");
		for (int i = 0; i < func.ParamTypes.Count; i++) {
			if (i > 0) sb.Append(", ");
			sb.Append(func.ParamTypes[i]);
		}
		sb.Append(')');
		if (func.ReturnType != null && func.ReturnType != MlirType.Void) {
			sb.Append($" -> {func.ReturnType}");
		}
		sb.AppendLine(" {");

		foreach (var block in func.Body.Blocks) {
			sb.AppendLine($"{indent}{block.Name}:");
			foreach (var op in block.Operations) {
				sb.Append($"{indent}  ");
				PrintOp(sb, op);
				sb.AppendLine();
			}
		}

		sb.AppendLine($"{indent}}}");
	}

	private static void PrintOp(StringBuilder sb, MlirOperation op) {
		if (op.Results.Count > 0) {
			sb.Append(string.Join(", ", op.Results));
			sb.Append(" = ");
		}
		sb.Append(op.Mnemonic);
		if (op.Operands.Count > 0) {
			sb.Append(' ');
			sb.Append(string.Join(", ", op.Operands));
		}
		foreach (var (key, attr) in op.Attributes) {
			sb.Append($" {{{key} = {attr}}}");
		}
	}
}
