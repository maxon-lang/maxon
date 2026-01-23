namespace MaxonSharp.Hir;

// HIR Types
public abstract record HirType;
public record HirIntType : HirType;
public record HirVoidType : HirType;

// HIR Value (SSA identifier)
public record HirValue(int Id);

// HIR Instructions
public abstract record HirInstr;
public record HirConstInt(HirValue Dest, long Value) : HirInstr;
public record HirRet(HirValue? Value) : HirInstr;

// HIR Block
public record HirBlock(string Label, List<HirInstr> Instructions);

// HIR Function
public record HirFunction(string Name, HirType ReturnType, List<HirBlock> Blocks);

// HIR Module
public record HirModule(List<HirFunction> Functions);
