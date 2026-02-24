module {
  func @HashBucket.lookup(__self_ptr: i64, target: i64) -> i1 {
  entry:
    %0 = arith.constant {value = 0 : i64}
    %1 = std.call_runtime @mm_scope_enter %0
    memref.store %1, __scope_0
    %2 = func.param self : StdI64
    memref.store %2, self
    %7 = func.param target : StdI64
    memref.store %7, target
    memref.store %2, __for_iter_1
    cf.br loop_0.header
  loop_0.header:
    %9 = memref.load __for_iter_1 : i64
    %10, %11 = func.try_call @HashBucket.next %9
    memref.store %10, __try_result_2
    %13 = arith.constant {value = 0 : i64}
    %14 = arith.cmpi eq %11, %13
    cf.cond_br %14 [then: loop_0, else: loop_0.exit]
  loop_0:
    %15 = arith.constant {value = 0 : i64}
    %16 = std.call_runtime @mm_scope_enter %15
    memref.store %16, __scope_11
    %17 = memref.load __try_result_2 : i64
    memref.store %17, item
    %18 = memref.load item : i64
    %19 = memref.load target : i64
    %20 = func.call @HashItem.equals %18, %19
    cf.cond_br %20 [then: found_3, else: found_3.after]
  found_3:
    %22 = arith.constant {value = 1 : i1}
    %23 = memref.load __scope_11 : i64
    std.call_runtime @mm_scope_exit %23
    %24 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %24
    func.return %22
  found_3.after:
    %25 = memref.load __scope_11 : i64
    std.call_runtime @mm_scope_exit %25
    cf.br loop_0.header
  loop_0.exit:
    %26 = arith.constant {value = 0 : i1}
    %27 = memref.load __scope_0 : i64
    std.call_runtime @mm_scope_exit %27
    func.return %26
  }
  func @HashItem.equals(__self_ptr: i64, other: i64) -> i1 {
  entry:
    %29 = func.param self : StdI64
    memref.store %29, self
    %30 = memref.load self : i64
    %31 = memref.load_indirect %30+0
    %32 = func.param other : StdI64
    memref.store %32, other
    %33 = memref.load other : i64
    %34 = memref.load_indirect %33+0
    %35 = arith.cmpi eq %31, %34
    func.return %35
  }
  func @HashBucket.next(__self_ptr: i64) -> i64 {
  entry:
    %36 = arith.constant {value = 0 : i64}
    memref.store %36, __scope_39
    %37 = func.param self : StdI64
    memref.store %37, self
    %40 = memref.load self : i64
    %41 = memref.load_indirect %40+8
    %42 = memref.load self : i64
    %43 = memref.load_indirect %42+0
    memref.store %43, __selfref_43
    %44 = memref.load __selfref_43 : i64
    %45 = func.call @HashItemArray.count %44
    %46 = arith.cmpi ge %41, %45
    cf.cond_br %46 [then: done_0, else: done_0.after]
  done_0:
    %48 = arith.constant {value = 0 : i64}
    %49 = arith.constant {value = 1 : i64}
    %50 = arith.addi %48, %49
    func.error_return %50
  done_0.after:
    %51 = memref.load self : i64
    %52 = memref.load_indirect %51+0
    memref.store %52, __selfref_48
    %53 = memref.load self : i64
    %54 = memref.load_indirect %53+8
    %55 = memref.load __selfref_48 : i64
    %56, %57 = func.try_call @HashItemArray.get %55, %54
    memref.store %56, __try_result_4
    %59 = arith.constant {value = 0 : i64}
    %60 = arith.cmpi ne %57, %59
    cf.cond_br %60 [then: otherwise_error_1, else: otherwise_continue_2]
  otherwise_error_1:
    %61 = arith.constant {value = 0 : i64}
    %62 = arith.constant {value = 1 : i64}
    %63 = arith.addi %61, %62
    func.error_return %63
  otherwise_continue_2:
    %64 = memref.load __try_result_4 : i64
    memref.store %64, v
    %65 = arith.constant {value = 1 : i64}
    %66 = memref.load self : i64
    %67 = memref.load_indirect %66+8
    %68 = arith.addi %67, %65
    %69 = memref.load self : i64
    memref.store_indirect %68, %69+8
    %70 = memref.load v : i64
    %71 = memref.load __scope_39 : i64
    %72 = memref.load_indirect %71+8
    %73 = arith.constant {value = 0 : i64}
    std.call_runtime @mm_move %70, %72, %73
    %74 = memref.load v : i64
    func.return %74
  }
  func @conditional-extensions.multiple-constraints.main() -> u32 {
  entry:
    %75 = arith.constant {value = 0 : i64}
    %76 = std.call_runtime @mm_scope_enter %75
    memref.store %76, __scope_75
    %77 = arith.constant {value = 1 : i64}
    %78 = arith.constant {value = 8 : i64}
    %79 = arith.constant {value = 0 : i64}
    %80 = std.call_runtime @mm_alloc %78, %79
    memref.store %80, __struct_77
    %81 = memref.load __struct_77 : i64
    memref.store_indirect %77, %81+0
    %82 = arith.constant {value = 2 : i64}
    %83 = arith.constant {value = 8 : i64}
    %84 = arith.constant {value = 0 : i64}
    %85 = std.call_runtime @mm_alloc %83, %84
    memref.store %85, __struct_79
    %86 = memref.load __struct_79 : i64
    memref.store_indirect %82, %86+0
    %87 = arith.constant {value = 3 : i64}
    %88 = arith.constant {value = 8 : i64}
    %89 = arith.constant {value = 0 : i64}
    %90 = std.call_runtime @mm_alloc %88, %89
    memref.store %90, __arr_0.2
    %91 = memref.load __arr_0.2 : i64
    memref.store_indirect %87, %91+0
    memref.store %85, __arr_0.1
    memref.store %80, __arr_0.0
    %94 = arith.constant {value = 0 : i64}
    %95 = arith.constant {value = 3 : i64}
    %96 = arith.constant {value = 0 : i64}
    %97 = arith.constant {value = 8 : i64}
    %98 = arith.constant {value = 32 : i64}
    %99 = arith.constant {value = 0 : i64}
    %100 = std.call_runtime @mm_alloc %98, %99
    memref.store %100, __struct_86
    %101 = memref.load __struct_86 : i64
    memref.store_indirect %94, %101+0
    %102 = memref.load __struct_86 : i64
    memref.store_indirect %95, %102+8
    %103 = memref.load __struct_86 : i64
    memref.store_indirect %96, %103+16
    %104 = memref.load __struct_86 : i64
    memref.store_indirect %97, %104+24
    %105 = arith.constant {value = 0 : i64}
    %106 = arith.constant {value = 16 : i64}
    %107 = arith.constant {value = 0 : i64}
    %108 = std.call_runtime @mm_alloc %106, %107
    memref.store %108, __struct_88
    %109 = memref.load __struct_88 : i64
    memref.store_indirect %105, %109+0
    %110 = memref.load __struct_86 : i64
    %111 = memref.load __struct_88 : i64
    memref.store_indirect %110, %111+8
    %112 = memref.load __struct_88 : i64
    %113 = arith.constant {value = 1 : i64}
    std.call_runtime @mm_move %110, %112, %113
    %114 = memref.lea __arr_0
    %115 = std.ptr_to_i64 %114
    %116 = memref.load __struct_88 : i64
    %117 = memref.load_indirect %116+8
    memref.store_indirect %115, %117+0
    %118 = arith.constant {value = 0 : i64}
    %119 = arith.constant {value = 16 : i64}
    %120 = arith.constant {value = 0 : i64}
    %121 = std.call_runtime @mm_alloc %119, %120
    memref.store %121, b
    %122 = memref.load __struct_88 : i64
    %123 = memref.load b : i64
    memref.store_indirect %122, %123+0
    %124 = memref.load b : i64
    %125 = arith.constant {value = 1 : i64}
    std.call_runtime @mm_move %122, %124, %125
    %126 = memref.load b : i64
    memref.store_indirect %118, %126+8
    %127 = arith.constant {value = 2 : i64}
    %128 = arith.constant {value = 8 : i64}
    %129 = arith.constant {value = 0 : i64}
    %130 = std.call_runtime @mm_alloc %128, %129
    memref.store %130, __struct_93
    %131 = memref.load __struct_93 : i64
    memref.store_indirect %127, %131+0
    %132 = memref.load b : i64
    %133 = memref.load __struct_93 : i64
    %134 = func.call @HashBucket.lookup %132, %133
    cf.cond_br %134 [then: found_1, else: found_1.after]
  found_1:
    %136 = arith.constant {value = 1 : i64}
    %137 = memref.load __scope_75 : i64
    std.call_runtime @mm_scope_exit %137
    func.return %136
  found_1.after:
    %138 = arith.constant {value = 0 : i64}
    %139 = memref.load __scope_75 : i64
    std.call_runtime @mm_scope_exit %139
    func.return %138
  }
  func @HashItemArray.count(__self_ptr: i64) -> u64 {
  entry:
    %140 = arith.constant {value = 0 : i64}
    %141 = std.call_runtime @mm_scope_enter %140
    memref.store %141, __scope_1553
    %142 = func.param self : StdI64
    memref.store %142, self
    %147 = memref.load self : i64
    %148 = memref.load_indirect %147+8
    memref.store %148, __selfref_1505
    %149 = memref.load __selfref_1505 : i64
    %150 = memref.load_indirect %149+8
    memref.store %150, __range_val_0
    %151 = arith.constant {value = 0 : i64}
    %152 = arith.cmpi lt %150, %151
    cf.cond_br %152 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    %153 = memref.lea_symdata __panic_msg_1509
    %154 = std.ptr_to_i64 %153
    std.call_runtime @maxon_panic %154
  __range_ok_0:
    %155 = memref.load __range_val_0 : i64
    %156 = memref.load __scope_1553 : i64
    std.call_runtime @mm_scope_exit %156
    func.return %155
  }
  func @HashItemArray.get(__self_ptr: i64, index: i64) -> i64 {
  entry:
    %157 = arith.constant {value = 0 : i64}
    %158 = std.call_runtime @mm_scope_enter %157
    memref.store %158, __scope_1609
    %159 = func.param self : StdI64
    memref.store %159, self
    %164 = func.param index : StdI64
    memref.store %164, index
    %165 = memref.load self : i64
    %166 = memref.load_indirect %165+8
    memref.store %166, __selfref_1555
    %167 = memref.load __selfref_1555 : i64
    %168 = memref.load_indirect %167+8
    %169 = arith.cmpui uge %164, %168
    cf.cond_br %169 [then: upper_0, else: upper_0.after]
  upper_0:
    %170 = arith.constant {value = 0 : i64}
    %171 = std.call_runtime @mm_scope_enter %170
    memref.store %171, __scope_1617
    %172 = arith.constant {value = 0 : i64}
    %173 = memref.load __scope_1617 : i64
    std.call_runtime @mm_scope_exit %173
    %174 = memref.load __scope_1609 : i64
    std.call_runtime @mm_scope_exit %174
    %175 = arith.constant {value = 1 : i64}
    %176 = arith.addi %172, %175
    func.error_return %176
  upper_0.after:
    %177 = memref.load self : i64
    %178 = memref.load_indirect %177+8
    memref.store %178, __selfref_1559
    %179 = memref.load index : i64
    %180 = memref.load __selfref_1559 : i64
    %181 = memref.load_indirect %180+24
    %182 = memref.load __selfref_1559 : i64
    %183 = memref.load_indirect %182+0
    %184 = arith.muli %179, %181
    %185 = arith.addi %183, %184
    %186 = memref.load_indirect %185+0
    memref.store %186, __memget_187
    %188 = memref.load __scope_1609 : i64
    std.call_runtime @mm_scope_exit %188
    %189 = memref.load __memget_187 : i64
    func.return %189
  }
}
