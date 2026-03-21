module {
  func @stdlib.Print.print(value: String) {
  entry:
    %6458 = maxon.struct_param @String
    %6459 = maxon.struct_var_ref value
    %6460 = maxon.field_access .managed %6459
    %6461 = maxon.managed_write_stdout %6460
    maxon.scope_end [value]
    maxon.return
  }
  func @Pair.toString(self: Pair) -> String {
  entry:
    %0 = maxon.struct_param @Pair
    %1 = maxon.field_access .first %0
    %2 = maxon.field_access .second %0
    %3 = maxon.string_interp
    maxon.assign %3 {var = __lit_tmp_3} {decl = 1 : i1}
    maxon.scope_end [self, first, second, __lit_tmp_3]
    maxon.return %3
  }
  func @string-interpolation.main() -> i64 {
  entry:
    %18 = maxon.literal {value = 1 : i64}
    %19 = maxon.literal {value = 2 : i64}
    %20 = maxon.struct_literal @Pair
    maxon.assign %20 {var = p} {decl = 1 : i1} {mut = 1 : i1}
    %21 = maxon.struct_var_ref p
    %22 = maxon.call @Pair.toString %21
    maxon.assign %22 {var = __interp_tostr_22} {decl = 1 : i1}
    %23 = maxon.string_interp
    maxon.assign %23 {var = __lit_tmp_23} {decl = 1 : i1}
    maxon.call @stdlib.Print.print %23
    %24 = maxon.literal {value = 0 : i64}
    maxon.scope_end [p, __interp_tostr_22, __lit_tmp_23]
    maxon.return %24
  }
}
