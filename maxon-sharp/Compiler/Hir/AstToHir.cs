using MaxonSharp.Parser;

namespace MaxonSharp.Hir;

public class AstToHir {
	private int _nextValueId;

	public HirModule Lower(ProgramAst program) {
		var functions = new List<HirFunction>();
		foreach (var func in program.Functions) {
			functions.Add(LowerFunction(func));
		}
		return new HirModule(functions);
	}

	private HirFunction LowerFunction(FunctionDecl func) {
		_nextValueId = 0;

		var returnType = LowerType(func.ReturnType);
		var instructions = new List<HirInstr>();

		foreach (var stmt in func.Body) {
			LowerStatement(stmt, instructions);
		}

		var entryBlock = new HirBlock("entry", instructions);
		return new HirFunction(func.Name, returnType, [entryBlock]);
	}

	private void LowerStatement(Stmt stmt, List<HirInstr> instructions) {
		switch (stmt) {
			case ReturnStmt ret:
				HirValue? value = null;
				if (ret.Value != null) {
					value = LowerExpression(ret.Value, instructions);
				}
				instructions.Add(new HirRet(value));
				break;
		}
	}

	private HirValue LowerExpression(Expr expr, List<HirInstr> instructions) {
		switch (expr) {
			case IntLiteralExpr intLit:
				var dest = new HirValue(_nextValueId++);
				instructions.Add(new HirConstInt(dest, intLit.Value));
				return dest;

			default:
				throw new Exception($"Unsupported expression type: {expr.GetType().Name}");
		}
	}

	private static HirType LowerType(TypeRef? typeRef) {
		return typeRef switch {
			IntTypeRef => new HirIntType(),
			null => new HirVoidType(),
			_ => throw new Exception($"Unsupported type: {typeRef.GetType().Name}")
		};
	}
}
