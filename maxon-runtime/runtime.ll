; Maxon Runtime Library
; This file contains runtime functions needed by the compiler-generated code
; It is compiled once and linked with all Maxon programs

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

; floor - round down to nearest integer
; double floor(double x)
; Returns the largest integer value not greater than x
define double @floor(double %x) {
entry:
  ; Check for special cases: infinity, NaN, or already an integer
  %x_bits = bitcast double %x to i64
  %ix_u64 = lshr i64 %x_bits, 52
  %exponent = trunc i64 %ix_u64 to i32
  %exp_only = and i32 %exponent, 2047  ; 0x7ff - mask to get exponent
  
  ; If exponent >= 1023+52, the value is >= 2^52, which means it's already an integer
  ; or it's infinity/NaN
  %is_large = icmp uge i32 %exp_only, 1075  ; 1023 + 52
  br i1 %is_large, label %return_x, label %compute_floor

return_x:
  ret double %x

compute_floor:
  ; Convert to i64 (truncates toward zero)
  %x_as_i64 = fptosi double %x to i64
  %truncated = sitofp i64 %x_as_i64 to double
  
  ; For negative numbers, if there was a fractional part, subtract 1
  %is_negative = fcmp olt double %x, 0.0
  %has_frac = fcmp one double %x, %truncated
  %need_adjust = and i1 %is_negative, %has_frac
  
  %adjusted = fsub double %truncated, 1.0
  %result = select i1 %need_adjust, double %adjusted, double %truncated
  
  ret double %result
}

; sin - sine function with full range reduction
; Algorithm from musl libc (https://git.musl-libc.org/cgit/musl/tree/src/math/sin.c)
; Handles all input ranges correctly like other programming languages
; double sin(double x)
define double @sin(double %x) {
entry:
  ; Extract bits to check magnitude
  %x_bits = bitcast double %x to i64
  %ix_u64 = lshr i64 %x_bits, 32
  %ix = trunc i64 %ix_u64 to i32
  %ix_abs = and i32 %ix, 2147483647  ; 0x7fffffff - mask off sign bit
  
  ; Check if |x| ~<= pi/4 (0x3fe921fb)
  %is_small = icmp ule i32 %ix_abs, 1072243195  ; 0x3fe921fb
  br i1 %is_small, label %small_arg, label %need_reduction

small_arg:
  ; For small arguments, use kernel sin directly
  ; Check if |x| < 2**-27 (0x3e400000)
  %is_tiny = icmp ult i32 %ix_abs, 1041235968  ; 0x3e400000
  br i1 %is_tiny, label %tiny_arg, label %kernel_direct

tiny_arg:
  ; For tiny x, sin(x) ≈ x
  ret double %x

kernel_direct:
  ; Call kernel sin with y=0
  %result_kernel = call double @__sin_kernel(double %x, double 0.0)
  ret double %result_kernel

need_reduction:
  ; Check for infinity or NaN
  %is_inf_or_nan = icmp uge i32 %ix_abs, 2146435072  ; 0x7ff00000
  br i1 %is_inf_or_nan, label %inf_or_nan, label %do_reduction

inf_or_nan:
  ; sin(inf or NaN) = NaN
  %nan_result = fsub double %x, %x
  ret double %nan_result

do_reduction:
  ; Use rem_pio2 for range reduction
  %y_alloca = alloca [2 x double], align 8
  %n = call i32 @__rem_pio2(double %x, ptr %y_alloca)
  
  ; Load y[0] and y[1]
  %y0_ptr = getelementptr [2 x double], ptr %y_alloca, i64 0, i64 0
  %y1_ptr = getelementptr [2 x double], ptr %y_alloca, i64 0, i64 1
  %y0 = load double, ptr %y0_ptr
  %y1 = load double, ptr %y1_ptr
  
  ; Determine which function to use based on n
  ; n & 3: 0 => sin(y), 1 => cos(y), 2 => -sin(y), 3 => -cos(y)
  %n_mod_4 = and i32 %n, 3
  
  switch i32 %n_mod_4, label %case_0 [
    i32 0, label %case_0
    i32 1, label %case_1
    i32 2, label %case_2
    i32 3, label %case_3
  ]

case_0:
  ; sin(y)
  %result_0 = call double @__sin_kernel(double %y0, double %y1)
  ret double %result_0

case_1:
  ; cos(y)
  %result_1 = call double @__cos_kernel(double %y0, double %y1)
  ret double %result_1

case_2:
  ; -sin(y)
  %result_2_temp = call double @__sin_kernel(double %y0, double %y1)
  %result_2 = fneg double %result_2_temp
  ret double %result_2

case_3:
  ; -cos(y)
  %result_3_temp = call double @__cos_kernel(double %y0, double %y1)
  %result_3 = fneg double %result_3_temp
  ret double %result_3
}

; __sin_kernel - kernel sin function for arguments in ~[-pi/4, pi/4]
; Implements optimized polynomial approximation
; double __sin_kernel(double x, double y)
define double @__sin_kernel(double %x, double %y) {
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
  
  ; Compute final result: x + v*(S1 + z*r) + y
  %t9 = fmul double %z, %r
  %t10 = fadd double %S1, %t9
  %t11 = fmul double %v, %t10
  %t12 = fadd double %x, %t11
  %result = fadd double %t12, %y
  
  ret double %result
}

; cos - cosine function with full range reduction
; Algorithm from musl libc (https://git.musl-libc.org/cgit/musl/tree/src/math/cos.c)
; Handles all input ranges correctly like other programming languages
; double cos(double x)
define double @cos(double %x) {
entry:
  ; Extract bits to check magnitude
  %x_bits = bitcast double %x to i64
  %ix_u64 = lshr i64 %x_bits, 32
  %ix = trunc i64 %ix_u64 to i32
  %ix_abs = and i32 %ix, 2147483647  ; 0x7fffffff - mask off sign bit
  
  ; Check if |x| ~<= pi/4 (0x3fe921fb)
  %is_small = icmp ule i32 %ix_abs, 1072243195  ; 0x3fe921fb
  br i1 %is_small, label %small_arg, label %need_reduction

small_arg:
  ; For small arguments, use kernel cos directly
  %result_kernel = call double @__cos_kernel(double %x, double 0.0)
  ret double %result_kernel

need_reduction:
  ; Check for infinity or NaN
  %is_inf_or_nan = icmp uge i32 %ix_abs, 2146435072  ; 0x7ff00000
  br i1 %is_inf_or_nan, label %inf_or_nan, label %do_reduction

inf_or_nan:
  ; cos(inf or NaN) = NaN
  %nan_result = fsub double %x, %x
  ret double %nan_result

do_reduction:
  ; Use rem_pio2 for range reduction
  %y_alloca = alloca [2 x double], align 8
  %n = call i32 @__rem_pio2(double %x, ptr %y_alloca)
  
  ; Load y[0] and y[1]
  %y0_ptr = getelementptr [2 x double], ptr %y_alloca, i64 0, i64 0
  %y1_ptr = getelementptr [2 x double], ptr %y_alloca, i64 0, i64 1
  %y0 = load double, ptr %y0_ptr
  %y1 = load double, ptr %y1_ptr
  
  ; Determine which function to use based on n
  ; n & 3: 0 => cos(y), 1 => -sin(y), 2 => -cos(y), 3 => sin(y)
  %n_mod_4 = and i32 %n, 3
  
  switch i32 %n_mod_4, label %case_0 [
    i32 0, label %case_0
    i32 1, label %case_1
    i32 2, label %case_2
    i32 3, label %case_3
  ]

case_0:
  ; cos(y)
  %result_0 = call double @__cos_kernel(double %y0, double %y1)
  ret double %result_0

case_1:
  ; -sin(y)
  %result_1_temp = call double @__sin_kernel(double %y0, double %y1)
  %result_1 = fneg double %result_1_temp
  ret double %result_1

case_2:
  ; -cos(y)
  %result_2_temp = call double @__cos_kernel(double %y0, double %y1)
  %result_2 = fneg double %result_2_temp
  ret double %result_2

case_3:
  ; sin(y)
  %result_3 = call double @__sin_kernel(double %y0, double %y1)
  ret double %result_3
}

; __cos_kernel - kernel cos function for arguments in ~[-pi/4, pi/4]
; Implements optimized polynomial approximation
; double __cos_kernel(double x, double y)
define double @__cos_kernel(double %x, double %y) {
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
  
  ; Final result: w + (((1.0 - w) - hz) + (z*r - x*y))
  %t12 = fsub double 1.0, %w
  %t13 = fsub double %t12, %hz
  %t14 = fmul double %z, %r
  %x_y = fmul double %x, %y
  %t15 = fsub double %t14, %x_y
  %t16 = fadd double %t13, %t15
  %result = fadd double %w, %t16
  
  ret double %result
}

; tan - tangent function with full range reduction
; Algorithm from musl libc (https://git.musl-libc.org/cgit/musl/tree/src/math/tan.c)
; Handles all input ranges correctly like other programming languages
; double tan(double x)
define double @tan(double %x) {
entry:
  ; Extract bits to check magnitude
  %x_bits = bitcast double %x to i64
  %ix_u64 = lshr i64 %x_bits, 32
  %ix = trunc i64 %ix_u64 to i32
  %ix_abs = and i32 %ix, 2147483647  ; 0x7fffffff - mask off sign bit
  
  ; Check if |x| ~<= pi/4 (0x3fe921fb)
  %is_small = icmp ule i32 %ix_abs, 1072243195  ; 0x3fe921fb
  br i1 %is_small, label %small_arg, label %need_reduction

small_arg:
  ; For small arguments, use kernel tan directly
  ; Check if |x| < 2**-27 (0x3e400000)
  %is_tiny = icmp ult i32 %ix_abs, 1041235968  ; 0x3e400000
  br i1 %is_tiny, label %tiny_arg, label %kernel_direct

tiny_arg:
  ; For tiny x, tan(x) ≈ x
  ret double %x

kernel_direct:
  ; Call kernel tan with y=0, odd=false
  %result_kernel = call double @__tan_kernel(double %x, double 0.0, i1 false)
  ret double %result_kernel

need_reduction:
  ; Check for infinity or NaN
  %is_inf_or_nan = icmp uge i32 %ix_abs, 2146435072  ; 0x7ff00000
  br i1 %is_inf_or_nan, label %inf_or_nan, label %do_reduction

inf_or_nan:
  ; tan(inf or NaN) = NaN
  %nan_result = fsub double %x, %x
  ret double %nan_result

do_reduction:
  ; Use rem_pio2 for range reduction
  %y_alloca = alloca [2 x double], align 8
  %n = call i32 @__rem_pio2(double %x, ptr %y_alloca)
  
  ; Load y[0] and y[1]
  %y0_ptr = getelementptr [2 x double], ptr %y_alloca, i64 0, i64 0
  %y1_ptr = getelementptr [2 x double], ptr %y_alloca, i64 0, i64 1
  %y0 = load double, ptr %y0_ptr
  %y1 = load double, ptr %y1_ptr
  
  ; Determine if we need odd flag (n & 1)
  %n_and_1 = and i32 %n, 1
  %is_odd = icmp ne i32 %n_and_1, 0
  
  ; Call kernel with reduced argument
  %result_reduced = call double @__tan_kernel(double %y0, double %y1, i1 %is_odd)
  ret double %result_reduced
}

; __tan_kernel - kernel tan function for arguments in ~[-pi/4, pi/4]
; Implements optimized polynomial approximation
; double __tan_kernel(double x, double y, bool odd)
define double @__tan_kernel(double %x, double %y, i1 %odd) {
entry:
  ; Polynomial coefficients for tan approximation from musl
  %T0 = fadd double 0.0, 3.33333333333334091986e-01   ; T[0]
  %T1 = fadd double 0.0, 1.33333333333201242699e-01   ; T[1]
  %T2 = fadd double 0.0, 5.39682539762260521377e-02   ; T[2]
  %T3 = fadd double 0.0, 2.18694882948595424599e-02   ; T[3]
  %T4 = fadd double 0.0, 8.86323982359930005737e-03   ; T[4]
  %T5 = fadd double 0.0, 3.59212803657248101358e-03   ; T[5]
  %T6 = fadd double 0.0, 1.45620945432529025516e-03   ; T[6]
  %T7 = fadd double 0.0, 5.88041240820264096874e-04   ; T[7]
  %T8 = fadd double 0.0, 2.46463134818469906812e-04   ; T[8]
  %T9 = fadd double 0.0, 7.81794442939557092300e-05   ; T[9]
  %T10 = fadd double 0.0, 7.14072491382608190305e-05  ; T[10]
  %T11 = fadd double 0.0, -1.85586374855275456654e-05 ; T[11]
  %T12 = fadd double 0.0, 2.59073051863633712884e-05  ; T[12]
  
  ; z = x * x
  %z = fmul double %x, %x
  ; w = z * z
  %w = fmul double %z, %z
  
  ; Compute r = T[1] + w*(T[3] + w*(T[5] + w*(T[7] + w*(T[9] + w*T[11]))))
  %r1 = fmul double %w, %T11
  %r2 = fadd double %T9, %r1
  %r3 = fmul double %w, %r2
  %r4 = fadd double %T7, %r3
  %r5 = fmul double %w, %r4
  %r6 = fadd double %T5, %r5
  %r7 = fmul double %w, %r6
  %r8 = fadd double %T3, %r7
  %r9 = fmul double %w, %r8
  %r = fadd double %T1, %r9
  
  ; Compute v = z*(T[2] + w*(T[4] + w*(T[6] + w*(T[8] + w*(T[10] + w*T[12])))))
  %v1 = fmul double %w, %T12
  %v2 = fadd double %T10, %v1
  %v3 = fmul double %w, %v2
  %v4 = fadd double %T8, %v3
  %v5 = fmul double %w, %v4
  %v6 = fadd double %T6, %v5
  %v7 = fmul double %w, %v6
  %v8 = fadd double %T4, %v7
  %v9 = fmul double %w, %v8
  %v10 = fadd double %T2, %v9
  %v = fmul double %z, %v10
  
  ; s = z * x
  %s = fmul double %z, %x
  
  ; Compute: y + z*(s*(r+v) + y) + T[0]*s
  %rv_sum = fadd double %r, %v
  %s_rv = fmul double %s, %rv_sum
  %s_rv_y = fadd double %s_rv, %y
  %z_term = fmul double %z, %s_rv_y
  %y_z_term = fadd double %y, %z_term
  %t0_s = fmul double %T0, %s
  %poly_result = fadd double %y_z_term, %t0_s
  
  ; w = x + poly_result
  %w_result = fadd double %x, %poly_result
  
  ; Check if we need to return -1/w or w
  br i1 %odd, label %return_reciprocal, label %return_direct

return_direct:
  ret double %w_result

return_reciprocal:
  ; Return -1.0/w
  %neg_recip = fdiv double -1.0, %w_result
  ret double %neg_recip
}

; __rem_pio2 - range reduction for trigonometric functions
; Returns n where y = x - n*pi/2, with |y| < pi/2
; Based on musl libc implementation
; i32 __rem_pio2(double x, double* y)
define i32 @__rem_pio2(double %x, ptr %y) {
entry:
  ; Constants for range reduction
  %pio2_1 = fadd double 0.0, 1.57079632673412561417e+00   ; first 33 bits of pi/2
  %pio2_1t = fadd double 0.0, 6.07710050650619224932e-11  ; pi/2 - pio2_1
  %pio2_2 = fadd double 0.0, 6.07710050630396597660e-11   ; second 33 bits
  %pio2_2t = fadd double 0.0, 2.02226624879595063154e-21  ; correction term
  %pio2_3 = fadd double 0.0, 2.02226624871116645580e-21   ; third 33 bits
  %pio2_3t = fadd double 0.0, 8.47842766036889956997e-32  ; correction term
  %invpio2 = fadd double 0.0, 6.36619772367581382433e-01  ; 2/pi
  %pio4 = fadd double 0.0, 7.85398163397448278999e-01     ; pi/4
  
  ; Extract sign and magnitude
  %x_bits = bitcast double %x to i64
  %sign_bit = lshr i64 %x_bits, 63
  %is_negative = icmp ne i64 %sign_bit, 0
  %ix_u64 = lshr i64 %x_bits, 32
  %ix = trunc i64 %ix_u64 to i32
  %ix_abs = and i32 %ix, 2147483647  ; 0x7fffffff
  
  ; Check range |x| <= 5pi/4 (0x400f6a7a)
  %in_range = icmp ule i32 %ix_abs, 1074977402  ; 0x400f6a7a
  br i1 %in_range, label %fast_path, label %slow_path

fast_path:
  ; Check if |x| <= 3pi/4 (0x4002d97c)
  %small_range = icmp ule i32 %ix_abs, 1073928572  ; 0x4002d97c
  br i1 %small_range, label %range_1, label %range_2

range_1:
  ; |x| ~<= 3pi/4
  br i1 %is_negative, label %neg_1, label %pos_1

pos_1:
  ; x - pi/2
  %z_p1 = fsub double %x, %pio2_1
  %y0_p1 = fsub double %z_p1, %pio2_1t
  %y1_p1_temp = fsub double %z_p1, %y0_p1
  %y1_p1 = fsub double %y1_p1_temp, %pio2_1t
  %y0_ptr_p1 = getelementptr double, ptr %y, i64 0
  %y1_ptr_p1 = getelementptr double, ptr %y, i64 1
  store double %y0_p1, ptr %y0_ptr_p1
  store double %y1_p1, ptr %y1_ptr_p1
  ret i32 1

neg_1:
  ; x + pi/2
  %z_n1 = fadd double %x, %pio2_1
  %y0_n1 = fadd double %z_n1, %pio2_1t
  %y1_n1_temp = fsub double %z_n1, %y0_n1
  %y1_n1 = fadd double %y1_n1_temp, %pio2_1t
  %y0_ptr_n1 = getelementptr double, ptr %y, i64 0
  %y1_ptr_n1 = getelementptr double, ptr %y, i64 1
  store double %y0_n1, ptr %y0_ptr_n1
  store double %y1_n1, ptr %y1_ptr_n1
  ret i32 -1

range_2:
  ; 3pi/4 < |x| <= 5pi/4
  br i1 %is_negative, label %neg_2, label %pos_2

pos_2:
  ; x - 2*pi/2
  %two_pio2_1_p = fmul double %pio2_1, 2.0
  %two_pio2_1t_p = fmul double %pio2_1t, 2.0
  %z_p2 = fsub double %x, %two_pio2_1_p
  %y0_p2 = fsub double %z_p2, %two_pio2_1t_p
  %y1_p2_temp = fsub double %z_p2, %y0_p2
  %y1_p2 = fsub double %y1_p2_temp, %two_pio2_1t_p
  %y0_ptr_p2 = getelementptr double, ptr %y, i64 0
  %y1_ptr_p2 = getelementptr double, ptr %y, i64 1
  store double %y0_p2, ptr %y0_ptr_p2
  store double %y1_p2, ptr %y1_ptr_p2
  ret i32 2

neg_2:
  ; x + 2*pi/2
  %two_pio2_1_n = fmul double %pio2_1, 2.0
  %two_pio2_1t_n = fmul double %pio2_1t, 2.0
  %z_n2 = fadd double %x, %two_pio2_1_n
  %y0_n2 = fadd double %z_n2, %two_pio2_1t_n
  %y1_n2_temp = fsub double %z_n2, %y0_n2
  %y1_n2 = fadd double %y1_n2_temp, %two_pio2_1t_n
  %y0_ptr_n2 = getelementptr double, ptr %y, i64 0
  %y1_ptr_n2 = getelementptr double, ptr %y, i64 1
  store double %y0_n2, ptr %y0_ptr_n2
  store double %y1_n2, ptr %y1_ptr_n2
  ret i32 -2

slow_path:
  ; For larger values, use medium precision approach
  ; Compute fn = round(x / (pi/2))
  ; We'll use the "toint" trick for rounding
  %toint = fadd double 0.0, 6.75539944105574154e+15  ; 1.5/eps for rounding
  %x_div_pio2 = fmul double %x, %invpio2
  %fn_round1 = fadd double %x_div_pio2, %toint
  %fn_approx = fsub double %fn_round1, %toint
  %n_slow = fptosi double %fn_approx to i32
  
  ; r = x - fn * pio2_1
  %fn_pio2_1 = fmul double %fn_approx, %pio2_1
  %r = fsub double %x, %fn_pio2_1
  
  ; w = fn * pio2_1t
  %w = fmul double %fn_approx, %pio2_1t
  
  ; y[0] = r - w
  %y0_slow = fsub double %r, %w
  %y1_slow = fsub double %r, %y0_slow
  %y1_slow_final = fsub double %y1_slow, %w
  
  %y0_ptr_slow = getelementptr double, ptr %y, i64 0
  %y1_ptr_slow = getelementptr double, ptr %y, i64 1
  store double %y0_slow, ptr %y0_ptr_slow
  store double %y1_slow_final, ptr %y1_ptr_slow
  ret i32 %n_slow
}
