using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler.Mlir;

public partial class X86CodeEmitter {
  /// <summary>
  /// Generates per-type destructor functions (__destroy_TypeName) as x86 machine code.
  /// Each destructor performs null-check, decref, and recursive cleanup of managed fields
  /// before freeing the object. Must be called after EmitRuntimeFunctions so that
  /// maxon_free and other runtime primitives are already defined.
  /// </summary>
  public void EmitDestructors(Dictionary<string, MlirType> typeDefs) {
    // __ManagedMemory must be emitted first since all other destructors reference it
    EmitDestroyManagedMemory();

    foreach (var (name, type) in typeDefs) {
      // Skip __ManagedMemory (already emitted above)
      if (name == "__ManagedMemory") continue;

      // Skip __ManagedMemory (already emitted above)
      // Other __ types (__Map_*, __Set_*, __Tuple_*) are monomorphized stdlib types that need destructors
      if (name == "__ManagedMemory") continue;

      switch (type) {
        case MlirEnumType:
          // Simple enums are i64 scalars with no heap allocation — no destructor needed
          break;

        case MlirUnionType unionType when unionType.HasAssociatedValues:
          EmitDestroyAssociatedValueEnum(unionType, typeDefs);
          break;

        case MlirUnionType:
          // Non-associated-value union types without backing — no heap allocation
          break;

        case MlirStructType structType:
          EmitDestroyStruct(structType, typeDefs);
          break;

        // MlirInterfaceType, MlirFunctionType, MlirTypeParameterType — no destructors
      }
    }
  }

  /// <summary>
  /// __destroy___ManagedMemory: decref, free buffer if capacity > 0, free struct.
  /// Layout: [+0]=buffer, [+8]=length, [+16]=capacity, [+24]=element_size
  /// </summary>
  private void EmitDestroyManagedMemory() {
    var prefix = "__destroy___ManagedMemory";
    EmitRuntimeFunctionStart(prefix, 1);

    // Null check
    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", $"{prefix}_skip");

    // Decref and check
    EmitBytes(0x48, 0xFF, 0x49, 0xF8); // DEC qword [rcx-8]
    EmitBytes(0x48, 0x8B, 0x41, 0xF8); // MOV rax, [rcx-8]
    EmitBytes(0x48, 0x85, 0xC0);       // TEST rax, rax
    EmitJcc("nz", $"{prefix}_skip");

    // Refcount reached 0 — free buffer if capacity > 0
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // reload ptr
    EmitBytes(0x48, 0x8B, 0x41, 0x10);        // MOV rax, [rcx+16] (capacity)
    EmitBytes(0x48, 0x85, 0xC0);               // TEST rax, rax
    EmitJcc("z", $"{prefix}_no_buf");

    // Free the buffer at [managed+0]
    EmitMovRegMem(X86Register.Rcx, -0x08, 8); // reload ptr
    EmitBytes(0x48, 0x8B, 0x09);               // MOV rcx, [rcx] (buffer ptr)
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_free")); EmitDword(0);

    DefineLabel($"{prefix}_no_buf");
    // Free the __ManagedMemory struct itself
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_free")); EmitDword(0);

    DefineLabel($"{prefix}_skip");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// Emits a destructor for a struct type. Handles three sub-cases:
  ///   - Array-like types (with Element type param and __ManagedMemory at offset 8)
  ///   - Structs with managed fields (struct-typed or associated-value enum fields)
  ///   - Simple structs (only primitive fields)
  /// </summary>
  private void EmitDestroyStruct(MlirStructType structType, Dictionary<string, MlirType> typeDefs) {
    var sanitized = SanitizeDestructorName(structType.Name);
    var funcName = $"__destroy_{sanitized}";

    if (IsArrayLike(structType)) {
      EmitDestroyArrayLike(structType, funcName, typeDefs);
    } else if (HasManagedFields(structType, typeDefs)) {
      EmitDestroyStructWithManagedFields(structType, funcName, typeDefs);
    } else {
      EmitDestroySimpleStruct(funcName);
    }
  }

  /// <summary>
  /// Simple struct destructor: null check, decref, free.
  /// No managed fields to clean up.
  /// </summary>
  private void EmitDestroySimpleStruct(string funcName) {
    EmitRuntimeFunctionStart(funcName, 1);

    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", $"{funcName}_skip");

    EmitBytes(0x48, 0xFF, 0x49, 0xF8); // DEC qword [rcx-8]
    EmitBytes(0x48, 0x8B, 0x41, 0xF8); // MOV rax, [rcx-8]
    EmitBytes(0x48, 0x85, 0xC0);       // TEST rax, rax
    EmitJcc("nz", $"{funcName}_skip");

    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_free")); EmitDword(0);

    DefineLabel($"{funcName}_skip");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// Struct with managed fields: null check, decref, destroy each managed field, free self.
  /// Managed fields are struct-typed fields (call __destroy_FieldType) or
  /// associated-value enum fields (call __destroy_EnumType).
  /// </summary>
  private void EmitDestroyStructWithManagedFields(MlirStructType structType, string funcName,
      Dictionary<string, MlirType> typeDefs) {
    // Need extra stack space for saving ptr across multiple destroy calls
    EmitRuntimeFunctionStart(funcName, 1, 0x30);

    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", $"{funcName}_skip");

    EmitBytes(0x48, 0xFF, 0x49, 0xF8); // DEC qword [rcx-8]
    EmitBytes(0x48, 0x8B, 0x41, 0xF8); // MOV rax, [rcx-8]
    EmitBytes(0x48, 0x85, 0xC0);       // TEST rax, rax
    EmitJcc("nz", $"{funcName}_skip");

    // Destroy each managed field
    foreach (var field in structType.Fields) {
      var destroyTarget = GetFieldDestroyTarget(field.Type, typeDefs);
      if (destroyTarget == null) continue;

      // Load field value: [ptr + field.Offset] -> rcx
      EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = ptr (reload from stack)
      EmitLoadFieldFromRax(field.Offset);        // RCX = [rax + offset]
      EmitByte(0xE8); _relCallFixups.Add((_code.Count, destroyTarget)); EmitDword(0);
    }

    // Free the struct itself
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_free")); EmitDword(0);

    DefineLabel($"{funcName}_skip");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// Array-like type destructor. Layout: [+0]=iterIndex, [+8]=managed_ptr.
  /// Handles three sub-cases based on element type:
  ///   - Primitive elements: just destroy the __ManagedMemory and free self
  ///   - Struct elements: iterate and destroy each, then destroy managed, free self
  ///   - Associated-value enum elements: same as struct elements with enum destructor
  /// </summary>
  private void EmitDestroyArrayLike(MlirStructType structType, string funcName,
      Dictionary<string, MlirType> typeDefs) {
    structType.TypeParams.TryGetValue("Element", out var elemType);

    var elemDestroyTarget = GetFieldDestroyTarget(elemType!, typeDefs);

    if (elemDestroyTarget != null) {
      EmitDestroyArrayOfManaged(funcName, elemDestroyTarget);
    } else {
      EmitDestroyArrayOfPrimitive(funcName);
    }
  }

  /// <summary>
  /// Array of primitive elements: decref, destroy __ManagedMemory at [ptr+8], free self.
  /// No per-element cleanup needed.
  /// </summary>
  private void EmitDestroyArrayOfPrimitive(string funcName) {
    EmitRuntimeFunctionStart(funcName, 1, 0x30);

    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", $"{funcName}_skip");

    EmitBytes(0x48, 0xFF, 0x49, 0xF8); // DEC qword [rcx-8]
    EmitBytes(0x48, 0x8B, 0x41, 0xF8); // MOV rax, [rcx-8]
    EmitBytes(0x48, 0x85, 0xC0);       // TEST rax, rax
    EmitJcc("nz", $"{funcName}_skip");

    // Load managed_ptr = [ptr+8]
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = ptr
    EmitBytes(0x48, 0x8B, 0x40, 0x08);        // MOV rax, [rax+8] (managed_ptr)
    EmitBytes(0x48, 0x85, 0xC0);               // TEST rax, rax
    EmitJcc("z", $"{funcName}_free_self");

    // Destroy the __ManagedMemory
    EmitMovRegReg(X86Register.Rcx, X86Register.Rax);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "__destroy___ManagedMemory")); EmitDword(0);

    DefineLabel($"{funcName}_free_self");
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_free")); EmitDword(0);

    DefineLabel($"{funcName}_skip");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// Array of managed elements (struct or associated-value enum): decref, iterate elements
  /// calling the element destructor on each, then destroy __ManagedMemory, free self.
  /// Stack layout:
  ///   [rbp-0x08] = array_ptr (saved by prologue)
  ///   [rbp-0x10] = managed_ptr
  ///   [rbp-0x18] = buffer
  ///   [rbp-0x20] = length
  ///   [rbp-0x28] = loop counter i
  /// </summary>
  private void EmitDestroyArrayOfManaged(string funcName, string elemDestroyTarget) {
    EmitRuntimeFunctionStart(funcName, 1, 0x50);

    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", $"{funcName}_skip");

    EmitBytes(0x48, 0xFF, 0x49, 0xF8); // DEC qword [rcx-8]
    EmitBytes(0x48, 0x8B, 0x41, 0xF8); // MOV rax, [rcx-8]
    EmitBytes(0x48, 0x85, 0xC0);       // TEST rax, rax
    EmitJcc("nz", $"{funcName}_skip");

    // Load managed_ptr = [ptr+8]
    EmitMovRegMem(X86Register.Rax, -0x08, 8);
    EmitBytes(0x48, 0x8B, 0x40, 0x08); // MOV rax, [rax+8]
    EmitMovMemReg(-0x10, X86Register.Rax, 8); // [rbp-0x10] = managed_ptr
    EmitBytes(0x48, 0x85, 0xC0);               // TEST rax, rax
    EmitJcc("z", $"{funcName}_free_self");

    // Load buffer = [managed+0], length = [managed+8]
    EmitBytes(0x48, 0x8B, 0x08);               // MOV rcx, [rax] (buffer)
    EmitMovMemReg(-0x18, X86Register.Rcx, 8);  // [rbp-0x18] = buffer
    EmitBytes(0x48, 0x8B, 0x40, 0x08);         // MOV rax, [rax+8] (length)
    EmitMovMemReg(-0x20, X86Register.Rax, 8);  // [rbp-0x20] = length

    // Initialize loop counter i = 0
    EmitBytes(0x48, 0x31, 0xC0);               // XOR rax, rax
    EmitMovMemReg(-0x28, X86Register.Rax, 8);  // [rbp-0x28] = 0

    // Loop: for i = 0 to length
    DefineLabel($"{funcName}_loop");
    EmitMovRegMem(X86Register.Rax, -0x28, 8);  // RAX = i
    EmitMovRegMem(X86Register.Rcx, -0x20, 8);  // RCX = length
    EmitBytes(0x48, 0x39, 0xC8);               // CMP rax, rcx
    EmitJcc("ge", $"{funcName}_loop_done");

    // Load elem = [buffer + i*8]
    EmitMovRegMem(X86Register.Rax, -0x28, 8);  // RAX = i
    EmitBytes(0x48, 0xC1, 0xE0, 0x03);         // SHL rax, 3 (i * 8)
    EmitMovRegMem(X86Register.Rcx, -0x18, 8);  // RCX = buffer
    EmitBytes(0x48, 0x01, 0xC1);               // ADD rcx, rax
    EmitBytes(0x48, 0x8B, 0x09);               // MOV rcx, [rcx] (elem ptr)

    // Call element destructor
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, elemDestroyTarget)); EmitDword(0);

    // i++
    EmitMovRegMem(X86Register.Rax, -0x28, 8);
    EmitAddRegImm(X86Register.Rax, 1);
    EmitMovMemReg(-0x28, X86Register.Rax, 8);
    EmitJmp($"{funcName}_loop");

    DefineLabel($"{funcName}_loop_done");
    // Destroy the __ManagedMemory
    EmitMovRegMem(X86Register.Rcx, -0x10, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "__destroy___ManagedMemory")); EmitDword(0);

    DefineLabel($"{funcName}_free_self");
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_free")); EmitDword(0);

    DefineLabel($"{funcName}_skip");
    EmitRuntimeFunctionEnd();
  }

  /// <summary>
  /// Associated-value enum destructor. These are heap-allocated with layout:
  ///   [+0]=tag, [+8]=payload_0, [+16]=payload_1, ...
  /// For each case that has struct-typed or assoc-value-enum associated values,
  /// emit a branch that destroys those payload slots. Primitive payloads are skipped.
  /// </summary>
  private void EmitDestroyAssociatedValueEnum(MlirUnionType unionType,
      Dictionary<string, MlirType> typeDefs) {
    var sanitized = SanitizeDestructorName(unionType.Name);
    var funcName = $"__destroy_{sanitized}";

    // Check if any case actually has managed associated values that need cleanup
    var hasAnyCasesNeedingCleanup = unionType.Cases.Any(c =>
      c.AssociatedValues != null &&
      c.AssociatedValues.Any(av => GetFieldDestroyTarget(av.Type, typeDefs) != null));

    if (!hasAnyCasesNeedingCleanup) {
      // No cases need cleanup — emit a simple decref+free destructor
      EmitDestroySimpleStruct(funcName);
      return;
    }

    EmitRuntimeFunctionStart(funcName, 1, 0x30);

    EmitBytes(0x48, 0x85, 0xC9); // TEST rcx, rcx
    EmitJcc("z", $"{funcName}_skip");

    EmitBytes(0x48, 0xFF, 0x49, 0xF8); // DEC qword [rcx-8]
    EmitBytes(0x48, 0x8B, 0x41, 0xF8); // MOV rax, [rcx-8]
    EmitBytes(0x48, 0x85, 0xC0);       // TEST rax, rax
    EmitJcc("nz", $"{funcName}_skip");

    // Load tag = [ptr+0]
    EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = ptr
    EmitBytes(0x48, 0x8B, 0x00);               // MOV rax, [rax] (tag)

    // Branch on tag to handle each case
    foreach (var enumCase in unionType.Cases) {
      if (enumCase.AssociatedValues == null || enumCase.AssociatedValues.Count == 0)
        continue;

      // Check if this case has any managed associated values
      var managedSlots = new List<(int SlotIdx, string DestroyTarget)>();
      for (int slotIdx = 0; slotIdx < enumCase.AssociatedValues.Count; slotIdx++) {
        var av = enumCase.AssociatedValues[slotIdx];
        var target = GetFieldDestroyTarget(av.Type, typeDefs);
        if (target != null)
          managedSlots.Add((slotIdx, target));
      }

      if (managedSlots.Count == 0)
        continue;

      var caseLabel = $"{funcName}_case_{enumCase.Ordinal}";
      var nextLabel = $"{funcName}_next_{enumCase.Ordinal}";

      // CMP rax, ordinal
      EmitCmpRaxImm(enumCase.Ordinal);
      EmitJcc("ne", nextLabel);

      // Destroy each managed payload slot
      foreach (var (slotIdx, destroyTarget) in managedSlots) {
        int payloadOffset = 8 + slotIdx * 8; // tag is at +0, payloads start at +8
        EmitMovRegMem(X86Register.Rax, -0x08, 8); // RAX = ptr (reload)
        EmitLoadFieldFromRax(payloadOffset);       // RCX = [rax + payloadOffset]
        EmitByte(0xE8); _relCallFixups.Add((_code.Count, destroyTarget)); EmitDword(0);
      }

      EmitJmp($"{funcName}_free_self");

      DefineLabel(nextLabel);
    }

    // Fall-through: no managed payloads for this tag, just free
    DefineLabel($"{funcName}_free_self");
    EmitMovRegMem(X86Register.Rcx, -0x08, 8);
    EmitByte(0xE8); _relCallFixups.Add((_code.Count, "maxon_free")); EmitDword(0);

    DefineLabel($"{funcName}_skip");
    EmitRuntimeFunctionEnd();
  }

  // ---------------------------------------------------------------------------
  // Helper methods
  // ---------------------------------------------------------------------------

  /// <summary>
  /// Returns true if the struct is an array-like container: has a TypeParams["Element"]
  /// entry and a __ManagedMemory field at offset 8 (not offset 0 like String).
  /// </summary>
  private static bool IsArrayLike(MlirStructType structType) {
    return structType.TypeParams.TryGetValue("Element", out _)
      && structType.Fields.Any(f => f.Type is MlirStructType st
        && st.Name == "__ManagedMemory" && f.Offset == 8);
  }

  /// <summary>
  /// Returns true if any field of the struct needs managed cleanup (is a struct type
  /// or an associated-value enum type).
  /// </summary>
  private static bool HasManagedFields(MlirStructType structType, Dictionary<string, MlirType> typeDefs) {
    return structType.Fields.Any(f => GetFieldDestroyTarget(f.Type, typeDefs) != null);
  }

  /// <summary>
  /// Returns the destructor function name to call for the given field type, or null if
  /// the type doesn't need cleanup (primitives, simple enums, interfaces, etc.).
  /// </summary>
  private static string? GetFieldDestroyTarget(MlirType fieldType, Dictionary<string, MlirType> typeDefs) {
    switch (fieldType) {
      case MlirStructType st:
        // __ManagedMemory has its own dedicated destructor
        if (st.Name == "__ManagedMemory")
          return "__destroy___ManagedMemory";
        return $"__destroy_{SanitizeDestructorName(st.Name)}";

      case MlirEnumType:
        // Simple enums are scalars — no cleanup
        return null;

      case MlirUnionType ut when ut.HasAssociatedValues:
        return $"__destroy_{SanitizeDestructorName(ut.Name)}";

      default:
        return null;
    }
  }

  /// <summary>
  /// Emits: MOV rcx, [rax + offset]
  /// Loads a field value from the pointer in RAX into RCX.
  /// Handles both byte-sized (offset fits in signed byte) and dword-sized displacements.
  /// </summary>
  private void EmitLoadFieldFromRax(int offset) {
    if (offset == 0) {
      // MOV rcx, [rax]
      EmitBytes(0x48, 0x8B, 0x08);
    } else if (offset >= -128 && offset <= 127) {
      // MOV rcx, [rax + disp8]
      EmitBytes(0x48, 0x8B, 0x48, (byte)(offset & 0xFF));
    } else {
      // MOV rcx, [rax + disp32]
      EmitBytes(0x48, 0x8B, 0x88);
      EmitDword(offset);
    }
  }

  /// <summary>
  /// Emits CMP rax, imm for comparing the enum tag value.
  /// Uses the sign-extended imm8 form when possible, otherwise imm32.
  /// </summary>
  private void EmitCmpRaxImm(int value) {
    if (value >= -128 && value <= 127) {
      // CMP rax, imm8 (sign-extended): REX.W + 83 /7 ib
      EmitBytes(0x48, 0x83, 0xF8, (byte)(value & 0xFF));
    } else {
      // CMP rax, imm32: REX.W + 3D id
      EmitBytes(0x48, 0x3D);
      EmitDword(value);
    }
  }

  /// <summary>
  /// Sanitizes a type name for use in labels and function names.
  /// Replaces characters that are invalid in assembly labels.
  /// </summary>
  private static string SanitizeDestructorName(string typeName) {
    return typeName
      .Replace(" ", "_")
      .Replace("<", "_")
      .Replace(">", "_")
      .Replace(",", "_");
  }
}
