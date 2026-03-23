module {
  func @stdlib.Print.print(value: i64) {
  entry:
    %6400 = func.param value : StdHeapPtr
    memref.store %6400, value
    %6401 = memref.load value : i64
    %6402 = memref.load_indirect %6401+0
    memref.store %6402, __field_6401
    %6403 = memref.load __field_6401 : i64
    %6404 = memref.load_indirect %6403+0
    %6405 = memref.load __field_6401 : i64
    %6406 = memref.load_indirect %6405+8
    %6407 = std.call_runtime @maxon_managed_write_stdout %6404, %6406
    func.return
  }
  func @main() -> u32 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = arith.constant {value = 0 : i64}
    %2 = arith.constant {value = 0 : i64}
    %3 = arith.constant {value = 0 : i64}
    %4 = arith.constant {value = 8 : i64}
    %5 = arith.constant {value = 32 : i64}
    %6 = func.ref @__destruct___ManagedMemory_ByteArray
    %7 = std.ptr_to_i64 %6
    %8 = arith.constant {value = 1 : i64}
    %9 = std.call_runtime @mm_alloc %5, %7, %8
    memref.store %9, __struct_5
    %10 = memref.load __struct_5 : i64
    memref.store_indirect %1, %10+0
    %11 = memref.load __struct_5 : i64
    memref.store_indirect %2, %11+8
    %12 = memref.load __struct_5 : i64
    memref.store_indirect %3, %12+16
    %13 = memref.load __struct_5 : i64
    memref.store_indirect %4, %13+24
    %14 = memref.load __struct_5 : i64
    %15 = memref.load_indirect %14+24
    %16 = arith.constant {value = 0 : i64}
    %17 = memref.lea_symdata __mm_panic_element_size_zero
    %18 = std.ptr_to_i64 %17
    std.call_runtime @maxon_bounds_check %16, %15, %18
    %19 = arith.constant {value = 16 : i64}
    %20 = func.ref @__destruct_ByteArrayArray
    %21 = std.ptr_to_i64 %20
    %22 = arith.constant {value = 2 : i64}
    %23 = std.call_runtime @mm_alloc %19, %21, %22
    memref.store %23, names
    %24 = memref.load names : i64
    memref.store_indirect %0, %24+0
    %25 = memref.load __struct_5 : i64
    %26 = memref.load names : i64
    memref.store_indirect %25, %26+8
    std.call_runtime @mm_incref %25
    %27 = memref.load names : i64
    std.call_runtime @mm_incref %27
    %28 = memref.lea_rdata __bstr_0
    %29 = std.ptr_to_i64 %28
    %30 = arith.constant {value = 5 : i64}
    %31 = arith.constant {value = 16 : i64}
    %32 = func.ref @__destruct_ByteArray
    %33 = std.ptr_to_i64 %32
    %34 = arith.constant {value = 3 : i64}
    %35 = std.call_runtime @mm_alloc %31, %33, %34
    memref.store %35, __lit_tmp_7
    %36 = arith.constant {value = 0 : i64}
    %37 = memref.load __lit_tmp_7 : i64
    memref.store_indirect %36, %37+0
    %38 = arith.constant {value = 32 : i64}
    %39 = func.ref @__destruct___ManagedMemory
    %40 = std.ptr_to_i64 %39
    %41 = arith.constant {value = 4 : i64}
    %42 = std.call_runtime @mm_alloc %38, %40, %41
    memref.store %42, __bstrtmp_managed_7
    %43 = memref.load __bstrtmp_managed_7 : i64
    memref.store_indirect %29, %43+0
    %44 = memref.load __bstrtmp_managed_7 : i64
    memref.store_indirect %30, %44+8
    %45 = arith.constant {value = 0 : i64}
    %46 = memref.load __bstrtmp_managed_7 : i64
    memref.store_indirect %45, %46+16
    %47 = arith.constant {value = 1 : i64}
    %48 = memref.load __bstrtmp_managed_7 : i64
    memref.store_indirect %47, %48+24
    %49 = memref.load __bstrtmp_managed_7 : i64
    %50 = memref.load __lit_tmp_7 : i64
    memref.store_indirect %49, %50+8
    %51 = memref.load __bstrtmp_managed_7 : i64
    std.call_runtime @mm_incref %51
    %52 = memref.load __lit_tmp_7 : i64
    std.call_runtime @mm_incref %52
    %53 = memref.load names : i64
    %54 = memref.load __lit_tmp_7 : i64
    func.call @ByteArrayArray.push %53, %54
    %55 = memref.lea_rdata __bstr_1
    %56 = std.ptr_to_i64 %55
    %57 = arith.constant {value = 5 : i64}
    %58 = arith.constant {value = 16 : i64}
    %59 = func.ref @__destruct_ByteArray
    %60 = std.ptr_to_i64 %59
    %61 = arith.constant {value = 3 : i64}
    %62 = std.call_runtime @mm_alloc %58, %60, %61
    memref.store %62, __lit_tmp_8
    %63 = arith.constant {value = 0 : i64}
    %64 = memref.load __lit_tmp_8 : i64
    memref.store_indirect %63, %64+0
    %65 = arith.constant {value = 32 : i64}
    %66 = func.ref @__destruct___ManagedMemory
    %67 = std.ptr_to_i64 %66
    %68 = arith.constant {value = 4 : i64}
    %69 = std.call_runtime @mm_alloc %65, %67, %68
    memref.store %69, __bstrtmp_managed_8
    %70 = memref.load __bstrtmp_managed_8 : i64
    memref.store_indirect %56, %70+0
    %71 = memref.load __bstrtmp_managed_8 : i64
    memref.store_indirect %57, %71+8
    %72 = arith.constant {value = 0 : i64}
    %73 = memref.load __bstrtmp_managed_8 : i64
    memref.store_indirect %72, %73+16
    %74 = arith.constant {value = 1 : i64}
    %75 = memref.load __bstrtmp_managed_8 : i64
    memref.store_indirect %74, %75+24
    %76 = memref.load __bstrtmp_managed_8 : i64
    %77 = memref.load __lit_tmp_8 : i64
    memref.store_indirect %76, %77+8
    %78 = memref.load __bstrtmp_managed_8 : i64
    std.call_runtime @mm_incref %78
    %79 = memref.load __lit_tmp_8 : i64
    std.call_runtime @mm_incref %79
    %80 = memref.load names : i64
    %81 = memref.load __lit_tmp_8 : i64
    func.call @ByteArrayArray.push %80, %81
    %82 = memref.lea_rdata __bstr_0
    %83 = std.ptr_to_i64 %82
    %84 = arith.constant {value = 5 : i64}
    %85 = arith.constant {value = 16 : i64}
    %86 = func.ref @__destruct_ByteArray
    %87 = std.ptr_to_i64 %86
    %88 = arith.constant {value = 3 : i64}
    %89 = std.call_runtime @mm_alloc %85, %87, %88
    memref.store %89, __lit_tmp_10
    %90 = arith.constant {value = 0 : i64}
    %91 = memref.load __lit_tmp_10 : i64
    memref.store_indirect %90, %91+0
    %92 = arith.constant {value = 32 : i64}
    %93 = func.ref @__destruct___ManagedMemory
    %94 = std.ptr_to_i64 %93
    %95 = arith.constant {value = 4 : i64}
    %96 = std.call_runtime @mm_alloc %92, %94, %95
    memref.store %96, __bstrtmp_managed_10
    %97 = memref.load __bstrtmp_managed_10 : i64
    memref.store_indirect %83, %97+0
    %98 = memref.load __bstrtmp_managed_10 : i64
    memref.store_indirect %84, %98+8
    %99 = arith.constant {value = 0 : i64}
    %100 = memref.load __bstrtmp_managed_10 : i64
    memref.store_indirect %99, %100+16
    %101 = arith.constant {value = 1 : i64}
    %102 = memref.load __bstrtmp_managed_10 : i64
    memref.store_indirect %101, %102+24
    %103 = memref.load __bstrtmp_managed_10 : i64
    %104 = memref.load __lit_tmp_10 : i64
    memref.store_indirect %103, %104+8
    %105 = memref.load __bstrtmp_managed_10 : i64
    std.call_runtime @mm_incref %105
    %106 = memref.load __lit_tmp_10 : i64
    std.call_runtime @mm_incref %106
    %107 = memref.load names : i64
    %108 = memref.load __lit_tmp_10 : i64
    %109 = func.call @ByteArrayArray.contains$sequence %107, %108
    cf.cond_br %109 [then: found_0, else: notFound_1]
  found_0:
    %110 = memref.lea_rdata __str_3
    %111 = std.ptr_to_i64 %110
    %112 = arith.constant {value = 6 : i64}
    %113 = arith.constant {value = 16 : i64}
    %114 = func.ref @__destruct_String
    %115 = std.ptr_to_i64 %114
    %116 = arith.constant {value = 5 : i64}
    %117 = std.call_runtime @mm_alloc %113, %115, %116
    memref.store %117, __lit_tmp_12
    %118 = arith.constant {value = 32 : i64}
    %119 = func.ref @__destruct___ManagedMemory
    %120 = std.ptr_to_i64 %119
    %121 = arith.constant {value = 4 : i64}
    %122 = std.call_runtime @mm_alloc %118, %120, %121
    memref.store %122, __strtmp_managed_12
    %123 = memref.load __strtmp_managed_12 : i64
    memref.store_indirect %111, %123+0
    %124 = memref.load __strtmp_managed_12 : i64
    memref.store_indirect %112, %124+8
    %125 = arith.constant {value = 0 : i64}
    %126 = memref.load __strtmp_managed_12 : i64
    memref.store_indirect %125, %126+16
    %127 = arith.constant {value = 1 : i64}
    %128 = memref.load __strtmp_managed_12 : i64
    memref.store_indirect %127, %128+24
    %129 = memref.load __strtmp_managed_12 : i64
    %130 = memref.load __lit_tmp_12 : i64
    memref.store_indirect %129, %130+0
    %131 = memref.load __strtmp_managed_12 : i64
    std.call_runtime @mm_incref %131
    %132 = arith.constant {value = 0 : i64}
    %133 = memref.load __lit_tmp_12 : i64
    memref.store_indirect %132, %133+8
    %134 = memref.load __lit_tmp_12 : i64
    std.call_runtime @mm_incref %134
    %135 = memref.load __lit_tmp_12 : i64
    func.call @stdlib.Print.print %135
    %136 = memref.load __lit_tmp_12 : i64
    std.call_runtime_if_nonnull @mm_decref %136
    cf.br found_0.merge
  notFound_1:
    %138 = memref.lea_rdata __str_4
    %139 = std.ptr_to_i64 %138
    %140 = arith.constant {value = 10 : i64}
    %141 = arith.constant {value = 16 : i64}
    %142 = func.ref @__destruct_String
    %143 = std.ptr_to_i64 %142
    %144 = arith.constant {value = 5 : i64}
    %145 = std.call_runtime @mm_alloc %141, %143, %144
    memref.store %145, __lit_tmp_13
    %146 = arith.constant {value = 32 : i64}
    %147 = func.ref @__destruct___ManagedMemory
    %148 = std.ptr_to_i64 %147
    %149 = arith.constant {value = 4 : i64}
    %150 = std.call_runtime @mm_alloc %146, %148, %149
    memref.store %150, __strtmp_managed_13
    %151 = memref.load __strtmp_managed_13 : i64
    memref.store_indirect %139, %151+0
    %152 = memref.load __strtmp_managed_13 : i64
    memref.store_indirect %140, %152+8
    %153 = arith.constant {value = 0 : i64}
    %154 = memref.load __strtmp_managed_13 : i64
    memref.store_indirect %153, %154+16
    %155 = arith.constant {value = 1 : i64}
    %156 = memref.load __strtmp_managed_13 : i64
    memref.store_indirect %155, %156+24
    %157 = memref.load __strtmp_managed_13 : i64
    %158 = memref.load __lit_tmp_13 : i64
    memref.store_indirect %157, %158+0
    %159 = memref.load __strtmp_managed_13 : i64
    std.call_runtime @mm_incref %159
    %160 = arith.constant {value = 0 : i64}
    %161 = memref.load __lit_tmp_13 : i64
    memref.store_indirect %160, %161+8
    %162 = memref.load __lit_tmp_13 : i64
    std.call_runtime @mm_incref %162
    %163 = memref.load __lit_tmp_13 : i64
    func.call @stdlib.Print.print %163
    %164 = memref.load __lit_tmp_13 : i64
    std.call_runtime_if_nonnull @mm_decref %164
    cf.br found_0.merge
  found_0.merge:
    %166 = memref.lea_rdata __bstr_5
    %167 = std.ptr_to_i64 %166
    %168 = arith.constant {value = 7 : i64}
    %169 = arith.constant {value = 16 : i64}
    %170 = func.ref @__destruct_ByteArray
    %171 = std.ptr_to_i64 %170
    %172 = arith.constant {value = 3 : i64}
    %173 = std.call_runtime @mm_alloc %169, %171, %172
    memref.store %173, __lit_tmp_15
    %174 = arith.constant {value = 0 : i64}
    %175 = memref.load __lit_tmp_15 : i64
    memref.store_indirect %174, %175+0
    %176 = arith.constant {value = 32 : i64}
    %177 = func.ref @__destruct___ManagedMemory
    %178 = std.ptr_to_i64 %177
    %179 = arith.constant {value = 4 : i64}
    %180 = std.call_runtime @mm_alloc %176, %178, %179
    memref.store %180, __bstrtmp_managed_15
    %181 = memref.load __bstrtmp_managed_15 : i64
    memref.store_indirect %167, %181+0
    %182 = memref.load __bstrtmp_managed_15 : i64
    memref.store_indirect %168, %182+8
    %183 = arith.constant {value = 0 : i64}
    %184 = memref.load __bstrtmp_managed_15 : i64
    memref.store_indirect %183, %184+16
    %185 = arith.constant {value = 1 : i64}
    %186 = memref.load __bstrtmp_managed_15 : i64
    memref.store_indirect %185, %186+24
    %187 = memref.load __bstrtmp_managed_15 : i64
    %188 = memref.load __lit_tmp_15 : i64
    memref.store_indirect %187, %188+8
    %189 = memref.load __bstrtmp_managed_15 : i64
    std.call_runtime @mm_incref %189
    %190 = memref.load __lit_tmp_15 : i64
    std.call_runtime @mm_incref %190
    %191 = memref.load names : i64
    %192 = memref.load __lit_tmp_15 : i64
    %193 = func.call @ByteArrayArray.contains$sequence %191, %192
    cf.cond_br %193 [then: found2_2, else: notFound2_3]
  found2_2:
    %194 = memref.lea_rdata __str_3
    %195 = std.ptr_to_i64 %194
    %196 = arith.constant {value = 6 : i64}
    %197 = arith.constant {value = 16 : i64}
    %198 = func.ref @__destruct_String
    %199 = std.ptr_to_i64 %198
    %200 = arith.constant {value = 5 : i64}
    %201 = std.call_runtime @mm_alloc %197, %199, %200
    memref.store %201, __lit_tmp_17
    %202 = arith.constant {value = 32 : i64}
    %203 = func.ref @__destruct___ManagedMemory
    %204 = std.ptr_to_i64 %203
    %205 = arith.constant {value = 4 : i64}
    %206 = std.call_runtime @mm_alloc %202, %204, %205
    memref.store %206, __strtmp_managed_17
    %207 = memref.load __strtmp_managed_17 : i64
    memref.store_indirect %195, %207+0
    %208 = memref.load __strtmp_managed_17 : i64
    memref.store_indirect %196, %208+8
    %209 = arith.constant {value = 0 : i64}
    %210 = memref.load __strtmp_managed_17 : i64
    memref.store_indirect %209, %210+16
    %211 = arith.constant {value = 1 : i64}
    %212 = memref.load __strtmp_managed_17 : i64
    memref.store_indirect %211, %212+24
    %213 = memref.load __strtmp_managed_17 : i64
    %214 = memref.load __lit_tmp_17 : i64
    memref.store_indirect %213, %214+0
    %215 = memref.load __strtmp_managed_17 : i64
    std.call_runtime @mm_incref %215
    %216 = arith.constant {value = 0 : i64}
    %217 = memref.load __lit_tmp_17 : i64
    memref.store_indirect %216, %217+8
    %218 = memref.load __lit_tmp_17 : i64
    std.call_runtime @mm_incref %218
    %219 = memref.load __lit_tmp_17 : i64
    func.call @stdlib.Print.print %219
    %220 = memref.load __lit_tmp_17 : i64
    std.call_runtime_if_nonnull @mm_decref %220
    cf.br found2_2.merge
  notFound2_3:
    %222 = memref.lea_rdata __str_4
    %223 = std.ptr_to_i64 %222
    %224 = arith.constant {value = 10 : i64}
    %225 = arith.constant {value = 16 : i64}
    %226 = func.ref @__destruct_String
    %227 = std.ptr_to_i64 %226
    %228 = arith.constant {value = 5 : i64}
    %229 = std.call_runtime @mm_alloc %225, %227, %228
    memref.store %229, __lit_tmp_18
    %230 = arith.constant {value = 32 : i64}
    %231 = func.ref @__destruct___ManagedMemory
    %232 = std.ptr_to_i64 %231
    %233 = arith.constant {value = 4 : i64}
    %234 = std.call_runtime @mm_alloc %230, %232, %233
    memref.store %234, __strtmp_managed_18
    %235 = memref.load __strtmp_managed_18 : i64
    memref.store_indirect %223, %235+0
    %236 = memref.load __strtmp_managed_18 : i64
    memref.store_indirect %224, %236+8
    %237 = arith.constant {value = 0 : i64}
    %238 = memref.load __strtmp_managed_18 : i64
    memref.store_indirect %237, %238+16
    %239 = arith.constant {value = 1 : i64}
    %240 = memref.load __strtmp_managed_18 : i64
    memref.store_indirect %239, %240+24
    %241 = memref.load __strtmp_managed_18 : i64
    %242 = memref.load __lit_tmp_18 : i64
    memref.store_indirect %241, %242+0
    %243 = memref.load __strtmp_managed_18 : i64
    std.call_runtime @mm_incref %243
    %244 = arith.constant {value = 0 : i64}
    %245 = memref.load __lit_tmp_18 : i64
    memref.store_indirect %244, %245+8
    %246 = memref.load __lit_tmp_18 : i64
    std.call_runtime @mm_incref %246
    %247 = memref.load __lit_tmp_18 : i64
    func.call @stdlib.Print.print %247
    %248 = memref.load __lit_tmp_18 : i64
    std.call_runtime_if_nonnull @mm_decref %248
    cf.br found2_2.merge
  found2_2.merge:
    %250 = arith.constant {value = 0 : i64}
    %251 = memref.load __lit_tmp_15 : i64
    std.call_runtime_if_nonnull @mm_decref %251
    %253 = memref.load __lit_tmp_10 : i64
    std.call_runtime_if_nonnull @mm_decref %253
    %255 = memref.load __lit_tmp_8 : i64
    std.call_runtime_if_nonnull @mm_decref %255
    %257 = memref.load __lit_tmp_7 : i64
    std.call_runtime_if_nonnull @mm_decref %257
    %259 = memref.load names : i64
    std.call_runtime_if_nonnull @mm_decref %259
    func.return %250
  }
  func @ByteArray.clone(__self_ptr: i64) -> i64 {
  entry:
    %270 = func.param self : StdI64
    memref.store %270, self
    %275 = memref.load self : i64
    %276 = memref.load_indirect %275+8
    memref.store %276, __selfref_876
    %277 = memref.load __selfref_876 : i64
    %278 = memref.load_indirect %277+8
    %279 = arith.constant {value = 0 : i64}
    %280 = memref.load __selfref_876 : i64
    %281 = memref.load_indirect %280+8
    %282 = arith.constant {value = 1 : i64}
    %283 = arith.addi %281, %282
    %284 = memref.lea_symdata __mm_panic_slice_oob
    %285 = std.ptr_to_i64 %284
    std.call_runtime @maxon_bounds_check %278, %283, %285
    %286 = arith.addi %278, %282
    %287 = memref.lea_symdata __mm_panic_slice_oob
    %288 = std.ptr_to_i64 %287
    std.call_runtime @maxon_bounds_check %279, %286, %288
    %289 = memref.load __selfref_876 : i64
    %290 = memref.load_indirect %289+0
    %291 = memref.load __selfref_876 : i64
    %292 = memref.load_indirect %291+24
    %293 = arith.muli %279, %292
    %294 = arith.addi %290, %293
    %295 = arith.subi %278, %279
    %296 = arith.muli %295, %292
    %297 = arith.constant {value = 32 : i64}
    %298 = func.ref @__destruct___ManagedMemory
    %299 = std.ptr_to_i64 %298
    %300 = arith.constant {value = 6 : i64}
    %301 = std.call_runtime @mm_alloc %297, %299, %300
    memref.store %301, __lit_tmp_2292
    %302 = std.call_runtime @mm_raw_alloc %296
    std.memcopy %294, %302, %296
    %303 = memref.load __lit_tmp_2292 : i64
    memref.store_indirect %302, %303+0
    %304 = memref.load __lit_tmp_2292 : i64
    memref.store_indirect %295, %304+8
    %305 = memref.load __lit_tmp_2292 : i64
    memref.store_indirect %295, %305+16
    %306 = memref.load __lit_tmp_2292 : i64
    memref.store_indirect %292, %306+24
    %307 = memref.load __lit_tmp_2292 : i64
    std.call_runtime @mm_incref %307
    memref.store %301, newManaged
    %309 = memref.load newManaged : i64
    std.call_runtime @mm_incref %309
    %310 = arith.constant {value = 0 : i64}
    %311 = arith.constant {value = 16 : i64}
    %312 = func.ref @__destruct_ByteArray
    %313 = std.ptr_to_i64 %312
    %314 = arith.constant {value = 3 : i64}
    %315 = std.call_runtime @mm_alloc %311, %313, %314
    memref.store %315, __struct_883
    %316 = memref.load newManaged : i64
    %317 = memref.load __struct_883 : i64
    memref.store_indirect %316, %317+8
    std.call_runtime @mm_incref %316
    %318 = memref.load __struct_883 : i64
    memref.store_indirect %310, %318+0
    %319 = memref.load __lit_tmp_2292 : i64
    std.call_runtime_if_nonnull @mm_decref %319
    %321 = memref.load newManaged : i64
    std.call_runtime_if_nonnull @mm_decref %321
    %323 = memref.load __struct_883 : i64
    std.call_runtime @mm_incref %323
    %324 = memref.load __struct_883 : i64
    func.return %324
  }
  func @ByteArray.equals(__self_ptr: i64, other: i64) -> i1 {
  entry:
    %327 = func.param self : StdI64
    memref.store %327, self
    %332 = func.param other : StdHeapPtr
    memref.store %332, other
    %333 = memref.load self : i64
    %334 = memref.load_indirect %333+8
    memref.store %334, __selfref_1189
    %335 = memref.load __selfref_1189 : i64
    %336 = memref.load_indirect %335+8
    memref.store %336, selfLen
    %337 = memref.load other : i64
    %338 = memref.load_indirect %337+8
    memref.store %338, __field_1192
    %339 = memref.load __field_1192 : i64
    %340 = memref.load_indirect %339+8
    %341 = arith.cmpi ne %336, %340
    cf.cond_br %341 [then: len_0, else: len_0.after]
  len_0:
    %342 = arith.constant {value = 0 : i1}
    func.return %342
  len_0.after:
    %343 = memref.load self : i64
    %344 = memref.load_indirect %343+8
    memref.store %344, __selfref_1196
    %345 = memref.load __selfref_1196 : i64
    %346 = memref.load_indirect %345+24
    %347 = memref.load selfLen : i64
    %348 = arith.muli %347, %346
    %349 = arith.constant {value = 0 : i64}
    memref.store %348, __range_end_2
    memref.store %349, __range_current_2
    cf.br cmp_1.header
  cmp_1.header:
    %350 = memref.load __range_current_2 : i64
    %351 = memref.load __range_end_2 : i64
    %352 = arith.cmpi lt %350, %351
    cf.cond_br %352 [then: cmp_1, else: cmp_1.exit]
  cmp_1:
    %353 = memref.load __range_current_2 : i64
    %354 = memref.load self : i64
    %355 = memref.load_indirect %354+8
    memref.store %355, __selfref_1205
    %356 = memref.load __selfref_1205 : i64
    %357 = memref.load_indirect %356+8
    %358 = memref.load __selfref_1205 : i64
    %359 = memref.load_indirect %358+24
    %360 = arith.muli %357, %359
    %361 = memref.lea_symdata __mm_panic_byte_oob
    %362 = std.ptr_to_i64 %361
    std.call_runtime @maxon_bounds_check %353, %360, %362
    %363 = memref.load __selfref_1205 : i64
    %364 = memref.load_indirect %363+0
    %365 = arith.addi %364, %353
    %366 = memref.load_indirect %365+0
    %367 = memref.load other : i64
    %368 = memref.load_indirect %367+8
    memref.store %368, __field_1208
    %369 = memref.load __field_1208 : i64
    %370 = memref.load_indirect %369+8
    %371 = memref.load __field_1208 : i64
    %372 = memref.load_indirect %371+24
    %373 = arith.muli %370, %372
    %374 = memref.lea_symdata __mm_panic_byte_oob
    %375 = std.ptr_to_i64 %374
    std.call_runtime @maxon_bounds_check %353, %373, %375
    %376 = memref.load __field_1208 : i64
    %377 = memref.load_indirect %376+0
    %378 = arith.addi %377, %353
    %379 = memref.load_indirect %378+0
    %380 = arith.cmpi ne %366, %379
    cf.cond_br %380 [then: ne_2, else: ne_2.after]
  ne_2:
    %381 = arith.constant {value = 0 : i1}
    func.return %381
  ne_2.after:
    cf.br cmp_1.incr
  cmp_1.incr:
    %382 = arith.constant {value = 1 : i64}
    %383 = memref.load __range_current_2 : i64
    %384 = arith.addi %383, %382
    memref.store %384, __range_current_2
    cf.br cmp_1.header
  cmp_1.exit:
    %385 = arith.constant {value = 1 : i1}
    func.return %385
  }
  func @ByteArrayArray.count(__self_ptr: i64) -> u64 {
  entry:
    %386 = func.param self : StdI64
    memref.store %386, self
    %391 = memref.load self : i64
    %392 = memref.load_indirect %391+8
    memref.store %392, __selfref_4535
    %393 = memref.load __selfref_4535 : i64
    %394 = memref.load_indirect %393+8
    %395 = arith.constant {value = 0 : i64}
    %396 = arith.cmpi lt %394, %395
    cf.cond_br %396 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %397 = memref.lea_symdata __panic_msg_2
    %398 = std.ptr_to_i64 %397
    std.call_runtime @maxon_panic %398
  __range_ok_0:
    func.return %394
  }
  func @ByteArrayArray.get(__self_ptr: i64, index: i64) -> i64 {
  entry:
    %399 = func.param self : StdI64
    memref.store %399, self
    %404 = func.param index : StdI64
    memref.store %404, index
    %405 = memref.load self : i64
    %406 = memref.load_indirect %405+8
    memref.store %406, __selfref_4581
    %407 = memref.load __selfref_4581 : i64
    %408 = memref.load_indirect %407+8
    %409 = arith.cmpui uge %404, %408
    cf.cond_br %409 [then: upper_0, else: upper_0.after]
  upper_0:
    %410 = arith.constant {value = 0 : i64}
    %411 = arith.constant {value = 1 : i64}
    %412 = arith.addi %410, %411
    func.error_return %412
  upper_0.after:
    %413 = memref.load self : i64
    %414 = memref.load_indirect %413+8
    memref.store %414, __selfref_4585
    %415 = memref.load index : i64
    %416 = memref.load __selfref_4585 : i64
    %417 = memref.load_indirect %416+8
    %418 = memref.lea_symdata __mm_panic_index_oob
    %419 = std.ptr_to_i64 %418
    std.call_runtime @maxon_bounds_check %415, %417, %419
    %420 = memref.load __selfref_4585 : i64
    %421 = memref.load_indirect %420+24
    %422 = memref.load __selfref_4585 : i64
    %423 = memref.load_indirect %422+0
    %424 = arith.muli %415, %421
    %425 = arith.addi %423, %424
    %426 = memref.load_indirect %425+0
    %427 = arith.constant {value = 0 : i64}
    %428 = arith.cmpi eq %426, %427
    cf.cond_br %428 [then: __slot_empty_429, else: __slot_nonnull_429]
  __slot_empty_429:
    %430 = arith.constant {value = 2 : i64}
    func.error_return %430
  __slot_nonnull_429:
    std.call_runtime @mm_incref %426
    memref.store %426, __mmget_431
    %432 = memref.load __mmget_431 : i64
    func.return %432
  }
  func @ByteArrayArray.ensureCapacity(__self_ptr: i64, requiredLen: i64) {
  entry:
    %434 = func.param self : StdI64
    memref.store %434, self
    %437 = memref.load self : i64
    %438 = memref.load_indirect %437+8
    memref.store %438, __field_4624
    %439 = func.param requiredLen : StdI64
    memref.store %439, requiredLen
    %440 = memref.load self : i64
    %441 = memref.load_indirect %440+8
    memref.store %441, __selfref_4626
    %442 = memref.load __selfref_4626 : i64
    %443 = memref.load_indirect %442+16
    memref.store %443, cap
    %444 = arith.cmpui ugt %439, %443
    cf.cond_br %444 [then: grow_0, else: grow_0.merge]
  grow_0:
    %445 = memref.load cap : i64
    memref.store %445, newCap
    %446 = arith.constant {value = 4 : i64}
    %447 = arith.cmpi lt %445, %446
    cf.cond_br %447 [then: min_1, else: min_1.merge]
  min_1:
    %448 = arith.constant {value = 4 : i64}
    memref.store %448, newCap
    cf.br min_1.merge
  min_1.merge:
    cf.br double_2.header
  double_2.header:
    %449 = memref.load newCap : i64
    %450 = memref.load requiredLen : i64
    %451 = arith.cmpi lt %449, %450
    cf.cond_br %451 [then: double_2, else: double_2.exit]
  double_2:
    %452 = arith.constant {value = 2 : i64}
    %453 = memref.load newCap : i64
    %454 = arith.muli %453, %452
    memref.store %454, newCap
    cf.br double_2.header
  double_2.exit:
    %455 = memref.load newCap : i64
    %456 = memref.load __field_4624 : i64
    %457 = memref.load_indirect %456+24
    %458 = memref.load __field_4624 : i64
    %459 = memref.load_indirect %458+16
    %460 = arith.constant {value = 1 : i64}
    %461 = arith.addi %455, %460
    %462 = memref.lea_symdata __mm_panic_grow_shrink
    %463 = std.ptr_to_i64 %462
    std.call_runtime @maxon_bounds_check %459, %461, %463
    %464 = memref.load __field_4624 : i64
    %465 = memref.load_indirect %464+0
    %466 = memref.load __field_4624 : i64
    %467 = memref.load_indirect %466+16
    %468 = memref.load __field_4624 : i64
    %469 = memref.load_indirect %468+8
    memref.store %469, __cow_len_470
    %471 = memref.load __field_4624 : i64
    %472 = arith.muli %469, %457
    %473 = std.call_runtime @maxon_cow_check %465, %467, %472, %471
    %474 = memref.load __field_4624 : i64
    memref.store_indirect %473, %474+0
    %475 = arith.constant {value = 0 : i64}
    %476 = arith.cmpi eq %467, %475
    %477 = memref.load __cow_len_470 : i64
    %478 = arith.select %476, %477, %467
    %479 = memref.load __field_4624 : i64
    memref.store_indirect %478, %479+16
    %480 = memref.load __field_4624 : i64
    %481 = memref.load_indirect %480+0
    %482 = arith.muli %455, %457
    %483 = memref.load __field_4624 : i64
    %484 = std.call_runtime @mm_raw_realloc %481, %482, %483
    %485 = memref.load __field_4624 : i64
    memref.store_indirect %484, %485+0
    %486 = memref.load __field_4624 : i64
    memref.store_indirect %455, %486+16
    cf.br grow_0.merge
  grow_0.merge:
    func.return
  }
  func @ByteArrayArray.push(__self_ptr: i64, value: i64) {
  entry:
    %487 = func.param self : StdI64
    memref.store %487, self
    %490 = memref.load self : i64
    %491 = memref.load_indirect %490+8
    memref.store %491, __field_4642
    %492 = func.param value : StdHeapPtr
    memref.store %492, value
    %493 = memref.load self : i64
    %494 = memref.load_indirect %493+8
    memref.store %494, __selfref_4644
    %495 = memref.load __selfref_4644 : i64
    %496 = memref.load_indirect %495+8
    %497 = arith.constant {value = 1 : i64}
    %498 = arith.addi %496, %497
    %499 = memref.load self : i64
    func.call @ByteArrayArray.ensureCapacity %499, %498
    %502 = memref.load __field_4642 : i64
    %503 = memref.load_indirect %502+24
    %504 = memref.load __field_4642 : i64
    %505 = memref.load_indirect %504+0
    %506 = memref.load __field_4642 : i64
    %507 = memref.load_indirect %506+16
    %508 = memref.load __field_4642 : i64
    %509 = memref.load_indirect %508+8
    memref.store %509, __cow_len_510
    %511 = memref.load __field_4642 : i64
    %512 = arith.muli %509, %503
    %513 = std.call_runtime @maxon_cow_check %505, %507, %512, %511
    %514 = memref.load __field_4642 : i64
    memref.store_indirect %513, %514+0
    %515 = arith.constant {value = 0 : i64}
    %516 = arith.cmpi eq %507, %515
    %517 = memref.load __cow_len_510 : i64
    %518 = arith.select %516, %517, %507
    %519 = memref.load __field_4642 : i64
    memref.store_indirect %518, %519+16
    %520 = memref.load __field_4642 : i64
    %521 = memref.load_indirect %520+16
    %522 = memref.lea_symdata __mm_panic_index_oob
    %523 = std.ptr_to_i64 %522
    std.call_runtime @maxon_bounds_check %496, %521, %523
    %524 = memref.load __field_4642 : i64
    %525 = memref.load_indirect %524+0
    %526 = arith.muli %496, %503
    %527 = arith.addi %525, %526
    %528 = memref.load_indirect %527+0
    std.call_runtime_if_nonnull @mm_decref %528
    %529 = memref.load value : i64
    memref.store_indirect %529, %527+0
    std.call_runtime @mm_incref %529
    %530 = arith.constant {value = 1 : i64}
    %531 = arith.addi %496, %530
    %532 = memref.load __field_4642 : i64
    %533 = memref.load_indirect %532+16
    %534 = arith.constant {value = 1 : i64}
    %535 = arith.addi %533, %534
    %536 = memref.lea_symdata __mm_panic_setlength_oob
    %537 = std.ptr_to_i64 %536
    std.call_runtime @maxon_bounds_check %531, %535, %537
    %538 = memref.load __field_4642 : i64
    memref.store_indirect %531, %538+8
    func.return
  }
  func @ByteArrayArray.contains$sequence(__self_ptr: i64, sequence: i64) -> i1 {
  entry:
    %539 = func.param self : StdI64
    memref.store %539, self
    %544 = func.param sequence : StdHeapPtr
    memref.store %544, sequence
    %545 = memref.load self : i64
    %546 = func.call @ByteArrayArray.count %545
    memref.store %546, selfLen
    %549 = memref.load sequence : i64
    %550 = func.call @ByteArrayArray.count %549
    memref.store %550, elemLen
    %553 = arith.constant {value = 0 : i64}
    %554 = arith.cmpi eq %550, %553
    cf.cond_br %554 [then: empty_0, else: empty_0.after]
  empty_0:
    %555 = arith.constant {value = 1 : i1}
    func.return %555
  empty_0.after:
    %556 = arith.constant {value = 0 : i64}
    %557 = memref.load selfLen : i64
    %558 = memref.load elemLen : i64
    %559 = arith.subi %557, %558
    memref.store %559, __range_end_2
    memref.store %556, __range_current_2
    cf.br scan_1.header
  scan_1.header:
    %560 = memref.load __range_current_2 : i64
    %561 = memref.load __range_end_2 : i64
    %562 = arith.cmpi le %560, %561
    cf.cond_br %562 [then: scan_1, else: scan_1.exit]
  scan_1:
    %563 = memref.load __range_current_2 : i64
    memref.store %563, i
    %564 = arith.constant {value = 1 : i1}
    memref.store %564, matched
    %565 = arith.constant {value = 0 : i64}
    %566 = memref.load elemLen : i64
    memref.store %566, __range_end_3
    memref.store %565, __range_current_3
    cf.br cmp_2.header
  cmp_2.header:
    %567 = memref.load __range_current_3 : i64
    %568 = memref.load __range_end_3 : i64
    %569 = arith.cmpi lt %567, %568
    cf.cond_br %569 [then: cmp_2, else: cmp_2.exit]
  cmp_2:
    %570 = memref.load __range_current_3 : i64
    memref.store %570, j
    %571 = memref.load i : i64
    %572 = arith.addi %571, %570
    %573 = memref.load self : i64
    %574, %575 = func.try_call @ByteArrayArray.get %573, %572
    memref.store %574, __try_result_6
    %577 = arith.constant {value = 0 : i64}
    %578 = arith.cmpi ne %575, %577
    cf.cond_br %578 [then: otherwise_error_3, else: otherwise_continue_4]
  otherwise_error_3:
    %579 = memref.load __try_result_6 : i64
    std.call_runtime_if_nonnull @mm_decref %579
    cf.br cmp_2.exit
  otherwise_continue_4:
    %581 = memref.load __try_result_6 : i64
    memref.store %581, a
    %582 = memref.load a : i64
    std.call_runtime @mm_incref %582
    %583 = memref.load j : i64
    %584 = memref.load sequence : i64
    %585, %586 = func.try_call @ByteArrayArray.get %584, %583
    memref.store %585, __try_result_10
    %588 = arith.constant {value = 0 : i64}
    %589 = arith.cmpi ne %586, %588
    cf.cond_br %589 [then: otherwise_error_7, else: otherwise_continue_8]
  otherwise_error_7:
    %590 = memref.load __try_result_10 : i64
    std.call_runtime_if_nonnull @mm_decref %590
    %592 = memref.load a : i64
    std.call_runtime_if_nonnull @mm_decref %592
    %594 = memref.load __try_result_6 : i64
    std.call_runtime_if_nonnull @mm_decref %594
    cf.br cmp_2.exit
  otherwise_continue_8:
    %596 = memref.load __try_result_10 : i64
    memref.store %596, b
    %597 = memref.load b : i64
    std.call_runtime @mm_incref %597
    %598 = memref.load a : i64
    %599 = memref.load b : i64
    %600 = func.call @ByteArray.equals %598, %599
    %603 = arith.constant {value = 1 : i1}
    %604 = arith.xori1 %600, %603
    cf.cond_br %604 [then: ne_11, else: ne_11.after]
  ne_11:
    %605 = arith.constant {value = 0 : i1}
    memref.store %605, matched
    %606 = memref.load b : i64
    std.call_runtime_if_nonnull @mm_decref %606
    %608 = memref.load __try_result_10 : i64
    std.call_runtime_if_nonnull @mm_decref %608
    %610 = memref.load a : i64
    std.call_runtime_if_nonnull @mm_decref %610
    %612 = memref.load __try_result_6 : i64
    std.call_runtime_if_nonnull @mm_decref %612
    cf.br cmp_2.exit
  ne_11.after:
    %614 = memref.load b : i64
    std.call_runtime_if_nonnull @mm_decref %614
    %616 = memref.load __try_result_10 : i64
    std.call_runtime_if_nonnull @mm_decref %616
    %618 = memref.load a : i64
    std.call_runtime_if_nonnull @mm_decref %618
    %620 = memref.load __try_result_6 : i64
    std.call_runtime_if_nonnull @mm_decref %620
    cf.br cmp_2.incr
  cmp_2.incr:
    %622 = arith.constant {value = 1 : i64}
    %623 = memref.load __range_current_3 : i64
    %624 = arith.addi %623, %622
    memref.store %624, __range_current_3
    cf.br cmp_2.header
  cmp_2.exit:
    %625 = memref.load matched : i1
    cf.cond_br %625 [then: found_12, else: found_12.after]
  found_12:
    %626 = arith.constant {value = 1 : i1}
    func.return %626
  found_12.after:
    cf.br scan_1.incr
  scan_1.incr:
    %627 = arith.constant {value = 1 : i64}
    %628 = memref.load __range_current_2 : i64
    %629 = arith.addi %628, %627
    memref.store %629, __range_current_2
    cf.br scan_1.header
  scan_1.exit:
    %630 = arith.constant {value = 0 : i1}
    func.return %630
  }
  func @__destruct___ManagedMemory_ByteArray(ptr: i64) {
  entry:
    %635 = func.param ptr : StdI64
    memref.store %635, __destr_ptr
    %638 = memref.load __destr_ptr : i64
    %639 = memref.load_indirect %638+16
    %640 = arith.constant {value = 0 : i64}
    %641 = arith.cmpi ne %639, %640
    cf.cond_br %641 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %642 = memref.load __destr_ptr : i64
    std.call_runtime @mm_decref_managed_elements %642
    %643 = memref.load __destr_ptr : i64
    %644 = memref.load_indirect %643+0
    std.call_runtime @mm_raw_free %644
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_ByteArrayArray(ptr: i64) {
  entry:
    %645 = func.param ptr : StdI64
    memref.store %645, __destr_ptr
    %646 = memref.load __destr_ptr : i64
    %647 = memref.load_indirect %646+8
    std.call_runtime_if_nonnull @mm_decref %647
    cf.br done
  done:
    func.return
  }
  func @__destruct_ByteArray(ptr: i64) {
  entry:
    %648 = func.param ptr : StdI64
    memref.store %648, __destr_ptr
    %649 = memref.load __destr_ptr : i64
    %650 = memref.load_indirect %649+8
    std.call_runtime_if_nonnull @mm_decref %650
    cf.br done
  done:
    func.return
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    %651 = func.param ptr : StdI64
    memref.store %651, __destr_ptr
    %654 = memref.load __destr_ptr : i64
    %655 = memref.load_indirect %654+16
    %656 = arith.constant {value = 0 : i64}
    %657 = arith.cmpi ne %655, %656
    cf.cond_br %657 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %658 = memref.load __destr_ptr : i64
    %659 = memref.load_indirect %658+0
    std.call_runtime @mm_raw_free %659
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
  func @__destruct_String(ptr: i64) {
  entry:
    %660 = func.param ptr : StdI64
    memref.store %660, __destr_ptr
    %661 = memref.load __destr_ptr : i64
    %662 = memref.load_indirect %661+0
    std.call_runtime_if_nonnull @mm_decref %662
    cf.br done
  done:
    func.return
  }
}
