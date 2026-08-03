using System.Reflection;
using MaxonSharp.Compiler.Ir.Dialects;

namespace MaxonSharp.Compiler.Ir.Core;

/// <summary>
/// Copies Maxon ops and the SSA values they reference, so a cloned module owns them outright.
///
/// It is the second half of the same fact <see cref="TypeGraphCopier"/> states about types: a compile
/// WRITES to the IR it is handed, and the module it is handed comes from a cache that outlives it.
/// <c>IrFunction.DeepClone</c> copied the block LISTS but shared the op OBJECTS, and ops and values
/// carry per-compile conclusions — <see cref="MaxonStruct.TypeName"/> above all, which
/// monomorphization refines as it resolves a generic call to a concrete instance. Sharing it meant
/// compile B started from compile A's conclusions: measured on board row A4r, where
/// <c>stdlib.__roundedToLength</c>'s receiver read <c>__Array_DecimalDigit</c> when its program was
/// compiled alone and <c>DigitString</c> when another program had been compiled first in the same
/// process, and the two emitted different binaries from identical sources.
///
/// ⚠ The value map is keyed by SSA ID, not by object, and that is the point: the copy of a value is
/// the SAME SSA value in a different module, so every op that referenced id N must end up referencing
/// the one copy of id N.
/// </summary>
sealed class OpGraphCopier {
  private readonly Dictionary<int, MaxonValue> _values = [];

  /// <summary>
  /// How one field of an op must be treated when the op is copied. Built once per op TYPE and cached:
  /// the reflection cost is paid 112 times per process, not once per op.
  /// </summary>
  private enum FieldRebind { Value, ValueList, TupleList }

  // Concurrent because the spec runner clones a stdlib module on each of its workers, and this is read
  // once per OP — a lock here would serialize every worker's clone on a table that only ever grows to
  // the number of op classes.
  private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, (FieldInfo Field, FieldRebind Rebind)[]> _plans = new();

  public MaxonOp Copy(MaxonOp op) {
    var copy = op.ShallowCopy();
    foreach (var (field, rebind) in PlanFor(op.GetType())) {
      switch (rebind) {
        case FieldRebind.Value:
          field.SetValue(copy, CopyValue((MaxonValue?)field.GetValue(copy)));
          break;
        case FieldRebind.ValueList:
          field.SetValue(copy, CopyValueList((System.Collections.IList?)field.GetValue(copy), field.FieldType));
          break;
        case FieldRebind.TupleList:
          field.SetValue(copy, CopyTupleList((System.Collections.IList?)field.GetValue(copy), field.FieldType));
          break;
        default: throw new InvalidOperationException($"unhandled field rebind '{rebind}'");
      }
    }
    return copy;
  }

  private MaxonValue? CopyValue(MaxonValue? value) {
    if (value == null) return null;
    if (_values.TryGetValue(value.Id, out var existing)) {
      if (existing.GetType() != value.GetType())
        throw new InvalidOperationException(
          $"SSA id {value.Id} appears as both {existing.GetType().Name} and {value.GetType().Name}; " +
          "one id must denote one value");
      return existing;
    }

    var copy = value.CopyKeepingId();
    _values[value.Id] = copy;
    return copy;
  }

  private System.Collections.IList? CopyValueList(System.Collections.IList? source, Type listType) {
    if (source == null) return null;

    var copy = (System.Collections.IList)Activator.CreateInstance(listType, source.Count)!;
    foreach (var item in source) copy.Add(CopyValue((MaxonValue?)item));
    return copy;
  }

  /// <summary>
  /// A list of tuples with a value-typed slot (a struct literal's field list, an interpolation's
  /// parts). ValueTuple's slots are public FIELDS, so a boxed element can be rebound in place and
  /// re-added — which is why this needs no per-shape code and no knowledge of the tuple's arity.
  /// </summary>
  private System.Collections.IList? CopyTupleList(System.Collections.IList? source, Type listType) {
    if (source == null) return null;

    var tupleType = listType.GetGenericArguments()[0];
    var valueSlots = tupleType.GetFields().Where(f => typeof(MaxonValue).IsAssignableFrom(f.FieldType)).ToArray();
    var copy = (System.Collections.IList)Activator.CreateInstance(listType, source.Count)!;
    foreach (var item in source) {
      object boxed = item!;
      foreach (var slot in valueSlots)
        slot.SetValue(boxed, CopyValue((MaxonValue?)slot.GetValue(boxed)));
      copy.Add(boxed);
    }
    return copy;
  }

  /// <summary>
  /// The fields of <paramref name="opType"/> that hold SSA values, in any shape this copier knows how
  /// to rebind — and a THROW for any field that reaches a value in a shape it does not. Silence there
  /// would be the original bug in miniature: one op's field left pointing into the template, with
  /// nothing to say so.
  /// </summary>
  private static (FieldInfo, FieldRebind)[] PlanFor(Type opType) => _plans.GetOrAdd(opType, static type => {
    var plan = new List<(FieldInfo, FieldRebind)>();
    for (var declaring = type; declaring != null && declaring != typeof(object); declaring = declaring.BaseType) {
      foreach (var field in declaring.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)) {
        if (Classify(field.FieldType) is { } rebind) plan.Add((field, rebind));
        else if (ReachesValue(field.FieldType, []))
          throw new InvalidOperationException(
            $"{type.Name}.{field.Name} is a '{field.FieldType}', which holds SSA values in a shape " +
            "OpGraphCopier cannot rebind — teach it that shape, or a cloned module would share that " +
            "field with the module it was cloned from");
      }
    }
    return [.. plan];
  });

  private static FieldRebind? Classify(Type fieldType) {
    if (typeof(MaxonValue).IsAssignableFrom(fieldType)) return FieldRebind.Value;
    if (!fieldType.IsGenericType || fieldType.GetGenericTypeDefinition() != typeof(List<>)) return null;

    var element = fieldType.GetGenericArguments()[0];
    if (typeof(MaxonValue).IsAssignableFrom(element)) return FieldRebind.ValueList;
    if (element.IsGenericType && element.FullName?.StartsWith("System.ValueTuple`", StringComparison.Ordinal) == true
        && element.GetGenericArguments().Any(a => typeof(MaxonValue).IsAssignableFrom(a)))
      return FieldRebind.TupleList;
    return null;
  }

  /// Whether a value is reachable through <paramref name="type"/> at all — through its generic
  /// arguments, or through the fields of a type this compiler itself declares. The visited set is what
  /// makes a recursive type (a node holding a list of nodes) terminate.
  private static bool ReachesValue(Type type, HashSet<Type> visited) {
    if (typeof(MaxonValue).IsAssignableFrom(type)) return true;
    if (!visited.Add(type)) return false;
    if (type.IsGenericType && type.GetGenericArguments().Any(a => ReachesValue(a, visited))) return true;
    if (type.Assembly != typeof(MaxonValue).Assembly) return false;

    return type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
      .Any(f => ReachesValue(f.FieldType, visited));
  }
}
