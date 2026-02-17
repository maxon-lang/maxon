namespace MaxonSharp.Compiler.Mlir.Dialects;

public abstract class MaxonValue(int id) {
  public int Id { get; } = id;
  public override string ToString() => $"%{Id}";
  public override bool Equals(object? obj) => obj is MaxonValue other && Id == other.Id;
  public override int GetHashCode() => Id;
}

public class MaxonInteger(int id) : MaxonValue(id);
public class MaxonFloat(int id) : MaxonValue(id);
public class MaxonBool(int id) : MaxonValue(id);
public class MaxonByte(int id) : MaxonValue(id);
public class MaxonShort(int id) : MaxonValue(id);
public class MaxonStruct(int id, string typeName) : MaxonValue(id) {
  public string TypeName { get; set; } = typeName;
}
public class MaxonEnum(int id, string typeName) : MaxonValue(id) {
  public string TypeName { get; } = typeName;
}
public class MaxonFunctionPtr(int id) : MaxonValue(id);
