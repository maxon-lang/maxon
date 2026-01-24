using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Dialects.Arith;

/// <summary>
/// Arith dialect - arithmetic and logical operations.
/// </summary>
public sealed class ArithDialect : DialectBase {
	public override string Name => "arith";

	public override IEnumerable<Type> Operations => [
        // Constants
        typeof(ConstantOp),
        // Integer arithmetic
        typeof(AddIOp),
		typeof(SubIOp),
		typeof(MulIOp),
		typeof(DivSIOp),
		typeof(DivUIOp),
		typeof(RemSIOp),
		typeof(RemUIOp),
        // Bitwise operations
        typeof(AndIOp),
		typeof(OrIOp),
		typeof(XOrIOp),
		typeof(ShLIOp),
		typeof(ShRSIOp),
		typeof(ShRUIOp),
        // Integer comparisons
        typeof(CmpIOp),
        // Floating-point arithmetic
        typeof(AddFOp),
		typeof(SubFOp),
		typeof(MulFOp),
		typeof(DivFOp),
		typeof(RemFOp),
		typeof(NegFOp),
        // Floating-point comparisons
        typeof(CmpFOp),
        // Casts
        typeof(ExtSIOp),
		typeof(ExtUIOp),
		typeof(TruncIOp),
		typeof(SIToFPOp),
		typeof(FPToSIOp),
		typeof(ExtFOp),
		typeof(TruncFOp),
		typeof(IndexCastOp),
        // Select
        typeof(SelectOp),
	];
}
