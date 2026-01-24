using System.Text;

namespace MaxonSharp.Compiler.Mlir.Core;

/// <summary>
/// Helper for printing MLIR in textual format with indentation.
/// </summary>
public sealed class MlirPrinter {
	private readonly StringBuilder _sb = new();
	private int _indentLevel;
	private bool _atLineStart = true;
	private readonly string _indentStr = "  ";

	/// <summary>
	/// Increases indentation level.
	/// </summary>
	public void Indent() => _indentLevel++;

	/// <summary>
	/// Decreases indentation level.
	/// </summary>
	public void Dedent() => _indentLevel = Math.Max(0, _indentLevel - 1);

	/// <summary>
	/// Prints text without a newline.
	/// </summary>
	public void Print(string text) {
		if (_atLineStart && _indentLevel > 0) {
			for (int i = 0; i < _indentLevel; i++)
				_sb.Append(_indentStr);
			_atLineStart = false;
		}
		_sb.Append(text);
	}

	/// <summary>
	/// Prints text followed by a newline.
	/// </summary>
	public void PrintLine(string text = "") {
		Print(text);
		_sb.AppendLine();
		_atLineStart = true;
	}

	/// <summary>
	/// Prints a comment line.
	/// </summary>
	public void PrintComment(string comment) {
		PrintLine($"// {comment}");
	}

	/// <summary>
	/// Prints a section separator comment.
	/// </summary>
	public void PrintSection(string title) {
		PrintLine();
		PrintLine($"// === {title} ===");
	}

	public override string ToString() => _sb.ToString();
}
