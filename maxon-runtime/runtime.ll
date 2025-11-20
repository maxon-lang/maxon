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

; sin - sine function using musl's polynomial approximation
; Algorithm from musl libc (https://git.musl-libc.org/cgit/musl/tree/src/math/__sin.c)
; Kernel sin function for values bounded by ~pi/4 in magnitude
; Uses polynomial of degree 13 for approximation
; double sin(double x)
define double @sin(double %x) {
entry:
  ; Polynomial coefficients
  ; S1 = -1/6, S2 = 1/120, S3 = -1/5040, S4 = 1/362880, S5 = -1/39916800, S6 = 1/6227020800
  %S1 = fadd double 0.0, -1.66666666666666324348e-01  ; -0x1.5555555555549p-3
  %S2 = fadd double 0.0, 8.33333333332248946124e-03   ; 0x1.111111110f8a6p-7
  %S3 = fadd double 0.0, -1.98412698298579493134e-04  ; -0x1.a01a019c161d5p-13
  %S4 = fadd double 0.0, 2.75573137070700676789e-06   ; 0x1.71de357b1fe7dp-19
  %S5 = fadd double 0.0, -2.50507602534068634195e-08  ; -0x1.ae5e68a2b9cebp-26
  %S6 = fadd double 0.0, 1.58969099521155010221e-10   ; 0x1.5d93a5acfd57cp-33
  
  ; z = x * x
  %z = fmul double %x, %x
  ; w = z * z
  %w = fmul double %z, %z
  
  ; Evaluate polynomial: r = S2 + z*(S3 + z*S4) + z*w*(S5 + z*S6)
  %t1 = fmul double %z, %S6
  %t2 = fadd double %S5, %t1
  %t3 = fmul double %z, %t2
  %t4 = fmul double %w, %t3
  
  %t5 = fmul double %z, %S4
  %t6 = fadd double %S3, %t5
  %t7 = fmul double %z, %t6
  %t8 = fadd double %S2, %t7
  
  %r = fadd double %t8, %t4
  
  ; v = z * x
  %v = fmul double %z, %x
  
  ; Compute final result: x + v*(S1 + z*r)
  %t9 = fmul double %z, %r
  %t10 = fadd double %S1, %t9
  %t11 = fmul double %v, %t10
  %result = fadd double %x, %t11
  
  ret double %result
}

; cos - cosine function using musl's polynomial approximation
; Algorithm from musl libc (https://git.musl-libc.org/cgit/musl/tree/src/math/__cos.c)
; Kernel cos function for values bounded by ~pi/4 in magnitude
; Uses polynomial of degree 14 for approximation
; double cos(double x)
define double @cos(double %x) {
entry:
  ; Polynomial coefficients  
  ; C1 = 1/2!, C2 = -1/4!, C3 = 1/6!, C4 = -1/8!, C5 = 1/10!, C6 = -1/12!
  %C1 = fadd double 0.0, 4.16666666666666019037e-02   ; 0x1.555555555554cp-5
  %C2 = fadd double 0.0, -1.38888888888741095749e-03  ; -0x1.6c16c16c15177p-10
  %C3 = fadd double 0.0, 2.48015872894767294178e-05   ; 0x1.a01a019cb159p-16
  %C4 = fadd double 0.0, -2.75573143513906633035e-07  ; -0x1.27e4f809c52adp-22
  %C5 = fadd double 0.0, 2.08757232129817482790e-09   ; 0x1.1ee9ebdb4b1c4p-29
  %C6 = fadd double 0.0, -1.13596475577881948265e-11  ; -0x1.8fae9be8838d4p-37
  
  ; z = x * x
  %z = fmul double %x, %x
  ; zs = z * z  
  %zs = fmul double %z, %z
  
  ; Evaluate polynomial: r = z*(C1 + z*(C2 + z*C3)) + zs*zs*(C4 + z*(C5 + z*C6))
  %t1 = fmul double %z, %C6
  %t2 = fadd double %C5, %t1
  %t3 = fmul double %z, %t2
  %t4 = fadd double %C4, %t3
  %t5 = fmul double %zs, %zs
  %t6 = fmul double %t5, %t4
  
  %t7 = fmul double %z, %C3
  %t8 = fadd double %C2, %t7
  %t9 = fmul double %z, %t8
  %t10 = fadd double %C1, %t9
  %t11 = fmul double %z, %t10
  
  %r = fadd double %t11, %t6
  
  ; hz = 0.5 * z
  %hz = fmul double 0.5, %z
  ; w = 1.0 - hz
  %w = fsub double 1.0, %hz
  
  ; Final result: w + (((1.0 - w) - hz) + z*r)
  %t12 = fsub double 1.0, %w
  %t13 = fsub double %t12, %hz
  %t14 = fmul double %z, %r
  %t15 = fadd double %t13, %t14
  %result = fadd double %w, %t15
  
  ret double %result
}

; Future runtime functions can be added here:
; - memcpy
; - memcmp
; - memmove
; - strlen
