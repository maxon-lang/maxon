using MaxonSharp.Compiler.Mlir.Core;
using MaxonSharp.Compiler.Mlir.Dialects;

namespace MaxonSharp.Compiler;

[Flags]
public enum OwnershipFlags
{
    None       = 0,
    CallReturn = 1 << 0,  // Callee allocated at rc=1; skip incref on first assign
    Borrowed   = 1 << 1,  // Borrowed ref from parent struct field; not independently owned
    Orphan     = 1 << 2,  // Not consumed by parser-tracked var; needs scope-end cleanup
    SelfReturn = 1 << 3,  // Returned from self-returning method; alias, not fresh alloc
    IsTemp     = 1 << 4,  // Internal temp variable (cleaned after user vars at scope end)
    IsParam    = 1 << 5,  // Function parameter (not owned, skip decref at scope end)
}

public record EnumPayloadBinding(string EnumVarName, string EnumTypeName, int PayloadIndex);

public record VarInfo(
    string Name,
    MaxonValueKind Kind,
    bool Mutable,
    MaxonValue Value,
    MlirBlock<MaxonOp> DefinedInBlock,
    OwnershipFlags Flags = OwnershipFlags.None,
    string? StructTypeName = null,
    MlirFunctionType? FnTypeName = null,
    bool IsCaptured = false,
    bool IsSelfField = false,
    EnumPayloadBinding? PayloadBinding = null
);

public record TempVarInfo(string VarName, string StructTypeName, OwnershipFlags Flags);

public class VarRegistry
{
    // Parser-level variable storage (Dictionary preserves insertion order during enumeration
    // as long as only additions happen, or remove+re-add which moves to end)
    private readonly Dictionary<string, VarInfo> _vars = new();

    // Lowering-level temp storage (for temps created during MaxonToStandard conversion)
    private readonly Dictionary<string, TempVarInfo> _temps = new();

    // Scope stack (replaces parser's _scopeStack)
    private readonly Stack<HashSet<string>> _scopeStack = new();

    // ---- Parser-level Factory Methods ----

    /// <summary>
    /// Declare a variable with full VarInfo. Returns the VarInfo.
    /// </summary>
    public VarInfo Declare(string name, MaxonValueKind kind, bool mutable, MaxonValue value,
        MlirBlock<MaxonOp> block, OwnershipFlags flags = OwnershipFlags.None,
        string? structTypeName = null, MlirFunctionType? fnTypeName = null,
        bool isSelfField = false, EnumPayloadBinding? payloadBinding = null)
    {
        var info = new VarInfo(name, kind, mutable, value, block, flags, structTypeName, fnTypeName,
            IsSelfField: isSelfField, PayloadBinding: payloadBinding);
        _vars[name] = info;
        return info;
    }

    // ---- Lowering-level Factory Methods ----

    /// <summary>
    /// Create a lowering-level temp variable. Name is generated as <c>__{kind}_{id}</c>.
    /// Returns the generated name. This IS registration — impossible to forget.
    /// </summary>
    public string CreateTemp(string kind, int id, string structTypeName, OwnershipFlags flags)
    {
        var name = $"__{kind}_{id}";
        _temps[name] = new TempVarInfo(name, structTypeName, flags);
        return name;
    }

    /// <summary>
    /// Register a lowering-level temp with an already-known name.
    /// </summary>
    public void RegisterTemp(string name, string structTypeName, OwnershipFlags flags)
    {
        _temps[name] = new TempVarInfo(name, structTypeName, flags);
    }

    // ---- Parser Variable Lookup ----

    public VarInfo? TryGet(string name) => _vars.TryGetValue(name, out var info) ? info : null;

    public bool TryGetValue(string name, out VarInfo info) => _vars.TryGetValue(name, out info!);

    public VarInfo this[string name]
    {
        get => _vars[name];
        set => _vars[name] = value;
    }

    public bool ContainsKey(string name) => _vars.ContainsKey(name);

    public IEnumerable<string> Keys => _vars.Keys;

    public IEnumerable<VarInfo> Values => _vars.Values;

    public int Count => _vars.Count;

    // ---- Parser Variable Mutation ----

    public void Remove(string name) => _vars.Remove(name);

    public void Clear() => _vars.Clear();

    // ---- Scope Management (replaces _scopeStack) ----

    public void PushScope()
    {
        _scopeStack.Push([.. _vars.Keys]);
    }

    public void PopScope()
    {
        var parentKeys = _scopeStack.Pop();
        var toRemove = _vars.Keys.Where(k => !parentKeys.Contains(k)).ToList();
        foreach (var name in toRemove)
        {
            _vars.Remove(name);
        }
    }

    /// <summary>
    /// Snapshot current variable keys for manual scope tracking (loops, matches, etc.)
    /// </summary>
    public HashSet<string> SnapshotKeys() => [.. _vars.Keys];

    /// <summary>
    /// Get variables added since a snapshot (for scope-end cleanup lists).
    /// </summary>
    public List<string> KeysSince(HashSet<string> snapshot)
    {
        return _vars.Keys.Where(k => !snapshot.Contains(k)).ToList();
    }

    // ---- Ordered Enumeration ----

    /// <summary>
    /// Returns scope-end cleanup list: user vars first, then temps.
    /// Uses OwnershipFlags.IsTemp instead of prefix string matching.
    /// </summary>
    public List<string> GetScopeEndVars()
    {
        var userVars = new List<string>();
        var tempVars = new List<string>();
        foreach (var (name, info) in _vars)
        {
            if (info.Flags.HasFlag(OwnershipFlags.IsTemp))
                tempVars.Add(name);
            else
                userVars.Add(name);
        }
        var result = new List<string>(userVars.Count + tempVars.Count);
        result.AddRange(userVars);
        result.AddRange(tempVars);
        return result;
    }

    /// <summary>
    /// Get scope-end variable flags for propagation into MaxonScopeEndOp.
    /// Returns a dictionary mapping var name to (OwnershipFlags, StructTypeName).
    /// </summary>
    public Dictionary<string, (OwnershipFlags Flags, string? StructTypeName)> GetScopeEndVarMetadata()
    {
        var result = new Dictionary<string, (OwnershipFlags, string?)>();
        foreach (var (name, info) in _vars)
        {
            result[name] = (info.Flags, info.StructTypeName);
        }
        return result;
    }

    /// <summary>
    /// All key-value pairs (for closure save/restore).
    /// </summary>
    public Dictionary<string, VarInfo> SaveAll() => new(_vars);

    /// <summary>
    /// Restore from a saved snapshot (for closure restore).
    /// </summary>
    public void RestoreAll(Dictionary<string, VarInfo> saved)
    {
        _vars.Clear();
        foreach (var (k, v) in saved) _vars[k] = v;
    }

    // ---- Ownership Queries (replaces ALL prefix checking) ----

    // For parser-level variables:
    public bool HasFlag(string name, OwnershipFlags flag) =>
        _vars.TryGetValue(name, out var info) && info.Flags.HasFlag(flag);

    public OwnershipFlags GetFlags(string name) =>
        _vars.TryGetValue(name, out var info) ? info.Flags : OwnershipFlags.None;

    // For lowering-level temps:
    public bool IsTempRegistered(string name) => _temps.ContainsKey(name);

    public bool TempHasFlag(string name, OwnershipFlags flag) =>
        _temps.TryGetValue(name, out var info) && info.Flags.HasFlag(flag);

    public string? GetTempStructType(string name) =>
        _temps.TryGetValue(name, out var info) ? info.StructTypeName : null;

    /// <summary>
    /// Check if a temp is a call-return transfer (skip incref on first assign).
    /// Works for both parser-level and lowering-level temps.
    /// </summary>
    public bool IsCallReturnTransfer(string name) =>
        HasFlag(name, OwnershipFlags.CallReturn) || TempHasFlag(name, OwnershipFlags.CallReturn);

    /// <summary>
    /// Check if a temp needs scope-end orphan cleanup (lowering-level only).
    /// </summary>
    public IEnumerable<string> OrphanTemps =>
        _temps.Values.Where(v => v.Flags.HasFlag(OwnershipFlags.Orphan)).Select(v => v.VarName);

    /// <summary>
    /// Check if a lowering-level temp is managed (has struct type, needs cleanup).
    /// </summary>
    public bool IsTempManaged(string name) => _temps.ContainsKey(name);

    /// <summary>
    /// Mark a lowering-level temp as orphan after creation.
    /// </summary>
    public void MarkTempOrphan(string name)
    {
        if (_temps.TryGetValue(name, out var info))
            _temps[name] = info with { Flags = info.Flags | OwnershipFlags.Orphan };
    }

    // ---- Temp Ownership Transfer (replaces FixupTempOwnership prefix checks) ----

    /// <summary>
    /// When a named variable takes ownership of a value from a temp,
    /// either remove the temp (for call returns/literals) or reorder it (for try results).
    /// Uses flags instead of prefix checking.
    /// </summary>
    /// <param name="backedByVar">The temp variable that was backing the value.</param>
    public void TransferTempOwnership(string backedByVar)
    {
        if (!_vars.ContainsKey(backedByVar)) return;
        var info = _vars[backedByVar];
        if (info.Flags.HasFlag(OwnershipFlags.CallReturn))
        {
            // Ownership fully transferred to named var — remove the temp entirely
            _vars.Remove(backedByVar);
        }
        else if (info.Flags.HasFlag(OwnershipFlags.IsTemp) && !info.Flags.HasFlag(OwnershipFlags.CallReturn))
        {
            // Non-call-return temps (like __try_result_): re-insert at end for ordering
            _vars.Remove(backedByVar);
            _vars[backedByVar] = info;
        }
    }

    // ---- Lowering-level: clear temps for new function ----

    public void ClearTemps() => _temps.Clear();
}
