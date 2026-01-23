namespace MaxonSharp.Lir;

// LIR Types (machine types)
public enum LirType
{
    I64
}

// LIR Values
public abstract record LirValue;
public record LirVReg(int Id) : LirValue;
public record LirImmediate(long Value) : LirValue;

// LIR Instructions
public abstract record LirInstr;
public record LirMov(LirVReg Dest, LirValue Src) : LirInstr;
public record LirRet(LirValue? Value) : LirInstr;

// LIR Block
public record LirBlock(string Label, List<LirInstr> Instructions);

// LIR Function
public record LirFunction(string Name, List<LirBlock> Blocks);

// LIR Module
public record LirModule(List<LirFunction> Functions);
