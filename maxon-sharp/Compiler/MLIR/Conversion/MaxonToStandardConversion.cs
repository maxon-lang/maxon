using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir.Conversion;

public static class MaxonToStandardConversion {
	public static MlirModule<StandardOp> Run(MlirModule<MaxonOp> module) {
		var result = new MlirModule<StandardOp>();
		result.RdataEntries.AddRange(module.RdataEntries);
		result.Globals.AddRange(module.Globals);

		foreach (var func in module.Functions) {
			var newFunc = new MlirFunction<StandardOp>(func.Name, func.ParamNames, func.ParamTypes, func.ReturnType);
			var valueMap = new Dictionary<MaxonValue, StdValue>();
			var varTypes = new Dictionary<string, string>();

			foreach (var block in func.Body.Blocks) {
				var newBlock = newFunc.Body.AddBlock(block.Name);
				foreach (var op in block.Operations) {
					switch (op) {
						case MaxonParamOp paramOp: {
								var stdResult = paramOp.ValueKind.CreateStdValue();
								newBlock.AddOp(new StdParamOp(paramOp.Index, paramOp.Name, stdResult));
								valueMap[paramOp.Result] = stdResult;
								EmitStore(newBlock, stdResult, paramOp.Name, varTypes);
								break;
							}
						case MaxonLiteralOp litOp: {
								switch (litOp.ValueKind) {
									case MaxonValueKind.Integer: {
											var newOp = new StdConstI64Op(litOp.IntValue);
											newBlock.AddOp(newOp);
											valueMap[litOp.Result] = newOp.Result;
											break;
										}
									case MaxonValueKind.Float: {
											var newOp = new StdConstF64Op(litOp.FloatValue);
											newBlock.AddOp(newOp);
											valueMap[litOp.Result] = newOp.Result;
											break;
										}
									case MaxonValueKind.Bool:
										throw new InvalidOperationException("Bool literals not yet supported in MaxonToStandard conversion");
									default:
										// this is defensive in case new kinds are added in the future
										throw new InvalidOperationException($"Unsupported MaxonLiteralOp kind: {litOp.ValueKind}");
								}
								break;
							}
						case MaxonAssignOp assignOp: {
								var mappedValue = valueMap[assignOp.Value];
								EmitStore(newBlock, mappedValue, assignOp.VarName, varTypes);
								break;
							}
						case MaxonVarRefOp varRef: {
								var varTypeName = varTypes[varRef.VarName];
								switch (varTypeName) {
									case "i64": {
											var loadOp = new StdLoadI64Op(varRef.VarName);
											newBlock.AddOp(loadOp);
											valueMap[varRef.Result] = loadOp.Result;
											break;
										}
									case "f64": {
											var loadOp = new StdLoadF64Op(varRef.VarName);
											newBlock.AddOp(loadOp);
											valueMap[varRef.Result] = loadOp.Result;
											break;
										}
									default:
										throw new InvalidOperationException($"Unsupported var type for load: {varTypeName}");
								}
								break;
							}
						case MaxonBinOp binOp: {
								var key = (binOp.Operator, binOp.OperandKind);
								if (!BinOpFactories.TryGetValue(key, out var factory))
									throw new InvalidOperationException($"Unsupported binop: {binOp.Operator} on {binOp.OperandKind}");

								var lhs = valueMap[binOp.Lhs];
								var rhs = valueMap[binOp.Rhs];
								var (newOp, factoryResult) = factory(lhs, rhs);
								newBlock.AddOp(newOp);
								valueMap[binOp.Result] = factoryResult;

								break;
							}
						case MaxonCondBrOp condBr: {
								var cond = (StdBool)valueMap[condBr.Condition];
								newBlock.AddOp(new StdCondBrOp(cond, condBr.ThenBlock, condBr.ElseBlock));
								break;
							}
						case MaxonBrOp br: {
								newBlock.AddOp(new StdBrOp(br.Target));
								break;
							}
						case MaxonTruncOp truncOp: {
								var input = (StdF64)valueMap[truncOp.Input];
								var fpToSi = new StdFpToSiOp(input);
								newBlock.AddOp(fpToSi);
								valueMap[truncOp.Result] = fpToSi.Result;
								break;
							}
						case MaxonAbsOp absOp: {
								var input = (StdF64)valueMap[absOp.Input];
								var absF64 = new StdAbsF64Op(input);
								newBlock.AddOp(absF64);
								valueMap[absOp.Result] = absF64.Result;
								break;
							}
						case MaxonCallOp callOp: {
								var newArgs = callOp.Args.Select(a => valueMap[a]).ToList();
								var callResult = callOp.ResultKind?.CreateStdValue();
								var funcCall = new StdCallOp(callOp.Callee, newArgs, callResult);
								newBlock.AddOp(funcCall);
								if (callOp.Result != null && funcCall.Result != null) {
									valueMap[callOp.Result] = funcCall.Result;
								}
								break;
							}
						case MaxonReturnOp retOp: {
								StdValue? newRetVal = retOp.Value != null ? valueMap[retOp.Value] : null;
								newBlock.AddOp(new StdReturnOp(newRetVal));
								break;
							}
						default:
							throw new InvalidOperationException($"No MaxonToStandard conversion for: {op.GetType().Name} ({op.Mnemonic})");
					}
				}
			}
			result.AddFunction(newFunc);
		}
		return result;
	}

	private static void EmitStore(MlirBlock<StandardOp> block, StdValue value, string varName, Dictionary<string, string> varTypes) {
		switch (value) {
			case StdI64 i64:
				block.AddOp(new StdStoreI64Op(i64, varName));
				varTypes[varName] = "i64";
				break;
			case StdF64 f64:
				block.AddOp(new StdStoreF64Op(f64, varName));
				varTypes[varName] = "f64";
				break;
			default:
				throw new InvalidOperationException($"Unsupported value type for store: {value.GetType().Name}");
		}
	}

	private static readonly Dictionary<(MaxonBinOperator, MaxonValueKind), Func<StdValue, StdValue, (StandardOp Op, StdValue Result)>> BinOpFactories = new() {
		{ (MaxonBinOperator.Add, MaxonValueKind.Integer), (l, r) => { var op = new StdAddI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
		{ (MaxonBinOperator.Sub, MaxonValueKind.Integer), (l, r) => { var op = new StdSubI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
		{ (MaxonBinOperator.Mul, MaxonValueKind.Integer), (l, r) => { var op = new StdMulI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
		{ (MaxonBinOperator.Div, MaxonValueKind.Integer), (l, r) => { var op = new StdDivI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
		{ (MaxonBinOperator.Mod, MaxonValueKind.Integer), (l, r) => { var op = new StdRemI64Op((StdI64)l, (StdI64)r); return (op, op.Result); } },
		{ (MaxonBinOperator.Add, MaxonValueKind.Float), (l, r) => { var op = new StdAddF64Op((StdF64)l, (StdF64)r); return (op, op.Result); } },
		{ (MaxonBinOperator.Sub, MaxonValueKind.Float), (l, r) => { var op = new StdSubF64Op((StdF64)l, (StdF64)r); return (op, op.Result); } },
		{ (MaxonBinOperator.Eq, MaxonValueKind.Float), (l, r) => { var op = new StdCmpF64Op("eq", (StdF64)l, (StdF64)r); return (op, op.Result); } },
		{ (MaxonBinOperator.Eq, MaxonValueKind.Integer), (l, r) => { var op = new StdCmpI64Op("eq", (StdI64)l, (StdI64)r); return (op, op.Result); } },
		{ (MaxonBinOperator.Ne, MaxonValueKind.Integer), (l, r) => { var op = new StdCmpI64Op("ne", (StdI64)l, (StdI64)r); return (op, op.Result); } },
		{ (MaxonBinOperator.Lt, MaxonValueKind.Integer), (l, r) => { var op = new StdCmpI64Op("lt", (StdI64)l, (StdI64)r); return (op, op.Result); } },
		{ (MaxonBinOperator.Gt, MaxonValueKind.Integer), (l, r) => { var op = new StdCmpI64Op("gt", (StdI64)l, (StdI64)r); return (op, op.Result); } },
		{ (MaxonBinOperator.Le, MaxonValueKind.Integer), (l, r) => { var op = new StdCmpI64Op("le", (StdI64)l, (StdI64)r); return (op, op.Result); } },
		{ (MaxonBinOperator.Ge, MaxonValueKind.Integer), (l, r) => { var op = new StdCmpI64Op("ge", (StdI64)l, (StdI64)r); return (op, op.Result); } },
	};

}
