using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Dialects.X86;

/// <summary>
/// X86 dialect - target-specific machine operations.
/// </summary>
public sealed class X86Dialect : DialectBase {
	public override string Name => "x86";

	public override IEnumerable<Type> Types => [
		typeof(X86RegisterType),
		typeof(X86StackSlotType),
	];

	public override IEnumerable<Type> Operations => [
        // Data movement
        typeof(MovOp),
		typeof(LeaOp),
		typeof(PushOp),
		typeof(PopOp),
        // Integer arithmetic
        typeof(AddOp),
		typeof(SubOp),
		typeof(ImulOp),
		typeof(IdivOp),
		typeof(NegOp),
		typeof(IncOp),
		typeof(DecOp),
        // Bitwise operations
        typeof(AndOp),
		typeof(OrOp),
		typeof(XorOp),
		typeof(NotOp),
		typeof(ShlOp),
		typeof(ShrOp),
		typeof(SarOp),
        // Floating point
        typeof(MovsdOp),
		typeof(AddsdOp),
		typeof(SubsdOp),
		typeof(MulsdOp),
		typeof(DivsdOp),
        // Comparison and flags
        typeof(CmpOp),
		typeof(TestOp),
		typeof(SetccOp),
		typeof(ComiOp),
        // Control flow
        typeof(JmpOp),
		typeof(JccOp),
		typeof(CallOp),
		typeof(RetOp),
		typeof(LabelOp),
        // Conversions
        typeof(CvtsiOp),
		typeof(CvttsdOp),
		typeof(CdqOp),
	];
}
