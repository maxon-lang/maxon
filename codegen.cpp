#include "codegen.h"
#include <llvm/IR/Verifier.h>
#include <llvm/IR/LegacyPassManager.h>
#include <llvm/Support/FileSystem.h>
#include <llvm/Support/TargetSelect.h>
#include <llvm/MC/TargetRegistry.h>
#include <llvm/Target/TargetMachine.h>
#include <llvm/Target/TargetOptions.h>
#include <stdexcept>
#include <iostream>

CodeGenerator::CodeGenerator(const std::string& moduleName)
    : builder(context), module(std::make_unique<llvm::Module>(moduleName, context)) {}

llvm::AllocaInst* CodeGenerator::createEntryBlockAlloca(llvm::Function* function,
                                                         const std::string& varName) {
    llvm::IRBuilder<> tmpBuilder(&function->getEntryBlock(),
                                  function->getEntryBlock().begin());
    return tmpBuilder.CreateAlloca(llvm::Type::getInt32Ty(context), nullptr, varName);
}

llvm::Value* CodeGenerator::generateExpr(ExprAST* expr) {
    if (auto* numExpr = dynamic_cast<NumberExprAST*>(expr)) {
        return llvm::ConstantInt::get(context, llvm::APInt(32, numExpr->value, true));
    }
    
    if (auto* varExpr = dynamic_cast<VariableExprAST*>(expr)) {
        llvm::AllocaInst* alloca = namedValues[varExpr->name];
        if (!alloca) {
            throw std::runtime_error("Unknown variable name: " + varExpr->name);
        }
        return builder.CreateLoad(llvm::Type::getInt32Ty(context), alloca, varExpr->name);
    }
    
    if (auto* binExpr = dynamic_cast<BinaryExprAST*>(expr)) {
        llvm::Value* left = generateExpr(binExpr->left.get());
        llvm::Value* right = generateExpr(binExpr->right.get());
        
        if (!left || !right) {
            throw std::runtime_error("Failed to generate binary expression operands");
        }
        
        switch (binExpr->op) {
            case '+':
                return builder.CreateAdd(left, right, "addtmp");
            case '-':
                return builder.CreateSub(left, right, "subtmp");
            case '*':
                return builder.CreateMul(left, right, "multmp");
            case '/':
                return builder.CreateSDiv(left, right, "divtmp");
            case '>':
                return builder.CreateICmpSGT(left, right, "cmptmp");
            case '<':
                return builder.CreateICmpSLT(left, right, "cmptmp");
            case 'G': // >=
                return builder.CreateICmpSGE(left, right, "cmptmp");
            case 'L': // <=
                return builder.CreateICmpSLE(left, right, "cmptmp");
            case '=': // equality
                return builder.CreateICmpEQ(left, right, "cmptmp");
            default:
                throw std::runtime_error("Unknown binary operator");
        }
    }
    
    throw std::runtime_error("Unknown expression type");
}

void CodeGenerator::generateStmt(StmtAST* stmt, llvm::Function* function) {
    if (auto* varDecl = dynamic_cast<VarDeclStmtAST*>(stmt)) {
        llvm::Value* initVal = generateExpr(varDecl->initializer.get());
        if (!initVal) {
            throw std::runtime_error("Failed to generate variable initializer");
        }
        
        llvm::AllocaInst* alloca = createEntryBlockAlloca(function, varDecl->name);
        builder.CreateStore(initVal, alloca);
        namedValues[varDecl->name] = alloca;
        return;
    }
    
    if (auto* assign = dynamic_cast<AssignStmtAST*>(stmt)) {
        llvm::Value* val = generateExpr(assign->value.get());
        if (!val) {
            throw std::runtime_error("Failed to generate assignment value");
        }
        
        llvm::AllocaInst* alloca = namedValues[assign->name];
        if (!alloca) {
            throw std::runtime_error("Unknown variable name: " + assign->name);
        }
        
        builder.CreateStore(val, alloca);
        return;
    }
    
    if (auto* ifStmt = dynamic_cast<IfStmtAST*>(stmt)) {
        llvm::Value* condVal = generateExpr(ifStmt->condition.get());
        if (!condVal) {
            throw std::runtime_error("Failed to generate if condition");
        }
        
        // Convert condition to bool if it's not already an i1
        if (condVal->getType() != builder.getInt1Ty()) {
            condVal = builder.CreateICmpNE(
                condVal, llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)), "ifcond");
        }
        
        llvm::BasicBlock* thenBB = llvm::BasicBlock::Create(context, "then", function);
        llvm::BasicBlock* elseBB = llvm::BasicBlock::Create(context, "else");
        llvm::BasicBlock* mergeBB = llvm::BasicBlock::Create(context, "ifcont");
        
        if (ifStmt->elseBody.empty()) {
            builder.CreateCondBr(condVal, thenBB, mergeBB);
        } else {
            builder.CreateCondBr(condVal, thenBB, elseBB);
        }
        
        // Generate then block
        builder.SetInsertPoint(thenBB);
        for (auto& s : ifStmt->thenBody) {
            generateStmt(s.get(), function);
        }
        builder.CreateBr(mergeBB);
        
        // Generate else block
        if (!ifStmt->elseBody.empty()) {
            function->insert(function->end(), elseBB);
            builder.SetInsertPoint(elseBB);
            for (auto& s : ifStmt->elseBody) {
                generateStmt(s.get(), function);
            }
            builder.CreateBr(mergeBB);
        }
        
        // Generate merge block
        function->insert(function->end(), mergeBB);
        builder.SetInsertPoint(mergeBB);
        return;
    }
    
    if (auto* whileStmt = dynamic_cast<WhileStmtAST*>(stmt)) {
        llvm::BasicBlock* condBB = llvm::BasicBlock::Create(context, "whilecond", function);
        llvm::BasicBlock* loopBB = llvm::BasicBlock::Create(context, "loop");
        llvm::BasicBlock* afterBB = llvm::BasicBlock::Create(context, "afterloop");
        
        // Jump to condition block
        builder.CreateBr(condBB);
        
        // Generate condition block
        builder.SetInsertPoint(condBB);
        llvm::Value* condVal = generateExpr(whileStmt->condition.get());
        if (!condVal) {
            throw std::runtime_error("Failed to generate while condition");
        }
        
        // Convert condition to bool if it's not already an i1
        if (condVal->getType() != builder.getInt1Ty()) {
            condVal = builder.CreateICmpNE(
                condVal, llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)), "loopcond");
        }
        
        builder.CreateCondBr(condVal, loopBB, afterBB);
        
        // Generate loop body
        function->insert(function->end(), loopBB);
        builder.SetInsertPoint(loopBB);
        for (auto& s : whileStmt->body) {
            generateStmt(s.get(), function);
        }
        builder.CreateBr(condBB);
        
        // Generate after block
        function->insert(function->end(), afterBB);
        builder.SetInsertPoint(afterBB);
        return;
    }
    
    if (auto* retStmt = dynamic_cast<ReturnStmtAST*>(stmt)) {
        llvm::Value* retVal = generateExpr(retStmt->value.get());
        if (!retVal) {
            throw std::runtime_error("Failed to generate return value");
        }
        builder.CreateRet(retVal);
        return;
    }
    
    throw std::runtime_error("Unknown statement type");
}

void CodeGenerator::generateFunction(FunctionAST* func) {
    // Create function type
    llvm::Type* returnType = llvm::Type::getInt32Ty(context);
    llvm::FunctionType* funcType = llvm::FunctionType::get(returnType, false);
    
    // Create function
    llvm::Function* function = llvm::Function::Create(
        funcType, llvm::Function::ExternalLinkage, func->name, module.get());
    
    // Create entry block
    llvm::BasicBlock* entry = llvm::BasicBlock::Create(context, "entry", function);
    builder.SetInsertPoint(entry);
    
    // Clear named values for new function
    namedValues.clear();
    
    // Generate function body
    for (auto& stmt : func->body) {
        generateStmt(stmt.get(), function);
    }
    
    // Verify function
    if (llvm::verifyFunction(*function, &llvm::errs())) {
        throw std::runtime_error("Function verification failed for: " + func->name);
    }
}

void CodeGenerator::generate(ProgramAST* program) {
    for (auto& func : program->functions) {
        generateFunction(func.get());
    }
}

void CodeGenerator::printIR() {
    module->print(llvm::outs(), nullptr);
}

void CodeGenerator::writeObjectFile(const std::string& filename) {
    // Initialize targets
    llvm::InitializeAllTargetInfos();
    llvm::InitializeAllTargets();
    llvm::InitializeAllTargetMCs();
    llvm::InitializeAllAsmParsers();
    llvm::InitializeAllAsmPrinters();
    
    // Get target triple - hardcode for x86-64 since that's what we're building for
    llvm::Triple targetTriple("x86_64-pc-windows-msvc");
    module->setTargetTriple(targetTriple);
    
    // Get target
    std::string error;
    auto target = llvm::TargetRegistry::lookupTarget(targetTriple.str(), error);
    
    if (!target) {
        throw std::runtime_error("Failed to lookup target: " + error);
    }
    
    // Configure target machine
    auto CPU = "generic";
    auto features = "";
    llvm::TargetOptions opt;
    auto targetMachine = target->createTargetMachine(
        targetTriple, CPU, features, opt, llvm::Reloc::PIC_);
    
    module->setDataLayout(targetMachine->createDataLayout());
    
    // Open output file
    std::error_code ec;
    llvm::raw_fd_ostream dest(filename, ec, llvm::sys::fs::OF_None);
    
    if (ec) {
        throw std::runtime_error("Could not open file: " + ec.message());
    }
    
    // Emit object file
    llvm::legacy::PassManager pass;
    auto fileType = llvm::CodeGenFileType::ObjectFile;
    
    if (targetMachine->addPassesToEmitFile(pass, dest, nullptr, fileType)) {
        throw std::runtime_error("TargetMachine can't emit a file of this type");
    }
    
    pass.run(*module);
    dest.flush();
    
    std::cout << "Object file written to " << filename << std::endl;
}

void CodeGenerator::linkExecutable(const std::string& objectFile, const std::string& exeFile) {
    // Use MSVC linker - need to use cmd.exe /c to properly handle quoted paths
    std::string vsPath = "C:\\Program Files\\Microsoft Visual Studio\\2022\\Community\\VC\\Tools\\MSVC\\14.44.35207";
    std::string linkerPath = vsPath + "\\bin\\Hostx64\\x64\\link.exe";
    std::string libPath = vsPath + "\\lib\\x64";
    
    // Also need Windows SDK libs
    std::string sdkLibPath = "C:\\Program Files (x86)\\Windows Kits\\10\\Lib\\10.0.22621.0\\um\\x64";
    std::string ucrtLibPath = "C:\\Program Files (x86)\\Windows Kits\\10\\Lib\\10.0.22621.0\\ucrt\\x64";
    
    std::string linkCmd = "cmd.exe /c \"\"" + linkerPath + "\" ";
    linkCmd += "/NOLOGO /MACHINE:X64 /SUBSYSTEM:CONSOLE ";
    linkCmd += "/LIBPATH:\"" + libPath + "\" ";
    linkCmd += "/LIBPATH:\"" + sdkLibPath + "\" ";
    linkCmd += "/LIBPATH:\"" + ucrtLibPath + "\" ";
    linkCmd += "/OUT:\"" + exeFile + "\" \"" + objectFile + "\" ";
    linkCmd += "/DEFAULTLIB:libcmt.lib /DEFAULTLIB:oldnames.lib\"";
    
    std::cout << "Linking executable..." << std::endl;
    int result = system(linkCmd.c_str());
    
    if (result != 0) {
        throw std::runtime_error("Linking failed");
    }
    
    std::cout << "Executable written to " + exeFile << std::endl;
}
