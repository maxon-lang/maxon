using MaxonSharp.Lexer;
using MaxonSharp.Parser;
using MaxonSharp.Semantic;
using MaxonSharp.Hir;
using MaxonSharp.Lir;
using MaxonSharp.Codegen;
using MaxonSharp.Pe;

namespace MaxonSharp;

public class Compiler
{
    public bool Compile(string source, string outputPath)
    {
        try
        {
            // Stage 1: Lexing
            var lexer = new Lexer.Lexer(source);
            var tokens = lexer.Tokenize();

            // Stage 2: Parsing
            var parser = new Parser.Parser(tokens);
            var ast = parser.Parse();

            // Stage 3: Semantic analysis
            var analyzer = new SemanticAnalyzer();
            if (!analyzer.Analyze(ast))
            {
                Console.Error.WriteLine("Semantic analysis failed");
                return false;
            }

            // Stage 4: AST to HIR
            var astToHir = new AstToHir();
            var hirModule = astToHir.Lower(ast);

            // Stage 5: HIR to LIR
            var hirToLir = new HirToLir();
            var lirModule = hirToLir.Lower(hirModule);

            // Stage 6: Code generation
            var codeGen = new CodeGenerator();
            var code = codeGen.Generate(lirModule);

            // Stage 7: PE Writer
            var peWriter = new PeWriter();
            peWriter.Write(outputPath, code);

            Console.WriteLine($"Successfully compiled to {outputPath}");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Compilation error: {ex.Message}");
            return false;
        }
    }
}
