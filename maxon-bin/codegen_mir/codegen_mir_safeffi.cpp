/**
 * MIR Code Generator - Safe FFI Implementation
 *
 * This file implements code generation for Safe FFI, which isolates
 * extern function calls in a subprocess for memory safety.
 */

#include "../codegen_mir.h"
#include <sstream>
#include <stdexcept>

//==============================================================================
// Type Tag Conversion
//==============================================================================

safeffi::TypeTag MIRCodeGenerator::getTypeTagFromString(const std::string &typeStr) {
	if (typeStr == "int" || typeStr == "i32") {
		return safeffi::TypeTag::Int32;
	} else if (typeStr == "float" || typeStr == "f64") {
		return safeffi::TypeTag::Float64;
	} else if (typeStr == "bool") {
		return safeffi::TypeTag::Bool;
	} else if (typeStr == "ptr") {
		return safeffi::TypeTag::Ptr;
	} else if (typeStr == "char") {
		return safeffi::TypeTag::Char;
	} else if (typeStr == "void" || typeStr.empty()) {
		return safeffi::TypeTag::Void;
	}
	// Default to ptr for unknown types (structs, etc.)
	return safeffi::TypeTag::Ptr;
}

//==============================================================================
// Extern Function Registration
//==============================================================================

void MIRCodeGenerator::registerExternFunction(FunctionAST *func) {
	if (!func->isExtern)
		return;

	std::string funcName = func->namespaceName.empty()
							   ? func->name
							   : func->namespaceName + "." + func->name;

	// Any extern declaration means we may need worker mode checking
	hasExternCalls = true;

	// Skip if already registered
	if (externFunctions.find(funcName) != externFunctions.end()) {
		return;
	}

	// Skip internal runtime functions - these are defined in our runtime library
	// and should NOT go through Safe FFI. They are regular function calls.
	// Internal functions start with "ffi_" or "__ffi_" and are implementation details.
	if (funcName.find("ffi_") == 0 || funcName.find("__ffi_") == 0) {
		logTrace("Skipping internal runtime function: " + funcName);
		return;
	}

	ExternFuncInfo info;
	info.id = nextExternId++;
	info.name = funcName;
	info.exportName = func->name; // Raw name for DLL lookup
	info.dllName = func->dllName;
	info.returnType = getTypeTagFromString(func->returnType);

	for (const auto &param : func->parameters) {
		info.paramTypes.push_back(getTypeTagFromString(param.type));
	}

	externFunctions[funcName] = info;

	logTrace("Registered extern function: " + funcName + " (id=" + std::to_string(info.id) + ", dll=" + info.dllName + ")");
}

//==============================================================================
// FFI Global Variables
//==============================================================================

void MIRCodeGenerator::generateFFIGlobals() {
	if (externFunctions.empty()) {
		logDetail("No extern functions - skipping FFI globals");
		return;
	}

	logDetail("Generating FFI globals for " + std::to_string(externFunctions.size()) + " extern functions");

	// Global: __ffi_initialized (i32) - 0 = not initialized, 1 = initialized
	builder->createGlobal("__ffi_initialized", mir::MIRType::getInt32());

	// Global: __ffi_shm_handle (ptr) - handle to shared memory
	builder->createGlobal("__ffi_shm_handle", mir::MIRType::getPtr());

	// Global: __ffi_shm_ptr (ptr) - pointer to mapped shared memory
	builder->createGlobal("__ffi_shm_ptr", mir::MIRType::getPtr());

	// Global: __ffi_sem_request (ptr) - semaphore for signaling worker
	builder->createGlobal("__ffi_sem_request", mir::MIRType::getPtr());

	// Global: __ffi_sem_response (ptr) - semaphore for worker response
	builder->createGlobal("__ffi_sem_response", mir::MIRType::getPtr());

	// Global: __ffi_worker_handle (ptr) - handle to worker process
	builder->createGlobal("__ffi_worker_handle", mir::MIRType::getPtr());

	// Global: __ffi_is_worker (i32) - 1 if this is the worker process
	builder->createGlobal("__ffi_is_worker", mir::MIRType::getInt32());
}

//==============================================================================
// FFI Initialization Function
//==============================================================================

void MIRCodeGenerator::generateFFIInitFunction() {
	if (externFunctions.empty()) {
		return;
	}

	logDetail("Generating __ffi_init function");

	// Create function: void __ffi_init()
	mir::MIRFunction *initFunc = module->createFunction("__ffi_init", mir::MIRType::getVoid());
	builder->setFunction(initFunc);

	mir::MIRBasicBlock *entry = initFunc->createBasicBlock("entry");
	mir::MIRBasicBlock *doInit = initFunc->createBasicBlock("do_init");
	mir::MIRBasicBlock *alreadyInit = initFunc->createBasicBlock("already_init");

	builder->setInsertPoint(entry);

	// Check if already initialized
	mir::MIRValue *initAddr = mir::MIRValue::createGlobal(mir::MIRType::getPtr(), "__ffi_initialized");
	mir::MIRValue *flagVal = builder->createLoad(mir::MIRType::getInt32(), initAddr, "init_flag");
	mir::MIRValue *zero = builder->getInt32(0);
	mir::MIRValue *isInit = builder->createICmpNe(flagVal, zero, "is_init");
	builder->createCondBr(isInit, alreadyInit, doInit);

	// Already initialized - just return
	builder->setInsertPoint(alreadyInit);
	builder->createRetVoid();

	// Do initialization
	builder->setInsertPoint(doInit);

	// Set initialized flag
	mir::MIRValue *one = builder->getInt32(1);
	builder->createStore(one, initAddr);

	// Generate unique names for IPC resources based on process ID
	// For now, use fixed names (will be made unique later)
	// TODO: Use GetCurrentProcessId to make names unique

	// Create shared memory
	// ptr handle = ffi_create_shared_memory(4096, "MaxonFFI_SHM")
	mir::MIRFunction *createShmFunc = module->getFunction("ffi_create_shared_memory");
	if (!createShmFunc) {
		createShmFunc = getOrDeclareFunction("ffi_create_shared_memory", mir::MIRType::getPtr(),
											 {mir::MIRType::getInt32(), mir::MIRType::getPtr()});
	}

	// For now, just mark as initialized
	// Full IPC setup will be implemented when the worker loop is ready

	builder->createRetVoid();
}

//==============================================================================
// Safe FFI Call Generation
//==============================================================================

mir::MIRValue *MIRCodeGenerator::generateSafeFFICall(
	const std::string &funcName,
	const std::vector<mir::MIRValue *> &args,
	mir::MIRType *returnType) {

	auto it = externFunctions.find(funcName);
	if (it == externFunctions.end()) {
		throw std::runtime_error("Unknown extern function: " + funcName);
	}

	const ExternFuncInfo &info = it->second;
	hasExternCalls = true;

	logTrace("Generating safe FFI call to: " + funcName + " (id=" + std::to_string(info.id) + ")");

	// Get the current function to add error handling blocks
	mir::MIRFunction *currentFunc = builder->getFunction();

	// Create basic blocks for error handling
	mir::MIRBasicBlock *initSuccess = currentFunc->createBasicBlock("ffi_init_ok");
	mir::MIRBasicBlock *initFail = currentFunc->createBasicBlock("ffi_init_fail");

	// Ensure FFI parent side is initialized (lazy init)
	mir::MIRFunction *parentInitFunc = getOrDeclareFunction("__ffi_parent_init",
															mir::MIRType::getInt32(), {});
	mir::MIRValue *initResult = builder->createCall(parentInitFunc, {}, "ffi_init_result");

	// Check if init succeeded (returns 0 on success)
	mir::MIRValue *zero = builder->getInt32(0);
	mir::MIRValue *initOk = builder->createICmpEq(initResult, zero, "init_ok");
	builder->createCondBr(initOk, initSuccess, initFail);

	// Init failed - exit with error code 99
	builder->setInsertPoint(initFail);
	mir::MIRFunction *exitFunc = getOrDeclareFunction("exit", mir::MIRType::getVoid(), {mir::MIRType::getInt32()});
	builder->createCall(exitFunc, {builder->getInt32(99)});
	builder->createRetVoid(); // exit() never returns, but we need a terminator

	// Init succeeded - continue with FFI call
	builder->setInsertPoint(initSuccess);

	// Get shared memory pointer from global
	mir::MIRValue *shmGlobal = mir::MIRValue::createGlobal(mir::MIRType::getPtr(), "ffi_parent_shared_mem");
	mir::MIRValue *shmPtr = builder->createLoad(mir::MIRType::getPtr(), shmGlobal, "shm_ptr");

	// Shared memory layout:
	//   Offset 0:  i32 command (0=exit, 1=call)
	//   Offset 4:  i32 function_id
	//   Offset 8:  i32 arg_count
	//   Offset 12: i32 status (0=ok, 1=error, 2=unknown_function)
	//   Offset 16: [16 x i64] args
	//   Offset 144: i64 return_value

	// Write command = 1 (call)
	mir::MIRValue *cmdPtr = builder->createGEP(mir::MIRType::getInt8(), shmPtr,
											   {builder->getInt64(0)}, "cmd_ptr");
	builder->createStore(builder->getInt32(1), cmdPtr);

	// Write function ID
	mir::MIRValue *funcIdPtr = builder->createGEP(mir::MIRType::getInt8(), shmPtr,
												  {builder->getInt64(4)}, "func_id_ptr");
	builder->createStore(builder->getInt32(static_cast<int32_t>(info.id)), funcIdPtr);

	// Write arg count
	mir::MIRValue *argCountPtr = builder->createGEP(mir::MIRType::getInt8(), shmPtr,
													{builder->getInt64(8)}, "arg_count_ptr");
	builder->createStore(builder->getInt32(static_cast<int32_t>(args.size())), argCountPtr);

	// Write arguments (at offset 16, each arg is 8 bytes)
	for (size_t i = 0; i < args.size() && i < 16; i++) {
		mir::MIRValue *argPtr = builder->createGEP(mir::MIRType::getInt8(), shmPtr,
												   {builder->getInt64(16 + static_cast<int64_t>(i) * 8)}, "arg_ptr_" + std::to_string(i));

		mir::MIRValue *argVal = args[i];
		// Convert to i64 for storage
		if (argVal->type->kind == mir::MIRTypeKind::Int32) {
			argVal = builder->createZExt(argVal, mir::MIRType::getInt64(), "arg_i64_" + std::to_string(i));
		} else if (argVal->type->kind == mir::MIRTypeKind::Float64) {
			argVal = builder->createBitcast(argVal, mir::MIRType::getInt64(), "arg_i64_" + std::to_string(i));
		} else if (argVal->type->kind == mir::MIRTypeKind::Ptr) {
			argVal = builder->createPtrToInt(argVal, mir::MIRType::getInt64(), "arg_i64_" + std::to_string(i));
		}
		// Int64 is already the right type

		builder->createStore(argVal, argPtr);
	}

	// Signal request semaphore
	mir::MIRValue *reqSemGlobal = mir::MIRValue::createGlobal(mir::MIRType::getPtr(), "ffi_parent_req_sem");
	mir::MIRValue *reqSem = builder->createLoad(mir::MIRType::getPtr(), reqSemGlobal, "req_sem");
	mir::MIRFunction *signalFunc = getOrDeclareFunction("ffi_signal_semaphore",
														mir::MIRType::getInt1(), {mir::MIRType::getPtr()});
	builder->createCall(signalFunc, {reqSem});

	// Wait on response semaphore (INFINITE timeout = -1)
	mir::MIRValue *rspSemGlobal = mir::MIRValue::createGlobal(mir::MIRType::getPtr(), "ffi_parent_rsp_sem");
	mir::MIRValue *rspSem = builder->createLoad(mir::MIRType::getPtr(), rspSemGlobal, "rsp_sem");
	mir::MIRFunction *waitFunc = getOrDeclareFunction("ffi_wait_semaphore",
													  mir::MIRType::getInt32(), {mir::MIRType::getPtr(), mir::MIRType::getInt32()});
	builder->createCall(waitFunc, {rspSem, builder->getInt32(-1)});

	// Check status field (offset 12): 0=success, 100=DLL load failed, 101=function not found
	mir::MIRValue *statusPtr = builder->createGEP(mir::MIRType::getInt8(), shmPtr,
												  {builder->getInt64(12)}, "status_ptr");
	mir::MIRValue *status = builder->createLoad(mir::MIRType::getInt32(), statusPtr, "ffi_status");

	// Create blocks for error handling
	mir::MIRBasicBlock *callSuccess = currentFunc->createBasicBlock("ffi_call_ok");
	mir::MIRBasicBlock *checkError = currentFunc->createBasicBlock("ffi_check_error");
	mir::MIRBasicBlock *dllError = currentFunc->createBasicBlock("ffi_dll_error");
	mir::MIRBasicBlock *funcError = currentFunc->createBasicBlock("ffi_func_error");

	// Check for success (status == 0)
	mir::MIRValue *statusOk = builder->createICmpEq(status, builder->getInt32(0), "status_ok");
	builder->createCondBr(statusOk, callSuccess, checkError);

	// Check which error - status 100 = DLL error, status 101 = function not found
	builder->setInsertPoint(checkError);
	mir::MIRValue *isDllError = builder->createICmpEq(status, builder->getInt32(100), "is_dll_error");
	builder->createCondBr(isDllError, dllError, funcError);

	// DLL load error - exit code 100
	builder->setInsertPoint(dllError);
	{
		std::string errorMsg = "FFI Error: Failed to load DLL '" + info.dllName + ".dll'\n";
		std::string errorGlobalName = "__ffi_dll_error_" + std::to_string(info.id);
		builder->createStringConstant(errorGlobalName, errorMsg);
		mir::MIRFunction *writeStdout = getOrDeclareFunction("write_stdout", mir::MIRType::getInt32(),
															 {mir::MIRType::getPtr(), mir::MIRType::getInt32()});
		mir::MIRValue *errorMsgPtr = mir::MIRValue::createGlobal(mir::MIRType::getPtr(), errorGlobalName);
		builder->createCall(writeStdout, {errorMsgPtr, builder->getInt32(static_cast<int32_t>(errorMsg.size()))});
		builder->createCall(exitFunc, {builder->getInt32(100)});
		builder->createRetVoid();
	}

	// Function not found error - exit code 101
	builder->setInsertPoint(funcError);
	{
		std::string errorMsg = "FFI Error: Function '" + info.exportName + "' not found in '" + info.dllName + ".dll'\n";
		std::string errorGlobalName = "__ffi_func_error_" + std::to_string(info.id);
		builder->createStringConstant(errorGlobalName, errorMsg);
		mir::MIRFunction *writeStdout = getOrDeclareFunction("write_stdout", mir::MIRType::getInt32(),
															 {mir::MIRType::getPtr(), mir::MIRType::getInt32()});
		mir::MIRValue *errorMsgPtr = mir::MIRValue::createGlobal(mir::MIRType::getPtr(), errorGlobalName);
		builder->createCall(writeStdout, {errorMsgPtr, builder->getInt32(static_cast<int32_t>(errorMsg.size()))});
		builder->createCall(exitFunc, {builder->getInt32(101)});
		builder->createRetVoid();
	}

	// Call succeeded - continue with result
	builder->setInsertPoint(callSuccess);

	// Read return value from shared memory (offset 144)
	mir::MIRValue *resultPtr = builder->createGEP(mir::MIRType::getInt8(), shmPtr,
												  {builder->getInt64(144)}, "result_ptr");
	mir::MIRValue *resultI64 = builder->createLoad(mir::MIRType::getInt64(), resultPtr, "result_i64");

	// Convert return value to the expected type
	mir::MIRValue *result = resultI64;
	if (returnType->kind == mir::MIRTypeKind::Void) {
		return nullptr;
	} else if (returnType->kind == mir::MIRTypeKind::Int32) {
		result = builder->createTrunc(resultI64, mir::MIRType::getInt32(), "result_i32");
	} else if (returnType->kind == mir::MIRTypeKind::Float64) {
		result = builder->createBitcast(resultI64, mir::MIRType::getFloat64(), "result_f64");
	} else if (returnType->kind == mir::MIRTypeKind::Ptr) {
		result = builder->createIntToPtr(resultI64, mir::MIRType::getPtr(), "result_ptr");
	}
	// Int64 is already the right type

	return result;
}

//==============================================================================
// FFI Cleanup
//==============================================================================

void MIRCodeGenerator::generateFFICleanup() {
	if (externFunctions.empty()) {
		return;
	}

	logDetail("Generating __ffi_cleanup function");

	// Create function: void __ffi_cleanup()
	mir::MIRFunction *cleanupFunc = module->createFunction("__ffi_cleanup", mir::MIRType::getVoid());
	builder->setFunction(cleanupFunc);

	mir::MIRBasicBlock *entry = cleanupFunc->createBasicBlock("entry");
	builder->setInsertPoint(entry);

	// TODO: Implement cleanup:
	// 1. Signal shutdown to worker
	// 2. Wait for worker to exit (with timeout)
	// 3. Close handles
	// 4. Unmap shared memory

	builder->createRetVoid();
}

//==============================================================================
// FFI Dispatch Function - Called by worker to dispatch to extern functions
// Uses LoadLibraryA and GetProcAddress to dynamically load and call functions
//==============================================================================

void MIRCodeGenerator::generateFFIDispatch() {
	if (externFunctions.empty()) {
		return;
	}

	logDetail("Generating __ffi_dispatch function with dynamic DLL loading");

	// First, create string constants for DLL names and function names
	// We'll need DLL name + ".dll" and the function name
	std::map<std::string, mir::MIRGlobal *> dllNameGlobals;	 // dllName -> global for "dllName.dll"
	std::map<std::string, mir::MIRGlobal *> funcNameGlobals; // funcName -> global for function name

	for (const auto &entry : externFunctions) {
		const ExternFuncInfo &info = entry.second;

		// Create DLL name constant (dllName + ".dll")
		if (dllNameGlobals.find(info.dllName) == dllNameGlobals.end()) {
			std::string dllWithExt = info.dllName + ".dll";
			std::string globalName = "__ffi_dll_" + info.dllName;
			mir::MIRGlobal *dllGlobal = builder->createStringConstant(globalName, dllWithExt);
			dllNameGlobals[info.dllName] = dllGlobal;
		}

		// Create function name constant (use exportName for DLL lookup)
		std::string globalName = "__ffi_func_" + std::to_string(info.id);
		mir::MIRGlobal *funcGlobal = builder->createStringConstant(globalName, info.exportName);
		funcNameGlobals[info.name] = funcGlobal;
	}

	// Declare LoadLibraryA and GetProcAddress
	mir::MIRFunction *loadLibraryFunc = getOrDeclareFunction("LoadLibraryA", mir::MIRType::getPtr(),
															 {mir::MIRType::getPtr()});
	mir::MIRFunction *getProcAddrFunc = getOrDeclareFunction("GetProcAddress", mir::MIRType::getPtr(),
															 {mir::MIRType::getPtr(), mir::MIRType::getPtr()}); // Shared memory layout:
	//   Offset 0:  i32 command (0=exit, 1=call)
	//   Offset 4:  i32 function_id
	//   Offset 8:  i32 arg_count
	//   Offset 12: i32 status (0=ok, 1=error, 2=unknown_function)
	//   Offset 16: [16 x i64] args
	//   Offset 144: i64 return_value

	std::vector<std::pair<mir::MIRType *, std::string>> params = {
		{mir::MIRType::getPtr(), "shm_ptr"},
		{mir::MIRType::getInt32(), "func_id"}};

	mir::MIRFunction *dispatchFunc = builder->createFunction("__ffi_dispatch", mir::MIRType::getVoid(), params);

	mir::MIRBasicBlock *entry = dispatchFunc->createBasicBlock("entry");
	mir::MIRBasicBlock *unknownFunc = dispatchFunc->createBasicBlock("unknown_func");
	mir::MIRBasicBlock *done = dispatchFunc->createBasicBlock("done");

	builder->setInsertPoint(entry);

	mir::MIRValue *shmPtr = dispatchFunc->parameters[0];
	mir::MIRValue *funcId = dispatchFunc->parameters[1];

	// Create a basic block for each extern function
	std::vector<std::pair<uint32_t, mir::MIRBasicBlock *>> funcBlocks;
	for (const auto &entry : externFunctions) {
		const ExternFuncInfo &info = entry.second;
		mir::MIRBasicBlock *bb = dispatchFunc->createBasicBlock("call_" + std::to_string(info.id));
		funcBlocks.push_back({info.id, bb});
	}

	// Create switch on function ID using a chain of if-else
	mir::MIRBasicBlock *currentBlock = entry;

	for (const auto &pair : funcBlocks) {
		uint32_t id = pair.first;
		mir::MIRBasicBlock *bb = pair.second;

		builder->setInsertPoint(currentBlock);

		mir::MIRValue *idVal = builder->getInt32(static_cast<int32_t>(id));
		mir::MIRValue *isMatch = builder->createICmpEq(funcId, idVal, "is_func_" + std::to_string(id));

		mir::MIRBasicBlock *nextCheck = dispatchFunc->createBasicBlock("check_" + std::to_string(id + 1));
		builder->createCondBr(isMatch, bb, nextCheck);

		currentBlock = nextCheck;
	}

	// Last check falls through to unknown function
	builder->setInsertPoint(currentBlock);
	builder->createBr(unknownFunc);

	// Generate call blocks for each extern function
	for (const auto &entry : externFunctions) {
		const std::string &name = entry.first;
		const ExternFuncInfo &info = entry.second;

		auto bbIt = std::find_if(funcBlocks.begin(), funcBlocks.end(),
								 [id = info.id](const auto &p) { return p.first == id; });
		if (bbIt == funcBlocks.end())
			continue;

		mir::MIRBasicBlock *loadLibBlock = dispatchFunc->createBasicBlock("load_lib_" + std::to_string(info.id));
		mir::MIRBasicBlock *getAddrBlock = dispatchFunc->createBasicBlock("get_addr_" + std::to_string(info.id));
		mir::MIRBasicBlock *callBlock = dispatchFunc->createBasicBlock("do_call_" + std::to_string(info.id));
		mir::MIRBasicBlock *dllErrorBlock = dispatchFunc->createBasicBlock("dll_error_" + std::to_string(info.id));
		mir::MIRBasicBlock *funcErrorBlock = dispatchFunc->createBasicBlock("func_error_" + std::to_string(info.id));

		builder->setInsertPoint(bbIt->second);
		builder->createBr(loadLibBlock);

		// Load the DLL
		builder->setInsertPoint(loadLibBlock);
		mir::MIRGlobal *dllGlobal = dllNameGlobals[info.dllName];
		mir::MIRValue *dllNamePtr = mir::MIRValue::createGlobal(mir::MIRType::getPtr(), dllGlobal->name);
		mir::MIRValue *dllHandle = builder->createCall(loadLibraryFunc, {dllNamePtr}, "dll_handle");

		// Check if LoadLibrary succeeded - if not, go to DLL error block
		mir::MIRValue *nullPtr = builder->getNull();
		mir::MIRValue *loadOk = builder->createICmpNe(dllHandle, nullPtr, "load_ok");
		builder->createCondBr(loadOk, getAddrBlock, dllErrorBlock);

		// Get the function address
		builder->setInsertPoint(getAddrBlock);
		mir::MIRGlobal *funcGlobal = funcNameGlobals[name];
		mir::MIRValue *funcNamePtr = mir::MIRValue::createGlobal(mir::MIRType::getPtr(), funcGlobal->name);
		mir::MIRValue *funcAddr = builder->createCall(getProcAddrFunc, {dllHandle, funcNamePtr}, "func_addr");

		// Check if GetProcAddress succeeded - if not, go to function error block
		mir::MIRValue *addrOk = builder->createICmpNe(funcAddr, nullPtr, "addr_ok");
		builder->createCondBr(addrOk, callBlock, funcErrorBlock);

		// Prepare arguments and call the function
		builder->setInsertPoint(callBlock);

		// Load arguments from shared memory (offset 16 = args array)
		mir::MIRValue *argsBase = builder->createGEP(mir::MIRType::getInt8(), shmPtr,
													 {builder->getInt64(16)}, "args_base");

		std::vector<mir::MIRValue *> callArgs;
		std::vector<mir::MIRType *> paramTypes;
		for (size_t i = 0; i < info.paramTypes.size(); i++) {
			// Get pointer to arg[i] (each arg is 8 bytes)
			mir::MIRValue *argOffset = builder->getInt64(static_cast<int64_t>(i * 8));
			mir::MIRValue *argPtr = builder->createGEP(mir::MIRType::getInt8(), argsBase,
													   {argOffset}, "arg_ptr_" + std::to_string(i));

			// Load the argument value (as i64)
			mir::MIRValue *argVal = builder->createLoad(mir::MIRType::getInt64(), argPtr,
														"arg_" + std::to_string(i));

			// Convert to the expected type if needed
			safeffi::TypeTag paramType = info.paramTypes[i];
			if (paramType == safeffi::TypeTag::Int32) {
				argVal = builder->createTrunc(argVal, mir::MIRType::getInt32(), "arg_i32_" + std::to_string(i));
				paramTypes.push_back(mir::MIRType::getInt32());
			} else if (paramType == safeffi::TypeTag::Float64) {
				argVal = builder->createBitcast(argVal, mir::MIRType::getFloat64(), "arg_f64_" + std::to_string(i));
				paramTypes.push_back(mir::MIRType::getFloat64());
			} else if (paramType == safeffi::TypeTag::Ptr) {
				argVal = builder->createIntToPtr(argVal, mir::MIRType::getPtr(), "arg_ptr_" + std::to_string(i));
				paramTypes.push_back(mir::MIRType::getPtr());
			} else {
				// Int64
				paramTypes.push_back(mir::MIRType::getInt64());
			}

			callArgs.push_back(argVal);
		}

		// Get the return type
		mir::MIRType *returnType = mir::MIRType::getVoid();
		if (info.returnType == safeffi::TypeTag::Int32) {
			returnType = mir::MIRType::getInt32();
		} else if (info.returnType == safeffi::TypeTag::Float64) {
			returnType = mir::MIRType::getFloat64();
		} else if (info.returnType == safeffi::TypeTag::Ptr) {
			returnType = mir::MIRType::getPtr();
		} else if (info.returnType == safeffi::TypeTag::Int32) {
			returnType = mir::MIRType::getInt64();
		}

		// Call through the function pointer
		mir::MIRValue *result = nullptr;
		if (returnType->kind != mir::MIRTypeKind::Void) {
			result = builder->createCallIndirect(funcAddr, returnType, paramTypes, callArgs, "call_result");
		} else {
			builder->createCallIndirect(funcAddr, returnType, paramTypes, callArgs);
		}

		// Write result back to shared memory (offset 144)
		if (result) {
			mir::MIRValue *resultPtr = builder->createGEP(mir::MIRType::getInt8(), shmPtr,
														  {builder->getInt64(144)}, "result_ptr");

			// Convert result to i64 for storage
			mir::MIRValue *resultI64 = result;
			if (returnType->kind == mir::MIRTypeKind::Int32) {
				resultI64 = builder->createZExt(result, mir::MIRType::getInt64(), "result_i64");
			} else if (returnType->kind == mir::MIRTypeKind::Float64) {
				resultI64 = builder->createBitcast(result, mir::MIRType::getInt64(), "result_i64");
			} else if (returnType->kind == mir::MIRTypeKind::Ptr) {
				resultI64 = builder->createPtrToInt(result, mir::MIRType::getInt64(), "result_i64");
			}

			builder->createStore(resultI64, resultPtr);
		}

		// Set status to 0 (success) - offset 12
		mir::MIRValue *statusPtr = builder->createGEP(mir::MIRType::getInt8(), shmPtr,
													  {builder->getInt64(12)}, "status_ptr");
		builder->createStore(builder->getInt32(0), statusPtr);

		builder->createBr(done);

		// DLL error block - set status to 100 (DLL load failed)
		builder->setInsertPoint(dllErrorBlock);
		mir::MIRValue *dllErrorStatusPtr = builder->createGEP(mir::MIRType::getInt8(), shmPtr,
															  {builder->getInt64(12)}, "status_ptr");
		builder->createStore(builder->getInt32(100), dllErrorStatusPtr);
		builder->createBr(done);

		// Function error block - set status to 101 (function not found)
		builder->setInsertPoint(funcErrorBlock);
		mir::MIRValue *funcErrorStatusPtr = builder->createGEP(mir::MIRType::getInt8(), shmPtr,
															   {builder->getInt64(12)}, "status_ptr");
		builder->createStore(builder->getInt32(101), funcErrorStatusPtr);
		builder->createBr(done);
	}

	// Unknown function ID - set status to 102
	builder->setInsertPoint(unknownFunc);
	mir::MIRValue *statusPtrUnknown = builder->createGEP(mir::MIRType::getInt8(), shmPtr,
														 {builder->getInt64(12)}, "status_ptr");
	builder->createStore(builder->getInt32(102), statusPtrUnknown);
	builder->createBr(done);

	// Done - return void
	builder->setInsertPoint(done);
	builder->createRetVoid();
}

//==============================================================================
// FFI Worker Main - The main loop for the worker subprocess
//==============================================================================

void MIRCodeGenerator::generateFFIWorkerMain() {
	// The worker main function is provided by the runtime library (runtime_windows.mir)
	// It handles opening IPC resources, the worker loop, and dispatch.
	// We only generate the __ffi_dispatch function which the runtime calls.
	//
	// This function is kept as a no-op for backwards compatibility but all
	// worker functionality is in the runtime.
	logDetail("FFI worker main is provided by runtime library");
}

//==============================================================================
// FFI Call Function (generic wrapper)
//==============================================================================

void MIRCodeGenerator::generateFFICallFunction() {
	if (externFunctions.empty()) {
		return;
	}

	// This function will be generated as part of the IPC implementation
	// It's the generic wrapper that handles serialization/deserialization

	logDetail("FFI call function will be generated with full IPC implementation");
}
