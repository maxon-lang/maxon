namespace MaxonSharp.Compiler;

// ============================================================================
// HIR Types
// ============================================================================

public abstract record HirType {
	public abstract int SizeInBytes { get; }
	public abstract string Name { get; }
}

public record HirIntType : HirType {
	public override int SizeInBytes => 8;
	public override string Name => "int";
}

public record HirFloatType : HirType {
	public override int SizeInBytes => 8;
	public override string Name => "float";
}

public record HirBoolType : HirType {
	public override int SizeInBytes => 1;
	public override string Name => "bool";
}

public record HirByteType : HirType {
	public override int SizeInBytes => 1;
	public override string Name => "byte";
}

public record HirPtrType : HirType {
	public override int SizeInBytes => 8;
	public override string Name => "ptr";
}

public record HirVoidType : HirType {
	public override int SizeInBytes => 0;
	public override string Name => "void";
}

public record HirStructType(string StructName, List<HirStructField> Fields) : HirType {
	public override int SizeInBytes => Fields.Sum(f => f.Type.SizeInBytes);
	public override string Name => StructName;
}

public record HirStructField(string FieldName, HirType Type, int Offset);

public record HirArrayType(HirType ElementType) : HirType {
	public override int SizeInBytes => 8; // Managed array is a pointer
	public override string Name => $"Array<{ElementType.Name}>";
}

public record HirEnumType(string EnumName, int TagSize, int PayloadSize) : HirType {
	public override int SizeInBytes => TagSize + PayloadSize;
	public override string Name => EnumName;
}

// ============================================================================
// HIR Value (SSA identifier)
// ============================================================================

public record HirValue(int Id) {
	public override string ToString() => $"%{Id}";
}

// ============================================================================
// HIR Instructions
// ============================================================================

public abstract record HirInstr;

// Constants
public record HirConstInt(HirValue Dest, long Value) : HirInstr;
public record HirConstFloat(HirValue Dest, double Value) : HirInstr;
public record HirConstBool(HirValue Dest, bool Value) : HirInstr;
public record HirConstString(HirValue Dest, string Value) : HirInstr;

// Memory operations
public record HirAlloca(HirValue Dest, HirType Type) : HirInstr;
public record HirLoad(HirValue Dest, HirValue Ptr, HirType Type) : HirInstr;
public record HirStore(HirValue Ptr, HirValue Value, HirType Type) : HirInstr;
public record HirMemcpy(HirValue Dest, HirValue Src, int Size) : HirInstr;
public record HirGetFieldPtr(HirValue Dest, HirValue Base, string FieldName, int Offset) : HirInstr;
public record HirGetElemPtr(HirValue Dest, HirValue Base, HirValue Index, int ElemSize) : HirInstr;

// Integer arithmetic
public record HirAdd(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;
public record HirSub(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;
public record HirMul(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;
public record HirDiv(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;
public record HirMod(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;

// Bitwise operations
public record HirBand(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;
public record HirBor(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;
public record HirBxor(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;
public record HirShl(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;
public record HirShr(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;

// Float arithmetic
public record HirFAdd(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;
public record HirFSub(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;
public record HirFMul(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;
public record HirFDiv(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;

// Unary operations
public record HirNeg(HirValue Dest, HirValue Operand) : HirInstr;
public record HirNot(HirValue Dest, HirValue Operand) : HirInstr;

// Integer comparisons
public record HirCmpEq(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;
public record HirCmpNe(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;
public record HirCmpLt(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;
public record HirCmpLe(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;
public record HirCmpGt(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;
public record HirCmpGe(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;

// Logical operations (short-circuiting)
public record HirLogicalAnd(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;
public record HirLogicalOr(HirValue Dest, HirValue Left, HirValue Right) : HirInstr;

// Control flow
public record HirRet(HirValue? Value) : HirInstr;
public record HirBr(string Label) : HirInstr;
public record HirBrCond(HirValue Cond, string TrueLabel, string FalseLabel) : HirInstr;
public record HirLabel(string Name) : HirInstr;

// Function calls
public record HirCall(HirValue? Dest, string FuncName, List<HirValue> Args, HirType ReturnType) : HirInstr;
public record HirParam(HirValue Dest, int Index, HirType Type) : HirInstr;

// Conversions
public record HirIntToFloat(HirValue Dest, HirValue Value) : HirInstr;
public record HirFloatToInt(HirValue Dest, HirValue Value) : HirInstr;

// Heap operations
public record HirHeapAlloc(HirValue Dest, HirValue Size) : HirInstr;
public record HirHeapFree(HirValue Ptr) : HirInstr;

// Global variables
public record HirGlobalAddr(HirValue Dest, string Name) : HirInstr;

// ============================================================================
// HIR Block
// ============================================================================

public record HirBlock(string Label, List<HirInstr> Instructions);

// ============================================================================
// HIR Function
// ============================================================================

public record HirParam2(string Name, HirType Type);

public record HirFunction(
	string Name,
	bool IsExport,
	List<HirParam2> Params,
	HirType ReturnType,
	List<HirBlock> Blocks
);

// ============================================================================
// HIR Struct Definition
// ============================================================================

public record HirStructDef(string Name, List<HirStructField> Fields);

// ============================================================================
// HIR Enum Definition
// ============================================================================

public record HirEnumVariant(string Name, int Tag, List<HirStructField> PayloadFields);
public record HirEnumDef(string Name, List<HirEnumVariant> Variants, int TagSize, int MaxPayloadSize);

// ============================================================================
// HIR Global Variable
// ============================================================================

public record HirGlobalVar(string Name, HirType Type, HirInstr? InitValue);

// ============================================================================
// HIR Module
// ============================================================================

public record HirModule(
	List<HirStructDef> Structs,
	List<HirEnumDef> Enums,
	List<HirGlobalVar> Globals,
	List<HirFunction> Functions
) {
	/// <summary>
	/// Merges two HIR modules into one. First definition wins for duplicates.
	/// </summary>
	public static HirModule Merge(HirModule a, HirModule b) {
		var structs = new List<HirStructDef>(a.Structs);
		var enums = new List<HirEnumDef>(a.Enums);
		var globals = new List<HirGlobalVar>(a.Globals);
		var functions = new List<HirFunction>(a.Functions);

		// Track what's already defined
		var structNames = new HashSet<string>(a.Structs.Select(s => s.Name));
		var enumNames = new HashSet<string>(a.Enums.Select(e => e.Name));
		var globalNames = new HashSet<string>(a.Globals.Select(g => g.Name));
		var functionNames = new HashSet<string>(a.Functions.Select(f => f.Name));

		// Add from b if not already present
		foreach (var s in b.Structs) {
			if (structNames.Add(s.Name)) {
				structs.Add(s);
			}
		}
		foreach (var e in b.Enums) {
			if (enumNames.Add(e.Name)) {
				enums.Add(e);
			}
		}
		foreach (var g in b.Globals) {
			if (globalNames.Add(g.Name)) {
				globals.Add(g);
			}
		}
		foreach (var f in b.Functions) {
			if (functionNames.Add(f.Name)) {
				functions.Add(f);
			}
		}

		return new HirModule(structs, enums, globals, functions);
	}
}
