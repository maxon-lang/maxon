module {
  func @main() -> i64 {
  entry:
    %27 = arith.constant {value = 0 : i64}
    %28 = arith.constant {value = 0 : i64}
    %29 = arith.constant {value = 0 : i64}
    %30 = arith.constant {value = 0 : i64}
    memref.store %28, __struct_4.buffer
    memref.store %29, __struct_4.length
    memref.store %30, __struct_4.capacity
    memref.store %27, __struct_5.iterIndex
    %31 = memref.load __struct_4.buffer : i64
    memref.store %31, __struct_5.managed.buffer
    %32 = memref.load __struct_4.length : i64
    memref.store %32, __struct_5.managed.length
    %33 = memref.load __struct_4.capacity : i64
    memref.store %33, __struct_5.managed.capacity
    %34 = arith.constant {value = 0 : i64}
    %35 = arith.constant {value = 0 : i64}
    %36 = arith.constant {value = 0 : i64}
    %37 = arith.constant {value = 0 : i64}
    memref.store %35, __struct_10.buffer
    memref.store %36, __struct_10.length
    memref.store %37, __struct_10.capacity
    memref.store %34, __struct_11.iterIndex
    %38 = memref.load __struct_10.buffer : i64
    memref.store %38, __struct_11.managed.buffer
    %39 = memref.load __struct_10.length : i64
    memref.store %39, __struct_11.managed.length
    %40 = memref.load __struct_10.capacity : i64
    memref.store %40, __struct_11.managed.capacity
    %41 = arith.constant {value = 0 : i64}
    %42 = arith.constant {value = 0 : i64}
    %43 = arith.constant {value = 0 : i64}
    %44 = memref.load __struct_5.iterIndex : i64
    memref.store %44, __struct_15.elements.iterIndex
    %45 = memref.load __struct_5.managed.buffer : i64
    memref.store %45, __struct_15.elements.managed.buffer
    %46 = memref.load __struct_5.managed.length : i64
    memref.store %46, __struct_15.elements.managed.length
    %47 = memref.load __struct_5.managed.capacity : i64
    memref.store %47, __struct_15.elements.managed.capacity
    %48 = memref.load __struct_11.iterIndex : i64
    memref.store %48, __struct_15.states.iterIndex
    %49 = memref.load __struct_11.managed.buffer : i64
    memref.store %49, __struct_15.states.managed.buffer
    %50 = memref.load __struct_11.managed.length : i64
    memref.store %50, __struct_15.states.managed.length
    %51 = memref.load __struct_11.managed.capacity : i64
    memref.store %51, __struct_15.states.managed.capacity
    memref.store %41, __struct_15.count
    memref.store %42, __struct_15.capacity
    memref.store %43, __struct_15.iterIndex
    %52 = memref.load __struct_15.elements.iterIndex : i64
    memref.store %52, s.elements.iterIndex
    %53 = memref.load __struct_15.elements.managed.buffer : i64
    memref.store %53, s.elements.managed.buffer
    %54 = memref.load __struct_15.elements.managed.length : i64
    memref.store %54, s.elements.managed.length
    %55 = memref.load __struct_15.elements.managed.capacity : i64
    memref.store %55, s.elements.managed.capacity
    %56 = memref.load __struct_15.states.iterIndex : i64
    memref.store %56, s.states.iterIndex
    %57 = memref.load __struct_15.states.managed.buffer : i64
    memref.store %57, s.states.managed.buffer
    %58 = memref.load __struct_15.states.managed.length : i64
    memref.store %58, s.states.managed.length
    %59 = memref.load __struct_15.states.managed.capacity : i64
    memref.store %59, s.states.managed.capacity
    %60 = memref.load __struct_15.count : i64
    memref.store %60, s.count
    %61 = memref.load __struct_15.capacity : i64
    memref.store %61, s.capacity
    %62 = memref.load __struct_15.iterIndex : i64
    memref.store %62, s.iterIndex
    %64 = memref.load s.iterIndex : i64
    memref.store %64, __selfbuf_63.iterIndex
    %65 = memref.load s.capacity : i64
    memref.store %65, __selfbuf_63.capacity
    %66 = memref.load s.count : i64
    memref.store %66, __selfbuf_63.count
    %67 = memref.load s.states.managed.capacity : i64
    memref.store %67, __selfbuf_63.states.managed.capacity
    %68 = memref.load s.states.managed.length : i64
    memref.store %68, __selfbuf_63.states.managed.length
    %69 = memref.load s.states.managed.buffer : i64
    memref.store %69, __selfbuf_63.states.managed.buffer
    %70 = memref.load s.states.iterIndex : i64
    memref.store %70, __selfbuf_63.states.iterIndex
    %71 = memref.load s.elements.managed.capacity : i64
    memref.store %71, __selfbuf_63.elements.managed.capacity
    %72 = memref.load s.elements.managed.length : i64
    memref.store %72, __selfbuf_63.elements.managed.length
    %73 = memref.load s.elements.managed.buffer : i64
    memref.store %73, __selfbuf_63.elements.managed.buffer
    %74 = memref.load s.elements.iterIndex : i64
    memref.store %74, __selfbuf_63.elements.iterIndex
    %75 = memref.lea __selfbuf_63
    %76 = func.call @IntSet.count %75
    %77 = memref.load __selfbuf_63.elements.iterIndex : i64
    memref.store %77, s.elements.iterIndex
    %78 = memref.load __selfbuf_63.elements.managed.buffer : i64
    memref.store %78, s.elements.managed.buffer
    %79 = memref.load __selfbuf_63.elements.managed.length : i64
    memref.store %79, s.elements.managed.length
    %80 = memref.load __selfbuf_63.elements.managed.capacity : i64
    memref.store %80, s.elements.managed.capacity
    %81 = memref.load __selfbuf_63.states.iterIndex : i64
    memref.store %81, s.states.iterIndex
    %82 = memref.load __selfbuf_63.states.managed.buffer : i64
    memref.store %82, s.states.managed.buffer
    %83 = memref.load __selfbuf_63.states.managed.length : i64
    memref.store %83, s.states.managed.length
    %84 = memref.load __selfbuf_63.states.managed.capacity : i64
    memref.store %84, s.states.managed.capacity
    %85 = memref.load __selfbuf_63.count : i64
    memref.store %85, s.count
    %86 = memref.load __selfbuf_63.capacity : i64
    memref.store %86, s.capacity
    %87 = memref.load __selfbuf_63.iterIndex : i64
    memref.store %87, s.iterIndex
    %88 = arith.constant {value = 0 : i64}
    %89 = arith.cmpi ne %76, %88
    cf.cond_br %89 [then: check_0, else: check_0.after]
  check_0:
    %90 = arith.constant {value = 1 : i64}
    func.return %90
  check_0.after:
    %91 = arith.constant {value = 0 : i64}
    func.return %91
  }
  func @IntSet.count(__self_ptr: i64) -> i64 {
  entry:
    %92 = func.param __self_ptr : StdI64
    memref.store %92, __self_ptr
    %93 = arith.constant {value = 0 : i64}
    %94 = arith.addi %92, %93
    %95 = memref.load_indirect %94+0
    memref.store %95, self.elements.iterIndex
    %96 = arith.constant {value = 8 : i64}
    %97 = arith.addi %94, %96
    %98 = memref.load_indirect %97+0
    memref.store %98, self.elements.managed.buffer
    %99 = memref.load_indirect %97+8
    memref.store %99, self.elements.managed.length
    %100 = memref.load_indirect %97+16
    memref.store %100, self.elements.managed.capacity
    %101 = arith.constant {value = 32 : i64}
    %102 = arith.addi %92, %101
    %103 = memref.load_indirect %102+0
    memref.store %103, self.states.iterIndex
    %104 = arith.constant {value = 8 : i64}
    %105 = arith.addi %102, %104
    %106 = memref.load_indirect %105+0
    memref.store %106, self.states.managed.buffer
    %107 = memref.load_indirect %105+8
    memref.store %107, self.states.managed.length
    %108 = memref.load_indirect %105+16
    memref.store %108, self.states.managed.capacity
    %109 = memref.load_indirect %92+64
    memref.store %109, self.count
    %110 = memref.load_indirect %92+72
    memref.store %110, self.capacity
    %111 = memref.load_indirect %92+80
    memref.store %111, self.iterIndex
    %112 = memref.load self.count : i64
    %113 = memref.load self.capacity : i64
    %114 = memref.load self.iterIndex : i64
    func.return %112
  }
}
