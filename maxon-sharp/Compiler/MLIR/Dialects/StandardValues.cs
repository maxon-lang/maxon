namespace MaxonSharp.Compiler.Mlir.Dialects;

public abstract class StdValue(int id) {
	public int Id { get; } = id;
	public override string ToString() => $"%{Id}";
	public override bool Equals(object? obj) => obj is StdValue other && Id == other.Id;
	public override int GetHashCode() => Id;
}

public class StdI64(int id) : StdValue(id);
public class StdI32(int id) : StdValue(id);
public class StdF64(int id) : StdValue(id);
public class StdBool(int id) : StdValue(id);
