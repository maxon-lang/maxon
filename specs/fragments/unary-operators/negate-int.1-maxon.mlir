module {
  func @main() -> i64 {
  entry:
    %0 = maxon.literal {value = -42 : i64}
    maxon.assign %0 {var = x} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %1 = maxon.literal {value = 0 : i64}
    %2 = maxon.binop %1, %0 {op = sub} {kind = i64}
    maxon.assign %2 {var = y} {kind = i64} {decl = 1 : i1} {mut = 1 : i1}
    %3 = maxon.literal {value = 42 : i64}
    %4 = maxon.binop %2, %3 {op = eq} {kind = i64}
    maxon.cond_br %4 [then: check_0, else: check_0.after]
  check_0:
    %5 = maxon.literal {value = 0 : i64}
    maxon.return %5
  check_0.after:
    %6 = maxon.literal {value = 1 : i64}
    maxon.return %6
  }
}
