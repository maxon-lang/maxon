namespace MaxonSharp.Compiler.Mlir.Core;

public class MlirType(string name, int sizeInBytes) {
  public string Name { get; } = name;
  public int SizeInBytes { get; } = sizeInBytes;

  public static MlirType I32 { get; } = new("i32", 4);
  public static MlirType I64 { get; } = new("i64", 8);
  public static MlirType F64 { get; } = new("f64", 8);
  public static MlirType I1 { get; } = new("i1", 1);
  public static MlirType Void { get; } = new("void", 0);

  public override string ToString() => Name;
}
