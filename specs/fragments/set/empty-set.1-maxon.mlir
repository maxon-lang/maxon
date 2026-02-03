module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = 0 : i64}
    %1 = maxon.literal {value = 0 : i64}
    %2 = maxon.literal {value = 0 : i64}
    %3 = maxon.literal {value = 0 : i64}
    %4 = maxon.struct_literal @__ManagedMemory
    %5 = maxon.struct_literal @ElementArray
    %6 = maxon.literal {value = 0 : i64}
    %7 = maxon.literal {value = 0 : i64}
    %8 = maxon.literal {value = 0 : i64}
    %9 = maxon.literal {value = 0 : i64}
    %10 = maxon.struct_literal @__ManagedMemory
    %11 = maxon.struct_literal @IntArray
    %12 = maxon.literal {value = 0 : i64}
    %13 = maxon.literal {value = 0 : i64}
    %14 = maxon.literal {value = 0 : i64}
    %15 = maxon.struct_literal @IntSet
    maxon.assign %15 {var = s} {decl = 1 : i1} {mut = 1 : i1}
    %16 = maxon.call @IntSet.count %15
    %17 = maxon.literal {value = 0 : i64}
    %18 = maxon.binop %16, %17 {op = ne} {kind = i64}
    maxon.cond_br %18 [then: check_0, else: check_0.after]
  check_0:
    %19 = maxon.literal {value = 1 : i64}
    maxon.return %19
  check_0.after:
    %20 = maxon.literal {value = 0 : i64}
    maxon.return %20
  }
  func @IntSet.count(self: IntSet) -> i64 {
  entry:
    %21 = maxon.struct_param @IntSet
    %22 = maxon.field_access .elements %21
    %23 = maxon.field_access .states %21
    %24 = maxon.field_access .count %21
    %25 = maxon.field_access .capacity %21
    %26 = maxon.field_access .iterIndex %21
    maxon.return %24
  }
}
