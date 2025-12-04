/**
 * Unit tests for Phase 1: Maxon IR (MIR)
 *
 * Tests the core IR data structures: types, values, instructions,
 * basic blocks, functions, and modules.
 */

#include "../mir/mir.h"
#include "../mir/mir_builder.h"
#include <catch_amalgamated.hpp>

using namespace mir;

//==============================================================================
// Type System Tests
//==============================================================================

TEST_CASE("MIR primitive types", "[mir][types]") {
	SECTION("void type") {
		auto *t = MIRType::getVoid();
		REQUIRE(t != nullptr);
		REQUIRE(t->kind == MIRTypeKind::Void);
		REQUIRE(t->isVoid());
		REQUIRE(t->sizeInBytes == 0);
	}

	SECTION("integer types") {
		auto *i1 = MIRType::getInt1();
		REQUIRE(i1->kind == MIRTypeKind::Int1);
		REQUIRE(i1->isInteger());
		REQUIRE(i1->sizeInBytes == 1);

		auto *i8 = MIRType::getInt8();
		REQUIRE(i8->kind == MIRTypeKind::Int8);
		REQUIRE(i8->isInteger());
		REQUIRE(i8->sizeInBytes == 1);

		auto *i32 = MIRType::getInt32();
		REQUIRE(i32->kind == MIRTypeKind::Int32);
		REQUIRE(i32->isInteger());
		REQUIRE(i32->sizeInBytes == 4);

		auto *i64 = MIRType::getInt64();
		REQUIRE(i64->kind == MIRTypeKind::Int64);
		REQUIRE(i64->isInteger());
		REQUIRE(i64->sizeInBytes == 8);
	}

	SECTION("float type") {
		auto *f64 = MIRType::getFloat64();
		REQUIRE(f64->kind == MIRTypeKind::Float64);
		REQUIRE(f64->isFloat());
		REQUIRE_FALSE(f64->isInteger());
		REQUIRE(f64->sizeInBytes == 8);
	}

	SECTION("pointer type") {
		auto *ptr = MIRType::getPtr();
		REQUIRE(ptr->kind == MIRTypeKind::Ptr);
		REQUIRE(ptr->isPointer());
		REQUIRE(ptr->sizeInBytes == 8); // 64-bit pointers
	}

	SECTION("type singleton behavior") {
		// Each primitive type should return the same pointer
		REQUIRE(MIRType::getInt32() == MIRType::getInt32());
		REQUIRE(MIRType::getFloat64() == MIRType::getFloat64());
		REQUIRE(MIRType::getPtr() == MIRType::getPtr());
	}
}

TEST_CASE("MIR array types", "[mir][types]") {
	auto *elemType = MIRType::getInt32();
	auto *arrayType = MIRType::getArray(elemType, 10);

	REQUIRE(arrayType != nullptr);
	REQUIRE(arrayType->kind == MIRTypeKind::Array);
	REQUIRE(arrayType->isArray());
	REQUIRE(arrayType->elementType == elemType);
	REQUIRE(arrayType->arraySize == 10);
	REQUIRE(arrayType->sizeInBytes == 40); // 10 * 4 bytes
}

TEST_CASE("MIR struct types", "[mir][types]") {
	std::vector<MIRType *> fields = {
		MIRType::getInt32(),
		MIRType::getFloat64(),
		MIRType::getPtr()};
	auto *structType = MIRType::getStruct("MyStruct", fields);

	REQUIRE(structType != nullptr);
	REQUIRE(structType->kind == MIRTypeKind::Struct);
	REQUIRE(structType->isStruct());
	REQUIRE(structType->structName == "MyStruct");
	REQUIRE(structType->fieldTypes.size() == 3);
	// Size should account for alignment (4 + 4 padding + 8 + 8 = 24)
	// or similar depending on alignment rules
}

//==============================================================================
// Value Tests
//==============================================================================

TEST_CASE("MIR constant values", "[mir][values]") {
	SECTION("integer constants") {
		auto *c42 = MIRValue::createConstantInt(MIRType::getInt32(), 42);
		REQUIRE(c42 != nullptr);
		REQUIRE(c42->kind == MIRValueKind::ConstantInt);
		REQUIRE(c42->isConstant());
		REQUIRE(c42->intValue == 42);
		REQUIRE(c42->type == MIRType::getInt32());
	}

	SECTION("negative integer constants") {
		auto *neg = MIRValue::createConstantInt(MIRType::getInt32(), -100);
		REQUIRE(neg->intValue == -100);
	}

	SECTION("64-bit integer constants") {
		auto *large = MIRValue::createConstantInt(MIRType::getInt64(), 0x123456789ABCDEFLL);
		REQUIRE(large->intValue == 0x123456789ABCDEFLL);
	}

	SECTION("float constants") {
		auto *pi = MIRValue::createConstantFloat(3.14159);
		REQUIRE(pi != nullptr);
		REQUIRE(pi->kind == MIRValueKind::ConstantFloat);
		REQUIRE(pi->isConstant());
		REQUIRE(pi->floatValue == Catch::Approx(3.14159));
		REQUIRE(pi->type == MIRType::getFloat64());
	}

	SECTION("null pointer constant") {
		auto *null = MIRValue::createConstantNull();
		REQUIRE(null != nullptr);
		REQUIRE(null->kind == MIRValueKind::ConstantNull);
		REQUIRE(null->isConstant());
		REQUIRE(null->type == MIRType::getPtr());
	}
}

TEST_CASE("MIR virtual registers", "[mir][values]") {
	auto *vreg = MIRValue::createVirtualReg(MIRType::getInt32(), 5);
	REQUIRE(vreg != nullptr);
	REQUIRE(vreg->kind == MIRValueKind::VirtualReg);
	REQUIRE_FALSE(vreg->isConstant());
	REQUIRE(vreg->regId == 5);
	REQUIRE(vreg->type == MIRType::getInt32());
}

TEST_CASE("MIR global references", "[mir][values]") {
	auto *global = MIRValue::createGlobal(MIRType::getPtr(), "my_global");
	REQUIRE(global != nullptr);
	REQUIRE(global->kind == MIRValueKind::Global);
	REQUIRE(global->name == "my_global");
}

TEST_CASE("MIR parameters", "[mir][values]") {
	auto *param = MIRValue::createParameter(MIRType::getInt32(), "x", 0);
	REQUIRE(param != nullptr);
	REQUIRE(param->kind == MIRValueKind::Parameter);
	REQUIRE(param->name == "x");
	REQUIRE(param->regId == 0);
}

//==============================================================================
// Instruction Tests
//==============================================================================

TEST_CASE("MIR instruction properties", "[mir][instructions]") {
	SECTION("terminator detection") {
		MIRInstruction br(MIROpcode::Br);
		REQUIRE(br.isTerminator());
		REQUIRE(br.isBranch());
		REQUIRE_FALSE(br.hasResult());

		MIRInstruction condBr(MIROpcode::CondBr);
		REQUIRE(condBr.isTerminator());
		REQUIRE(condBr.isBranch());

		MIRInstruction ret(MIROpcode::Ret);
		REQUIRE(ret.isTerminator());
		REQUIRE_FALSE(ret.isBranch());

		MIRInstruction add(MIROpcode::Add);
		REQUIRE_FALSE(add.isTerminator());
		REQUIRE_FALSE(add.isBranch());
		REQUIRE(add.hasResult());
	}

	SECTION("opcode to string") {
		REQUIRE(std::string(MIRInstruction::opcodeToString(MIROpcode::Add)) == "add");
		REQUIRE(std::string(MIRInstruction::opcodeToString(MIROpcode::Sub)) == "sub");
		REQUIRE(std::string(MIRInstruction::opcodeToString(MIROpcode::Load)) == "load");
		REQUIRE(std::string(MIRInstruction::opcodeToString(MIROpcode::Store)) == "store");
		REQUIRE(std::string(MIRInstruction::opcodeToString(MIROpcode::Call)) == "call");
	}
}

//==============================================================================
// Basic Block Tests
//==============================================================================

TEST_CASE("MIR basic block", "[mir][basicblock]") {
	MIRBasicBlock bb("entry");

	REQUIRE(bb.name == "entry");
	REQUIRE(bb.instructions.empty());
	REQUIRE_FALSE(bb.hasTerminator());
	REQUIRE(bb.getTerminator() == nullptr);

	// Add a non-terminator instruction
	auto addInst = std::make_unique<MIRInstruction>(MIROpcode::Add);
	bb.addInstruction(std::move(addInst));
	REQUIRE(bb.instructions.size() == 1);
	REQUIRE_FALSE(bb.hasTerminator());

	// Add a terminator
	auto retInst = std::make_unique<MIRInstruction>(MIROpcode::RetVoid);
	bb.addInstruction(std::move(retInst));
	REQUIRE(bb.instructions.size() == 2);
	REQUIRE(bb.hasTerminator());
	REQUIRE(bb.getTerminator() != nullptr);
	REQUIRE(bb.getTerminator()->opcode == MIROpcode::RetVoid);
}

//==============================================================================
// Function Tests
//==============================================================================

TEST_CASE("MIR function creation", "[mir][function]") {
	MIRFunction func("test_func", MIRType::getInt32());

	REQUIRE(func.name == "test_func");
	REQUIRE(func.returnType == MIRType::getInt32());
	REQUIRE(func.basicBlocks.empty());
	REQUIRE(func.parameters.empty());
	REQUIRE(func.nextRegId == 0);

	SECTION("create basic blocks") {
		auto *entry = func.createBasicBlock("entry");
		REQUIRE(entry != nullptr);
		REQUIRE(entry->name == "entry0"); // Block names get unique suffix appended
		REQUIRE(entry->parent == &func);
		REQUIRE(func.basicBlocks.size() == 1);
		REQUIRE(func.getEntryBlock() == entry);

		auto *loop = func.createBasicBlock("loop");
		REQUIRE(loop != nullptr);
		REQUIRE(loop->name == "loop1"); // Second block gets id 1
		REQUIRE(func.basicBlocks.size() == 2);
		REQUIRE(func.getEntryBlock() == entry); // Entry is still first
	}

	SECTION("create virtual registers") {
		auto *r1 = func.createVirtualReg(MIRType::getInt32());
		REQUIRE(r1 != nullptr);
		REQUIRE(r1->regId == 0);
		REQUIRE(func.nextRegId == 1);

		auto *r2 = func.createVirtualReg(MIRType::getFloat64());
		REQUIRE(r2->regId == 1);
		REQUIRE(func.nextRegId == 2);
	}

	SECTION("add parameters") {
		auto *p1 = func.addParameter(MIRType::getInt32(), "a");
		REQUIRE(p1 != nullptr);
		REQUIRE(p1->name == "a");
		REQUIRE(func.parameters.size() == 1);

		auto *p2 = func.addParameter(MIRType::getFloat64(), "b");
		REQUIRE(p2->name == "b");
		REQUIRE(func.parameters.size() == 2);
	}
}

//==============================================================================
// Module Tests
//==============================================================================

TEST_CASE("MIR module", "[mir][module]") {
	MIRModule mod("test_module");

	REQUIRE(mod.name == "test_module");
	REQUIRE(mod.functions.empty());
	REQUIRE(mod.globals.empty());

	SECTION("create functions") {
		auto *fn = mod.createFunction("foo", MIRType::getVoid());
		REQUIRE(fn != nullptr);
		REQUIRE(fn->name == "foo");
		REQUIRE(fn->parent == &mod);
		REQUIRE(mod.functions.size() == 1);

		REQUIRE(mod.getFunction("foo") == fn);
		REQUIRE(mod.getFunction("bar") == nullptr);
	}

	SECTION("create globals") {
		auto *g = mod.createGlobal("counter", MIRType::getInt32());
		REQUIRE(g != nullptr);
		REQUIRE(g->name == "counter");
		REQUIRE(mod.globals.size() == 1);

		REQUIRE(mod.getGlobal("counter") == g);
		REQUIRE(mod.getGlobal("other") == nullptr);
	}
}

//==============================================================================
// MIR Builder Tests
//==============================================================================

TEST_CASE("MIR builder basic operations", "[mir][builder]") {
	MIRModule mod("test");
	MIRBuilder builder(&mod);

	auto *func = builder.createFunction("add_numbers", MIRType::getInt32(), {});
	REQUIRE(func != nullptr);

	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	// Create parameters
	auto *paramA = func->addParameter(MIRType::getInt32(), "a");
	auto *paramB = func->addParameter(MIRType::getInt32(), "b");

	// Build: return a + b
	auto *sum = builder.createAdd(paramA, paramB);
	REQUIRE(sum != nullptr);
	REQUIRE(sum->kind == MIRValueKind::VirtualReg);

	builder.createRet(sum);
	REQUIRE(entry->hasTerminator());
}

TEST_CASE("MIR builder arithmetic operations", "[mir][builder]") {
	MIRModule mod("test");
	MIRBuilder builder(&mod);

	(void)builder.createFunction("arith", MIRType::getInt32(), {});
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	auto *x = MIRValue::createConstantInt(MIRType::getInt32(), 10);
	auto *y = MIRValue::createConstantInt(MIRType::getInt32(), 3);

	SECTION("integer arithmetic") {
		auto *add = builder.createAdd(x, y);
		REQUIRE(add->type == MIRType::getInt32());

		auto *sub = builder.createSub(x, y);
		REQUIRE(sub != nullptr);

		auto *mul = builder.createMul(x, y);
		REQUIRE(mul != nullptr);

		auto *div = builder.createSDiv(x, y);
		REQUIRE(div != nullptr);

		auto *rem = builder.createSRem(x, y);
		REQUIRE(rem != nullptr);
	}

	SECTION("floating-point arithmetic") {
		auto *a = MIRValue::createConstantFloat(2.5);
		auto *b = MIRValue::createConstantFloat(1.5);

		auto *fadd = builder.createFAdd(a, b);
		REQUIRE(fadd->type == MIRType::getFloat64());

		auto *fsub = builder.createFSub(a, b);
		REQUIRE(fsub != nullptr);

		auto *fmul = builder.createFMul(a, b);
		REQUIRE(fmul != nullptr);

		auto *fdiv = builder.createFDiv(a, b);
		REQUIRE(fdiv != nullptr);
	}
}

TEST_CASE("MIR builder comparisons", "[mir][builder]") {
	MIRModule mod("test");
	MIRBuilder builder(&mod);

	(void)builder.createFunction("cmp", MIRType::getInt1(), {});
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	auto *x = MIRValue::createConstantInt(MIRType::getInt32(), 5);
	auto *y = MIRValue::createConstantInt(MIRType::getInt32(), 10);

	SECTION("integer comparisons") {
		auto *eq = builder.createICmpEq(x, y);
		REQUIRE(eq != nullptr);
		REQUIRE(eq->type == MIRType::getInt1());

		auto *ne = builder.createICmpNe(x, y);
		REQUIRE(ne != nullptr);

		auto *slt = builder.createICmpSLT(x, y);
		REQUIRE(slt != nullptr);

		auto *sle = builder.createICmpSLE(x, y);
		REQUIRE(sle != nullptr);

		auto *sgt = builder.createICmpSGT(x, y);
		REQUIRE(sgt != nullptr);

		auto *sge = builder.createICmpSGE(x, y);
		REQUIRE(sge != nullptr);
	}
}

TEST_CASE("MIR builder control flow", "[mir][builder]") {
	MIRModule mod("test");
	MIRBuilder builder(&mod);

	(void)builder.createFunction("control_flow", MIRType::getVoid(), {});
	auto *entry = builder.createBasicBlock("entry");
	auto *thenBB = builder.createBasicBlock("then");
	auto *elseBB = builder.createBasicBlock("else");
	auto *mergeBB = builder.createBasicBlock("merge");

	builder.setInsertPoint(entry);
	auto *cond = MIRValue::createConstantInt(MIRType::getInt1(), 1);
	builder.createCondBr(cond, thenBB, elseBB);

	REQUIRE(entry->hasTerminator());
	REQUIRE(entry->successors.size() == 2);
	REQUIRE(thenBB->predecessors.size() == 1);
	REQUIRE(elseBB->predecessors.size() == 1);

	builder.setInsertPoint(thenBB);
	builder.createBr(mergeBB);

	builder.setInsertPoint(elseBB);
	builder.createBr(mergeBB);

	REQUIRE(mergeBB->predecessors.size() == 2);

	builder.setInsertPoint(mergeBB);
	builder.createRetVoid();
}

TEST_CASE("MIR builder memory operations", "[mir][builder]") {
	MIRModule mod("test");
	MIRBuilder builder(&mod);

	(void)builder.createFunction("memory", MIRType::getVoid(), {});
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	SECTION("alloca and load/store") {
		auto *alloc = builder.createAlloca(MIRType::getInt32());
		REQUIRE(alloc != nullptr);
		REQUIRE(alloc->type == MIRType::getPtr());

		auto *val = MIRValue::createConstantInt(MIRType::getInt32(), 42);
		builder.createStore(val, alloc);

		auto *loaded = builder.createLoad(MIRType::getInt32(), alloc);
		REQUIRE(loaded != nullptr);
		REQUIRE(loaded->type == MIRType::getInt32());
	}
}

TEST_CASE("MIR builder function calls", "[mir][builder]") {
	MIRModule mod("test");
	MIRBuilder builder(&mod);

	// Create a function to call
	auto *callee = builder.createFunction("add", MIRType::getInt32(), {});
	callee->addParameter(MIRType::getInt32(), "a");
	callee->addParameter(MIRType::getInt32(), "b");

	// Create caller
	(void)builder.createFunction("main", MIRType::getInt32(), {});
	auto *entry = builder.createBasicBlock("entry");
	builder.setInsertPoint(entry);

	std::vector<MIRValue *> args = {
		MIRValue::createConstantInt(MIRType::getInt32(), 1),
		MIRValue::createConstantInt(MIRType::getInt32(), 2)};

	auto *result = builder.createCall(callee, args);
	REQUIRE(result != nullptr);
	REQUIRE(result->type == MIRType::getInt32());

	builder.createRet(result);
}
