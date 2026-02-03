module {
  func @Array.count(self: Array) -> i64 {
  entry:
    %3 = maxon.struct_param @Array
    %4 = maxon.field_access .iterIndex %3
    %5 = maxon.field_access .managed %3
    %6 = maxon.field_access .length %5
    maxon.return %6
  }
  func @Array.get(self: Array, index: i64) -> i64 {
  entry:
    %39 = maxon.struct_param @Array
    %40 = maxon.field_access .iterIndex %39
    %41 = maxon.field_access .managed %39
    %42 = maxon.param {index = 1 : i32} {name = index} {type = i64}
    %43 = maxon.field_access .length %41
    maxon.assign %43 {var = len} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %44 = maxon.literal {value = 0 : i64}
    %45 = maxon.binop %42, %44 {op = lt} {kind = i64}
    maxon.cond_br %45 [then: lower_0, else: lower_0.after]
  lower_0:
    %46 = maxon.enum_literal @ArrayError.indexOutOfBounds
    maxon.throw @ArrayError %46
  lower_0.after:
    %47 = maxon.var_ref {var = index} {type = i64}
    %48 = maxon.var_ref {var = len} {type = i64}
    %49 = maxon.binop %47, %48 {op = ge} {kind = i64}
    maxon.cond_br %49 [then: upper_1, else: upper_1.after]
  upper_1:
    %50 = maxon.enum_literal @ArrayError.indexOutOfBounds
    maxon.throw @ArrayError %50
  upper_1.after:
    %51 = maxon.struct_var_ref managed
    %52 = maxon.var_ref {var = index} {type = i64}
    %53 = maxon.managed_mem_get %51, %52
    maxon.return %53
  }
  func @Array.set(self: Array, index: i64, value: i64) {
  entry:
    %54 = maxon.struct_param @Array
    %55 = maxon.field_access .iterIndex %54
    %56 = maxon.field_access .managed %54
    %57 = maxon.param {index = 1 : i32} {name = index} {type = i64}
    %58 = maxon.param {index = 2 : i32} {name = value} {type = i64}
    %59 = maxon.field_access .length %56
    maxon.assign %59 {var = len} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %60 = maxon.literal {value = 0 : i64}
    %61 = maxon.binop %57, %60 {op = ge} {kind = i64}
    maxon.cond_br %61 [then: lower_0, else: lower_0.after]
  lower_0:
    %62 = maxon.var_ref {var = index} {type = i64}
    %63 = maxon.var_ref {var = len} {type = i64}
    %64 = maxon.binop %62, %63 {op = lt} {kind = i64}
    maxon.cond_br %64 [then: upper_1, else: upper_1.merge]
  upper_1:
    %65 = maxon.struct_var_ref managed
    %66 = maxon.var_ref {var = index} {type = i64}
    %67 = maxon.var_ref {var = value} {type = i64}
    maxon.managed_mem_set %65, %66, %67
    maxon.br upper_1.merge
  upper_1.merge:
  lower_0.after:
    maxon.return
  }
  func @Array.reserve(self: Array, minCapacity: i64) {
  entry:
    %202 = maxon.struct_param @Array
    %203 = maxon.field_access .iterIndex %202
    %204 = maxon.field_access .managed %202
    %205 = maxon.param {index = 1 : i32} {name = minCapacity} {type = i64}
    %206 = maxon.field_access .capacity %204
    maxon.assign %206 {var = cap} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %207 = maxon.binop %205, %206 {op = gt} {kind = i64}
    maxon.cond_br %207 [then: grow_0, else: grow_0.merge]
  grow_0:
    %208 = maxon.struct_var_ref managed
    %209 = maxon.var_ref {var = minCapacity} {type = i64}
    maxon.managed_mem_grow %208, %209
    maxon.br grow_0.merge
  grow_0.merge:
    maxon.return
  }
  func @Array.resize(self: Array, newLength: i64) {
  entry:
    %210 = maxon.struct_param @Array
    %211 = maxon.field_access .iterIndex %210
    %212 = maxon.field_access .managed %210
    %213 = maxon.param {index = 1 : i32} {name = newLength} {type = i64}
    maxon.call @Array.reserve %210, %213
    maxon.field_assign .length %212, %213
    maxon.return
  }
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 1 : i64}
    %1 = maxon.literal {value = 2 : i64}
    %2 = maxon.literal {value = 3 : i64}
    maxon.assign %2 {var = __arr_0.2} {kind = i64} {decl = 1 : i1}
    maxon.assign %1 {var = __arr_0.1} {kind = i64} {decl = 1 : i1}
    maxon.assign %0 {var = __arr_0.0} {kind = i64} {decl = 1 : i1}
    %3 = maxon.literal {value = 0 : i64}
    %4 = maxon.literal {value = 3 : i64}
    %5 = maxon.literal {value = 0 : i64}
    %6 = maxon.struct_literal @__ManagedMemory
    %7 = maxon.literal {value = 0 : i64}
    %8 = maxon.struct_literal @Array
    %9 = maxon.call @__Set_3_i64.init %8
    maxon.assign %9 {var = s} {decl = 1 : i1} {mut = 1 : i1}
    %10 = maxon.call @__Set_3_i64.count %9
    maxon.return %10
  }
  func @IntArray.get(self: IntArray, index: i64) -> i64 {
  entry:
    %60 = maxon.struct_param @IntArray
    %61 = maxon.field_access .iterIndex %60
    %62 = maxon.field_access .managed %60
    %63 = maxon.param {index = 1 : i32} {name = index} {type = i64}
    %64 = maxon.field_access .length %62
    maxon.assign %64 {var = len} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %65 = maxon.literal {value = 0 : i64}
    %66 = maxon.binop %63, %65 {op = lt} {kind = i64}
    maxon.cond_br %66 [then: lower_0, else: lower_0.after]
  lower_0:
    %67 = maxon.enum_literal @ArrayError.indexOutOfBounds
    maxon.throw @ArrayError %67
  lower_0.after:
    %68 = maxon.var_ref {var = index} {type = i64}
    %69 = maxon.var_ref {var = len} {type = i64}
    %70 = maxon.binop %68, %69 {op = ge} {kind = i64}
    maxon.cond_br %70 [then: upper_1, else: upper_1.after]
  upper_1:
    %71 = maxon.enum_literal @ArrayError.indexOutOfBounds
    maxon.throw @ArrayError %71
  upper_1.after:
    %72 = maxon.struct_var_ref managed
    %73 = maxon.var_ref {var = index} {type = i64}
    %74 = maxon.managed_mem_get %72, %73
    maxon.return %74
  }
  func @IntArray.set(self: IntArray, index: i64, value: i64) {
  entry:
    %75 = maxon.struct_param @IntArray
    %76 = maxon.field_access .iterIndex %75
    %77 = maxon.field_access .managed %75
    %78 = maxon.param {index = 1 : i32} {name = index} {type = i64}
    %79 = maxon.param {index = 2 : i32} {name = value} {type = i64}
    %80 = maxon.field_access .length %77
    maxon.assign %80 {var = len} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %81 = maxon.literal {value = 0 : i64}
    %82 = maxon.binop %78, %81 {op = ge} {kind = i64}
    maxon.cond_br %82 [then: lower_0, else: lower_0.after]
  lower_0:
    %83 = maxon.var_ref {var = index} {type = i64}
    %84 = maxon.var_ref {var = len} {type = i64}
    %85 = maxon.binop %83, %84 {op = lt} {kind = i64}
    maxon.cond_br %85 [then: upper_1, else: upper_1.merge]
  upper_1:
    %86 = maxon.struct_var_ref managed
    %87 = maxon.var_ref {var = index} {type = i64}
    %88 = maxon.var_ref {var = value} {type = i64}
    maxon.managed_mem_set %86, %87, %88
    maxon.br upper_1.merge
  upper_1.merge:
  lower_0.after:
    maxon.return
  }
  func @IntArray.reserve(self: IntArray, minCapacity: i64) {
  entry:
    %89 = maxon.struct_param @IntArray
    %90 = maxon.field_access .iterIndex %89
    %91 = maxon.field_access .managed %89
    %92 = maxon.param {index = 1 : i32} {name = minCapacity} {type = i64}
    %93 = maxon.field_access .capacity %91
    maxon.assign %93 {var = cap} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %94 = maxon.binop %92, %93 {op = gt} {kind = i64}
    maxon.cond_br %94 [then: grow_0, else: grow_0.merge]
  grow_0:
    %95 = maxon.struct_var_ref managed
    %96 = maxon.var_ref {var = minCapacity} {type = i64}
    maxon.managed_mem_grow %95, %96
    maxon.br grow_0.merge
  grow_0.merge:
    maxon.return
  }
  func @IntArray.resize(self: IntArray, newLength: i64) {
  entry:
    %97 = maxon.struct_param @IntArray
    %98 = maxon.field_access .iterIndex %97
    %99 = maxon.field_access .managed %97
    %100 = maxon.param {index = 1 : i32} {name = newLength} {type = i64}
    maxon.call @IntArray.reserve %97, %100
    maxon.field_assign .length %99, %100
    maxon.return
  }
  func @__Set_3_i64.init(arr: Array) -> __Set_3_i64 {
  entry:
    %101 = maxon.struct_param @Array
    %102 = maxon.call @Array.count %101
    maxon.assign %102 {var = numElements} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %103 = maxon.literal {value = 16 : i64}
    maxon.assign %103 {var = cap} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br calc_cap_0.header
  calc_cap_0.header:
    %104 = maxon.literal {value = 2 : i64}
    %105 = maxon.var_ref {var = numElements} {type = i64}
    %106 = maxon.binop %105, %104 {op = mul} {kind = i64}
    %107 = maxon.var_ref {var = cap} {type = i64}
    %108 = maxon.binop %107, %106 {op = lt} {kind = i64}
    maxon.cond_br %108 [then: calc_cap_0, else: calc_cap_0.exit]
  calc_cap_0:
    %109 = maxon.literal {value = 2 : i64}
    %110 = maxon.var_ref {var = cap} {type = i64}
    %111 = maxon.binop %110, %109 {op = mul} {kind = i64}
    maxon.assign %111 {var = cap} {kind = i64} {mut = 1 : i1}
    maxon.br calc_cap_0.header
  calc_cap_0.exit:
    %112 = maxon.literal {value = 0 : i64}
    %113 = maxon.literal {value = 0 : i64}
    %114 = maxon.literal {value = 0 : i64}
    %115 = maxon.literal {value = 0 : i64}
    %116 = maxon.struct_literal @__ManagedMemory
    %117 = maxon.struct_literal @Array
    maxon.assign %117 {var = elems} {decl = 1 : i1} {mut = 1 : i1}
    %118 = maxon.var_ref {var = cap} {type = i64}
    maxon.call @Array.resize %117, %118
    %119 = maxon.literal {value = 0 : i64}
    %120 = maxon.literal {value = 0 : i64}
    %121 = maxon.literal {value = 0 : i64}
    %122 = maxon.literal {value = 0 : i64}
    %123 = maxon.struct_literal @__ManagedMemory
    %124 = maxon.struct_literal @IntArray
    maxon.assign %124 {var = sts} {decl = 1 : i1} {mut = 1 : i1}
    %125 = maxon.var_ref {var = cap} {type = i64}
    maxon.call @IntArray.resize %124, %125
    %126 = maxon.literal {value = 0 : i64}
    %127 = maxon.var_ref {var = cap} {type = i64}
    %128 = maxon.literal {value = 0 : i64}
    %129 = maxon.struct_literal @__Set_3_i64
    maxon.assign %129 {var = result} {decl = 1 : i1} {mut = 1 : i1}
    %130 = maxon.struct_var_ref arr
    %131 = maxon.literal {value = 0 : i64}
    maxon.assign %131 {var = __for_idx_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %132 = maxon.field_access .managed %130
    %133 = maxon.field_access .length %132
    maxon.assign %133 {var = __for_len_2} {kind = i64} {decl = 1 : i1}
    maxon.br insert_loop_1.header
  insert_loop_1.header:
    %134 = maxon.var_ref {var = __for_idx_2} {type = i64}
    %135 = maxon.var_ref {var = __for_len_2} {type = i64}
    %136 = maxon.binop %134, %135 {op = lt} {kind = i64}
    maxon.cond_br %136 [then: insert_loop_1, else: insert_loop_1.exit]
  insert_loop_1:
    %137 = maxon.var_ref {var = __for_idx_2} {type = i64}
    %138 = maxon.field_access .managed %130
    %139 = maxon.managed_mem_get %138, %137
    maxon.assign %139 {var = elem} {kind = i64} {decl = 1 : i1}
    %140 = maxon.var_ref {var = __for_idx_2} {type = i64}
    %141 = maxon.literal {value = 1 : i64}
    %142 = maxon.binop %140, %141 {op = add} {kind = i64}
    maxon.assign %142 {var = __for_idx_2} {kind = i64} {mut = 1 : i1}
    maxon.call @__Set_3_i64.insert %129, %139
    maxon.br insert_loop_1.header
  insert_loop_1.exit:
    %143 = maxon.struct_var_ref result
    maxon.return %143
  }
  func @__Set_3_i64.insert(self: __Set_3_i64, element: i64) {
  entry:
    %144 = maxon.struct_param @__Set_3_i64
    %145 = maxon.field_access .elements %144
    %146 = maxon.field_access .states %144
    %147 = maxon.field_access .count %144
    %148 = maxon.field_access .capacity %144
    %149 = maxon.field_access .iterIndex %144
    %150 = maxon.param {index = 1 : i32} {name = element} {type = i64}
    %151 = maxon.literal {value = 0 : i64}
    %152 = maxon.binop %148, %151 {op = eq} {kind = i64}
    maxon.cond_br %152 [then: init_empty_0, else: init_empty_0.merge]
  init_empty_0:
    %153 = maxon.literal {value = 0 : i64}
    %154 = maxon.literal {value = 0 : i64}
    %155 = maxon.literal {value = 0 : i64}
    %156 = maxon.literal {value = 0 : i64}
    %157 = maxon.struct_literal @__ManagedMemory
    %158 = maxon.struct_literal @Array
    maxon.assign %158 {var = newElements} {decl = 1 : i1} {mut = 1 : i1}
    %159 = maxon.literal {value = 16 : i64}
    maxon.call @Array.resize %158, %159
    %160 = maxon.literal {value = 0 : i64}
    %161 = maxon.literal {value = 0 : i64}
    %162 = maxon.literal {value = 0 : i64}
    %163 = maxon.literal {value = 0 : i64}
    %164 = maxon.struct_literal @__ManagedMemory
    %165 = maxon.struct_literal @IntArray
    maxon.assign %165 {var = newStates} {decl = 1 : i1} {mut = 1 : i1}
    %166 = maxon.literal {value = 16 : i64}
    maxon.call @IntArray.resize %165, %166
    maxon.assign %158 {var = elements} {mut = 1 : i1}
    maxon.assign %165 {var = states} {mut = 1 : i1}
    %167 = maxon.literal {value = 16 : i64}
    maxon.assign %167 {var = capacity} {kind = i64} {mut = 1 : i1}
    maxon.br init_empty_0.merge
  init_empty_0.merge:
    %168 = maxon.literal {value = 3 : i64}
    %169 = maxon.var_ref {var = capacity} {type = i64}
    %170 = maxon.binop %169, %168 {op = mul} {kind = i64}
    %171 = maxon.literal {value = 4 : i64}
    %172 = maxon.int_to_float %170
    %173 = maxon.int_to_float %171
    %174 = maxon.binop %172, %173 {op = div} {kind = f64}
    %175 = maxon.trunc %174
    maxon.assign %175 {var = loadLimit} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %176 = maxon.var_ref {var = count} {type = i64}
    %177 = maxon.binop %176, %175 {op = ge} {kind = i64}
    maxon.cond_br %177 [then: grow_1, else: grow_1.merge]
  grow_1:
    %178 = maxon.struct_var_ref self
    maxon.call @__Set_3_i64.grow %178
    maxon.br grow_1.merge
  grow_1.merge:
    %179 = maxon.var_ref {var = element} {type = i64}
    %180 = maxon.call @i64.hash %179
    maxon.assign %180 {var = hash} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %181 = maxon.var_ref {var = capacity} {type = i64}
    %182 = maxon.binop %180, %181 {op = mod} {kind = i64}
    maxon.assign %182 {var = index} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %183 = maxon.literal {value = 0 : i64}
    %184 = maxon.binop %182, %183 {op = lt} {kind = i64}
    maxon.cond_br %184 [then: fix_negative_2, else: fix_negative_2.merge]
  fix_negative_2:
    %185 = maxon.var_ref {var = index} {type = i64}
    %186 = maxon.var_ref {var = capacity} {type = i64}
    %187 = maxon.binop %185, %186 {op = add} {kind = i64}
    maxon.assign %187 {var = index} {kind = i64} {mut = 1 : i1}
    maxon.br fix_negative_2.merge
  fix_negative_2.merge:
    %188 = maxon.literal {value = -1 : i64}
    maxon.assign %188 {var = firstDeleted} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %189 = maxon.literal {value = 0 : i64}
    maxon.assign %189 {var = probes} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br probe_3.header
  probe_3.header:
    %190 = maxon.var_ref {var = probes} {type = i64}
    %191 = maxon.var_ref {var = capacity} {type = i64}
    %192 = maxon.binop %190, %191 {op = lt} {kind = i64}
    maxon.cond_br %192 [then: probe_3, else: probe_3.exit]
  probe_3:
    %193 = maxon.struct_var_ref states
    %194 = maxon.var_ref {var = index} {type = i64}
    %196, %195 = maxon.try_call @IntArray.get %193, %194
    %197 = maxon.literal {value = 0 : i64}
    maxon.assign %197 {var = __try_default_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %196 {var = __try_result_4} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %198 = maxon.literal {value = 0 : i64}
    %199 = maxon.binop %195, %198 {op = ne} {kind = i64}
    maxon.cond_br %199 [then: otherwise_default_error_6, else: otherwise_default_continue_7]
  otherwise_default_error_6:
    %200 = maxon.var_ref {var = __try_default_5} {type = i64}
    maxon.assign %200 {var = __try_result_4} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_7
  otherwise_default_continue_7:
    %201 = maxon.var_ref {var = __try_result_4} {type = i64}
    maxon.assign %201 {var = state} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %202 = maxon.literal {value = 0 : i64}
    %203 = maxon.binop %201, %202 {op = eq} {kind = i64}
    maxon.cond_br %203 [then: empty_8, else: empty_8.after]
  empty_8:
    %204 = maxon.var_ref {var = index} {type = i64}
    maxon.assign %204 {var = insertIndex} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %205 = maxon.literal {value = 0 : i64}
    %206 = maxon.var_ref {var = firstDeleted} {type = i64}
    %207 = maxon.binop %206, %205 {op = ge} {kind = i64}
    maxon.cond_br %207 [then: use_deleted_9, else: use_deleted_9.merge]
  use_deleted_9:
    %208 = maxon.var_ref {var = firstDeleted} {type = i64}
    maxon.assign %208 {var = insertIndex} {kind = i64} {mut = 1 : i1}
    maxon.br use_deleted_9.merge
  use_deleted_9.merge:
    %209 = maxon.var_ref {var = insertIndex} {type = i64}
    %210 = maxon.var_ref {var = element} {type = i64}
    maxon.call @Array.set %158, %209, %210
    %211 = maxon.var_ref {var = insertIndex} {type = i64}
    %212 = maxon.literal {value = 1 : i64}
    maxon.call @IntArray.set %165, %211, %212
    %213 = maxon.literal {value = 1 : i64}
    %214 = maxon.var_ref {var = count} {type = i64}
    %215 = maxon.binop %214, %213 {op = add} {kind = i64}
    maxon.assign %215 {var = count} {kind = i64} {mut = 1 : i1}
    maxon.return
  empty_8.after:
    %216 = maxon.literal {value = 2 : i64}
    %217 = maxon.var_ref {var = state} {type = i64}
    %218 = maxon.binop %217, %216 {op = eq} {kind = i64}
    %219 = maxon.literal {value = 0 : i64}
    %220 = maxon.var_ref {var = firstDeleted} {type = i64}
    %221 = maxon.binop %220, %219 {op = lt} {kind = i64}
    %222 = maxon.binop %218, %221 {op = and} {kind = i1}
    maxon.cond_br %222 [then: mark_deleted_10, else: mark_deleted_10.merge]
  mark_deleted_10:
    %223 = maxon.var_ref {var = index} {type = i64}
    maxon.assign %223 {var = firstDeleted} {kind = i64} {mut = 1 : i1}
    maxon.br mark_deleted_10.merge
  mark_deleted_10.merge:
    %224 = maxon.literal {value = 1 : i64}
    %225 = maxon.var_ref {var = state} {type = i64}
    %226 = maxon.binop %225, %224 {op = eq} {kind = i64}
    maxon.cond_br %226 [then: check_exists_11, else: check_exists_11.after]
  check_exists_11:
    %227 = maxon.struct_var_ref elements
    %228 = maxon.var_ref {var = index} {type = i64}
    %230, %229 = maxon.try_call @Array.get %227, %228
    maxon.assign %229 {var = __try_error_12} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %230 {var = __try_result_13} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %231 = maxon.literal {value = 0 : i64}
    %232 = maxon.binop %229, %231 {op = eq} {kind = i64}
    maxon.cond_br %232 [then: get_existing_14, else: get_existing_14.after]
  get_existing_14:
    %233 = maxon.var_ref {var = __try_result_13} {type = i64}
    maxon.assign %233 {var = existing} {kind = i64} {decl = 1 : i1}
    %234 = maxon.var_ref {var = element} {type = i64}
    %235 = maxon.binop %233, %234 {op = eq} {kind = i64}
    maxon.cond_br %235 [then: exists_15, else: exists_15.after]
  exists_15:
    maxon.return
  exists_15.after:
  get_existing_14.after:
  check_exists_11.after:
    %236 = maxon.literal {value = 1 : i64}
    %237 = maxon.var_ref {var = index} {type = i64}
    %238 = maxon.binop %237, %236 {op = add} {kind = i64}
    %239 = maxon.var_ref {var = capacity} {type = i64}
    %240 = maxon.binop %238, %239 {op = mod} {kind = i64}
    maxon.assign %240 {var = index} {kind = i64} {mut = 1 : i1}
    %241 = maxon.literal {value = 1 : i64}
    %242 = maxon.var_ref {var = probes} {type = i64}
    %243 = maxon.binop %242, %241 {op = add} {kind = i64}
    maxon.assign %243 {var = probes} {kind = i64} {mut = 1 : i1}
    maxon.br probe_3.header
  probe_3.exit:
    %244 = maxon.literal {value = 0 : i64}
    %245 = maxon.var_ref {var = firstDeleted} {type = i64}
    %246 = maxon.binop %245, %244 {op = ge} {kind = i64}
    maxon.cond_br %246 [then: fallback_16, else: fallback_16.merge]
  fallback_16:
    %247 = maxon.var_ref {var = firstDeleted} {type = i64}
    %248 = maxon.var_ref {var = element} {type = i64}
    maxon.call @Array.set %158, %247, %248
    %249 = maxon.var_ref {var = firstDeleted} {type = i64}
    %250 = maxon.literal {value = 1 : i64}
    maxon.call @IntArray.set %165, %249, %250
    %251 = maxon.literal {value = 1 : i64}
    %252 = maxon.var_ref {var = count} {type = i64}
    %253 = maxon.binop %252, %251 {op = add} {kind = i64}
    maxon.assign %253 {var = count} {kind = i64} {mut = 1 : i1}
    maxon.br fallback_16.merge
  fallback_16.merge:
    maxon.return
  }
  func @__Set_3_i64.count(self: __Set_3_i64) -> i64 {
  entry:
    %254 = maxon.struct_param @__Set_3_i64
    %255 = maxon.field_access .elements %254
    %256 = maxon.field_access .states %254
    %257 = maxon.field_access .count %254
    %258 = maxon.field_access .capacity %254
    %259 = maxon.field_access .iterIndex %254
    maxon.return %257
  }
  func @__Set_3_i64.grow(self: __Set_3_i64) {
  entry:
    %260 = maxon.struct_param @__Set_3_i64
    %261 = maxon.field_access .elements %260
    %262 = maxon.field_access .states %260
    %263 = maxon.field_access .count %260
    %264 = maxon.field_access .capacity %260
    %265 = maxon.field_access .iterIndex %260
    maxon.assign %264 {var = oldCapacity} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %266 = maxon.literal {value = 2 : i64}
    %267 = maxon.binop %264, %266 {op = mul} {kind = i64}
    maxon.assign %267 {var = newCapacity} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %268 = maxon.literal {value = 0 : i64}
    %269 = maxon.binop %267, %268 {op = eq} {kind = i64}
    maxon.cond_br %269 [then: handle_zero_0, else: handle_zero_0.merge]
  handle_zero_0:
    %270 = maxon.literal {value = 16 : i64}
    maxon.assign %270 {var = newCapacity} {kind = i64} {mut = 1 : i1}
    maxon.br handle_zero_0.merge
  handle_zero_0.merge:
    %271 = maxon.struct_var_ref elements
    maxon.assign %271 {var = oldElements} {decl = 1 : i1} {mut = 1 : i1}
    %272 = maxon.struct_var_ref states
    maxon.assign %272 {var = oldStates} {decl = 1 : i1} {mut = 1 : i1}
    %273 = maxon.literal {value = 0 : i64}
    %274 = maxon.literal {value = 0 : i64}
    %275 = maxon.literal {value = 0 : i64}
    %276 = maxon.literal {value = 0 : i64}
    %277 = maxon.struct_literal @__ManagedMemory
    %278 = maxon.struct_literal @Array
    maxon.assign %278 {var = newElements} {decl = 1 : i1} {mut = 1 : i1}
    %279 = maxon.var_ref {var = newCapacity} {type = i64}
    maxon.call @Array.resize %278, %279
    %280 = maxon.literal {value = 0 : i64}
    %281 = maxon.literal {value = 0 : i64}
    %282 = maxon.literal {value = 0 : i64}
    %283 = maxon.literal {value = 0 : i64}
    %284 = maxon.struct_literal @__ManagedMemory
    %285 = maxon.struct_literal @IntArray
    maxon.assign %285 {var = newStates} {decl = 1 : i1} {mut = 1 : i1}
    %286 = maxon.var_ref {var = newCapacity} {type = i64}
    maxon.call @IntArray.resize %285, %286
    maxon.assign %278 {var = elements} {mut = 1 : i1}
    maxon.assign %285 {var = states} {mut = 1 : i1}
    %287 = maxon.var_ref {var = newCapacity} {type = i64}
    maxon.assign %287 {var = capacity} {kind = i64} {mut = 1 : i1}
    %288 = maxon.literal {value = 0 : i64}
    maxon.assign %288 {var = count} {kind = i64} {mut = 1 : i1}
    %289 = maxon.literal {value = 0 : i64}
    maxon.assign %289 {var = i} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br rehash_1.header
  rehash_1.header:
    %290 = maxon.var_ref {var = i} {type = i64}
    %291 = maxon.var_ref {var = oldCapacity} {type = i64}
    %292 = maxon.binop %290, %291 {op = lt} {kind = i64}
    maxon.cond_br %292 [then: rehash_1, else: rehash_1.exit]
  rehash_1:
    %293 = maxon.struct_var_ref oldStates
    %294 = maxon.var_ref {var = i} {type = i64}
    %296, %295 = maxon.try_call @IntArray.get %293, %294
    %297 = maxon.literal {value = 0 : i64}
    maxon.assign %297 {var = __try_default_3} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %296 {var = __try_result_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %298 = maxon.literal {value = 0 : i64}
    %299 = maxon.binop %295, %298 {op = ne} {kind = i64}
    maxon.cond_br %299 [then: otherwise_default_error_4, else: otherwise_default_continue_5]
  otherwise_default_error_4:
    %300 = maxon.var_ref {var = __try_default_3} {type = i64}
    maxon.assign %300 {var = __try_result_2} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_5
  otherwise_default_continue_5:
    %301 = maxon.var_ref {var = __try_result_2} {type = i64}
    maxon.assign %301 {var = state} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %302 = maxon.literal {value = 1 : i64}
    %303 = maxon.binop %301, %302 {op = eq} {kind = i64}
    maxon.cond_br %303 [then: occupied_6, else: occupied_6.after]
  occupied_6:
    %304 = maxon.struct_var_ref oldElements
    %305 = maxon.var_ref {var = i} {type = i64}
    %307, %306 = maxon.try_call @Array.get %304, %305
    maxon.assign %306 {var = __try_error_7} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %307 {var = __try_result_8} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %308 = maxon.literal {value = 0 : i64}
    %309 = maxon.binop %306, %308 {op = eq} {kind = i64}
    maxon.cond_br %309 [then: get_elem_9, else: get_elem_9.after]
  get_elem_9:
    %310 = maxon.var_ref {var = __try_result_8} {type = i64}
    maxon.assign %310 {var = element} {kind = i64} {decl = 1 : i1}
    %311 = maxon.call @i64.hash %310
    maxon.assign %311 {var = hash} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %312 = maxon.var_ref {var = newCapacity} {type = i64}
    %313 = maxon.binop %311, %312 {op = mod} {kind = i64}
    maxon.assign %313 {var = index} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %314 = maxon.literal {value = 0 : i64}
    %315 = maxon.binop %313, %314 {op = lt} {kind = i64}
    maxon.cond_br %315 [then: fix_negative_10, else: fix_negative_10.merge]
  fix_negative_10:
    %316 = maxon.var_ref {var = index} {type = i64}
    %317 = maxon.var_ref {var = newCapacity} {type = i64}
    %318 = maxon.binop %316, %317 {op = add} {kind = i64}
    maxon.assign %318 {var = index} {kind = i64} {mut = 1 : i1}
    maxon.br fix_negative_10.merge
  fix_negative_10.merge:
    %319 = maxon.struct_var_ref states
    %320 = maxon.var_ref {var = index} {type = i64}
    %322, %321 = maxon.try_call @IntArray.get %319, %320
    %323 = maxon.literal {value = 0 : i64}
    maxon.assign %323 {var = __try_default_12} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %322 {var = __try_result_11} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %324 = maxon.literal {value = 0 : i64}
    %325 = maxon.binop %321, %324 {op = ne} {kind = i64}
    maxon.cond_br %325 [then: otherwise_default_error_13, else: otherwise_default_continue_14]
  otherwise_default_error_13:
    %326 = maxon.var_ref {var = __try_default_12} {type = i64}
    maxon.assign %326 {var = __try_result_11} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_14
  otherwise_default_continue_14:
    %327 = maxon.var_ref {var = __try_result_11} {type = i64}
    maxon.assign %327 {var = currentState} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br find_slot_15.header
  find_slot_15.header:
    %328 = maxon.literal {value = 0 : i64}
    %329 = maxon.var_ref {var = currentState} {type = i64}
    %330 = maxon.binop %329, %328 {op = ne} {kind = i64}
    maxon.cond_br %330 [then: find_slot_15, else: find_slot_15.exit]
  find_slot_15:
    %331 = maxon.literal {value = 1 : i64}
    %332 = maxon.var_ref {var = index} {type = i64}
    %333 = maxon.binop %332, %331 {op = add} {kind = i64}
    %334 = maxon.var_ref {var = newCapacity} {type = i64}
    %335 = maxon.binop %333, %334 {op = mod} {kind = i64}
    maxon.assign %335 {var = index} {kind = i64} {mut = 1 : i1}
    %336 = maxon.struct_var_ref states
    %338, %337 = maxon.try_call @IntArray.get %336, %335
    %339 = maxon.literal {value = 0 : i64}
    maxon.assign %339 {var = __try_default_17} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %338 {var = __try_result_16} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %340 = maxon.literal {value = 0 : i64}
    %341 = maxon.binop %337, %340 {op = ne} {kind = i64}
    maxon.cond_br %341 [then: otherwise_default_error_18, else: otherwise_default_continue_19]
  otherwise_default_error_18:
    %342 = maxon.var_ref {var = __try_default_17} {type = i64}
    maxon.assign %342 {var = __try_result_16} {kind = i64} {mut = 1 : i1}
    maxon.br otherwise_default_continue_19
  otherwise_default_continue_19:
    %343 = maxon.var_ref {var = __try_result_16} {type = i64}
    maxon.assign %343 {var = currentState} {kind = i64} {mut = 1 : i1}
    maxon.br find_slot_15.header
  find_slot_15.exit:
    %344 = maxon.var_ref {var = index} {type = i64}
    %345 = maxon.var_ref {var = element} {type = i64}
    maxon.call @Array.set %278, %344, %345
    %346 = maxon.var_ref {var = index} {type = i64}
    %347 = maxon.literal {value = 1 : i64}
    maxon.call @IntArray.set %285, %346, %347
    %348 = maxon.literal {value = 1 : i64}
    %349 = maxon.var_ref {var = count} {type = i64}
    %350 = maxon.binop %349, %348 {op = add} {kind = i64}
    maxon.assign %350 {var = count} {kind = i64} {mut = 1 : i1}
  get_elem_9.after:
  occupied_6.after:
    %351 = maxon.literal {value = 1 : i64}
    %352 = maxon.var_ref {var = i} {type = i64}
    %353 = maxon.binop %352, %351 {op = add} {kind = i64}
    maxon.assign %353 {var = i} {kind = i64} {mut = 1 : i1}
    maxon.br rehash_1.header
  rehash_1.exit:
    maxon.return
  }
}
