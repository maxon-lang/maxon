module {
  func @stdlib.Print.print(value: String) {
  entry:
    %6399 = maxon.struct_param @String
    %6400 = maxon.struct_var_ref value
    %6401 = maxon.field_access .managed %6400
    %6402 = maxon.managed_write_stdout %6401
    maxon.scope_end [value]
    maxon.return
  }
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 0 : i64}
    %1 = maxon.literal {value = 0 : i64}
    %2 = maxon.literal {value = 0 : i64}
    %3 = maxon.literal {value = 0 : i64}
    %4 = maxon.literal {value = 8 : i64}
    %5 = maxon.struct_literal @__ManagedMemory_ByteArray
    %6 = maxon.struct_literal @ByteArrayArray
    maxon.assign %6 {var = names} {decl = 1 : i1} {mut = 1 : i1}
    %7 = maxon.byte_string_literal "hello"
    maxon.assign %7 {var = __lit_tmp_7} {decl = 1 : i1}
    maxon.call @ByteArrayArray.push %6, %7
    %8 = maxon.byte_string_literal "world"
    maxon.assign %8 {var = __lit_tmp_8} {decl = 1 : i1}
    maxon.call @ByteArrayArray.push %6, %8
    %9 = maxon.struct_var_ref names
    %10 = maxon.byte_string_literal "hello"
    maxon.assign %10 {var = __lit_tmp_10} {decl = 1 : i1}
    %11 = maxon.call @ByteArrayArray.contains$sequence %9, %10
    maxon.cond_br %11 [then: found_0, else: notFound_1]
  found_0:
    %12 = maxon.string_literal "found
"
    maxon.assign %12 {var = __lit_tmp_12} {decl = 1 : i1}
    maxon.call @stdlib.Print.print %12
    maxon.scope_end [__lit_tmp_12]
    maxon.br found_0.merge
  notFound_1:
    %13 = maxon.string_literal "not found
"
    maxon.assign %13 {var = __lit_tmp_13} {decl = 1 : i1}
    maxon.call @stdlib.Print.print %13
    maxon.scope_end [__lit_tmp_13]
    maxon.br found_0.merge
  found_0.merge:
    %14 = maxon.struct_var_ref names
    %15 = maxon.byte_string_literal "missing"
    maxon.assign %15 {var = __lit_tmp_15} {decl = 1 : i1}
    %16 = maxon.call @ByteArrayArray.contains$sequence %14, %15
    maxon.cond_br %16 [then: found2_2, else: notFound2_3]
  found2_2:
    %17 = maxon.string_literal "found
"
    maxon.assign %17 {var = __lit_tmp_17} {decl = 1 : i1}
    maxon.call @stdlib.Print.print %17
    maxon.scope_end [__lit_tmp_17]
    maxon.br found2_2.merge
  notFound2_3:
    %18 = maxon.string_literal "not found
"
    maxon.assign %18 {var = __lit_tmp_18} {decl = 1 : i1}
    maxon.call @stdlib.Print.print %18
    maxon.scope_end [__lit_tmp_18]
    maxon.br found2_2.merge
  found2_2.merge:
    %19 = maxon.literal {value = 0 : i64}
    maxon.scope_end [names, __lit_tmp_7, __lit_tmp_8, __lit_tmp_10, __lit_tmp_15]
    maxon.return %19
  }
  func @ByteArray.clone(self: ByteArray) -> ByteArray {
  entry:
    %873 = maxon.struct_param @ByteArray
    %874 = maxon.field_access .iterIndex %873
    %875 = maxon.field_access .managed %873
    %876 = maxon.struct_var_ref managed
    %877 = maxon.field_access .length %876
    maxon.assign %877 {var = len} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %878 = maxon.struct_var_ref managed
    %879 = maxon.literal {value = 0 : i64}
    %880 = maxon.managed_mem_slice %878, %879, %877
    maxon.assign %880 {var = __lit_tmp_2292} {decl = 1 : i1}
    maxon.assign %880 {var = newManaged} {decl = 1 : i1} {mut = 1 : i1}
    %881 = maxon.struct_var_ref newManaged
    %882 = maxon.literal {value = 0 : i64}
    %883 = maxon.struct_literal @ByteArray
    maxon.scope_end [self, iterIndex, managed, len, newManaged, __lit_tmp_2292]
    maxon.return %883
  }
  func @ByteArray.equals(self: ByteArray, other: ByteArray) -> i1 {
  entry:
    %1185 = maxon.struct_param @ByteArray
    %1186 = maxon.field_access .iterIndex %1185
    %1187 = maxon.field_access .managed %1185
    %1188 = maxon.struct_param @ByteArray
    %1189 = maxon.struct_var_ref managed
    %1190 = maxon.field_access .length %1189
    maxon.assign %1190 {var = selfLen} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1191 = maxon.struct_var_ref other
    %1192 = maxon.field_access .managed %1191
    %1193 = maxon.field_access .length %1192
    maxon.assign %1193 {var = otherLen} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1194 = maxon.binop %1190, %1193 {op = ne}
    maxon.cond_br %1194 [then: len_0, else: len_0.after]
  len_0:
    %1195 = maxon.literal {value = 0 : i1}
    maxon.scope_end [self, iterIndex, managed, other, selfLen, otherLen]
    maxon.return %1195
  len_0.after:
    %1196 = maxon.struct_var_ref managed
    %1197 = maxon.field_access .element_size %1196
    %1198 = maxon.var_ref {var = selfLen} {type = i64}
    %1199 = maxon.binop %1198, %1197 {op = mul}
    maxon.assign %1199 {var = byteLen} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1200 = maxon.literal {value = 0 : i64}
    maxon.assign %1199 {var = __range_end_2} {kind = i64} {decl = 1 : i1}
    maxon.assign %1200 {var = __range_current_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br cmp_1.header
  cmp_1.header:
    %1201 = maxon.var_ref {var = __range_current_2} {type = i64}
    %1202 = maxon.var_ref {var = __range_end_2} {type = i64}
    %1203 = maxon.binop %1201, %1202 {op = lt}
    maxon.cond_br %1203 [then: cmp_1, else: cmp_1.exit]
  cmp_1:
    %1204 = maxon.var_ref {var = __range_current_2} {type = i64}
    maxon.assign %1204 {var = i} {kind = i64} {decl = 1 : i1}
    %1205 = maxon.struct_var_ref managed
    %1206 = maxon.managed_mem_byte_get %1205, %1204
    %1207 = maxon.struct_var_ref other
    %1208 = maxon.field_access .managed %1207
    %1209 = maxon.managed_mem_byte_get %1208, %1204
    %1210 = maxon.binop %1206, %1209 {op = ne}
    maxon.cond_br %1210 [then: ne_2, else: ne_2.after]
  ne_2:
    %1211 = maxon.literal {value = 0 : i1}
    maxon.scope_end [self, iterIndex, managed, other, selfLen, otherLen, byteLen, __range_end_2, __range_current_2, i]
    maxon.return %1211
  ne_2.after:
    maxon.scope_end [i]
    maxon.br cmp_1.incr
  cmp_1.incr:
    %1212 = maxon.literal {value = 1 : i64}
    %1213 = maxon.var_ref {var = __range_current_2} {type = i64}
    %1214 = maxon.binop %1213, %1212 {op = add}
    maxon.assign %1214 {var = __range_current_2} {kind = i64} {mut = 1 : i1}
    maxon.br cmp_1.header
  cmp_1.exit:
    %1215 = maxon.literal {value = 1 : i1}
    maxon.scope_end [self, iterIndex, managed, other, selfLen, otherLen, byteLen, __range_end_2, __range_current_2]
    maxon.return %1215
  }
  func @ByteArrayArray.count(self: ByteArrayArray) -> i64 {
  entry:
    %4532 = maxon.struct_param @ByteArrayArray
    %4533 = maxon.field_access .iterIndex %4532
    %4534 = maxon.field_access .managed %4532
    %4535 = maxon.struct_var_ref managed
    %4536 = maxon.field_access .length %4535
    %4537 = maxon.literal {value = 0 : i64}
    %4538 = maxon.binop %4536, %4537 {op = lt}
    maxon.cond_br %4538 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at Array.maxon:48: Range check failed for type 'Count': value outside int(0 to 1.8446744073709552E+19)"
  __range_ok_0:
    maxon.scope_end [self, iterIndex, managed]
    maxon.return %4536
  }
  func @ByteArrayArray.get(self: ByteArrayArray, index: i64) -> ByteArray {
  entry:
    %4577 = maxon.struct_param @ByteArrayArray
    %4578 = maxon.field_access .iterIndex %4577
    %4579 = maxon.field_access .managed %4577
    %4580 = maxon.param {index = 1 : i32} {name = index} {type = i64}
    %4581 = maxon.struct_var_ref managed
    %4582 = maxon.field_access .length %4581
    maxon.assign %4582 {var = len} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4583 = maxon.binop %4580, %4582 {op = ge} {optimalType = u64}
    maxon.cond_br %4583 [then: upper_0, else: upper_0.after]
  upper_0:
    %4584 = maxon.enum_literal @ArrayError.indexOutOfBounds
    maxon.scope_end [self, iterIndex, managed, index, len]
    maxon.throw @ArrayError %4584
  upper_0.after:
    %4585 = maxon.struct_var_ref managed
    %4586 = maxon.var_ref {var = index} {type = i64}
    %4587 = maxon.managed_mem_get %4585, %4586
    maxon.scope_end [self, iterIndex, managed, index, len]
    maxon.return %4587
  }
  func @ByteArrayArray.ensureCapacity(self: ByteArrayArray, requiredLen: i64) {
  entry:
    %4622 = maxon.struct_param @ByteArrayArray
    %4623 = maxon.field_access .iterIndex %4622
    %4624 = maxon.field_access .managed %4622
    %4625 = maxon.param {index = 1 : i32} {name = requiredLen} {type = i64}
    %4626 = maxon.struct_var_ref managed
    %4627 = maxon.field_access .capacity %4626
    maxon.assign %4627 {var = cap} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4628 = maxon.binop %4625, %4627 {op = gt} {optimalType = u64}
    maxon.cond_br %4628 [then: grow_0, else: grow_0.merge]
  grow_0:
    %4629 = maxon.var_ref {var = cap} {type = i64}
    maxon.assign %4629 {var = newCap} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4630 = maxon.literal {value = 4 : i64}
    %4631 = maxon.binop %4629, %4630 {op = lt}
    maxon.cond_br %4631 [then: min_1, else: min_1.merge]
  min_1:
    %4632 = maxon.literal {value = 4 : i64}
    maxon.assign %4632 {var = newCap} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br min_1.merge
  min_1.merge:
    maxon.br double_2.header
  double_2.header:
    %4633 = maxon.var_ref {var = newCap} {type = i64}
    %4634 = maxon.var_ref {var = requiredLen} {type = i64}
    %4635 = maxon.binop %4633, %4634 {op = lt}
    maxon.cond_br %4635 [then: double_2, else: double_2.exit]
  double_2:
    %4636 = maxon.literal {value = 2 : i64}
    %4637 = maxon.var_ref {var = newCap} {type = i64}
    %4638 = maxon.binop %4637, %4636 {op = mul}
    maxon.assign %4638 {var = newCap} {kind = i64} {mut = 1 : i1}
    maxon.scope_end []
    maxon.br double_2.header
  double_2.exit:
    %4639 = maxon.var_ref {var = newCap} {type = i64}
    maxon.managed_mem_grow %4624, %4639
    maxon.scope_end [newCap]
    maxon.br grow_0.merge
  grow_0.merge:
    maxon.scope_end [self, iterIndex, managed, requiredLen, cap]
    maxon.return
  }
  func @ByteArrayArray.push(self: ByteArrayArray, value: ByteArray) {
  entry:
    %4640 = maxon.struct_param @ByteArrayArray
    %4641 = maxon.field_access .iterIndex %4640
    %4642 = maxon.field_access .managed %4640
    %4643 = maxon.struct_param @ByteArray
    %4644 = maxon.struct_var_ref managed
    %4645 = maxon.field_access .length %4644
    maxon.assign %4645 {var = len} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %4646 = maxon.struct_var_ref self
    %4647 = maxon.literal {value = 1 : i64}
    %4648 = maxon.binop %4645, %4647 {op = add}
    maxon.call @ByteArrayArray.ensureCapacity %4646, %4648
    maxon.managed_mem_set %4642, %4645, %4643
    %4649 = maxon.literal {value = 1 : i64}
    %4650 = maxon.binop %4645, %4649 {op = add}
    maxon.managed_mem_set_length %4642, %4650
    maxon.scope_end [self, iterIndex, managed, value, len]
    maxon.return
  }
  func @ByteArrayArray.contains$sequence(self: ByteArrayArray, sequence: ByteArrayArray) -> i1 {
  entry:
    %4749 = maxon.struct_param @ByteArrayArray
    %4750 = maxon.field_access .iterIndex %4749
    %4751 = maxon.field_access .managed %4749
    %4752 = maxon.struct_param @ByteArrayArray
    %4753 = maxon.struct_var_ref self
    %4754 = maxon.call @ByteArrayArray.count %4753
    maxon.assign %4754 {var = selfLen} {kind = i64} {decl = 1 : i1}
    %4755 = maxon.struct_var_ref sequence
    %4756 = maxon.call @ByteArrayArray.count %4755
    maxon.assign %4756 {var = elemLen} {kind = i64} {decl = 1 : i1}
    %4757 = maxon.literal {value = 0 : i64}
    %4758 = maxon.binop %4756, %4757 {op = eq}
    maxon.cond_br %4758 [then: empty_0, else: empty_0.after]
  empty_0:
    %4759 = maxon.literal {value = 1 : i1}
    maxon.scope_end [self, iterIndex, managed, sequence, selfLen, elemLen]
    maxon.return %4759
  empty_0.after:
    %4760 = maxon.literal {value = 0 : i64}
    %4761 = maxon.var_ref {var = selfLen} {type = i64}
    %4762 = maxon.var_ref {var = elemLen} {type = i64}
    %4763 = maxon.binop %4761, %4762 {op = sub}
    maxon.assign %4763 {var = __range_end_2} {kind = i64} {decl = 1 : i1}
    maxon.assign %4760 {var = __range_current_2} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br scan_1.header
  scan_1.header:
    %4764 = maxon.var_ref {var = __range_current_2} {type = i64}
    %4765 = maxon.var_ref {var = __range_end_2} {type = i64}
    %4766 = maxon.binop %4764, %4765 {op = le}
    maxon.cond_br %4766 [then: scan_1, else: scan_1.exit]
  scan_1:
    %4767 = maxon.var_ref {var = __range_current_2} {type = i64}
    maxon.assign %4767 {var = i} {kind = i64} {decl = 1 : i1}
    %4768 = maxon.literal {value = 1 : i1}
    maxon.assign %4768 {var = matched} {kind = i1} {decl = 1 : i1} {mut = 1 : i1}
    %4769 = maxon.literal {value = 0 : i64}
    %4770 = maxon.var_ref {var = elemLen} {type = i64}
    maxon.assign %4770 {var = __range_end_3} {kind = i64} {decl = 1 : i1}
    maxon.assign %4769 {var = __range_current_3} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br cmp_2.header
  cmp_2.header:
    %4771 = maxon.var_ref {var = __range_current_3} {type = i64}
    %4772 = maxon.var_ref {var = __range_end_3} {type = i64}
    %4773 = maxon.binop %4771, %4772 {op = lt}
    maxon.cond_br %4773 [then: cmp_2, else: cmp_2.exit]
  cmp_2:
    %4774 = maxon.var_ref {var = __range_current_3} {type = i64}
    maxon.assign %4774 {var = j} {kind = i64} {decl = 1 : i1}
    %4775 = maxon.struct_var_ref self
    %4776 = maxon.var_ref {var = i} {type = i64}
    %4777 = maxon.binop %4776, %4774 {op = add}
    %4779, %4778 = maxon.try_call @ByteArrayArray.get %4775, %4777
    maxon.assign %4778 {var = __try_error_5} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %4779 {var = __try_result_6} {decl = 1 : i1} {mut = 1 : i1}
    %4780 = maxon.literal {value = 0 : i64}
    %4781 = maxon.binop %4778, %4780 {op = ne}
    maxon.cond_br %4781 [then: otherwise_error_3, else: otherwise_continue_4]
  otherwise_error_3:
    maxon.scope_end [j, __try_error_5, __try_result_6]
    maxon.br cmp_2.exit
  otherwise_continue_4:
    %4782 = maxon.struct_var_ref __try_result_6
    maxon.assign %4782 {var = a} {decl = 1 : i1}
    %4783 = maxon.struct_var_ref sequence
    %4784 = maxon.var_ref {var = j} {type = i64}
    %4786, %4785 = maxon.try_call @ByteArrayArray.get %4783, %4784
    maxon.assign %4785 {var = __try_error_9} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %4786 {var = __try_result_10} {decl = 1 : i1} {mut = 1 : i1}
    %4787 = maxon.literal {value = 0 : i64}
    %4788 = maxon.binop %4785, %4787 {op = ne}
    maxon.cond_br %4788 [then: otherwise_error_7, else: otherwise_continue_8]
  otherwise_error_7:
    maxon.scope_end [j, __try_error_5, __try_result_6, a, __try_error_9, __try_result_10]
    maxon.br cmp_2.exit
  otherwise_continue_8:
    %4789 = maxon.struct_var_ref __try_result_10
    maxon.assign %4789 {var = b} {decl = 1 : i1}
    %4790 = maxon.struct_var_ref a
    %4791 = maxon.call @ByteArray.equals %4790, %4789
    %4792 = maxon.literal {value = 1 : i1}
    %4793 = maxon.binop %4791, %4792 {op = bitxor}
    maxon.cond_br %4793 [then: ne_11, else: ne_11.after]
  ne_11:
    %4794 = maxon.literal {value = 0 : i1}
    maxon.assign %4794 {var = matched} {kind = i1} {mut = 1 : i1}
    maxon.scope_end [j, __try_error_5, __try_result_6, a, __try_error_9, __try_result_10, b]
    maxon.br cmp_2.exit
  ne_11.after:
    maxon.scope_end [j, __try_error_5, __try_result_6, a, __try_error_9, __try_result_10, b]
    maxon.br cmp_2.incr
  cmp_2.incr:
    %4795 = maxon.literal {value = 1 : i64}
    %4796 = maxon.var_ref {var = __range_current_3} {type = i64}
    %4797 = maxon.binop %4796, %4795 {op = add}
    maxon.assign %4797 {var = __range_current_3} {kind = i64} {mut = 1 : i1}
    maxon.br cmp_2.header
  cmp_2.exit:
    %4798 = maxon.var_ref {var = matched} {type = i1}
    maxon.cond_br %4798 [then: found_12, else: found_12.after]
  found_12:
    %4799 = maxon.literal {value = 1 : i1}
    maxon.scope_end [self, iterIndex, managed, sequence, selfLen, elemLen, __range_end_2, __range_current_2, i, matched, __range_end_3, __range_current_3]
    maxon.return %4799
  found_12.after:
    maxon.scope_end [i, matched, __range_end_3, __range_current_3]
    maxon.br scan_1.incr
  scan_1.incr:
    %4800 = maxon.literal {value = 1 : i64}
    %4801 = maxon.var_ref {var = __range_current_2} {type = i64}
    %4802 = maxon.binop %4801, %4800 {op = add}
    maxon.assign %4802 {var = __range_current_2} {kind = i64} {mut = 1 : i1}
    maxon.br scan_1.header
  scan_1.exit:
    %4803 = maxon.literal {value = 0 : i1}
    maxon.scope_end [self, iterIndex, managed, sequence, selfLen, elemLen, __range_end_2, __range_current_2]
    maxon.return %4803
  }
}
