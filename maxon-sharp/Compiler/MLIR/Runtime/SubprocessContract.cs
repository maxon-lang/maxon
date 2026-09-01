namespace MaxonSharp.Compiler.Ir.Runtime;

/// <summary>
/// The spawn contract's own constants, shared by every backend that decodes them.
///
/// AUTHORITY: <c>stdlib/Subprocess.maxon</c>. These values are DECIDED there — the enum
/// <c>EnvSource</c> and the byte <c>BlobTokenTerminator</c> — and every runtime below merely reads
/// what that file wrote. Nothing here may be changed on its own; change the stdlib and follow it.
///
/// This class exists because three hand-written machine-code walks (x86 twice, arm64 once) plus
/// shv2's Std-graph builder each read the same block and each spelled the same numbers as bare
/// literals, so the same fact was written down four times in three languages. The WALKS cannot be
/// collapsed — they are different instruction sequences over two different encodings — but the
/// numbers they agree about can be, and are, here.
/// </summary>
internal static class SubprocessContract {
  /// <summary>
  /// The NUL that ends ONE TOKEN of a runtime blob — an argv token, an environment entry, and, one
  /// more of it after the last entry, the environment block itself. `stdlib/Subprocess.maxon`'s
  /// <c>BlobTokenTerminator</c>, which is what makes the block SELF-DELIMITING: only its bytes reach
  /// the OS and no length travels beside them.
  /// </summary>
  public const int BlobTokenTerminator = 0;

  /// <summary>
  /// The <c>envInherit</c> slot's value meaning "the child inherits this process's environment; the
  /// <c>env</c> slot beside it is not read". `stdlib/Subprocess.maxon`'s <c>EnvSource.parent</c>.
  ///
  /// ⚠ THE TEST IS <c>== EnvSourceParent</c> AND NOT "non-zero", AND THE DIFFERENCE IS THE DIRECTION
  /// AN UNKNOWN VALUE FAILS IN. Both spellings agree on the only two values the stdlib produces, but
  /// a "non-zero" test sends anything it does not recognise down the INHERIT path — which hands the
  /// child the parent's environment while its caller was told it had another, the exact silent wrong
  /// answer the caller-built-block path exists to prevent. Testing for the value that means inherit
  /// sends an unrecognised one down the block path instead, where a wrong pointer is loud.
  /// Two backends spelled it "non-zero" and shv2 spelled it "== 1"; this is the one spelling.
  /// </summary>
  public const int EnvSourceParent = 1;
}
