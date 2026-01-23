namespace MaxonSharp.Lir;

// ============================================================================
// LIR Types (machine types)
// ============================================================================

public enum LirType {
    I8,
    I64,
    F64,
    Ptr
}

// ============================================================================
// LIR Values
// ============================================================================

public abstract record LirValue;

public record LirVReg(int Id) : LirValue {
    public override string ToString() => $"v{Id}";
}

public record LirImmediate(long Value) : LirValue {
    public override string ToString() => Value.ToString();
}

public record LirFloatImmediate(double Value) : LirValue {
    public override string ToString() => Value.ToString();
}

public record LirLabel(string Name) : LirValue {
    public override string ToString() => Name;
}

public record LirStackSlot(int Offset, int Size) : LirValue {
    public override string ToString() => $"[rbp{(Offset >= 0 ? "+" : "")}{Offset}]";
}

public record LirGlobalRef(string Name) : LirValue {
    public override string ToString() => $"@{Name}";
}

public record LirStringRef(int Id, string Value) : LirValue {
    public override string ToString() => $"str{Id}";
}

// ============================================================================
// LIR Instructions
// ============================================================================

public abstract record LirInstr;

// Data movement
public record LirMov(LirVReg Dest, LirValue Src) : LirInstr;
public record LirLoad(LirVReg Dest, LirValue Ptr, int Size) : LirInstr;
public record LirStore(LirValue Ptr, LirValue Value, int Size) : LirInstr;
public record LirLea(LirVReg Dest, LirValue Addr) : LirInstr;

// Integer arithmetic
public record LirAdd(LirVReg Dest, LirValue Left, LirValue Right) : LirInstr;
public record LirSub(LirVReg Dest, LirValue Left, LirValue Right) : LirInstr;
public record LirIMul(LirVReg Dest, LirValue Left, LirValue Right) : LirInstr;
public record LirIDiv(LirVReg Dest, LirValue Left, LirValue Right) : LirInstr;
public record LirMod(LirVReg Dest, LirValue Left, LirValue Right) : LirInstr;
public record LirNeg(LirVReg Dest, LirValue Src) : LirInstr;

// Bitwise operations
public record LirAnd(LirVReg Dest, LirValue Left, LirValue Right) : LirInstr;
public record LirOr(LirVReg Dest, LirValue Left, LirValue Right) : LirInstr;
public record LirXor(LirVReg Dest, LirValue Left, LirValue Right) : LirInstr;
public record LirNot(LirVReg Dest, LirValue Src) : LirInstr;
public record LirShl(LirVReg Dest, LirValue Left, LirValue Right) : LirInstr;
public record LirShr(LirVReg Dest, LirValue Left, LirValue Right) : LirInstr;

// Floating point arithmetic
public record LirFAdd(LirVReg Dest, LirValue Left, LirValue Right) : LirInstr;
public record LirFSub(LirVReg Dest, LirValue Left, LirValue Right) : LirInstr;
public record LirFMul(LirVReg Dest, LirValue Left, LirValue Right) : LirInstr;
public record LirFDiv(LirVReg Dest, LirValue Left, LirValue Right) : LirInstr;
public record LirFNeg(LirVReg Dest, LirValue Src) : LirInstr;

// Comparisons (set flags or result register)
public record LirCmp(LirValue Left, LirValue Right) : LirInstr;
public record LirFCmp(LirValue Left, LirValue Right) : LirInstr;
public record LirSetCC(LirVReg Dest, CondCode Cond) : LirInstr;

// Control flow
public record LirRet(LirValue? Value) : LirInstr;
public record LirJmp(string Label) : LirInstr;
public record LirJmpCC(CondCode Cond, string TrueLabel, string FalseLabel) : LirInstr;
public record LirLabelDef(string Name) : LirInstr;

// Function calls
public record LirCall(LirVReg? Dest, string FuncName, List<LirValue> Args) : LirInstr;
public record LirPush(LirValue Value) : LirInstr;
public record LirPop(LirVReg Dest) : LirInstr;

// Conversions
public record LirIntToFloat(LirVReg Dest, LirValue Src) : LirInstr;
public record LirFloatToInt(LirVReg Dest, LirValue Src) : LirInstr;
public record LirSignExtend(LirVReg Dest, LirValue Src, int FromSize, int ToSize) : LirInstr;
public record LirZeroExtend(LirVReg Dest, LirValue Src, int FromSize, int ToSize) : LirInstr;

// Memory operations
public record LirAddressOf(LirVReg Dest, LirStackSlot Slot) : LirInstr;

// ============================================================================
// Condition Codes
// ============================================================================

public enum CondCode {
    Eq,   // Equal (ZF=1)
    Ne,   // Not equal (ZF=0)
    Lt,   // Less than (SF≠OF)
    Le,   // Less or equal (ZF=1 or SF≠OF)
    Gt,   // Greater than (ZF=0 and SF=OF)
    Ge,   // Greater or equal (SF=OF)
    LtU,  // Less than unsigned (CF=1)
    LeU,  // Less or equal unsigned (CF=1 or ZF=1)
    GtU,  // Greater than unsigned (CF=0 and ZF=0)
    GeU   // Greater or equal unsigned (CF=0)
}

// ============================================================================
// LIR Block
// ============================================================================

public record LirBlock(string Label, List<LirInstr> Instructions);

// ============================================================================
// LIR Function
// ============================================================================

public record LirParam(string Name, LirType Type, int Index);

public record LirFunction(
    string Name,
    bool IsExport,
    List<LirParam> Params,
    LirType? ReturnType,
    int StackSize,
    List<LirBlock> Blocks
);

// ============================================================================
// LIR Global Data
// ============================================================================

public record LirStringData(int Id, string Value);
public record LirGlobalData(string Name, byte[] Data);

// ============================================================================
// LIR Module
// ============================================================================

public record LirModule(
    List<LirStringData> Strings,
    List<LirGlobalData> Globals,
    List<LirFunction> Functions
);

