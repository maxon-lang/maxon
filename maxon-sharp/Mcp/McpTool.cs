using System.Text.Json;

namespace MaxonSharp.Mcp;

/// <summary>
/// One property's JSON-Schema fragment, written into an object the caller has already opened. A
/// delegate rather than a data type because a schema is only ever emitted, never inspected: the
/// server writes `tools/list` and nothing reads a schema back.
/// </summary>
internal delegate void McpSchemaWriter(Utf8JsonWriter w);

/// <summary>
/// The JSON-Schema fragments a tool's arguments are built from. Stated once, so two tools cannot
/// describe "an optional string" with two different schema shapes.
/// </summary>
internal static class McpSchema {

  public static McpSchemaWriter String(string description) => w => {
    w.WriteString("type", "string");
    w.WriteString("description", description);
  };

  public static McpSchemaWriter Integer(string description) => w => {
    w.WriteString("type", "integer");
    w.WriteString("description", description);
  };

  public static McpSchemaWriter Number(string description) => w => {
    w.WriteString("type", "number");
    w.WriteString("description", description);
  };

  public static McpSchemaWriter Boolean(string description) => w => {
    w.WriteString("type", "boolean");
    w.WriteString("description", description);
  };

  /// A string argument whose value must be one of <paramref name="choices"/>. The SAME array the
  /// handler validates against is what a client sees, so a rejected value can never be one the schema
  /// advertised.
  public static McpSchemaWriter StringEnum(string description, IReadOnlyList<string> choices) => w => {
    w.WriteString("type", "string");
    w.WriteString("description", description);
    w.WriteStartArray("enum");
    foreach (var choice in choices) w.WriteStringValue(choice);
    w.WriteEndArray();
  };

  public static McpSchemaWriter ArrayOf(string description, McpSchemaWriter item) => w => {
    w.WriteString("type", "array");
    w.WriteString("description", description);
    w.WriteStartObject("items");
    item(w);
    w.WriteEndObject();
  };

  /// An object whose keys are free-form and whose values are all strings (an environment block).
  public static McpSchemaWriter StringMap(string description, string valueDescription) => w => {
    w.WriteString("type", "object");
    w.WriteString("description", description);
    w.WriteStartObject("additionalProperties");
    String(valueDescription)(w);
    w.WriteEndObject();
  };

  public static McpSchemaWriter Object(string description, IReadOnlyList<McpToolArg> properties) => w => {
    w.WriteString("type", "object");
    w.WriteString("description", description);
    WriteProperties(w, properties);
  };

  /// <summary>
  /// The `properties` + `required` pair, written the ONE way — for a tool's top-level input schema and
  /// for a nested object argument alike. `required` derives from the same rows `properties` does, so a
  /// field cannot be required in one list and absent from the other.
  /// </summary>
  public static void WriteProperties(Utf8JsonWriter w, IReadOnlyList<McpToolArg> properties) {
    w.WriteStartObject("properties");
    foreach (var arg in properties) {
      w.WriteStartObject(arg.Name);
      arg.Schema(w);
      w.WriteEndObject();
    }
    w.WriteEndObject();

    w.WriteStartArray("required");
    foreach (var arg in properties)
      if (arg.Required) w.WriteStringValue(arg.Name);
    w.WriteEndArray();
  }
}

/// One argument of a tool (or one property of an object argument): its name, its schema, whether it
/// must be present.
internal readonly record struct McpToolArg(string Name, McpSchemaWriter Schema, bool Required);

/// <summary>
/// What a tool ANSWERED.
///
/// The two arms are the MCP distinction that matters to an agent: a tool that RAN and refused (no live
/// session, an ambiguous breakpoint, a program that already exited) is a RESULT the model must read and
/// act on, so it comes back as content with <c>isError</c> set; a call that was malformed never ran at
/// all and is a JSON-RPC error instead. Flattening the two would either hide a refusal from the model
/// or report a working server as broken.
/// </summary>
internal readonly record struct McpToolResult(bool IsError, string Text) {

  /// A tool that produced an answer. The text is a compact JSON object, because every caller of this
  /// server is an agent parsing a result rather than a human reading one.
  public static McpToolResult Json(string json) => new(false, json);

  /// A tool that ran and could not do what was asked. Prose, because the whole content of the answer is
  /// the reason — and because the reasons come from the debugger's own single-sourced wordings.
  public static McpToolResult Refusal(string reason) => new(true, reason);
}

/// <summary>
/// One tool: its name, its ONE description, its arguments, and the code that runs it. `tools/list` and
/// `tools/call` walk the same rows, so a tool cannot be half-registered — the shape that ships a tool
/// with a missing half when only one of two parallel name ladders gets updated.
/// </summary>
internal sealed record McpTool(
  string Name,
  string Description,
  IReadOnlyList<McpToolArg> Args,
  Func<McpSession, JsonElement, McpToolResult> Handler);

/// <summary>
/// A call whose ARGUMENTS were wrong — a missing required field, a value outside a schema's enum, a
/// string where an object was wanted. Distinct from a refusal: nothing ran, so it is reported as a
/// JSON-RPC `invalidParams` rather than as a tool result.
/// </summary>
internal sealed class McpInvalidParamsException(string message) : Exception(message);

/// <summary>
/// Reading a tool call's `arguments`. One reader per shape, so "this argument is missing" and "this
/// argument is the wrong type" are worded once for every tool rather than once per handler.
/// </summary>
internal static class McpArgs {

  /// The `arguments` object, or a JSON null when the caller sent none — in which case every lookup
  /// below finds its key missing, which is exactly what "called with no arguments" means.
  public static bool TryProperty(JsonElement args, string name, out JsonElement value) {
    value = default;
    return args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out value)
      && value.ValueKind != JsonValueKind.Null;
  }

  public static string RequireString(JsonElement args, string name) =>
    OptionalString(args, name) ?? throw new McpInvalidParamsException($"`{name}` is required");

  public static string? OptionalString(JsonElement args, string name) {
    if (!TryProperty(args, name, out var value)) return null;
    if (value.ValueKind != JsonValueKind.String)
      throw new McpInvalidParamsException($"`{name}` must be a string, got {KindName(value)}");
    return value.GetString();
  }

  /// A string argument constrained to a closed vocabulary — validated against the SAME list the schema
  /// advertises, and refused by naming every value that would have worked.
  public static string RequireChoice(JsonElement args, string name, IReadOnlyList<string> choices) {
    var value = RequireString(args, name);
    if (!choices.Contains(value, StringComparer.Ordinal))
      throw new McpInvalidParamsException(
        $"`{name}` must be one of {string.Join(", ", choices)}; got '{value}'");
    return value;
  }

  public static bool? OptionalBool(JsonElement args, string name) {
    if (!TryProperty(args, name, out var value)) return null;
    return value.ValueKind switch {
      JsonValueKind.True => true,
      JsonValueKind.False => false,
      _ => throw new McpInvalidParamsException($"`{name}` must be a boolean, got {KindName(value)}"),
    };
  }

  public static int RequireInt(JsonElement args, string name) {
    if (!TryProperty(args, name, out var value))
      throw new McpInvalidParamsException($"`{name}` is required");
    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
      throw new McpInvalidParamsException($"`{name}` must be an integer, got {KindName(value)}");
    return number;
  }

  public static double? OptionalNumber(JsonElement args, string name) {
    if (!TryProperty(args, name, out var value)) return null;
    if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number))
      throw new McpInvalidParamsException($"`{name}` must be a number, got {KindName(value)}");
    return number;
  }

  /// <summary>
  /// An optional deadline a caller spelled in SECONDS, or <paramref name="fallback"/>.
  ///
  /// Refused rather than clamped, through the SAME <see cref="PositiveSeconds"/> rule every CLI flag
  /// is refused by: a deadline that is non-positive — or that merely ROUNDS to zero, which is how the
  /// copies used to disagree — expires before the target could possibly finish, and would be reported
  /// as a configured setting rather than as the broken one it is.
  ///
  /// Here rather than in one family because two now take one, and the previous single copy of this
  /// validation is exactly the shape that had already drifted three ways once.
  /// </summary>
  public static TimeSpan OptionalTimeoutSeconds(JsonElement args, string name, TimeSpan fallback) {
    if (OptionalNumber(args, name) is not { } seconds) return fallback;

    if (!PositiveSeconds.TryFrom(seconds, out var timeout))
      throw new McpInvalidParamsException($"`{name}` {PositiveSeconds.RequirementText}, got {seconds}");

    return timeout;
  }

  public static JsonElement RequireObject(JsonElement args, string name) {
    if (!TryProperty(args, name, out var value))
      throw new McpInvalidParamsException($"`{name}` is required");
    if (value.ValueKind != JsonValueKind.Object)
      throw new McpInvalidParamsException($"`{name}` must be an object, got {KindName(value)}");
    return value;
  }

  public static IReadOnlyList<string> OptionalStringArray(JsonElement args, string name) {
    if (!TryProperty(args, name, out var value)) return [];
    if (value.ValueKind != JsonValueKind.Array)
      throw new McpInvalidParamsException($"`{name}` must be an array of strings, got {KindName(value)}");

    var items = new List<string>();
    foreach (var item in value.EnumerateArray()) {
      if (item.ValueKind != JsonValueKind.String)
        throw new McpInvalidParamsException($"every element of `{name}` must be a string, got {KindName(item)}");
      items.Add(item.GetString()!);
    }
    return items;
  }

  public static IReadOnlyDictionary<string, string>? OptionalStringMap(JsonElement args, string name) {
    if (!TryProperty(args, name, out var value)) return null;
    if (value.ValueKind != JsonValueKind.Object)
      throw new McpInvalidParamsException($"`{name}` must be an object of string values, got {KindName(value)}");

    var map = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var field in value.EnumerateObject()) {
      if (field.Value.ValueKind != JsonValueKind.String)
        throw new McpInvalidParamsException(
          $"`{name}.{field.Name}` must be a string, got {KindName(field.Value)}");
      map[field.Name] = field.Value.GetString()!;
    }
    return map;
  }

  /// A JSON kind as a refusal names it — lower-case, and "nothing" for an absent value, so a message
  /// reads as a sentence rather than as an enum member.
  private static string KindName(JsonElement value) => value.ValueKind switch {
    JsonValueKind.Undefined or JsonValueKind.Null => "nothing",
    JsonValueKind.Object => "an object",
    JsonValueKind.Array => "an array",
    JsonValueKind.String => "a string",
    JsonValueKind.Number => "a number",
    JsonValueKind.True or JsonValueKind.False => "a boolean",
    _ => throw new InvalidOperationException($"Unhandled JSON value kind {value.ValueKind}"),
  };
}
