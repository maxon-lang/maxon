namespace MaxonSharp;

/// <summary>
/// Log levels for debug output, from least to most verbose.
/// </summary>
public enum LogLevel {
	None = 0,
	Error = 1,
	Info = 2,
	Debug = 3,
	Trace = 4
}

/// <summary>
/// Log categories for per-component logging control.
/// </summary>
public enum LogCategory {
	Compiler,
	Lexer,
	Parser,
	Semantic,
	Hir,
	Lir,
	Optimizer,
	Codegen,
	Pe,
	Testing,
	Mlir,
	RegAlloc
}

/// <summary>
/// Static logger with per-category log levels.
/// </summary>
public static class Logger {
	private static readonly Dictionary<LogCategory, LogLevel> CategoryLevels = [];
	private static LogLevel _globalLevel = LogLevel.Info;

	/// <summary>
	/// Sets the global log level for all categories.
	/// </summary>
	public static void SetLevel(LogLevel level) {
		_globalLevel = level;
		CategoryLevels.Clear();
	}

	/// <summary>
	/// Sets the log level for a specific category.
	/// </summary>
	public static void SetLevel(LogCategory category, LogLevel level) {
		CategoryLevels[category] = level;
	}

	/// <summary>
	/// Gets the effective log level for a category.
	/// </summary>
	public static LogLevel GetLevel(LogCategory category) {
		return CategoryLevels.TryGetValue(category, out var level) ? level : _globalLevel;
	}

	/// <summary>
	/// Returns true if the given level is enabled for the category.
	/// </summary>
	public static bool IsEnabled(LogCategory category, LogLevel level) {
		return level <= GetLevel(category);
	}

	private static string CategoryCode(LogCategory category) => category switch {
		LogCategory.Compiler => "CMP",
		LogCategory.Lexer => "LEX",
		LogCategory.Parser => "PAR",
		LogCategory.Semantic => "SEM",
		LogCategory.Hir => "HIR",
		LogCategory.Lir => "LIR",
		LogCategory.Optimizer => "OPT",
		LogCategory.Codegen => "GEN",
		LogCategory.Pe => "PE ",
		LogCategory.Testing => "TST",
		LogCategory.Mlir => "MLR",
		LogCategory.RegAlloc => "REG",
		_ => "???"
	};

	/// <summary>
	/// Logs an error message.
	/// </summary>
	public static void Error(LogCategory category, string message) {
		if (IsEnabled(category, LogLevel.Error)) {
			Console.Error.WriteLine($"[{CategoryCode(category)}] ERROR: {message}");
		}
	}

	/// <summary>
	/// Logs an info message.
	/// </summary>
	public static void Info(LogCategory category, string message) {
		if (IsEnabled(category, LogLevel.Info)) {
			Console.Error.WriteLine($"[{CategoryCode(category)}] {message}");
		}
	}

	/// <summary>
	/// Logs a debug message.
	/// </summary>
	public static void Debug(LogCategory category, string message) {
		if (IsEnabled(category, LogLevel.Debug)) {
			Console.Error.WriteLine($"[{CategoryCode(category)}] DEBUG: {message}");
		}
	}

	/// <summary>
	/// Logs a trace message.
	/// </summary>
	public static void Trace(LogCategory category, string message) {
		if (IsEnabled(category, LogLevel.Trace)) {
			Console.Error.WriteLine($"[{CategoryCode(category)}] TRACE: {message}");
		}
	}

	/// <summary>
	/// Parses a log option string like "debug" or "lexer:trace".
	/// Returns true if successful.
	/// </summary>
	public static bool ParseOption(string option) {
		var colonIndex = option.IndexOf(':');
		if (colonIndex == -1) {
			// Global level: --log=debug
			if (TryParseLevel(option, out var level)) {
				SetLevel(level);
				return true;
			}
			return false;
		}

		// Per-category: --log=lexer:trace
		var categoryStr = option[..colonIndex];
		var levelStr = option[(colonIndex + 1)..];

		if (TryParseCategory(categoryStr, out var category) && TryParseLevel(levelStr, out var catLevel)) {
			SetLevel(category, catLevel);
			return true;
		}
		return false;
	}

	private static bool TryParseLevel(string s, out LogLevel level) {
		level = s.ToLowerInvariant() switch {
			"none" => LogLevel.None,
			"error" => LogLevel.Error,
			"info" => LogLevel.Info,
			"debug" => LogLevel.Debug,
			"trace" => LogLevel.Trace,
			_ => LogLevel.None
		};
		return s.ToLowerInvariant() is "none" or "error" or "info" or "debug" or "trace";
	}

	private static bool TryParseCategory(string s, out LogCategory category) {
		category = s.ToLowerInvariant() switch {
			"compiler" => LogCategory.Compiler,
			"lexer" => LogCategory.Lexer,
			"parser" => LogCategory.Parser,
			"semantic" => LogCategory.Semantic,
			"hir" => LogCategory.Hir,
			"lir" => LogCategory.Lir,
			"optimizer" => LogCategory.Optimizer,
			"codegen" => LogCategory.Codegen,
			"pe" => LogCategory.Pe,
			"testing" => LogCategory.Testing,
			"mlir" => LogCategory.Mlir,
			"regalloc" => LogCategory.RegAlloc,
			_ => LogCategory.Compiler
		};
		return s.ToLowerInvariant() is "compiler" or "lexer" or "parser" or "semantic" or "hir" or "lir" or "optimizer" or "codegen" or "pe" or "testing" or "mlir" or "regalloc";
	}
}
