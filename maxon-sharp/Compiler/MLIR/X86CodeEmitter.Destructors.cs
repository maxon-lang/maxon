using MaxonSharp.Compiler.Mlir.Core;

namespace MaxonSharp.Compiler.Mlir;

public partial class X86CodeEmitter {
  // Destructors are no longer needed — the runtime memory manager's tree walk
  // (mm_scope_exit → mm_free_entry) handles all recursive cleanup.
  public void EmitDestructors(Dictionary<string, MlirType> typeDefs) { }
}
