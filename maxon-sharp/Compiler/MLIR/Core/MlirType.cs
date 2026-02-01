namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirType(string name, int sizeInBytes) {
  public string Name { get; } = name;
  public int SizeInBytes { get; } = sizeInBytes;

  public static MlirType I8 { get; } = new("i8", 1);
  public static MlirType I32 { get; } = new("i32", 4);
  public static MlirType I64 { get; } = new("i64", 8);
  public static MlirType F64 { get; } = new("f64", 8);
  public static MlirType I1 { get; } = new("i1", 1);
  public static MlirType Void { get; } = new("void", 0);

  public override string ToString() => Name;
}

public class MlirStructField(string name, MlirType type, bool isExported, bool isMutable, MlirAttribute? defaultValue = null) {
  public string Name { get; } = name;
  public MlirType Type { get; } = type;
  public bool IsExported { get; } = isExported;
  public bool IsMutable { get; } = isMutable;
  public MlirAttribute? DefaultValue { get; } = defaultValue;
  public int Offset { get; set; }
}

public class MlirStructType : MlirType {
  public List<MlirStructField> Fields { get; }
  public List<string> AssociatedTypeNames { get; }
  public List<string> ConformingInterfaces { get; }

  public MlirStructType(string name, List<MlirStructField> fields, List<string>? associatedTypeNames = null, List<string>? conformingInterfaces = null) : base(name, ComputeSize(fields)) {
    Fields = fields;
    AssociatedTypeNames = associatedTypeNames ?? [];
    ConformingInterfaces = conformingInterfaces ?? [];
    int offset = 0;
    foreach (var field in Fields) {
      field.Offset = offset;
      // All scalar types are stored as 8 bytes on the stack
      offset += 8;
    }
  }

  private static int ComputeSize(List<MlirStructField> fields) {
    return fields.Count * 8;
  }

  public MlirStructField? GetField(string name) => Fields.FirstOrDefault(f => f.Name == name);
}
