module {
  func @stdlib.Print.print(value: i64) {
  entry:
    %4903 = func.param value : StdHeapPtr
    memref.store %4903, value
    %4904 = memref.load value : i64
    %4905 = memref.load_indirect %4904+0
    memref.store %4905, __field_6460
    %4906 = memref.load __field_6460 : i64
    %4907 = memref.load_indirect %4906+0
    %4908 = memref.load __field_6460 : i64
    %4909 = memref.load_indirect %4908+8
    %4910 = std.call_runtime @maxon_managed_write_stdout %4907, %4909
    func.return
  }
  func @Pair.toString(__self_ptr: i64) -> i64 {
  entry:
    %0 = func.param self : StdI64
    memref.store %0, self
    %1 = memref.load self : i64
    %2 = memref.load_indirect %1+0
    %3 = memref.load self : i64
    %4 = memref.load_indirect %3+8
    %5 = memref.lea_rdata __interp_lit_0
    %6 = std.ptr_to_i64 %5
    %7 = arith.constant {value = 1 : i64}
    %8 = arith.constant {value = 21 : i64}
    %9 = std.call_runtime @mm_raw_alloc %8
    memref.store %9, __tostr_buf_9
    %10 = std.call_runtime @maxon_i64_to_string %2, %9
    %11 = memref.load __tostr_buf_9 : i64
    %12 = memref.lea_rdata __interp_lit_1
    %13 = std.ptr_to_i64 %12
    %14 = arith.constant {value = 2 : i64}
    %15 = arith.constant {value = 21 : i64}
    %16 = std.call_runtime @mm_raw_alloc %15
    memref.store %16, __tostr_buf_16
    %17 = std.call_runtime @maxon_i64_to_string %4, %16
    %18 = memref.load __tostr_buf_16 : i64
    %19 = memref.lea_rdata __interp_lit_2
    %20 = std.ptr_to_i64 %19
    %21 = arith.constant {value = 1 : i64}
    %22 = arith.addi %7, %10
    %23 = arith.addi %22, %14
    %24 = arith.addi %23, %17
    %25 = arith.addi %24, %21
    %26 = arith.constant {value = 16 : i64}
    %27 = func.ref @__destruct_String
    %28 = std.ptr_to_i64 %27
    %29 = arith.constant {value = 1 : i64}
    %30 = std.call_runtime @mm_alloc %26, %28, %29
    memref.store %30, __lit_tmp_3
    %31 = arith.constant {value = 32 : i64}
    %32 = func.ref @__destruct___ManagedMemory
    %33 = std.ptr_to_i64 %32
    %34 = arith.constant {value = 2 : i64}
    %35 = std.call_runtime @mm_alloc %31, %33, %34
    memref.store %35, __interp_managed_3
    %36 = arith.constant {value = 1 : i64}
    %37 = arith.addi %25, %36
    %38 = std.call_runtime @mm_raw_alloc %37
    %39 = arith.constant {value = 0 : i64}
    memref.store %39, __interp_offset_3
    memref.store %38, __interp_buf_3
    memref.store %25, __interp_totallen_3
    memref.store %6, __interp_partbuf_3_0
    memref.store %7, __interp_partlen_3_0
    memref.store %11, __interp_partbuf_3_1
    memref.store %10, __interp_partlen_3_1
    memref.store %13, __interp_partbuf_3_2
    memref.store %14, __interp_partlen_3_2
    memref.store %18, __interp_partbuf_3_3
    memref.store %17, __interp_partlen_3_3
    memref.store %20, __interp_partbuf_3_4
    memref.store %21, __interp_partlen_3_4
    %40 = memref.load __interp_buf_3 : i64
    %41 = memref.load __interp_offset_3 : i64
    %42 = arith.addi %40, %41
    %43 = memref.load __interp_partbuf_3_0 : i64
    %44 = memref.load __interp_partlen_3_0 : i64
    std.memcopy %43, %42, %44
    %45 = memref.load __interp_offset_3 : i64
    %46 = memref.load __interp_partlen_3_0 : i64
    %47 = arith.addi %45, %46
    memref.store %47, __interp_offset_3
    %48 = memref.load __interp_buf_3 : i64
    %49 = memref.load __interp_offset_3 : i64
    %50 = arith.addi %48, %49
    %51 = memref.load __interp_partbuf_3_1 : i64
    %52 = memref.load __interp_partlen_3_1 : i64
    std.memcopy %51, %50, %52
    %53 = memref.load __interp_offset_3 : i64
    %54 = memref.load __interp_partlen_3_1 : i64
    %55 = arith.addi %53, %54
    memref.store %55, __interp_offset_3
    %56 = memref.load __interp_buf_3 : i64
    %57 = memref.load __interp_offset_3 : i64
    %58 = arith.addi %56, %57
    %59 = memref.load __interp_partbuf_3_2 : i64
    %60 = memref.load __interp_partlen_3_2 : i64
    std.memcopy %59, %58, %60
    %61 = memref.load __interp_offset_3 : i64
    %62 = memref.load __interp_partlen_3_2 : i64
    %63 = arith.addi %61, %62
    memref.store %63, __interp_offset_3
    %64 = memref.load __interp_buf_3 : i64
    %65 = memref.load __interp_offset_3 : i64
    %66 = arith.addi %64, %65
    %67 = memref.load __interp_partbuf_3_3 : i64
    %68 = memref.load __interp_partlen_3_3 : i64
    std.memcopy %67, %66, %68
    %69 = memref.load __interp_offset_3 : i64
    %70 = memref.load __interp_partlen_3_3 : i64
    %71 = arith.addi %69, %70
    memref.store %71, __interp_offset_3
    %72 = memref.load __interp_buf_3 : i64
    %73 = memref.load __interp_offset_3 : i64
    %74 = arith.addi %72, %73
    %75 = memref.load __interp_partbuf_3_4 : i64
    %76 = memref.load __interp_partlen_3_4 : i64
    std.memcopy %75, %74, %76
    %80 = memref.load __interp_buf_3 : i64
    %81 = memref.load __interp_totallen_3 : i64
    %82 = arith.addi %80, %81
    %83 = arith.constant {value = 0 : i64}
    memref.store_indirect %83, %82+0
    %84 = memref.load __tostr_buf_9 : i64
    std.call_runtime @mm_raw_free %84
    %85 = memref.load __tostr_buf_16 : i64
    std.call_runtime @mm_raw_free %85
    %86 = memref.load __interp_buf_3 : i64
    %87 = memref.load __interp_managed_3 : i64
    memref.store_indirect %86, %87+0
    %88 = memref.load __interp_totallen_3 : i64
    %89 = memref.load __interp_managed_3 : i64
    memref.store_indirect %88, %89+8
    %90 = memref.load __interp_managed_3 : i64
    memref.store_indirect %88, %90+16
    %91 = arith.constant {value = 1 : i64}
    %92 = memref.load __interp_managed_3 : i64
    memref.store_indirect %91, %92+24
    %93 = memref.load __interp_managed_3 : i64
    %94 = memref.load __lit_tmp_3 : i64
    memref.store_indirect %93, %94+0
    %95 = memref.load __interp_managed_3 : i64
    std.call_runtime @mm_incref %95
    %96 = arith.constant {value = 0 : i64}
    %97 = memref.load __lit_tmp_3 : i64
    memref.store_indirect %96, %97+8
    %98 = memref.load __lit_tmp_3 : i64
    std.call_runtime @mm_incref %98
    %99 = memref.load __lit_tmp_3 : i64
    func.return %99
  }
  func @string-interpolation.main() -> u32 {
  entry:
    %103 = func.call @Pair.toString %21
    memref.store %103, __interp_tostr_22
    %105 = memref.load __interp_tostr_22 : i64
    %106 = memref.load_indirect %105+0
    memref.store %106, __interp_managed_ptr_107
    %108 = memref.load __interp_managed_ptr_107 : i64
    %109 = memref.load_indirect %108+0
    %110 = memref.load __interp_managed_ptr_107 : i64
    %111 = memref.load_indirect %110+8
    %112 = memref.lea_rdata __interp_lit_3
    %113 = std.ptr_to_i64 %112
    %114 = arith.constant {value = 1 : i64}
    %115 = arith.addi %111, %114
    %116 = arith.constant {value = 16 : i64}
    %117 = func.ref @__destruct_String
    %118 = std.ptr_to_i64 %117
    %119 = arith.constant {value = 1 : i64}
    %120 = std.call_runtime @mm_alloc %116, %118, %119
    memref.store %120, __lit_tmp_23
    %121 = arith.constant {value = 32 : i64}
    %122 = func.ref @__destruct___ManagedMemory
    %123 = std.ptr_to_i64 %122
    %124 = arith.constant {value = 2 : i64}
    %125 = std.call_runtime @mm_alloc %121, %123, %124
    memref.store %125, __interp_managed_23
    %126 = arith.constant {value = 1 : i64}
    %127 = arith.addi %115, %126
    %128 = std.call_runtime @mm_raw_alloc %127
    %129 = arith.constant {value = 0 : i64}
    memref.store %129, __interp_offset_23
    memref.store %128, __interp_buf_23
    memref.store %115, __interp_totallen_23
    memref.store %109, __interp_partbuf_23_0
    memref.store %111, __interp_partlen_23_0
    memref.store %113, __interp_partbuf_23_1
    memref.store %114, __interp_partlen_23_1
    %130 = memref.load __interp_buf_23 : i64
    %131 = memref.load __interp_offset_23 : i64
    %132 = arith.addi %130, %131
    %133 = memref.load __interp_partbuf_23_0 : i64
    %134 = memref.load __interp_partlen_23_0 : i64
    std.memcopy %133, %132, %134
    %135 = memref.load __interp_offset_23 : i64
    %136 = memref.load __interp_partlen_23_0 : i64
    %137 = arith.addi %135, %136
    memref.store %137, __interp_offset_23
    %138 = memref.load __interp_buf_23 : i64
    %139 = memref.load __interp_offset_23 : i64
    %140 = arith.addi %138, %139
    %141 = memref.load __interp_partbuf_23_1 : i64
    %142 = memref.load __interp_partlen_23_1 : i64
    std.memcopy %141, %140, %142
    %146 = memref.load __interp_buf_23 : i64
    %147 = memref.load __interp_totallen_23 : i64
    %148 = arith.addi %146, %147
    %149 = arith.constant {value = 0 : i64}
    memref.store_indirect %149, %148+0
    %150 = memref.load __interp_buf_23 : i64
    %151 = memref.load __interp_managed_23 : i64
    memref.store_indirect %150, %151+0
    %152 = memref.load __interp_totallen_23 : i64
    %153 = memref.load __interp_managed_23 : i64
    memref.store_indirect %152, %153+8
    %154 = memref.load __interp_managed_23 : i64
    memref.store_indirect %152, %154+16
    %155 = arith.constant {value = 1 : i64}
    %156 = memref.load __interp_managed_23 : i64
    memref.store_indirect %155, %156+24
    %157 = memref.load __interp_managed_23 : i64
    %158 = memref.load __lit_tmp_23 : i64
    memref.store_indirect %157, %158+0
    %159 = memref.load __interp_managed_23 : i64
    std.call_runtime @mm_incref %159
    %160 = arith.constant {value = 0 : i64}
    %161 = memref.load __lit_tmp_23 : i64
    memref.store_indirect %160, %161+8
    %162 = memref.load __lit_tmp_23 : i64
    std.call_runtime @mm_incref %162
    %163 = memref.load __lit_tmp_23 : i64
    func.call @stdlib.Print.print %163
    %164 = arith.constant {value = 0 : i64}
    %165 = memref.load __lit_tmp_23 : i64
    std.call_runtime_if_nonnull @mm_decref %165
    %167 = memref.load __interp_tostr_22 : i64
    std.call_runtime_if_nonnull @mm_decref %167
    func.return %164
  }
  func @__destruct_String(ptr: i64) {
  entry:
    %171 = func.param ptr : StdI64
    memref.store %171, __destr_ptr
    %172 = memref.load __destr_ptr : i64
    %173 = memref.load_indirect %172+0
    std.call_runtime_if_nonnull @mm_decref %173
    cf.br done
  done:
    func.return
  }
  func @__destruct___ManagedMemory(ptr: i64) {
  entry:
    %174 = func.param ptr : StdI64
    memref.store %174, __destr_ptr
    %177 = memref.load __destr_ptr : i64
    %178 = memref.load_indirect %177+16
    %179 = arith.constant {value = 0 : i64}
    %180 = arith.cmpi ne %178, %179
    cf.cond_br %180 [then: free_buf_0, else: skip_buf_0]
  free_buf_0:
    %181 = memref.load __destr_ptr : i64
    %182 = memref.load_indirect %181+0
    std.call_runtime @mm_raw_free %182
    cf.br skip_buf_0
  skip_buf_0:
    cf.br done
  done:
    func.return
  }
}
