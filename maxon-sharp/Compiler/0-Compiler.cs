namespace MaxonSharp.Compiler;

/// <summary>
/// Result of compiling source code to IR.
/// </summary>
public record CompileToIrResult(
	string? Hir,
	string? Lir,
	bool Success,
	string? Error
);

public class Compiler {
	/// <summary>
	/// Compile source code and return HIR and LIR as strings.
	/// </summary>
	public static CompileToIrResult CompileToIr(string source) {
		try {
			// Stage 1: Lexing
			var lexer = new Lexer(source);
			var tokens = lexer.Tokenize();

			// Stage 2: Parsing
			var parser = new Parser(tokens);
			var ast = parser.Parse();

			// Stage 3: Semantic analysis
			var semanticAnalyzer = new SemanticAnalyzer();
			if (!semanticAnalyzer.Analyze(ast)) {
				return new CompileToIrResult(null, null, false, "Semantic analysis failed");
			}

			// Stage 4: AST to HIR
			var astToHir = new AstToHir();
			var hirModule = astToHir.Lower(ast, semanticAnalyzer.MutationAnalyzer);

			// Write HIR to string
			var hirWriter = new StringWriter();
			WriteHir(hirWriter, hirModule);
			var hirString = hirWriter.ToString();

			// Stage 5: HIR to LIR
			var hirToLir = new HirToLir();
			var lirModule = hirToLir.Lower(hirModule);

			// Write LIR to string
			var lirWriter = new StringWriter();
			WriteLir(lirWriter, lirModule);
			var lirString = lirWriter.ToString();

			return new CompileToIrResult(hirString, lirString, true, null);
		} catch (CompileError ex) {
			return new CompileToIrResult(null, null, false, ex.Format());
		} catch (Exception ex) {
			return new CompileToIrResult(null, null, false, ex.Message);
		}
	}

	public static bool Compile(string source, string outputPath, string? hirOutputPath = null, string? lirOutputPath = null) {
		try {
			Logger.Info(LogCategory.Compiler, "Starting compilation");

			// Stage 1: Lexing
			Logger.Info(LogCategory.Compiler, "Stage 1: Lexing");
			var lexer = new Lexer(source);
			var tokens = lexer.Tokenize();

			// Stage 2: Parsing
			Logger.Info(LogCategory.Compiler, "Stage 2: Parsing");
			var parser = new Parser(tokens);
			var ast = parser.Parse();

			// Stage 3: Semantic analysis (includes mutation analysis)
			Logger.Info(LogCategory.Compiler, "Stage 3: Semantic analysis");
			var semanticAnalyzer = new SemanticAnalyzer();
			if (!semanticAnalyzer.Analyze(ast)) {
				Console.Error.WriteLine("Semantic analysis failed");
				return false;
			}

			// Stage 4: AST to HIR (with ownership tracking)
			Logger.Info(LogCategory.Compiler, "Stage 4: AST to HIR");
			var astToHir = new AstToHir();
			var hirModule = astToHir.Lower(ast, semanticAnalyzer.MutationAnalyzer);

			if (hirOutputPath != null) {
				WriteHirFile(hirOutputPath, hirModule);
			}

			// Stage 5: HIR to LIR
			Logger.Info(LogCategory.Compiler, "Stage 5: HIR to LIR");
			var hirToLir = new HirToLir();
			var lirModule = hirToLir.Lower(hirModule);

			if (lirOutputPath != null) {
				WriteLirFile(lirOutputPath, lirModule);
			}

			// Stage 6: Code generation
			Logger.Info(LogCategory.Compiler, "Stage 6: Code generation");
			var codeGen = new CodeGen();
			var code = codeGen.Generate(lirModule);

			// Stage 7: PE Writer
			Logger.Info(LogCategory.Compiler, "Stage 7: PE Writer");
			var peWriter = new PeWriter();
			PeWriter.Write(outputPath, code);

			Logger.Info(LogCategory.Compiler, "Compilation complete");
			Console.WriteLine($"Successfully compiled to {outputPath}");
			return true;
		} catch (CompileError ex) {
			Console.Error.WriteLine(ex.Format());
			return false;
		} catch (Exception ex) {
			Console.Error.WriteLine($"Compilation error: {ex.Message}");
			return false;
		}
	}

	private static void WriteHirFile(string path, HirModule module) {
		using var writer = new StreamWriter(path);
		WriteHir(writer, module);
		Console.WriteLine($"Wrote {path}");
	}

	private static void WriteLirFile(string path, LirModule module) {
		using var writer = new StreamWriter(path);
		WriteLir(writer, module);
		Console.WriteLine($"Wrote {path}");
	}

	private static void WriteHir(TextWriter w, HirModule module) {
		foreach (var s in module.Structs) {
			w.WriteLine($"struct {s.Name} {{");
			foreach (var f in s.Fields) {
				w.WriteLine($"  {f.FieldName}: {f.Type.Name} (offset {f.Offset})");
			}
			w.WriteLine("}");
		}

		foreach (var e in module.Enums) {
			w.WriteLine($"enum {e.Name} {{");
			foreach (var v in e.Variants) {
				var payload = v.PayloadFields.Count > 0
					? $"({string.Join(", ", v.PayloadFields.Select(f => $"{f.FieldName}: {f.Type.Name}"))})"
					: "";
				w.WriteLine($"  {v.Name}{payload} = {v.Tag}");
			}
			w.WriteLine("}");
		}

		foreach (var func in module.Functions) {
			var export = func.IsExport ? "export " : "";
			var paramStr = string.Join(", ", func.Params.Select(p => $"{p.Name}: {p.Type.Name}"));
			w.WriteLine($"\n{export}fn {func.Name}({paramStr}) -> {func.ReturnType.Name} {{");

			foreach (var block in func.Blocks) {
				w.WriteLine($"{block.Label}:");
				foreach (var instr in block.Instructions) {
					w.WriteLine($"  {FormatHirInstr(instr)}");
				}
			}
			w.WriteLine("}");
		}
	}

	private static string FormatHirInstr(HirInstr instr) {
		return instr switch {
			HirConstInt c => $"{c.Dest} = const {c.Value}",
			HirConstFloat c => $"{c.Dest} = const {c.Value}f",
			HirConstBool c => $"{c.Dest} = const {c.Value}",
			HirAlloca a => $"{a.Dest} = alloca {a.Type.Name}",
			HirLoad l => $"{l.Dest} = load {l.Ptr}",
			HirStore s => $"store {s.Ptr}, {s.Value} : {s.Type.Name}",
			HirMemcpy m => $"memcpy {m.Dest}, {m.Src} ({m.Size})",
			HirGetFieldPtr g => $"{g.Dest} = getfieldptr {g.Base}, .{g.FieldName} (+{g.Offset})",
			HirAdd a => $"{a.Dest} = add {a.Left}, {a.Right}",
			HirSub s => $"{s.Dest} = sub {s.Left}, {s.Right}",
			HirMul m => $"{m.Dest} = mul {m.Left}, {m.Right}",
			HirDiv d => $"{d.Dest} = div {d.Left}, {d.Right}",
			HirMod m => $"{m.Dest} = mod {m.Left}, {m.Right}",
			HirNeg n => $"{n.Dest} = neg {n.Operand}",
			HirNot n => $"{n.Dest} = not {n.Operand}",
			HirCmpEq c => $"{c.Dest} = eq {c.Left}, {c.Right}",
			HirCmpNe c => $"{c.Dest} = ne {c.Left}, {c.Right}",
			HirCmpLt c => $"{c.Dest} = lt {c.Left}, {c.Right}",
			HirCmpLe c => $"{c.Dest} = le {c.Left}, {c.Right}",
			HirCmpGt c => $"{c.Dest} = gt {c.Left}, {c.Right}",
			HirCmpGe c => $"{c.Dest} = ge {c.Left}, {c.Right}",
			HirBr b => $"br {b.Label}",
			HirBrCond b => $"brcond {b.Cond}, {b.TrueLabel}, {b.FalseLabel}",
			HirRet r => r.Value != null ? $"ret {r.Value}" : "ret",
			HirCall c => c.Dest != null
				? $"{c.Dest} = call {c.FuncName}({string.Join(", ", c.Args)})"
				: $"call {c.FuncName}({string.Join(", ", c.Args)})",
			HirParam p => $"{p.Dest} = param {p.Index}",
			HirLabel l => $"{l.Name}:",
			_ => instr.ToString() ?? "???"
		};
	}

	private static void WriteLir(TextWriter w, LirModule module) {
		foreach (var func in module.Functions) {
			var export = func.IsExport ? "export " : "";
			var paramStr = string.Join(", ", func.Params.Select(p => $"{p.Name}: {p.Type}"));
			var retType = func.ReturnType?.ToString() ?? "void";
			w.WriteLine($"\n{export}fn {func.Name}({paramStr}) -> {retType} [stack={func.StackSize}] {{");

			foreach (var block in func.Blocks) {
				w.WriteLine($"{block.Label}:");
				foreach (var instr in block.Instructions) {
					w.WriteLine($"  {FormatLirInstr(instr)}");
				}
			}
			w.WriteLine("}");
		}
	}

	private static string FormatLirInstr(LirInstr instr) {
		return instr switch {
			LirMov m => $"{m.Dest} = mov {m.Src}",
			LirLoad l => $"{l.Dest} = load {l.Ptr} ({l.Size})",
			LirStore s => $"store {s.Ptr}, {s.Value} ({s.Size})",
			LirMemcpy m => $"memcpy {m.Dest}, {m.Src} ({m.Size})",
			LirLea l => $"{l.Dest} = lea {l.Addr}",
			LirAdd a => $"{a.Dest} = add {a.Left}, {a.Right}",
			LirSub s => $"{s.Dest} = sub {s.Left}, {s.Right}",
			LirIMul m => $"{m.Dest} = imul {m.Left}, {m.Right}",
			LirIDiv d => $"{d.Dest} = idiv {d.Left}, {d.Right}",
			LirMod m => $"{m.Dest} = mod {m.Left}, {m.Right}",
			LirNeg n => $"{n.Dest} = neg {n.Src}",
			LirAnd a => $"{a.Dest} = and {a.Left}, {a.Right}",
			LirOr o => $"{o.Dest} = or {o.Left}, {o.Right}",
			LirXor x => $"{x.Dest} = xor {x.Left}, {x.Right}",
			LirNot n => $"{n.Dest} = not {n.Src}",
			LirShl s => $"{s.Dest} = shl {s.Left}, {s.Right}",
			LirShr s => $"{s.Dest} = shr {s.Left}, {s.Right}",
			LirCmp c => $"cmp {c.Left}, {c.Right}",
			LirSetCC s => $"{s.Dest} = set{s.Cond}",
			LirJmp j => $"jmp {j.Label}",
			LirJmpCC j => $"j{j.Cond} {j.TrueLabel}, {j.FalseLabel}",
			LirRet r => r.Value != null ? $"ret {r.Value}" : "ret",
			LirCall c => c.Dest != null
				? $"{c.Dest} = call {c.FuncName}({string.Join(", ", c.Args)})"
				: $"call {c.FuncName}({string.Join(", ", c.Args)})",
			LirAddressOf a => $"{a.Dest} = addressof {a.Slot}",
			_ => instr.ToString() ?? "???"
		};
	}
}
