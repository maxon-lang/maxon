namespace MaxonSharp.Compiler;

public class HirToLir {
	private readonly Dictionary<int, LirVReg> _valueMap = [];
	private readonly Dictionary<string, LirStringData> _strings = [];
	private readonly List<LirStringData> _stringList = [];
	private int _nextVRegId;
	private int _nextStringId;
	private int _stackOffset;
	private readonly Dictionary<string, int> _localOffsets = [];

	// Mapping from HIR binary operation types to their corresponding LIR instruction factory
	private static readonly Dictionary<Type, Func<LirVReg, LirValue, LirValue, LirInstr>> BinaryOpMap = new() {
		[typeof(HirAdd)] = (d, l, r) => new LirAdd(d, l, r),
		[typeof(HirSub)] = (d, l, r) => new LirSub(d, l, r),
		[typeof(HirMul)] = (d, l, r) => new LirIMul(d, l, r),
		[typeof(HirDiv)] = (d, l, r) => new LirIDiv(d, l, r),
		[typeof(HirMod)] = (d, l, r) => new LirMod(d, l, r),
		[typeof(HirBand)] = (d, l, r) => new LirAnd(d, l, r),
		[typeof(HirBor)] = (d, l, r) => new LirOr(d, l, r),
		[typeof(HirBxor)] = (d, l, r) => new LirXor(d, l, r),
		[typeof(HirShl)] = (d, l, r) => new LirShl(d, l, r),
		[typeof(HirShr)] = (d, l, r) => new LirShr(d, l, r),
		[typeof(HirFAdd)] = (d, l, r) => new LirFAdd(d, l, r),
		[typeof(HirFSub)] = (d, l, r) => new LirFSub(d, l, r),
		[typeof(HirFMul)] = (d, l, r) => new LirFMul(d, l, r),
		[typeof(HirFDiv)] = (d, l, r) => new LirFDiv(d, l, r),
		[typeof(HirLogicalAnd)] = (d, l, r) => new LirAnd(d, l, r),
		[typeof(HirLogicalOr)] = (d, l, r) => new LirOr(d, l, r),
	};

	// Mapping from HIR comparison types to their LIR condition codes
	private static readonly Dictionary<Type, LirCondCode> CmpOpMap = new() {
		[typeof(HirCmpEq)] = LirCondCode.Eq,
		[typeof(HirCmpNe)] = LirCondCode.Ne,
		[typeof(HirCmpLt)] = LirCondCode.Lt,
		[typeof(HirCmpLe)] = LirCondCode.Le,
		[typeof(HirCmpGt)] = LirCondCode.Gt,
		[typeof(HirCmpGe)] = LirCondCode.Ge,
	};

	// Mapping from HIR unary operation types to their corresponding LIR instruction factory
	private static readonly Dictionary<Type, Func<LirVReg, LirValue, LirInstr>> UnaryOpMap = new() {
		[typeof(HirNeg)] = (d, o) => new LirNeg(d, o),
		[typeof(HirNot)] = (d, o) => new LirNot(d, o),
	};

	public LirModule Lower(HirModule hirModule) {
		Logger.Info(LogCategory.Lir, "Starting HIR to LIR lowering");
		var functions = new List<LirFunction>();

		foreach (var func in hirModule.Functions) {
			Logger.Debug(LogCategory.Lir, $"Lowering function: {func.Name}");
			functions.Add(LowerFunction(func));
		}

		var globals = new List<LirGlobalData>();
		foreach (var global in hirModule.Globals) {
			// TODO: lower global initializers
			globals.Add(new LirGlobalData(global.Name, new byte[global.Type.SizeInBytes]));
		}

		Logger.Info(LogCategory.Lir, $"LIR complete: {functions.Count} functions");
		return new LirModule(_stringList, globals, functions);
	}

	private LirFunction LowerFunction(HirFunction func) {
		_valueMap.Clear();
		_localOffsets.Clear();
		_nextVRegId = 0;
		_stackOffset = 0;

		// Track param index to vreg mapping
		var paramToVReg = new Dictionary<int, int>();

		var blocks = new List<LirBlock>();
		foreach (var block in func.Blocks) {
			blocks.Add(LowerBlock(block, paramToVReg));
		}

		// Build parameter list with vreg IDs
		var parameters = new List<LirParam>();
		for (var i = 0; i < func.Params.Count; i++) {
			var p = func.Params[i];
			var lirType = HirTypeToLirType(p.Type);
			var vregId = paramToVReg.TryGetValue(i, out var vid) ? vid : -1;
			parameters.Add(new LirParam(p.Name, lirType, i, vregId));
		}

		// Align stack to 16 bytes
		var stackSize = ((_stackOffset + 15) / 16) * 16;
		Logger.Debug(LogCategory.Lir, $"Stack size: {stackSize}");

		LirType? retType = func.ReturnType is HirVoidType ? null : HirTypeToLirType(func.ReturnType);

		return new LirFunction(func.Name, func.IsExport, parameters, retType, stackSize, blocks);
	}

	private LirBlock LowerBlock(HirBlock block, Dictionary<int, int> paramToVReg) {
		var instructions = new List<LirInstr>();

		foreach (var instr in block.Instructions) {
			LowerInstruction(instr, instructions, paramToVReg);
		}

		return new LirBlock(block.Label, instructions);
	}

	private bool TryLowerBinaryOp(HirInstr instr, List<LirInstr> instructions) {
		if (instr is not IHirBinaryOp binOp) return false;
		if (!BinaryOpMap.TryGetValue(instr.GetType(), out var factory)) return false;

		var dest = GetOrCreateVReg(binOp.Dest.Id);
		var left = GetVReg(binOp.Left.Id);
		var right = GetVReg(binOp.Right.Id);
		instructions.Add(factory(dest, left, right));
		return true;
	}

	private bool TryLowerCmpOp(HirInstr instr, List<LirInstr> instructions) {
		if (instr is not IHirCmpOp cmpOp) return false;
		if (!CmpOpMap.TryGetValue(instr.GetType(), out var condCode)) return false;

		var dest = GetOrCreateVReg(cmpOp.Dest.Id);
		var left = GetVReg(cmpOp.Left.Id);
		var right = GetVReg(cmpOp.Right.Id);
		instructions.Add(new LirCmp(left, right));
		instructions.Add(new LirSetCC(dest, condCode));
		return true;
	}

	private bool TryLowerUnaryOp(HirInstr instr, List<LirInstr> instructions) {
		if (instr is not IHirUnaryOp unaryOp) return false;
		if (!UnaryOpMap.TryGetValue(instr.GetType(), out var factory)) return false;

		var dest = GetOrCreateVReg(unaryOp.Dest.Id);
		var operand = GetVReg(unaryOp.Operand.Id);
		instructions.Add(factory(dest, operand));
		return true;
	}

	private void LowerInstruction(HirInstr instr, List<LirInstr> instructions, Dictionary<int, int> paramToVReg) {
		// Try pattern-based lowering first
		if (TryLowerBinaryOp(instr, instructions)) return;
		if (TryLowerCmpOp(instr, instructions)) return;
		if (TryLowerUnaryOp(instr, instructions)) return;

		switch (instr) {
			// Constants
			case HirConstInt constInt: {
					var dest = GetOrCreateVReg(constInt.Dest.Id);
					instructions.Add(new LirMov(dest, new LirImmediate(constInt.Value)));
					break;
				}

			case HirConstFloat constFloat: {
					var dest = GetOrCreateVReg(constFloat.Dest.Id);
					instructions.Add(new LirMov(dest, new LirFloatImmediate(constFloat.Value)));
					break;
				}

			case HirConstBool constBool: {
					var dest = GetOrCreateVReg(constBool.Dest.Id);
					instructions.Add(new LirMov(dest, new LirImmediate(constBool.Value ? 1 : 0)));
					break;
				}

			case HirConstString constString: {
					var dest = GetOrCreateVReg(constString.Dest.Id);
					var strData = GetOrCreateString(constString.Value);
					instructions.Add(new LirLea(dest, new LirStringRef(strData.Id, strData.Value)));
					break;
				}

			// Memory operations
			case HirAlloca alloca: {
					var size = alloca.Type.SizeInBytes;
					_stackOffset += size;
					var slot = new LirStackSlot(-_stackOffset, size);
					var dest = GetOrCreateVReg(alloca.Dest.Id);
					instructions.Add(new LirAddressOf(dest, slot));
					break;
				}

			case HirLoad load: {
					var dest = GetOrCreateVReg(load.Dest.Id);
					var ptr = GetVReg(load.Ptr.Id);
					instructions.Add(new LirLoad(dest, ptr, load.Type.SizeInBytes));
					break;
				}

			case HirStore store: {
					var ptr = GetVReg(store.Ptr.Id);
					var value = GetVReg(store.Value.Id);
					instructions.Add(new LirStore(ptr, value, store.Type.SizeInBytes));
					break;
				}

			case HirMemcpy memcpy: {
					var dest = GetVReg(memcpy.Dest.Id);
					var src = GetVReg(memcpy.Src.Id);
					instructions.Add(new LirMemcpy(dest, src, memcpy.Size));
					break;
				}

			case HirGetFieldPtr getField: {
					var dest = GetOrCreateVReg(getField.Dest.Id);
					var baseVal = GetVReg(getField.Base.Id);
					// Add offset to base
					if (getField.Offset == 0) {
						instructions.Add(new LirMov(dest, baseVal));
					} else {
						instructions.Add(new LirAdd(dest, baseVal, new LirImmediate(getField.Offset)));
					}
					break;
				}

			case HirGetElemPtr getElem: {
					var dest = GetOrCreateVReg(getElem.Dest.Id);
					var baseVal = GetVReg(getElem.Base.Id);
					var idx = GetVReg(getElem.Index.Id);

					// ptr + (index * elemSize) + 16 (skip array header)
					var scaledIdx = NewVReg();
					instructions.Add(new LirIMul(scaledIdx, idx, new LirImmediate(getElem.ElemSize)));
					var headerOffset = NewVReg();
					instructions.Add(new LirAdd(headerOffset, scaledIdx, new LirImmediate(16)));
					instructions.Add(new LirAdd(dest, baseVal, headerOffset));
					break;
				}

			// Control flow
			case HirRet ret: {
					LirValue? value = null;
					if (ret.Value != null) {
						value = GetVReg(ret.Value.Id);
					}
					instructions.Add(new LirRet(value));
					break;
				}

			case HirBr br: {
					instructions.Add(new LirJmp(br.Label));
					break;
				}

			case HirBrCond brCond: {
					var cond = GetVReg(brCond.Cond.Id);
					// Test the condition register
					instructions.Add(new LirCmp(cond, new LirImmediate(0)));
					instructions.Add(new LirJmpCC(LirCondCode.Ne, brCond.TrueLabel, brCond.FalseLabel));
					break;
				}

			case HirLabel label: {
					instructions.Add(new LirLabelDef(label.Name));
					break;
				}

			// Function calls
			case HirCall call: {
					var args = call.Args.Select(a => (LirValue)GetVReg(a.Id)).ToList();
					LirVReg? dest = call.Dest != null ? GetOrCreateVReg(call.Dest.Id) : null;
					instructions.Add(new LirCall(dest, call.FuncName, args));
					break;
				}

			case HirParam param: {
					// Params are passed in registers (Windows x64 ABI)
					// RCX, RDX, R8, R9 for first 4, then stack
					// The code generator handles storing params to their vreg slots
					// Register the vreg and record the param index -> vreg mapping
					var vreg = GetOrCreateVReg(param.Dest.Id);
					paramToVReg[param.Index] = vreg.Id;
					break;
				}

			// Conversions
			case HirIntToFloat itof: {
					var dest = GetOrCreateVReg(itof.Dest.Id);
					var src = GetVReg(itof.Value.Id);
					instructions.Add(new LirIntToFloat(dest, src));
					break;
				}

			case HirFloatToInt ftoi: {
					var dest = GetOrCreateVReg(ftoi.Dest.Id);
					var src = GetVReg(ftoi.Value.Id);
					instructions.Add(new LirFloatToInt(dest, src));
					break;
				}

			// Heap operations
			case HirHeapAlloc heapAlloc: {
					var dest = GetOrCreateVReg(heapAlloc.Dest.Id);
					var size = GetVReg(heapAlloc.Size.Id);
					// Call runtime allocation function
					instructions.Add(new LirCall(dest, "__maxon_alloc", [size]));
					break;
				}

			case HirHeapFree heapFree: {
					var ptr = GetVReg(heapFree.Ptr.Id);
					instructions.Add(new LirCall(null, "__maxon_free", [ptr]));
					break;
				}

			// Global variables
			case HirGlobalAddr globalAddr: {
					var dest = GetOrCreateVReg(globalAddr.Dest.Id);
					instructions.Add(new LirLea(dest, new LirGlobalRef(globalAddr.Name)));
					break;
				}

			default:
				throw new Exception($"Unsupported HIR instruction: {instr.GetType().Name}");
		}
	}

	private static LirType HirTypeToLirType(HirType type) {
		return type switch {
			HirIntType => LirType.I64,
			HirFloatType => LirType.F64,
			HirBoolType => LirType.I8,
			HirByteType => LirType.I8,
			HirPtrType => LirType.Ptr,
			HirStructType => LirType.Ptr,
			HirArrayType => LirType.Ptr,
			HirEnumType => LirType.I64,
			_ => LirType.I64
		};
	}

	private LirVReg GetOrCreateVReg(int hirValueId) {
		if (!_valueMap.TryGetValue(hirValueId, out var vreg)) {
			vreg = new LirVReg(_nextVRegId++);
			_valueMap[hirValueId] = vreg;
		}
		return vreg;
	}

	private LirVReg GetVReg(int hirValueId) {
		if (!_valueMap.TryGetValue(hirValueId, out var vreg)) {
			throw new Exception($"HIR value %{hirValueId} not found");
		}
		return vreg;
	}

	private LirVReg NewVReg() {
		return new LirVReg(_nextVRegId++);
	}

	private LirStringData GetOrCreateString(string value) {
		if (!_strings.TryGetValue(value, out var strData)) {
			strData = new LirStringData(_nextStringId++, value);
			_strings[value] = strData;
			_stringList.Add(strData);
		}
		return strData;
	}
}

