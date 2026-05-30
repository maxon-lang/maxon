namespace MaxonSharp.Testing;

/// <summary>
/// Unit tests for <see cref="BatchRewriter"/>. Run via `maxon batch-rewriter-test`.
/// Returns 0 on all-pass, 1 on any failure.
/// </summary>
public static partial class BatchRewriterTests {

  private static int _passed;
  private static int _failed;

  public static int RunAll() {
    _passed = 0;
    _failed = 0;

    Test_TypealiasRenameAndReference();
    Test_TypeRenameAndStructFieldAccess();
    Test_TopLevelLetRename();
    Test_MainRename();
    Test_WordBoundary_DoesNotRewriteSuffixes();
    Test_CommentedKeyword_DoesNotTrigger();
    Test_MultiLineTypeBlock_OnlyHeaderMatches();
    Test_LocalsInsideFunction_NotRenamed();
    Test_StringLiteralCollision_MarksUnbatchable();
    Test_StringLiteralWithoutCollision_OK();
    Test_MangleSymbolHandlesHyphens();
    Test_MissingMain_ReturnsUnbatchable();
    Test_ExportLetIsRenamed();
    Test_ExportTypeIsRenamed();

    Console.WriteLine();
    if (_failed == 0) {
      Console.WriteLine($"BatchRewriter tests: {_passed} passed");
      return 0;
    } else {
      Console.WriteLine($"BatchRewriter tests: {_passed} passed, {_failed} failed");
      return 1;
    }
  }

  private static void Assert(bool cond, string testName, string detail = "") {
    if (cond) {
      _passed++;
    } else {
      _failed++;
      Console.WriteLine($"FAIL: {testName}{(detail.Length > 0 ? $" — {detail}" : "")}");
    }
  }

  private static void AssertContains(string haystack, string needle, string testName) {
    Assert(haystack.Contains(needle), testName, $"expected to contain '{needle}', got:\n{haystack}");
  }

  private static void AssertNotContains(string haystack, string needle, string testName) {
    Assert(!haystack.Contains(needle), testName, $"expected NOT to contain '{needle}', got:\n{haystack}");
  }

  private static void Test_TypealiasRenameAndReference() {
    var src = "typealias Integer = int(i64.min to i64.max)\n" +
              "function divLive(a Integer, b Integer) returns Integer\n" +
              "\treturn a / b\n" +
              "end 'divLive'\n" +
              "function main() returns ExitCode\n" +
              "\treturn divLive(10, b: 2)\n" +
              "end 'main'\n";
    var r = BatchRewriter.Rewrite("div-live-values", src);
    Assert(r.Batchable, "Test_TypealiasRenameAndReference: batchable", r.Reason);
    if (r.RewrittenSource != null) {
      AssertContains(r.RewrittenSource, "typealias _b_div_live_values_Integer", "Test_TypealiasRenameAndReference: typealias renamed");
      // Free functions are also mangled to avoid overload-resolution collisions across
      // batched fragments that happen to define same-named helpers.
      AssertContains(r.RewrittenSource, "function _b_div_live_values_divLive(a _b_div_live_values_Integer", "Test_TypealiasRenameAndReference: helper renamed with prefixed param type");
      AssertContains(r.RewrittenSource, "function _b_div_live_values_main()", "Test_TypealiasRenameAndReference: main renamed");
      Assert(r.MangledMainName == "_b_div_live_values_main", "Test_TypealiasRenameAndReference: MangledMainName");
    }
  }

  private static void Test_TypeRenameAndStructFieldAccess() {
    var src = "type Point\n" +
              "\tx Int32\n" +
              "\ty Int32\n" +
              "end 'Point'\n" +
              "function main() returns ExitCode\n" +
              "\tlet p = Point{x: 3, y: 4}\n" +
              "\treturn p.x as ExitCode\n" +
              "end 'main'\n";
    var r = BatchRewriter.Rewrite("origin", src);
    Assert(r.Batchable, "Test_TypeRenameAndStructFieldAccess: batchable", r.Reason);
    if (r.RewrittenSource != null) {
      AssertContains(r.RewrittenSource, "type _b_origin_Point", "Test_TypeRenameAndStructFieldAccess: type decl renamed");
      AssertContains(r.RewrittenSource, "let p = _b_origin_Point{", "Test_TypeRenameAndStructFieldAccess: struct literal renamed");
    }
  }

  private static void Test_TopLevelLetRename() {
    var src = "let MAX = 100\n" +
              "function main() returns ExitCode\n" +
              "\treturn MAX as ExitCode\n" +
              "end 'main'\n";
    var r = BatchRewriter.Rewrite("max-const", src);
    Assert(r.Batchable, "Test_TopLevelLetRename: batchable", r.Reason);
    if (r.RewrittenSource != null) {
      AssertContains(r.RewrittenSource, "let _b_max_const_MAX = 100", "Test_TopLevelLetRename: let decl renamed");
      AssertContains(r.RewrittenSource, "return _b_max_const_MAX as", "Test_TopLevelLetRename: reference renamed");
    }
  }

  private static void Test_MainRename() {
    var src = "function main() returns ExitCode\n" +
              "\treturn 42\n" +
              "end 'main'\n";
    var r = BatchRewriter.Rewrite("simple", src);
    Assert(r.Batchable, "Test_MainRename: batchable", r.Reason);
    Assert(r.MangledMainName == "_b_simple_main", "Test_MainRename: MangledMainName");
    if (r.RewrittenSource != null) {
      AssertContains(r.RewrittenSource, "function _b_simple_main()", "Test_MainRename: main signature renamed");
    }
  }

  private static void Test_WordBoundary_DoesNotRewriteSuffixes() {
    // `Integer` is renamed; the substring `Integer` inside `helperIntegers`
    // should NOT be split — the word boundary preserves the full identifier.
    // Note: `helperIntegers` is itself a top-level function so it does get
    // mangled, but as a whole word, not as a partial substring.
    var src = "typealias Integer = int(i64.min to i64.max)\n" +
              "function helperIntegers() returns Integer\n" +
              "\treturn 1\n" +
              "end 'helperIntegers'\n" +
              "function main() returns ExitCode\n" +
              "\treturn helperIntegers() as ExitCode\n" +
              "end 'main'\n";
    var r = BatchRewriter.Rewrite("wb", src);
    Assert(r.Batchable, "Test_WordBoundary: batchable", r.Reason);
    if (r.RewrittenSource != null) {
      // `helperIntegers` is a top-level function, so it IS renamed as a whole.
      AssertContains(r.RewrittenSource, "_b_wb_helperIntegers", "Test_WordBoundary: helperIntegers as a whole word renamed");
      // But the partial substring `Integers` inside it must NOT be split out:
      // we should see `_b_wb_helperIntegers`, not `helper_b_wb_Integers`.
      AssertNotContains(r.RewrittenSource, "helper_b_wb_Integer", "Test_WordBoundary: substring 'Integer' inside 'helperIntegers' not split");
    }
  }

  private static void Test_CommentedKeyword_DoesNotTrigger() {
    // A `// typealias Foo` comment line should NOT cause `Foo` to be renamed.
    var src = "// typealias Foo = int(0 to 9)\n" +
              "function main() returns ExitCode\n" +
              "\treturn 0\n" +
              "end 'main'\n";
    var r = BatchRewriter.Rewrite("comment", src);
    Assert(r.Batchable, "Test_CommentedKeyword: batchable", r.Reason);
    if (r.RewrittenSource != null) {
      // Foo is in a comment, the rewriter should not have created a rename for it.
      AssertNotContains(r.RewrittenSource, "_b_comment_Foo", "Test_CommentedKeyword: Foo not renamed");
    }
  }

  private static void Test_MultiLineTypeBlock_OnlyHeaderMatches() {
    // Field declarations inside `type` are tab-indented; only the header should match.
    var src = "type Point\n" +
              "\tx Integer\n" +  // 'Integer' here is a TYPE REFERENCE, not a top-level decl
              "\ty Integer\n" +
              "end 'Point'\n" +
              "typealias Integer = int(i64.min to i64.max)\n" +
              "function main() returns ExitCode\n" +
              "\tlet p = Point{x: 3, y: 4}\n" +
              "\treturn 0\n" +
              "end 'main'\n";
    var r = BatchRewriter.Rewrite("multiline", src);
    Assert(r.Batchable, "Test_MultiLineTypeBlock: batchable", r.Reason);
    if (r.RewrittenSource != null) {
      AssertContains(r.RewrittenSource, "type _b_multiline_Point", "Test_MultiLineTypeBlock: header renamed");
      AssertContains(r.RewrittenSource, "\tx _b_multiline_Integer", "Test_MultiLineTypeBlock: field type ref renamed");
      // Only one typealias decl line; `\tx Integer` should not have been registered as a top-level decl.
      Assert(MyRegex().Count(r.RewrittenSource) == 1,
             "Test_MultiLineTypeBlock: only one typealias rename");
    }
  }

  private static void Test_LocalsInsideFunction_NotRenamed() {
    // A `let X = ...` inside a function body is tab-indented; should not be a top-level rename.
    var src = "function main() returns ExitCode\n" +
              "\tlet X = 5\n" +  // local, indented
              "\treturn X as ExitCode\n" +
              "end 'main'\n";
    var r = BatchRewriter.Rewrite("locals", src);
    Assert(r.Batchable, "Test_LocalsInsideFunction: batchable", r.Reason);
    if (r.RewrittenSource != null) {
      AssertNotContains(r.RewrittenSource, "_b_locals_X", "Test_LocalsInsideFunction: local X not renamed");
      AssertContains(r.RewrittenSource, "\tlet X = 5", "Test_LocalsInsideFunction: local untouched");
      AssertContains(r.RewrittenSource, "\treturn X as", "Test_LocalsInsideFunction: ref to local untouched");
    }
  }

  private static void Test_StringLiteralCollision_MarksUnbatchable() {
    // 'Integer' is renamed; if a string literal contains the word "Integer", bail.
    var src = "typealias Integer = int(i64.min to i64.max)\n" +
              "function main() returns ExitCode\n" +
              "\tprintln(\"my Integer is 5\")\n" +
              "\treturn 0\n" +
              "end 'main'\n";
    var r = BatchRewriter.Rewrite("strcoll", src);
    Assert(!r.Batchable, "Test_StringLiteralCollision: NOT batchable");
    Assert(r.Reason.Contains("Integer"), "Test_StringLiteralCollision: reason mentions Integer", r.Reason);
  }

  private static void Test_StringLiteralWithoutCollision_OK() {
    // String contains words, but none match a renamed name.
    var src = "typealias Integer = int(i64.min to i64.max)\n" +
              "function main() returns ExitCode\n" +
              "\tprintln(\"hello world\")\n" +
              "\treturn 0\n" +
              "end 'main'\n";
    var r = BatchRewriter.Rewrite("strok", src);
    Assert(r.Batchable, "Test_StringLiteralWithoutCollision: batchable", r.Reason);
  }

  private static void Test_MangleSymbolHandlesHyphens() {
    var mangled = BatchRewriter.MangleSymbol("div-live-values", "Integer");
    Assert(mangled == "_b_div_live_values_Integer", "Test_MangleSymbolHandlesHyphens", mangled);
  }

  private static void Test_MissingMain_ReturnsUnbatchable() {
    var src = "typealias Integer = int(i64.min to i64.max)\n";
    var r = BatchRewriter.Rewrite("nomain", src);
    Assert(!r.Batchable, "Test_MissingMain: not batchable");
  }

  private static void Test_ExportLetIsRenamed() {
    var src = "export let MAX = 100\n" +
              "function main() returns ExitCode\n" +
              "\treturn MAX as ExitCode\n" +
              "end 'main'\n";
    var r = BatchRewriter.Rewrite("expconst", src);
    Assert(r.Batchable, "Test_ExportLetIsRenamed: batchable", r.Reason);
    if (r.RewrittenSource != null) {
      AssertContains(r.RewrittenSource, "export let _b_expconst_MAX", "Test_ExportLetIsRenamed: export let renamed");
      AssertContains(r.RewrittenSource, "return _b_expconst_MAX as", "Test_ExportLetIsRenamed: reference renamed");
    }
  }

  private static void Test_ExportTypeIsRenamed() {
    var src = "export type Foo\n" +
              "\tx Int32\n" +
              "end 'Foo'\n" +
              "function main() returns ExitCode\n" +
              "\tlet f = Foo{x: 1}\n" +
              "\treturn f.x as ExitCode\n" +
              "end 'main'\n";
    var r = BatchRewriter.Rewrite("exptype", src);
    Assert(r.Batchable, "Test_ExportTypeIsRenamed: batchable", r.Reason);
    if (r.RewrittenSource != null) {
      AssertContains(r.RewrittenSource, "export type _b_exptype_Foo", "Test_ExportTypeIsRenamed: export type renamed");
      AssertContains(r.RewrittenSource, "let f = _b_exptype_Foo{", "Test_ExportTypeIsRenamed: struct literal renamed");
    }
  }

  [System.Text.RegularExpressions.GeneratedRegex("typealias _b_multiline_Integer")]
  private static partial System.Text.RegularExpressions.Regex MyRegex();
}
