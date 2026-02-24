module {
  func @HashBucket.lookup(self: HashBucket, target: HashItem) -> i1 {
  entry:
    __scope_0 = maxon.scope_enter {tag = HashBucket.lookup}
    %1 = maxon.struct_param @HashBucket
    %2 = maxon.field_access .items %1
    %3 = maxon.field_access .idx %1
    %4 = maxon.struct_param @HashItem
    %5 = maxon.struct_var_ref self
    maxon.assign %5 {var = __for_iter_1} {decl = 1 : i1} {mut = 1 : i1}
    maxon.br loop_0.header
  loop_0.header:
    %6 = maxon.struct_var_ref __for_iter_1
    %8, %7 = maxon.try_call @HashBucket.next %6
    maxon.assign %7 {var = __try_error_1} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %8 {var = __try_result_2} {decl = 1 : i1} {mut = 1 : i1}
    %9 = maxon.literal {value = 0 : i64}
    %10 = maxon.binop %7, %9 {op = eq}
    maxon.cond_br %10 [then: loop_0, else: loop_0.exit]
  loop_0:
    __scope_11 = maxon.scope_enter {tag = for_in}
    %12 = maxon.struct_var_ref __try_result_2
    maxon.assign %12 {var = item} {decl = 1 : i1}
    %13 = maxon.struct_var_ref item
    %14 = maxon.struct_var_ref target
    %15 = maxon.call @HashItem.equals %13, %14
    maxon.cond_br %15 [then: found_3, else: found_3.after]
  found_3:
    __scope_16 = maxon.scope_enter {tag = if_then}
    %17 = maxon.literal {value = 1 : i1}
    maxon.scope_exit {scope = __scope_16} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_11} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %17
  found_3.after:
    maxon.scope_exit {scope = __scope_11} {tag = block_exit}
    maxon.br loop_0.header
  loop_0.exit:
    %18 = maxon.literal {value = 0 : i1}
    maxon.scope_exit {scope = __scope_0} {tag = return_cleanup}
    maxon.return %18
  }
  func @HashItem.equals(self: HashItem, other: HashItem) -> i1 {
  entry:
    __scope_19 = maxon.scope_enter {tag = HashItem.equals}
    %20 = maxon.struct_param @HashItem
    %21 = maxon.field_access .v %20
    %22 = maxon.struct_param @HashItem
    %23 = maxon.struct_var_ref other
    %24 = maxon.field_access .v %23
    %25 = maxon.binop %21, %24 {op = eq}
    maxon.scope_exit {scope = __scope_19} {tag = return_cleanup}
    maxon.return %25
  }
  func @HashBucket.next(self: HashBucket) -> HashItem {
  entry:
    __scope_39 = maxon.scope_enter {tag = HashBucket.next}
    %40 = maxon.struct_param @HashBucket
    %41 = maxon.field_access .items %40
    %42 = maxon.field_access .idx %40
    %43 = maxon.struct_var_ref items
    %44 = maxon.call @HashItemArray.count %43
    %45 = maxon.binop %42, %44 {op = ge}
    maxon.cond_br %45 [then: done_0, else: done_0.after]
  done_0:
    __scope_46 = maxon.scope_enter {tag = if_then}
    %47 = maxon.enum_literal @IterationError.exhausted
    maxon.scope_exit {scope = __scope_46} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_39} {tag = return_cleanup}
    maxon.throw @IterationError %47
  done_0.after:
    %48 = maxon.struct_var_ref items
    %49 = maxon.var_ref {var = idx} {type = i64}
    %52, %51 = maxon.try_call @HashItemArray.get %48, %49
    maxon.assign %51 {var = __try_error_3} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    maxon.assign %52 {var = __try_result_4} {decl = 1 : i1} {mut = 1 : i1}
    %53 = maxon.literal {value = 0 : i64}
    %54 = maxon.binop %51, %53 {op = ne}
    maxon.cond_br %54 [then: otherwise_error_1, else: otherwise_continue_2]
  otherwise_error_1:
    %55 = maxon.enum_literal @IterationError.exhausted
    maxon.scope_exit {scope = __scope_39} {tag = return_cleanup}
    maxon.throw @IterationError %55
  otherwise_continue_2:
    %56 = maxon.struct_var_ref __try_result_4
    maxon.assign %56 {var = v} {decl = 1 : i1} {mut = 1 : i1}
    %57 = maxon.literal {value = 1 : i64}
    %58 = maxon.var_ref {var = idx} {type = i64}
    %59 = maxon.binop %58, %57 {op = add}
    maxon.assign %59 {var = idx} {kind = i64} {mut = 1 : i1}
    %60 = maxon.struct_var_ref v
    maxon.move {var = v} {dest = __scope_39} {tag = return_move}
    maxon.scope_exit {scope = __scope_39} {tag = return_cleanup}
    maxon.return %60
  }
  func @conditional-extensions.multiple-constraints.main() -> i64 {
  entry:
    __scope_75 = maxon.scope_enter {tag = conditional-extensions.multiple-constraints.main}
    %76 = maxon.literal {value = 1 : i64}
    %77 = maxon.struct_literal @HashItem
    %78 = maxon.literal {value = 2 : i64}
    %79 = maxon.struct_literal @HashItem
    %80 = maxon.literal {value = 3 : i64}
    %81 = maxon.struct_literal @HashItem
    maxon.assign %81 {var = __arr_0.2} {decl = 1 : i1}
    maxon.assign %79 {var = __arr_0.1} {decl = 1 : i1}
    maxon.assign %77 {var = __arr_0.0} {decl = 1 : i1}
    %82 = maxon.literal {value = 0 : i64}
    %83 = maxon.literal {value = 3 : i64}
    %84 = maxon.literal {value = 0 : i64}
    %85 = maxon.literal {value = 8 : i64}
    %86 = maxon.struct_literal @__ManagedMemory
    %87 = maxon.literal {value = 0 : i64}
    %88 = maxon.struct_literal @HashItemArray
    %89 = maxon.literal {value = 0 : i64}
    %90 = maxon.struct_literal @HashBucket
    maxon.assign %90 {var = b} {decl = 1 : i1} {mut = 1 : i1}
    %91 = maxon.struct_var_ref b
    %92 = maxon.literal {value = 2 : i64}
    %93 = maxon.struct_literal @HashItem
    %94 = maxon.call @HashBucket.lookup %91, %93
    maxon.cond_br %94 [then: found_1, else: found_1.after]
  found_1:
    __scope_95 = maxon.scope_enter {tag = if_then}
    %96 = maxon.literal {value = 1 : i64}
    maxon.scope_exit {scope = __scope_95} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_75} {tag = return_cleanup}
    maxon.return %96
  found_1.after:
    %97 = maxon.literal {value = 0 : i64}
    maxon.scope_exit {scope = __scope_75} {tag = return_cleanup}
    maxon.return %97
  }
  func @HashItemArray.count(self: HashItemArray) -> i64 {
  entry:
    __scope_1553 = maxon.scope_enter {tag = stdlib.Array.count}
    %1502 = maxon.struct_param @HashItemArray
    %1503 = maxon.field_access .iterIndex %1502
    %1504 = maxon.field_access .managed %1502
    %1505 = maxon.struct_var_ref managed
    %1506 = maxon.field_access .length %1505
    maxon.assign %1506 {var = __range_val_0} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1507 = maxon.literal {value = 0 : i64}
    %1508 = maxon.binop %1506, %1507 {op = lt}
    maxon.cond_br %1508 [then: __range_panic_0, else: __range_ok_0]
  __range_panic_0:
    maxon.panic "panic at Array.maxon:47: Range check failed for type 'Count': value outside int(0 to 1.8446744073709552E+19)"
  __range_ok_0:
    %1510 = maxon.var_ref {var = __range_val_0} {type = i64}
    maxon.scope_exit {scope = __scope_1553} {tag = return_cleanup}
    maxon.return %1510
  }
  func @HashItemArray.get(self: HashItemArray, index: i64) -> HashItem {
  entry:
    __scope_1609 = maxon.scope_enter {tag = stdlib.Array.get}
    %1551 = maxon.struct_param @HashItemArray
    %1552 = maxon.field_access .iterIndex %1551
    %1553 = maxon.field_access .managed %1551
    %1554 = maxon.param {index = 1 : i32} {name = index} {type = i64}
    %1555 = maxon.struct_var_ref managed
    %1556 = maxon.field_access .length %1555
    maxon.assign %1556 {var = len} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1557 = maxon.binop %1554, %1556 {op = ge} {optimalType = u64}
    maxon.cond_br %1557 [then: upper_0, else: upper_0.after]
  upper_0:
    __scope_1617 = maxon.scope_enter {tag = if_then}
    %1558 = maxon.enum_literal @ArrayError.indexOutOfBounds
    maxon.scope_exit {scope = __scope_1617} {tag = return_cleanup}
    maxon.scope_exit {scope = __scope_1609} {tag = return_cleanup}
    maxon.throw @ArrayError %1558
  upper_0.after:
    %1559 = maxon.struct_var_ref managed
    %1560 = maxon.var_ref {var = index} {type = i64}
    %1561 = maxon.managed_mem_get %1559, %1560
    maxon.scope_exit {scope = __scope_1609} {tag = return_cleanup}
    maxon.return %1561
  }
}
