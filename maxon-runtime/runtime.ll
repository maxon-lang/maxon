; Maxon Runtime Library
; This file contains runtime functions needed by the compiler-generated code
; It is compiled once and linked with all Maxon programs

target triple = "x86_64-pc-windows-msvc"

; _fltused - Windows floating-point support symbol
; Required when using floating-point operations on Windows
@_fltused = constant i32 39029

; memset - fill memory with a constant byte value
; ptr memset(ptr dest, i32 val, i64 count)
define ptr @memset(ptr %dest, i32 %val, i64 %count) {
entry:
  ; Convert value to i8
  %byteVal = trunc i32 %val to i8
  
  ; Allocate loop counter
  %i = alloca i64
  store i64 0, ptr %i
  br label %loop.cond

loop.cond:
  %iVal = load i64, ptr %i
  %cond = icmp ult i64 %iVal, %count
  br i1 %cond, label %loop.body, label %loop.end

loop.body:
  ; Calculate pointer: dest + i
  %ptr = getelementptr i8, ptr %dest, i64 %iVal
  ; Store byte
  store i8 %byteVal, ptr %ptr
  ; Increment counter
  %iNext = add i64 %iVal, 1
  store i64 %iNext, ptr %i
  br label %loop.cond

loop.end:
  ret ptr %dest
}

; __chkstk - stack probe for large stack allocations
; This is called by LLVM when allocating large stack arrays
; Windows requires stack probing to ensure stack pages are committed
define void @__chkstk() {
entry:
  ret void
}

; round - round floating point value to nearest integer (half away from zero)
; double round(double x)
; This is needed because LLVM's llvm.round intrinsic lowers to a library call on Windows
define double @round(double %x) {
entry:
  ; Convert to i64 and back to double to truncate decimal part
  ; This effectively does floor for positive, ceil for negative
  %x_as_i64 = fptosi double %x to i64
  %truncated = sitofp i64 %x_as_i64 to double
  
  ; Get the fractional part
  %frac = fsub double %x, %truncated
  
  ; Check if we need to round up or down
  ; For positive: if frac >= 0.5, add 1
  ; For negative: if frac <= -0.5, subtract 1
  %is_pos_and_round_up = fcmp oge double %frac, 5.000000e-01
  %is_neg_and_round_down = fcmp ole double %frac, -5.000000e-01
  
  ; Apply rounding
  %add_one = fadd double %truncated, 1.0
  %sub_one = fsub double %truncated, 1.0
  
  %result1 = select i1 %is_pos_and_round_up, double %add_one, double %truncated
  %result2 = select i1 %is_neg_and_round_down, double %sub_one, double %result1
  
  ret double %result2
}

; Future runtime functions can be added here:
; - memcpy
; - memcmp
; - memmove
; - strlen
