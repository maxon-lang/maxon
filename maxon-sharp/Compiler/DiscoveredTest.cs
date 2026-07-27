using System.Text.Json.Serialization;

namespace MaxonSharp.Compiler;

/// <summary>
/// One <c>test</c> declaration, as the PARSER saw it — produced only by
/// <see cref="Compiler.DiscoverTests"/>.
///
/// A test has two names and this carries both, for the reason
/// <c>specs/test-declaration.md</c> gives: the prose name cannot be a symbol, so the compiler mints
/// a sanitized one beside it. Neither is derivable from the other (sanitizing is lossy), so both
/// travel together everywhere.
/// </summary>
/// <param name="Symbol">
/// The mangled, namespace-qualified name the function actually has —
/// <c>&lt;namespace&gt;.__test_&lt;sanitized&gt;</c>. This is what generated source calls and what
/// reaches the executable's symbol table.
/// </param>
/// <param name="DisplayName">The prose name, verbatim. What a report shows a human.</param>
/// <param name="File">
/// The <c>*.test.maxon</c> file holding the declaration, as the compiler recorded it. Results are
/// grouped and ordered by this, so it is identity, not decoration.
/// </param>
/// <param name="Line">The line the declaration's name sits on.</param>
public sealed record DiscoveredTest(string Symbol, string DisplayName, string File, int Line) {
  /// <summary>
  /// The namespace part of <see cref="Symbol"/> — the test's directory under the compile root — or
  /// the empty string for a test at the root.
  ///
  /// Read off the symbol the COMPILER minted rather than re-derived from the file path. The
  /// namespace rule (rel(file, root).parent) is the compiler's, and a second implementation of it
  /// here could disagree with the real one, which would produce a generated call to a function that
  /// does not exist. The sanitized half of the symbol contains no <c>.</c> — every character outside
  /// <c>[A-Za-z0-9_]</c> became <c>_</c> — so the last <c>.</c> is unambiguously the separator.
  /// </summary>
  [JsonIgnore]
  public string Namespace {
    get {
      var lastDot = Symbol.LastIndexOf('.');
      return lastDot < 0 ? "" : Symbol[..lastDot];
    }
  }

  /// <summary>
  /// <see cref="Symbol"/> without its namespace. Generated source that lives in the test's OWN file
  /// calls this form: an unqualified name resolves within the enclosing namespace, which is what
  /// lets the dispatcher be appended to the test file and still reach it.
  /// </summary>
  [JsonIgnore]
  public string ShortSymbol {
    get {
      var lastDot = Symbol.LastIndexOf('.');
      return lastDot < 0 ? Symbol : Symbol[(lastDot + 1)..];
    }
  }
}
