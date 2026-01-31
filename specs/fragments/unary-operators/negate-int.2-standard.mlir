module {
  func @main() -> i64 {
  entry:
    %7 = arith.constant {value = -42 : i64}
    memref.store %7, x
    %8 = arith.constant {value = 0 : i64}
    %9 = arith.subi %8, %7
    memref.store %9, y
    %10 = arith.constant {value = 42 : i64}
    %11 = arith.cmpi eq %9, %10
    cf.cond_br %11 [then: check_0, else: check_0.after]
  check_0:
    %12 = arith.constant {value = 0 : i64}
    func.return %12
  check_0.after:
    %13 = arith.constant {value = 1 : i64}
    func.return %13
  }
}
