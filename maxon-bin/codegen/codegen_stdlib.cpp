#include "../codegen.h"
#include "codegen_types.h"
#include "../lexer.h"
#include <llvm/IR/Constants.h>
#include <llvm/IR/DerivedTypes.h>
#include <llvm/IR/Function.h>
#include <llvm/IR/Instructions.h>
#include <llvm/IR/Intrinsics.h>

void CodeGenerator::initStandardLibrary()
{
    // Check if print function already exists (to avoid double-initialization)
    if (module->getFunction("print"))
    {
        return;
    }

    // Get memset function declaration once for use throughout this function
    llvm::Function *memsetFunc = getOrDeclareMemset();

    // Declare Windows API functions for direct I/O (no CRT dependency)
    // ptr GetStdHandle(i32)
    llvm::FunctionType *getStdHandleType = llvm::FunctionType::get(
        llvm::PointerType::get(context, 0),
        {llvm::Type::getInt32Ty(context)},
        false);
    llvm::Function::Create(getStdHandleType, llvm::Function::ExternalLinkage,
                           "GetStdHandle", module.get());

    // i32 WriteFile(ptr, ptr, i32, ptr, ptr)
    llvm::FunctionType *writeFileType = llvm::FunctionType::get(
        llvm::Type::getInt32Ty(context),
        {
            llvm::PointerType::get(context, 0), // hFile
            llvm::PointerType::get(context, 0), // lpBuffer
            llvm::Type::getInt32Ty(context),    // nNumberOfBytesToWrite
            llvm::PointerType::get(context, 0), // lpNumberOfBytesWritten
            llvm::PointerType::get(context, 0)  // lpOverlapped
        },
        false);
    llvm::Function::Create(writeFileType, llvm::Function::ExternalLinkage,
                           "WriteFile", module.get());

    // Create print(int) -> int function
    llvm::FunctionType *printFuncType = llvm::FunctionType::get(
        llvm::Type::getInt32Ty(context),
        {llvm::Type::getInt32Ty(context)},
        false);
    llvm::Function *printFunc = llvm::Function::Create(
        printFuncType,
        llvm::Function::ExternalLinkage,
        "print",
        module.get());

    // Generate the body of the print function
    llvm::BasicBlock *entry = llvm::BasicBlock::Create(context, "entry", printFunc);
    llvm::BasicBlock *savedBlock = builder.GetInsertBlock();
    builder.SetInsertPoint(entry);

    llvm::Value *valueArg = printFunc->getArg(0);

    // Allocate buffers on stack
    llvm::Type *charType = llvm::Type::getInt8Ty(context);
    llvm::Type *bufferType = llvm::ArrayType::get(charType, 12);
    llvm::Type *tempType = llvm::ArrayType::get(charType, 12);

    llvm::Value *buffer = builder.CreateAlloca(bufferType, nullptr, "buffer");
    llvm::Value *temp = builder.CreateAlloca(tempType, nullptr, "temp");
    llvm::Value *bytesWritten = builder.CreateAlloca(llvm::Type::getInt32Ty(context), nullptr, "bytesWritten");

    // Initialize buffers to zero using runtime memset
    builder.CreateCall(memsetFunc, {buffer, llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 0), llvm::ConstantInt::get(llvm::Type::getInt64Ty(context), 12)});
    builder.CreateCall(memsetFunc, {temp, llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 0), llvm::ConstantInt::get(llvm::Type::getInt64Ty(context), 12)});

    // Check if value is zero
    llvm::BasicBlock *zeroCase = llvm::BasicBlock::Create(context, "zero_case", printFunc);
    llvm::BasicBlock *nonZeroCase = llvm::BasicBlock::Create(context, "non_zero", printFunc);
    llvm::BasicBlock *formatDone = llvm::BasicBlock::Create(context, "format_done", printFunc);

    llvm::Value *isZero = builder.CreateICmpEQ(valueArg, llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)));
    builder.CreateCondBr(isZero, zeroCase, nonZeroCase);

    // Zero case: buffer[0] = '0', length = 1
    builder.SetInsertPoint(zeroCase);
    llvm::Value *bufferZeroPtr = builder.CreateInBoundsGEP(bufferType, buffer,
                                                           {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), llvm::ConstantInt::get(context, llvm::APInt(32, 0))});
    builder.CreateStore(llvm::ConstantInt::get(charType, '0'), bufferZeroPtr);
    builder.CreateBr(formatDone);

    // Non-zero case: format the integer
    builder.SetInsertPoint(nonZeroCase);

    // Handle negative numbers
    llvm::BasicBlock *negativeCase = llvm::BasicBlock::Create(context, "negative", printFunc);
    llvm::BasicBlock *positiveCase = llvm::BasicBlock::Create(context, "positive", printFunc);
    llvm::BasicBlock *extractDigits = llvm::BasicBlock::Create(context, "extract_digits", printFunc);

    llvm::Value *isNegative = builder.CreateICmpSLT(valueArg, llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)));
    builder.CreateCondBr(isNegative, negativeCase, positiveCase);

    // Negative case: add '-' and negate value
    builder.SetInsertPoint(negativeCase);
    llvm::Value *bufferNegPtr = builder.CreateInBoundsGEP(bufferType, buffer,
                                                          {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), llvm::ConstantInt::get(context, llvm::APInt(32, 0))});
    builder.CreateStore(llvm::ConstantInt::get(charType, '-'), bufferNegPtr);
    llvm::Value *absValue = builder.CreateNeg(valueArg);
    llvm::Value *startOffsetNeg = llvm::ConstantInt::get(context, llvm::APInt(32, 1, true));
    builder.CreateBr(extractDigits);

    // Positive case
    builder.SetInsertPoint(positiveCase);
    llvm::Value *startOffsetPos = llvm::ConstantInt::get(context, llvm::APInt(32, 0, true));
    builder.CreateBr(extractDigits);

    // Extract digits
    builder.SetInsertPoint(extractDigits);
    llvm::PHINode *valuePhi = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "value_abs");
    valuePhi->addIncoming(absValue, negativeCase);
    valuePhi->addIncoming(valueArg, positiveCase);
    llvm::PHINode *offsetPhi = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "start_offset");
    offsetPhi->addIncoming(startOffsetNeg, negativeCase);
    offsetPhi->addIncoming(startOffsetPos, positiveCase);

    // Digit extraction loop
    llvm::BasicBlock *digitLoop = llvm::BasicBlock::Create(context, "digit_loop", printFunc);
    llvm::BasicBlock *digitLoopBody = llvm::BasicBlock::Create(context, "digit_loop_body", printFunc);
    llvm::BasicBlock *reverseLoop = llvm::BasicBlock::Create(context, "reverse_loop", printFunc);
    llvm::BasicBlock *reverseBody = llvm::BasicBlock::Create(context, "reverse_body", printFunc);

    builder.CreateBr(digitLoop);

    // Digit loop header
    builder.SetInsertPoint(digitLoop);
    llvm::PHINode *remainingValue = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "remaining");
    remainingValue->addIncoming(valuePhi, extractDigits);
    llvm::PHINode *tempIdx = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "temp_idx");
    tempIdx->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)), extractDigits);

    llvm::Value *loopCond = builder.CreateICmpNE(remainingValue, llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)));
    builder.CreateCondBr(loopCond, digitLoopBody, reverseLoop);

    // Digit loop body: extract digit, store in temp
    builder.SetInsertPoint(digitLoopBody);
    llvm::Value *digit = builder.CreateSRem(remainingValue, llvm::ConstantInt::get(context, llvm::APInt(32, 10, true)));
    llvm::Value *digitChar = builder.CreateAdd(digit, llvm::ConstantInt::get(context, llvm::APInt(32, '0', true)));
    llvm::Value *digitChar8 = builder.CreateTrunc(digitChar, charType);

    llvm::Value *tempPtr = builder.CreateInBoundsGEP(tempType, temp,
                                                     {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), tempIdx});
    builder.CreateStore(digitChar8, tempPtr);

    llvm::Value *nextRemaining = builder.CreateSDiv(remainingValue, llvm::ConstantInt::get(context, llvm::APInt(32, 10, true)));
    llvm::Value *nextTempIdx = builder.CreateAdd(tempIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));

    remainingValue->addIncoming(nextRemaining, digitLoopBody);
    tempIdx->addIncoming(nextTempIdx, digitLoopBody);
    builder.CreateBr(digitLoop);

    // Reverse loop: copy from temp to buffer in reverse order
    builder.SetInsertPoint(reverseLoop);
    llvm::PHINode *reverseIdx = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "reverse_idx");
    reverseIdx->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)), digitLoop);

    // Pass through offsetPhi and tempIdx via PHI nodes
    llvm::PHINode *offsetInReverse = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "offset_in_reverse");
    offsetInReverse->addIncoming(offsetPhi, digitLoop);
    llvm::PHINode *digitCountInReverse = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "digit_count_in_reverse");
    digitCountInReverse->addIncoming(tempIdx, digitLoop);

    llvm::Value *reverseCond = builder.CreateICmpSLT(reverseIdx, digitCountInReverse);
    builder.CreateCondBr(reverseCond, reverseBody, formatDone);

    // Reverse body
    builder.SetInsertPoint(reverseBody);
    llvm::Value *srcIdx = builder.CreateSub(digitCountInReverse, reverseIdx);
    srcIdx = builder.CreateSub(srcIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));

    llvm::Value *srcPtr = builder.CreateInBoundsGEP(tempType, temp,
                                                    {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), srcIdx});
    llvm::Value *digitValue = builder.CreateLoad(charType, srcPtr);

    llvm::Value *dstIdx = builder.CreateAdd(offsetInReverse, reverseIdx);
    llvm::Value *dstPtr = builder.CreateInBoundsGEP(bufferType, buffer,
                                                    {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), dstIdx});
    builder.CreateStore(digitValue, dstPtr);

    llvm::Value *nextReverseIdx = builder.CreateAdd(reverseIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    reverseIdx->addIncoming(nextReverseIdx, reverseBody);
    offsetInReverse->addIncoming(offsetInReverse, reverseBody);
    digitCountInReverse->addIncoming(digitCountInReverse, reverseBody);
    builder.CreateBr(reverseLoop);

    // Format done - write to stdout
    builder.SetInsertPoint(formatDone);

    // Create PHI nodes for values from different predecessors
    llvm::PHINode *finalOffset = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "final_offset");
    finalOffset->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)), zeroCase); // Zero case has offset 0
    finalOffset->addIncoming(offsetInReverse, reverseLoop);

    llvm::PHINode *finalDigitCount = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "final_digit_count");
    finalDigitCount->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)), zeroCase); // Zero case has 1 digit
    finalDigitCount->addIncoming(digitCountInReverse, reverseLoop);

    // Compute final length = offset + digit_count (works for both zero and non-zero cases)
    llvm::Value *finalLength = builder.CreateAdd(finalOffset, finalDigitCount);

    // Get stdout handle (-11)
    llvm::Function *getStdHandle = module->getFunction("GetStdHandle");
    llvm::Value *stdoutHandle = builder.CreateCall(getStdHandle,
                                                   {llvm::ConstantInt::get(context, llvm::APInt(32, -11, true))});

    // Get buffer pointer
    llvm::Value *bufferPtr = builder.CreateBitCast(buffer, llvm::PointerType::get(context, 0));

    // Call WriteFile
    llvm::Function *writeFile = module->getFunction("WriteFile");
    builder.CreateCall(writeFile, {stdoutHandle,
                                   bufferPtr,
                                   finalLength,
                                   bytesWritten,
                                   llvm::ConstantPointerNull::get(llvm::PointerType::get(context, 0))});

    // Write newline
    llvm::Constant *newlineStr = llvm::ConstantDataArray::getString(context, "\n", false);
    llvm::GlobalVariable *newlineVar = new llvm::GlobalVariable(
        *module,
        newlineStr->getType(),
        true,
        llvm::GlobalValue::PrivateLinkage,
        newlineStr,
        ".str.newline");
    llvm::Value *newlinePtr = builder.CreateBitCast(newlineVar, llvm::PointerType::get(context, 0));
    builder.CreateCall(writeFile, {stdoutHandle,
                                   newlinePtr,
                                   llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)),
                                   bytesWritten,
                                   llvm::ConstantPointerNull::get(llvm::PointerType::get(context, 0))});

    // Return 0
    builder.CreateRet(llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)));

    // Add attribute to prevent LLVM from converting memset calls to intrinsics
    printFunc->addFnAttr("no-builtin-memset");

    // Restore insert point
    if (savedBlock)
    {
        builder.SetInsertPoint(savedBlock);
    }

    // Create print_float(float, int) -> int function
    llvm::FunctionType *printFloatFuncType = llvm::FunctionType::get(
        llvm::Type::getInt32Ty(context),
        {llvm::Type::getDoubleTy(context), llvm::Type::getInt32Ty(context)},
        false);
    llvm::Function *printFloatFunc = llvm::Function::Create(
        printFloatFuncType,
        llvm::Function::ExternalLinkage,
        "print_float",
        module.get());

    // Generate the body of the print_float function
    llvm::BasicBlock *floatEntry = llvm::BasicBlock::Create(context, "entry", printFloatFunc);
    savedBlock = builder.GetInsertBlock();
    builder.SetInsertPoint(floatEntry);

    llvm::Value *floatValueArg = printFloatFunc->getArg(0);
    llvm::Value *precisionArg = printFloatFunc->getArg(1);

    // Store parameter to local variable and load it back to ensure proper handling
    llvm::Value *floatValueLocal = builder.CreateAlloca(llvm::Type::getDoubleTy(context), nullptr, "float_value_local");
    builder.CreateStore(floatValueArg, floatValueLocal);
    llvm::Value *floatValue = builder.CreateLoad(llvm::Type::getDoubleTy(context), floatValueLocal, "float_value");

    // Allocate buffer on stack (32 bytes for float)
    llvm::Type *floatBufferType = llvm::ArrayType::get(charType, 32);
    llvm::Value *floatBuffer = builder.CreateAlloca(floatBufferType, nullptr, "buffer");
    llvm::Value *floatBytesWritten = builder.CreateAlloca(llvm::Type::getInt32Ty(context), nullptr, "bytesWritten");

    // Initialize buffer to zero using runtime memset
    builder.CreateCall(memsetFunc, {floatBuffer, llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 0), llvm::ConstantInt::get(llvm::Type::getInt64Ty(context), 32)});

    // Check if value is zero (exactly 0.0)
    llvm::BasicBlock *floatZeroCase = llvm::BasicBlock::Create(context, "zero_case", printFloatFunc);
    llvm::BasicBlock *floatNonZeroCase = llvm::BasicBlock::Create(context, "non_zero", printFloatFunc);
    llvm::BasicBlock *floatFormatDone = llvm::BasicBlock::Create(context, "format_done", printFloatFunc);

    llvm::Value *floatIsZero = builder.CreateFCmpOEQ(floatValue, llvm::ConstantFP::get(context, llvm::APFloat(0.0)));
    builder.CreateCondBr(floatIsZero, floatZeroCase, floatNonZeroCase);

    // Zero case: format as "0.000000" (with precision decimal places)
    builder.SetInsertPoint(floatZeroCase);
    llvm::Value *zeroPtr0 = builder.CreateInBoundsGEP(floatBufferType, floatBuffer,
                                                      {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), llvm::ConstantInt::get(context, llvm::APInt(32, 0))});
    builder.CreateStore(llvm::ConstantInt::get(charType, '0'), zeroPtr0);
    llvm::Value *zeroPtr1 = builder.CreateInBoundsGEP(floatBufferType, floatBuffer,
                                                      {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), llvm::ConstantInt::get(context, llvm::APInt(32, 1))});
    builder.CreateStore(llvm::ConstantInt::get(charType, '.'), zeroPtr1);

    // Fill in precision decimal places with '0' (runtime loop)
    llvm::BasicBlock *zeroFillLoop = llvm::BasicBlock::Create(context, "zero_fill_loop", printFloatFunc);
    llvm::BasicBlock *zeroFillBody = llvm::BasicBlock::Create(context, "zero_fill_body", printFloatFunc);
    llvm::BasicBlock *zeroFillDone = llvm::BasicBlock::Create(context, "zero_fill_done", printFloatFunc);

    builder.CreateBr(zeroFillLoop);

    builder.SetInsertPoint(zeroFillLoop);
    llvm::PHINode *zeroFillIdx = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "zero_fill_idx");
    zeroFillIdx->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)), floatZeroCase);

    llvm::Value *zeroFillCond = builder.CreateICmpSLT(zeroFillIdx, precisionArg);
    builder.CreateCondBr(zeroFillCond, zeroFillBody, zeroFillDone);

    builder.SetInsertPoint(zeroFillBody);
    llvm::Value *zeroFillOffset = builder.CreateAdd(llvm::ConstantInt::get(context, llvm::APInt(32, 2, true)), zeroFillIdx);
    llvm::Value *zeroDecPtr = builder.CreateInBoundsGEP(floatBufferType, floatBuffer,
                                                        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), zeroFillOffset});
    builder.CreateStore(llvm::ConstantInt::get(charType, '0'), zeroDecPtr);

    llvm::Value *nextZeroFillIdx = builder.CreateAdd(zeroFillIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    zeroFillIdx->addIncoming(nextZeroFillIdx, zeroFillBody);
    builder.CreateBr(zeroFillLoop);

    builder.SetInsertPoint(zeroFillDone);
    llvm::Value *zeroLength = builder.CreateAdd(llvm::ConstantInt::get(context, llvm::APInt(32, 2, true)), precisionArg);
    builder.CreateBr(floatFormatDone);

    // Non-zero case: format the float
    builder.SetInsertPoint(floatNonZeroCase);
    llvm::Value *floatPos = builder.CreateAlloca(llvm::Type::getInt32Ty(context), nullptr, "pos");
    builder.CreateStore(llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)), floatPos);

    // Handle negative numbers
    llvm::BasicBlock *floatNegativeCase = llvm::BasicBlock::Create(context, "negative", printFloatFunc);
    llvm::BasicBlock *floatPositiveCase = llvm::BasicBlock::Create(context, "positive", printFloatFunc);
    llvm::BasicBlock *floatExtractInt = llvm::BasicBlock::Create(context, "extract_int", printFloatFunc);

    // Check if value is negative using fcmp (handles -0.0 correctly and avoids optimizer issues)
    llvm::Value *floatIsNegative = builder.CreateFCmpOLT(floatValue, llvm::ConstantFP::get(context, llvm::APFloat(0.0)));
    builder.CreateCondBr(floatIsNegative, floatNegativeCase, floatPositiveCase);

    // Negative case: add '-' and take absolute value
    builder.SetInsertPoint(floatNegativeCase);
    llvm::Value *negPtr = builder.CreateInBoundsGEP(floatBufferType, floatBuffer,
                                                    {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), llvm::ConstantInt::get(context, llvm::APInt(32, 0))});
    builder.CreateStore(llvm::ConstantInt::get(charType, '-'), negPtr);
    builder.CreateStore(llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)), floatPos);
    llvm::Function *fabsFn = llvm::Intrinsic::getOrInsertDeclaration(
        module.get(),
        llvm::Intrinsic::fabs,
        {llvm::Type::getDoubleTy(context)});
    llvm::Value *absFloatValue = builder.CreateCall(fabsFn, {floatValue});
    builder.CreateBr(floatExtractInt);

    // Positive case
    builder.SetInsertPoint(floatPositiveCase);
    builder.CreateBr(floatExtractInt);

    // Extract integer and fractional parts
    builder.SetInsertPoint(floatExtractInt);
    llvm::PHINode *workValue = builder.CreatePHI(llvm::Type::getDoubleTy(context), 2, "work_value");
    workValue->addIncoming(absFloatValue, floatNegativeCase);
    workValue->addIncoming(floatValue, floatPositiveCase);

    // Use llvm.round to handle precision issues where values like 1.9999999 should be 2.0
    // Check if value is very close to next integer (within epsilon)
    llvm::Value *intPartTrunc = builder.CreateFPToSI(workValue, llvm::Type::getInt32Ty(context), "int_part_trunc");
    llvm::Value *intPartTruncFloat = builder.CreateSIToFP(intPartTrunc, llvm::Type::getDoubleTy(context), "int_part_trunc_float");
    llvm::Value *fracPartInitial = builder.CreateFSub(workValue, intPartTruncFloat, "frac_part_initial");

    // If fractional part > 0.9999, it's likely a precision error and should round up
    llvm::Value *shouldRoundUp = builder.CreateFCmpOGT(fracPartInitial, llvm::ConstantFP::get(context, llvm::APFloat(0.9999)));
    llvm::Value *intPartRounded = builder.CreateAdd(intPartTrunc, llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 1), "int_part_rounded");
    llvm::Value *intPart = builder.CreateSelect(shouldRoundUp, intPartRounded, intPartTrunc, "int_part");

    // Recalculate integer part as float and fractional part with correct integer
    llvm::Value *intPartFloat = builder.CreateSIToFP(intPart, llvm::Type::getDoubleTy(context), "int_part_float");
    llvm::Value *fracPart = builder.CreateFSub(workValue, intPartFloat, "frac_part");

    // Use intPart and fracPart for formatting
    // Format integer part (similar to print(int))
    llvm::Type *floatTempType = llvm::ArrayType::get(charType, 20);
    llvm::Value *floatTemp = builder.CreateAlloca(floatTempType, nullptr, "float_temp");
    builder.CreateCall(memsetFunc, {floatTemp, llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 0), llvm::ConstantInt::get(llvm::Type::getInt64Ty(context), 20)});

    llvm::Value *floatTempIdx = builder.CreateAlloca(llvm::Type::getInt32Ty(context), nullptr, "float_temp_idx");
    builder.CreateStore(llvm::ConstantInt::get(context, llvm::APInt(32, 19, true)), floatTempIdx);

    // Check if integer part is zero
    llvm::BasicBlock *intZeroCase = llvm::BasicBlock::Create(context, "int_zero", printFloatFunc);
    llvm::BasicBlock *intNonZeroCase = llvm::BasicBlock::Create(context, "int_nonzero", printFloatFunc);
    llvm::BasicBlock *intDigitsDone = llvm::BasicBlock::Create(context, "int_digits_done", printFloatFunc);

    llvm::Value *intIsZero = builder.CreateICmpEQ(intPart, llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)));
    builder.CreateCondBr(intIsZero, intZeroCase, intNonZeroCase);

    // Integer part is zero: just write '0'
    builder.SetInsertPoint(intZeroCase);
    llvm::Value *tempZeroPtr = builder.CreateInBoundsGEP(floatTempType, floatTemp,
                                                         {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), llvm::ConstantInt::get(context, llvm::APInt(32, 19))});
    builder.CreateStore(llvm::ConstantInt::get(charType, '0'), tempZeroPtr);
    builder.CreateStore(llvm::ConstantInt::get(context, llvm::APInt(32, 18, true)), floatTempIdx);
    builder.CreateBr(intDigitsDone);

    // Integer part is non-zero: extract digits
    builder.SetInsertPoint(intNonZeroCase);
    llvm::BasicBlock *intDigitLoop = llvm::BasicBlock::Create(context, "int_digit_loop", printFloatFunc);
    llvm::BasicBlock *intDigitBody = llvm::BasicBlock::Create(context, "int_digit_body", printFloatFunc);
    builder.CreateBr(intDigitLoop);

    builder.SetInsertPoint(intDigitLoop);
    llvm::PHINode *remainingInt = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "remaining_int");
    remainingInt->addIncoming(intPart, intNonZeroCase);
    llvm::PHINode *currentTempIdx = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "current_temp_idx");
    currentTempIdx->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 19, true)), intNonZeroCase);

    llvm::Value *intLoopCond = builder.CreateICmpNE(remainingInt, llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)));
    builder.CreateCondBr(intLoopCond, intDigitBody, intDigitsDone);

    builder.SetInsertPoint(intDigitBody);
    llvm::Value *intDigit = builder.CreateSRem(remainingInt, llvm::ConstantInt::get(context, llvm::APInt(32, 10, true)));
    llvm::Value *intDigitChar = builder.CreateAdd(intDigit, llvm::ConstantInt::get(context, llvm::APInt(32, '0', true)));
    llvm::Value *intDigitChar8 = builder.CreateTrunc(intDigitChar, charType);

    llvm::Value *tempDigitPtr = builder.CreateInBoundsGEP(floatTempType, floatTemp,
                                                          {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), currentTempIdx});
    builder.CreateStore(intDigitChar8, tempDigitPtr);

    llvm::Value *nextRemainingInt = builder.CreateSDiv(remainingInt, llvm::ConstantInt::get(context, llvm::APInt(32, 10, true)));
    llvm::Value *nextIntTempIdx = builder.CreateSub(currentTempIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));

    remainingInt->addIncoming(nextRemainingInt, intDigitBody);
    currentTempIdx->addIncoming(nextIntTempIdx, intDigitBody);
    builder.CreateBr(intDigitLoop);

    // Integer digits done: copy to buffer
    builder.SetInsertPoint(intDigitsDone);
    llvm::PHINode *finalTempIdx = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "final_temp_idx");
    finalTempIdx->addIncoming(currentTempIdx, intDigitLoop);
    finalTempIdx->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 18, true)), intZeroCase);

    // Copy integer digits from temp to buffer
    llvm::Value *startIdx = builder.CreateAdd(finalTempIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    llvm::Value *intLength = builder.CreateSub(llvm::ConstantInt::get(context, llvm::APInt(32, 20, true)), startIdx);

    llvm::BasicBlock *copyIntLoop = llvm::BasicBlock::Create(context, "copy_int_loop", printFloatFunc);
    llvm::BasicBlock *copyIntBody = llvm::BasicBlock::Create(context, "copy_int_body", printFloatFunc);
    llvm::BasicBlock *copyIntDone = llvm::BasicBlock::Create(context, "copy_int_done", printFloatFunc);

    builder.CreateBr(copyIntLoop);

    builder.SetInsertPoint(copyIntLoop);
    llvm::PHINode *copyIdx = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "copy_idx");
    copyIdx->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)), intDigitsDone);

    llvm::Value *copyCond = builder.CreateICmpSLT(copyIdx, intLength);
    builder.CreateCondBr(copyCond, copyIntBody, copyIntDone);

    builder.SetInsertPoint(copyIntBody);
    llvm::Value *currentPos = builder.CreateLoad(llvm::Type::getInt32Ty(context), floatPos, "current_pos");
    llvm::Value *intSrcIdx = builder.CreateAdd(startIdx, copyIdx);
    llvm::Value *intSrcPtr = builder.CreateInBoundsGEP(floatTempType, floatTemp,
                                                       {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), intSrcIdx});
    llvm::Value *charValue = builder.CreateLoad(charType, intSrcPtr);

    llvm::Value *intDstIdx = builder.CreateAdd(currentPos, copyIdx);
    llvm::Value *intDstPtr = builder.CreateInBoundsGEP(floatBufferType, floatBuffer,
                                                       {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), intDstIdx});
    builder.CreateStore(charValue, intDstPtr);

    llvm::Value *nextCopyIdx = builder.CreateAdd(copyIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    copyIdx->addIncoming(nextCopyIdx, copyIntBody);
    builder.CreateBr(copyIntLoop);

    // Update position after integer part
    builder.SetInsertPoint(copyIntDone);
    llvm::Value *currentPosAfterInt = builder.CreateLoad(llvm::Type::getInt32Ty(context), floatPos, "pos_after_int");
    llvm::Value *newPos = builder.CreateAdd(currentPosAfterInt, intLength);
    builder.CreateStore(newPos, floatPos);

    // Add decimal point
    llvm::Value *decimalPos = builder.CreateLoad(llvm::Type::getInt32Ty(context), floatPos);
    llvm::Value *decimalPtr = builder.CreateInBoundsGEP(floatBufferType, floatBuffer,
                                                        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), decimalPos});
    builder.CreateStore(llvm::ConstantInt::get(charType, '.'), decimalPtr);
    llvm::Value *posAfterDecimal = builder.CreateAdd(decimalPos, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    builder.CreateStore(posAfterDecimal, floatPos);

    // Compute scale = 10^precision dynamically
    llvm::BasicBlock *scaleLoop = llvm::BasicBlock::Create(context, "scale_loop", printFloatFunc);
    llvm::BasicBlock *scaleBody = llvm::BasicBlock::Create(context, "scale_body", printFloatFunc);
    llvm::BasicBlock *scaleDone = llvm::BasicBlock::Create(context, "scale_done", printFloatFunc);

    builder.CreateBr(scaleLoop);

    builder.SetInsertPoint(scaleLoop);
    llvm::PHINode *scaleIdx = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "scale_idx");
    scaleIdx->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)), copyIntDone);
    llvm::PHINode *scaleAccum = builder.CreatePHI(llvm::Type::getDoubleTy(context), 2, "scale_accum");
    scaleAccum->addIncoming(llvm::ConstantFP::get(context, llvm::APFloat(1.0)), copyIntDone);

    llvm::Value *scaleCond = builder.CreateICmpSLT(scaleIdx, precisionArg);
    builder.CreateCondBr(scaleCond, scaleBody, scaleDone);

    builder.SetInsertPoint(scaleBody);
    llvm::Value *nextScale = builder.CreateFMul(scaleAccum, llvm::ConstantFP::get(context, llvm::APFloat(10.0)), "next_scale");
    llvm::Value *nextScaleIdx = builder.CreateAdd(scaleIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));

    scaleAccum->addIncoming(nextScale, scaleBody);
    scaleIdx->addIncoming(nextScaleIdx, scaleBody);
    builder.CreateBr(scaleLoop);

    builder.SetInsertPoint(scaleDone);
    llvm::Value *scale = scaleAccum;

    // Scale by 10^precision and round
    llvm::Value *fracScaled = builder.CreateFMul(fracPart, scale, "frac_scaled");
    llvm::Value *fracRounded = builder.CreateFAdd(fracScaled, llvm::ConstantFP::get(context, llvm::APFloat(0.5)), "frac_rounded");
    // Use int64 to avoid overflow for high precision values
    llvm::Value *fracInt = builder.CreateFPToSI(fracRounded, llvm::Type::getInt64Ty(context), "frac_int");

    // Handle overflow case: if fracInt >= scale (10^precision), need to carry to integer part
    // This happens when rounding causes fractional part to overflow, e.g., 0.9999995 with precision 6
    llvm::BasicBlock *fracOverflowCheck = llvm::BasicBlock::Create(context, "frac_overflow_check", printFloatFunc);
    llvm::BasicBlock *fracOverflowCase = llvm::BasicBlock::Create(context, "frac_overflow", printFloatFunc);
    llvm::BasicBlock *fracNoOverflow = llvm::BasicBlock::Create(context, "frac_no_overflow", printFloatFunc);

    builder.CreateBr(fracOverflowCheck);

    builder.SetInsertPoint(fracOverflowCheck);
    llvm::Value *scaleInt64 = builder.CreateFPToSI(scale, llvm::Type::getInt64Ty(context));
    llvm::Value *hasOverflow = builder.CreateICmpSGE(fracInt, scaleInt64);
    builder.CreateCondBr(hasOverflow, fracOverflowCase, fracNoOverflow);

    // Overflow case: need to increment integer part and regenerate buffer
    builder.SetInsertPoint(fracOverflowCase);
    // Increment the integer part
    llvm::Value *newIntPart = builder.CreateAdd(intPart, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    // Clear and regenerate integer part in buffer
    // Reset position to handle potential sign
    llvm::Value *signOffset = builder.CreateLoad(llvm::Type::getInt32Ty(context), floatPos);
    llvm::Value *overflowIsNegative = builder.CreateICmpEQ(signOffset, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    llvm::Value *overflowStartPos = builder.CreateSelect(overflowIsNegative,
                                                         llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)),
                                                         llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)));

    // Clear temp buffer and regenerate digits
    builder.CreateCall(memsetFunc, {floatTemp, llvm::ConstantInt::get(llvm::Type::getInt32Ty(context), 0), llvm::ConstantInt::get(llvm::Type::getInt64Ty(context), 20)});
    builder.CreateStore(llvm::ConstantInt::get(context, llvm::APInt(32, 19, true)), floatTempIdx);

    // Extract digits of newIntPart
    llvm::BasicBlock *overflowIntLoop = llvm::BasicBlock::Create(context, "overflow_int_loop", printFloatFunc);
    llvm::BasicBlock *overflowIntBody = llvm::BasicBlock::Create(context, "overflow_int_body", printFloatFunc);
    llvm::BasicBlock *overflowIntDone = llvm::BasicBlock::Create(context, "overflow_int_done", printFloatFunc);

    builder.CreateBr(overflowIntLoop);

    builder.SetInsertPoint(overflowIntLoop);
    llvm::PHINode *overflowRemaining = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "overflow_remaining");
    overflowRemaining->addIncoming(newIntPart, fracOverflowCase);
    llvm::PHINode *overflowTempIdx = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "overflow_temp_idx");
    overflowTempIdx->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 19, true)), fracOverflowCase);

    llvm::Value *overflowLoopCond = builder.CreateICmpNE(overflowRemaining, llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)));
    builder.CreateCondBr(overflowLoopCond, overflowIntBody, overflowIntDone);

    builder.SetInsertPoint(overflowIntBody);
    llvm::Value *overflowDigit = builder.CreateSRem(overflowRemaining, llvm::ConstantInt::get(context, llvm::APInt(32, 10, true)));
    llvm::Value *overflowDigitChar = builder.CreateAdd(overflowDigit, llvm::ConstantInt::get(context, llvm::APInt(32, '0', true)));
    llvm::Value *overflowDigitChar8 = builder.CreateTrunc(overflowDigitChar, charType);
    llvm::Value *overflowTempPtr = builder.CreateInBoundsGEP(floatTempType, floatTemp,
                                                             {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), overflowTempIdx});
    builder.CreateStore(overflowDigitChar8, overflowTempPtr);
    llvm::Value *nextOverflowRemaining = builder.CreateSDiv(overflowRemaining, llvm::ConstantInt::get(context, llvm::APInt(32, 10, true)));
    llvm::Value *nextOverflowTempIdx = builder.CreateSub(overflowTempIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    overflowRemaining->addIncoming(nextOverflowRemaining, overflowIntBody);
    overflowTempIdx->addIncoming(nextOverflowTempIdx, overflowIntBody);
    builder.CreateBr(overflowIntLoop);

    builder.SetInsertPoint(overflowIntDone);
    // Copy the new integer part to buffer
    llvm::Value *overflowTempFinalIdx = overflowTempIdx;
    llvm::Value *overflowStartIdx = builder.CreateAdd(overflowTempFinalIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    llvm::Value *overflowIntLen = builder.CreateSub(llvm::ConstantInt::get(context, llvm::APInt(32, 20, true)), overflowStartIdx);

    llvm::BasicBlock *overflowCopyLoop = llvm::BasicBlock::Create(context, "overflow_copy_loop", printFloatFunc);
    llvm::BasicBlock *overflowCopyBody = llvm::BasicBlock::Create(context, "overflow_copy_body", printFloatFunc);
    llvm::BasicBlock *overflowCopyDone = llvm::BasicBlock::Create(context, "overflow_copy_done", printFloatFunc);

    builder.CreateBr(overflowCopyLoop);

    builder.SetInsertPoint(overflowCopyLoop);
    llvm::PHINode *overflowCopyIdx = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "overflow_copy_idx");
    overflowCopyIdx->addIncoming(llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)), overflowIntDone);
    llvm::Value *overflowCopyCond = builder.CreateICmpSLT(overflowCopyIdx, overflowIntLen);
    builder.CreateCondBr(overflowCopyCond, overflowCopyBody, overflowCopyDone);

    builder.SetInsertPoint(overflowCopyBody);
    llvm::Value *overflowSrcIdx = builder.CreateAdd(overflowStartIdx, overflowCopyIdx);
    llvm::Value *overflowSrcPtr = builder.CreateInBoundsGEP(floatTempType, floatTemp,
                                                            {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), overflowSrcIdx});
    llvm::Value *overflowSrcChar = builder.CreateLoad(charType, overflowSrcPtr);
    llvm::Value *overflowDstIdx = builder.CreateAdd(overflowStartPos, overflowCopyIdx);
    llvm::Value *overflowDstPtr = builder.CreateInBoundsGEP(floatBufferType, floatBuffer,
                                                            {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), overflowDstIdx});
    builder.CreateStore(overflowSrcChar, overflowDstPtr);
    llvm::Value *nextOverflowCopyIdx = builder.CreateAdd(overflowCopyIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    overflowCopyIdx->addIncoming(nextOverflowCopyIdx, overflowCopyBody);
    builder.CreateBr(overflowCopyLoop);

    builder.SetInsertPoint(overflowCopyDone);
    llvm::Value *overflowNewPos = builder.CreateAdd(overflowStartPos, overflowIntLen);
    builder.CreateStore(overflowNewPos, floatPos);

    // Add decimal point
    llvm::Value *overflowDecimalPos = builder.CreateLoad(llvm::Type::getInt32Ty(context), floatPos);
    llvm::Value *overflowDecimalPtr = builder.CreateInBoundsGEP(floatBufferType, floatBuffer,
                                                                {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), overflowDecimalPos});
    builder.CreateStore(llvm::ConstantInt::get(charType, '.'), overflowDecimalPtr);
    llvm::Value *overflowPosAfterDecimal = builder.CreateAdd(overflowDecimalPos, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));
    builder.CreateStore(overflowPosAfterDecimal, floatPos);

    // Set fracInt to 0 (all zeros in fractional part)
    llvm::Value *overflowFracInt = llvm::ConstantInt::get(context, llvm::APInt(64, 0, true));
    builder.CreateBr(fracNoOverflow);

    // No overflow case
    builder.SetInsertPoint(fracNoOverflow);
    llvm::PHINode *finalFracInt = builder.CreatePHI(llvm::Type::getInt64Ty(context), 2, "final_frac_int");
    finalFracInt->addIncoming(overflowFracInt, overflowCopyDone);
    finalFracInt->addIncoming(fracInt, fracOverflowCheck);

    // Compute precision - 1 for loop initialization
    llvm::Value *precisionMinus1 = builder.CreateSub(precisionArg, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));

    // Extract fractional digits (right to left)
    llvm::BasicBlock *fracDigitLoop = llvm::BasicBlock::Create(context, "frac_digit_loop", printFloatFunc);
    llvm::BasicBlock *fracDigitBody = llvm::BasicBlock::Create(context, "frac_digit_body", printFloatFunc);
    llvm::BasicBlock *fracDigitsDone = llvm::BasicBlock::Create(context, "frac_digits_done", printFloatFunc);

    builder.CreateBr(fracDigitLoop);

    builder.SetInsertPoint(fracDigitLoop);
    llvm::PHINode *fracIdx = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "frac_idx");
    fracIdx->addIncoming(precisionMinus1, fracNoOverflow);
    llvm::PHINode *fracRemaining = builder.CreatePHI(llvm::Type::getInt64Ty(context), 2, "frac_remaining");
    fracRemaining->addIncoming(finalFracInt, fracNoOverflow);

    llvm::Value *fracCond = builder.CreateICmpSGE(fracIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)));
    builder.CreateCondBr(fracCond, fracDigitBody, fracDigitsDone);

    builder.SetInsertPoint(fracDigitBody);
    llvm::Value *fracDigit = builder.CreateSRem(fracRemaining, llvm::ConstantInt::get(context, llvm::APInt(64, 10, true)));
    llvm::Value *fracDigitChar = builder.CreateAdd(fracDigit, llvm::ConstantInt::get(context, llvm::APInt(64, '0', true)));
    llvm::Value *fracDigitChar8 = builder.CreateTrunc(fracDigitChar, charType);

    llvm::Value *fracPosValue = builder.CreateLoad(llvm::Type::getInt32Ty(context), floatPos);
    llvm::Value *fracDstIdx = builder.CreateAdd(fracPosValue, fracIdx);
    llvm::Value *fracDstPtr = builder.CreateInBoundsGEP(floatBufferType, floatBuffer,
                                                        {llvm::ConstantInt::get(context, llvm::APInt(32, 0)), fracDstIdx});
    builder.CreateStore(fracDigitChar8, fracDstPtr);

    llvm::Value *nextFracRemaining = builder.CreateSDiv(fracRemaining, llvm::ConstantInt::get(context, llvm::APInt(64, 10, true)));
    llvm::Value *nextFracIdx = builder.CreateSub(fracIdx, llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)));

    fracRemaining->addIncoming(nextFracRemaining, fracDigitBody);
    fracIdx->addIncoming(nextFracIdx, fracDigitBody);
    builder.CreateBr(fracDigitLoop);

    // Fractional digits done
    builder.SetInsertPoint(fracDigitsDone);
    llvm::Value *finalPosValue = builder.CreateLoad(llvm::Type::getInt32Ty(context), floatPos);
    llvm::Value *nonZeroLength = builder.CreateAdd(finalPosValue, precisionArg);
    builder.CreateBr(floatFormatDone);

    // Format done - write to stdout
    builder.SetInsertPoint(floatFormatDone);
    llvm::PHINode *finalFloatLength = builder.CreatePHI(llvm::Type::getInt32Ty(context), 2, "final_float_length");
    finalFloatLength->addIncoming(zeroLength, zeroFillDone);
    finalFloatLength->addIncoming(nonZeroLength, fracDigitsDone);

    // Get stdout handle (-11)
    llvm::Value *floatStdoutHandle = builder.CreateCall(getStdHandle,
                                                        {llvm::ConstantInt::get(context, llvm::APInt(32, -11, true))});

    // Get buffer pointer
    llvm::Value *floatBufferPtr = builder.CreateBitCast(floatBuffer, llvm::PointerType::get(context, 0));

    // Call WriteFile
    builder.CreateCall(writeFile, {floatStdoutHandle,
                                   floatBufferPtr,
                                   finalFloatLength,
                                   floatBytesWritten,
                                   llvm::ConstantPointerNull::get(llvm::PointerType::get(context, 0))});

    // Write newline
    builder.CreateCall(writeFile, {floatStdoutHandle,
                                   newlinePtr,
                                   llvm::ConstantInt::get(context, llvm::APInt(32, 1, true)),
                                   floatBytesWritten,
                                   llvm::ConstantPointerNull::get(llvm::PointerType::get(context, 0))});

    // Return 0
    builder.CreateRet(llvm::ConstantInt::get(context, llvm::APInt(32, 0, true)));

    // Add attribute to prevent LLVM from converting memset calls to intrinsics
    printFloatFunc->addFnAttr("no-builtin-memset");

    // Restore insert point
    if (savedBlock)
    {
        builder.SetInsertPoint(savedBlock);
    }
}

llvm::Function *CodeGenerator::getOrDeclareMemset()
{
    llvm::Function *memsetFunc = module->getFunction("memset");
    if (!memsetFunc)
    {
        llvm::FunctionType *memsetType = llvm::FunctionType::get(
            llvm::PointerType::get(context, 0),
            {llvm::PointerType::get(context, 0), llvm::Type::getInt32Ty(context), llvm::Type::getInt64Ty(context)},
            false);
        memsetFunc = llvm::Function::Create(memsetType, llvm::Function::ExternalLinkage, "memset", module.get());
    }
    return memsetFunc;
}

void CodeGenerator::initHeapManagement()
{
    // Check if malloc already exists
    if (module->getFunction("malloc"))
    {
        return;
    }

#ifdef _WIN32
    // Windows: Create malloc/free wrappers that use Windows Heap API

    // Declare Windows Heap API functions
    // ptr GetProcessHeap()
    llvm::FunctionType *getProcessHeapType = llvm::FunctionType::get(
        llvm::PointerType::get(context, 0),
        {},
        false);
    llvm::Function::Create(getProcessHeapType, llvm::Function::ExternalLinkage,
                           "GetProcessHeap", module.get());

    // ptr HeapAlloc(ptr heap, i32 flags, i64 bytes)
    llvm::FunctionType *heapAllocType = llvm::FunctionType::get(
        llvm::PointerType::get(context, 0),
        {
            llvm::PointerType::get(context, 0), // hHeap
            llvm::Type::getInt32Ty(context),    // dwFlags
            llvm::Type::getInt64Ty(context)     // dwBytes
        },
        false);
    llvm::Function::Create(heapAllocType, llvm::Function::ExternalLinkage,
                           "HeapAlloc", module.get());

    // i32 HeapFree(ptr heap, i32 flags, ptr mem)
    llvm::FunctionType *heapFreeType = llvm::FunctionType::get(
        llvm::Type::getInt32Ty(context),
        {
            llvm::PointerType::get(context, 0), // hHeap
            llvm::Type::getInt32Ty(context),    // dwFlags
            llvm::PointerType::get(context, 0)  // lpMem
        },
        false);
    llvm::Function::Create(heapFreeType, llvm::Function::ExternalLinkage,
                           "HeapFree", module.get());

    // Create malloc wrapper function
    llvm::FunctionType *mallocType = llvm::FunctionType::get(
        llvm::PointerType::get(context, 0),
        {llvm::Type::getInt64Ty(context)},
        false);
    llvm::Function *mallocFunc = llvm::Function::Create(
        mallocType,
        llvm::Function::ExternalLinkage,
        "malloc",
        module.get());

    llvm::BasicBlock *entry = llvm::BasicBlock::Create(context, "entry", mallocFunc);
    llvm::BasicBlock *savedBlock = builder.GetInsertBlock();
    builder.SetInsertPoint(entry);

    llvm::Value *sizeArg = mallocFunc->getArg(0);
    llvm::Function *getProcessHeap = module->getFunction("GetProcessHeap");
    llvm::Function *heapAlloc = module->getFunction("HeapAlloc");

    llvm::Value *heap = builder.CreateCall(getProcessHeap, {});
    llvm::Value *ptr = builder.CreateCall(heapAlloc, {heap,
                                                      llvm::ConstantInt::get(context, llvm::APInt(32, 0, false)), // flags = 0
                                                      sizeArg});
    builder.CreateRet(ptr);

    if (savedBlock)
    {
        builder.SetInsertPoint(savedBlock);
    }

    // Create free wrapper function
    llvm::FunctionType *freeType = llvm::FunctionType::get(
        llvm::Type::getVoidTy(context),
        {llvm::PointerType::get(context, 0)},
        false);
    llvm::Function *freeFunc = llvm::Function::Create(
        freeType,
        llvm::Function::ExternalLinkage,
        "free",
        module.get());

    entry = llvm::BasicBlock::Create(context, "entry", freeFunc);
    savedBlock = builder.GetInsertBlock();
    builder.SetInsertPoint(entry);

    llvm::Value *ptrArg = freeFunc->getArg(0);
    llvm::Function *heapFree = module->getFunction("HeapFree");

    heap = builder.CreateCall(getProcessHeap, {});
    builder.CreateCall(heapFree, {heap,
                                  llvm::ConstantInt::get(context, llvm::APInt(32, 0, false)), // flags = 0
                                  ptrArg});
    builder.CreateRetVoid();

    if (savedBlock)
    {
        builder.SetInsertPoint(savedBlock);
    }
#else
    // Linux: Just declare malloc and free as external functions
    // They will be provided by stubs.cpp
    llvm::FunctionType *mallocType = llvm::FunctionType::get(
        llvm::PointerType::get(context, 0),
        {llvm::Type::getInt64Ty(context)},
        false);
    llvm::Function::Create(
        mallocType,
        llvm::Function::ExternalLinkage,
        "malloc",
        module.get());

    llvm::FunctionType *freeType = llvm::FunctionType::get(
        llvm::Type::getVoidTy(context),
        {llvm::PointerType::get(context, 0)},
        false);
    llvm::Function::Create(
        freeType,
        llvm::Function::ExternalLinkage,
        "free",
        module.get());
#endif
}
