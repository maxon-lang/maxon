using System.Diagnostics;
using System.Text.RegularExpressions;
using Markdig;

namespace DocGen {
	internal class SpecInfo {
		public string Name { get; set; } = "";
		public string Category { get; set; } = "uncategorized";
		public string DocumentationContent { get; set; } = "";
		public string Title { get; set; } = "";
	}

	internal partial class Program {
		static void Main() {
			var inputPath = "../specs";

			if (Directory.Exists("Output") == false) {
				Directory.CreateDirectory("Output");
			}

			foreach (FileInfo file in new DirectoryInfo("Output").EnumerateFiles()) {
				// Keep the CSS file if it exists
				if (file.Name == "style.css" || file.Name == "nav.js") continue;
				file.Delete();
			}

			// Generate CSS file
			GenerateStylesheet();

			var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseBootstrap().Build();            // Collect all specs and group by category
			var specsByCategory = new Dictionary<string, List<SpecInfo>>();
			var validationErrors = new List<string>();

			foreach (var filename in Directory.EnumerateFiles(inputPath, "*.md")) {
				var basename = Path.GetFileNameWithoutExtension(filename);

				// Skip README and status files
				if (basename.Equals("README", StringComparison.OrdinalIgnoreCase) ||
					basename.Contains("CLEANUP") || basename.Contains("IMPLEMENTATION")) {
					continue;
				}

				var content = File.ReadAllText(filename);

				// Validate documentation examples have expected outputs
				ValidateDocumentationExamples(content, basename, validationErrors);

				var spec = new SpecInfo {
					Name = basename,
					Category = ExtractCategory(content),
					DocumentationContent = ExtractDocumentationSection(content),
					Title = GenerateTitle(basename)
				};

				if (!string.IsNullOrWhiteSpace(spec.DocumentationContent)) {
					if (!specsByCategory.TryGetValue(spec.Category, out var value)) {
						value = [];
						specsByCategory[spec.Category] = value;
					}

					value.Add(spec);
				}
			}

			// Report validation errors
			if (validationErrors.Count > 0) {
				Console.ForegroundColor = ConsoleColor.Red;
				Console.WriteLine($"\n{validationErrors.Count} validation error(s) found:");
				Console.ResetColor();
				foreach (var error in validationErrors) {
					Console.WriteLine($"  {error}");
				}
				Console.WriteLine("\nAll maxon code blocks in Documentation sections must have corresponding ```output blocks.");
				Environment.Exit(1);
			}

			// Sort specs within each category by name
			foreach (var category in specsByCategory.Keys) {
				specsByCategory[category].Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.Ordinal));
			}

			// Generate navigation JavaScript
			GenerateNavigationScript(specsByCategory);

			// Generate category pages
			foreach (var kvp in specsByCategory.OrderBy(x => x.Key)) {
				var category = kvp.Key;
				var specs = kvp.Value;

				var htmlContent = GenerateCategoryPage(category, specs, pipeline);
				var filename = $"Output/{category}.html";
				File.WriteAllText(filename, htmlContent);
				Console.WriteLine($"Generated {category}.html ({specs.Count} specs)");
			}

			// Generate index page
			var indexContent = GenerateIndexPage(specsByCategory);
			File.WriteAllText("Output/index.html", indexContent);
			Console.WriteLine("Generated index.html");
		}

		static void GenerateStylesheet() {
			var css = @"/* Maxon Language Documentation Styles */

/* Base styles */
body {
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif;
    line-height: 1.6;
    margin: 0;
    padding: 0;
    background-color: #f5f5f5;
}

/* Category page layout */
.container {
    display: flex;
    min-height: 100vh;
}

/* Sidebar navigation */
.sidebar {
    width: 250px;
    background-color: #2c3e50;
    color: #ecf0f1;
    padding: 20px;
    position: fixed;
    height: 100vh;
    overflow-y: auto;
}

.sidebar h2 {
    margin-top: 0;
    color: #3498db;
    font-size: 1.5em;
}

.sidebar ul {
    list-style: none;
    padding: 0;
}

.sidebar li {
    margin: 8px 0;
}

.sidebar a {
    color: #ecf0f1;
    text-decoration: none;
    display: block;
    padding: 5px 10px;
    border-radius: 4px;
    transition: background-color 0.2s;
}

.sidebar a:hover, .sidebar a.active {
    background-color: #34495e;
}

/* Content area */
.content {
    margin-left: 250px;
    padding: 40px;
    flex: 1;
    max-width: 900px;
}

/* Header section */
.header {
    background-color: white;
    padding: 30px;
    margin-bottom: 30px;
    border-radius: 8px;
    box-shadow: 0 2px 4px rgba(0,0,0,0.1);
}

.header h1 {
    margin: 0 0 10px 0;
    color: #2c3e50;
}

.header .breadcrumb {
    color: #7f8c8d;
    font-size: 0.9em;
}

.header .breadcrumb a {
    color: #3498db;
    text-decoration: none;
}

/* Table of contents */
.toc {
    background-color: white;
    padding: 20px;
    margin-bottom: 30px;
    border-radius: 8px;
    box-shadow: 0 2px 4px rgba(0,0,0,0.1);
}

.toc h2 {
    margin-top: 0;
    color: #2c3e50;
    font-size: 1.2em;
}

.toc ul {
    list-style: none;
    padding: 0;
}

.toc li {
    margin: 8px 0;
}

.toc a {
    color: #3498db;
    text-decoration: none;
}

.toc a:hover {
    text-decoration: underline;
}

/* Spec sections */
.spec-section {
    background-color: white;
    padding: 30px;
    margin-bottom: 30px;
    border-radius: 8px;
    box-shadow: 0 2px 4px rgba(0,0,0,0.1);
}

.spec-section h2 {
    margin-top: 0;
    color: #2c3e50;
    border-bottom: 2px solid #3498db;
    padding-bottom: 10px;
}

.spec-section h3 {
    color: #34495e;
    margin-top: 1.5em;
}

/* Code blocks */
pre {
    background-color: #f8f8f8;
    border: 1px solid #ddd;
    border-radius: 4px;
    padding: 15px;
    overflow-x: auto;
}

code {
    background-color: #f8f8f8;
    padding: 2px 6px;
    border-radius: 3px;
    font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
}

pre code {
    background-color: transparent;
    padding: 0;
}

/* Expected output styling */
.expected-output {
    background-color: #e8f5e9;
    border: 1px solid #a5d6a7;
    border-radius: 4px;
    padding: 15px;
    margin-top: 10px;
    font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
    position: relative;
}

.expected-output legend {
    color: #2e7d32;
    font-weight: bold;
    padding: 0 5px;
    font-size: 0.9em;
}

.expected-output pre {
    background-color: transparent;
    border: none;
    padding: 0;
    margin: 0;
}

/* Error output styling (for MaxoncStderr) */
.error-output {
    background-color: #ffebee;
    border: 1px solid #ef9a9a;
    border-radius: 4px;
    padding: 15px;
    margin-top: 10px;
    font-family: 'Consolas', 'Monaco', 'Courier New', monospace;
    position: relative;
}

.error-output legend {
    color: #c62828;
    font-weight: bold;
    padding: 0 5px;
    font-size: 0.9em;
}

.error-output pre {
    background-color: transparent;
    border: none;
    padding: 0;
    margin: 0;
}

/* Index page styles */
body:has(.categories) .container {
    max-width: 1000px;
    margin: 0 auto;
    padding: 40px 20px;
    display: block;
}

body:has(.categories) .header {
    text-align: center;
    padding: 40px;
    margin-bottom: 40px;
}

body:has(.categories) .header h1 {
    font-size: 2.5em;
}

body:has(.categories) .header p {
    color: #7f8c8d;
    font-size: 1.1em;
}

/* Category cards (index page) */
.categories {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(300px, 1fr));
    gap: 20px;
}

.category-card {
    background-color: white;
    padding: 25px;
    border-radius: 8px;
    box-shadow: 0 2px 4px rgba(0,0,0,0.1);
    transition: transform 0.2s, box-shadow 0.2s;
    text-decoration: none;
    color: inherit;
    display: block;
}

.category-card:hover {
    transform: translateY(-2px);
    box-shadow: 0 4px 8px rgba(0,0,0,0.15);
}

.category-card h2 {
    margin: 0 0 10px 0;
    color: #3498db;
    font-size: 1.3em;
}

.category-card p {
    margin: 0;
    color: #7f8c8d;
}

.spec-count {
    font-weight: bold;
    color: #2c3e50;
}
";
			File.WriteAllText("Output/style.css", css);
			Console.WriteLine("Generated style.css");
		}

		static void GenerateNavigationScript(Dictionary<string, List<SpecInfo>> specsByCategory) {
			var js = new System.Text.StringBuilder();
			js.AppendLine("// Navigation data and dynamic sidebar generation");
			js.AppendLine("const categories = {");

			var categoryList = specsByCategory.Keys.OrderBy(x => x).ToList();
			for (int i = 0; i < categoryList.Count; i++) {
				var cat = categoryList[i];
				var displayName = GetCategoryDisplayName(cat);
				var comma = i < categoryList.Count - 1 ? "," : "";
				js.AppendLine($"    '{cat}': '{displayName}'{comma}");
			}

			js.AppendLine("};");
			js.AppendLine("");
			js.AppendLine("// Build and insert sidebar navigation");
			js.AppendLine("document.addEventListener('DOMContentLoaded', function() {");
			js.AppendLine("    const sidebar = document.getElementById('sidebar');");
			js.AppendLine("    if (!sidebar) return;");
			js.AppendLine("");
			js.AppendLine("    const activeCategory = sidebar.dataset.active;");
			js.AppendLine("");
			js.AppendLine("    let html = '<h2>Maxon Docs</h2><ul>';");
			js.AppendLine("    html += '<li><a href=\"index.html\">← Home</a></li>';");
			js.AppendLine("");
			js.AppendLine("    for (const [key, displayName] of Object.entries(categories)) {");
			js.AppendLine("        const activeClass = key === activeCategory ? ' class=\"active\"' : '';");
			js.AppendLine("        html += `<li><a href=\"${key}.html\"${activeClass}>${displayName}</a></li>`;");
			js.AppendLine("    }");
			js.AppendLine("");
			js.AppendLine("    html += '</ul>';");
			js.AppendLine("    sidebar.innerHTML = html;");
			js.AppendLine("});");

			File.WriteAllText("Output/nav.js", js.ToString());
			Console.WriteLine("Generated nav.js");
		}

		static void ValidateDocumentationExamples(string content, string basename, List<string> errors) {
			// Find the Documentation section
			var docMatch = MyRegex1().Match(content);
			if (!docMatch.Success) {
				return; // No Documentation section
			}

			var docSection = docMatch.Groups[1].Value;
			var lines = content.Split('\n');

			// Find line number where Documentation section starts
			int docSectionLine = 0;
			for (int i = 0; i < lines.Length; i++) {
				if (lines[i].Contains("## Documentation")) {
					docSectionLine = i + 1;
					break;
				}
			}

			// Find all ```maxon blocks in the Documentation section
			var maxonBlocks = MyRegex6().Matches(docSection);

			foreach (Match match in maxonBlocks) {
				// Get position after the maxon block
				int blockStart = match.Index + match.Length;

				// Find the closing ``` for this maxon block
				int blockEnd = docSection.IndexOf("```", blockStart);
				if (blockEnd == -1) continue;

				// Extract the code block content
				var codeBlock = docSection[blockStart..blockEnd];

				// Only validate blocks that contain "function main()" - these should be executable examples
				if (!codeBlock.Contains("function main()")) {
					continue; // Skip syntax examples and snippets
				}

				// Check if there's an ```exitcode or ```maxoncstderr block immediately after
				int nextBlockStart = blockEnd + 3;
				if (nextBlockStart >= docSection.Length) {
					// Calculate approximate line number
					int lineNum = docSectionLine + docSection[..match.Index].Count(c => c == '\n');
					errors.Add($"{basename}.md (line ~{lineNum}): maxon code block without ```exitcode or ```maxoncstderr block");
					continue;
				}

				// Skip whitespace after closing ```
				while (nextBlockStart < docSection.Length &&
					   (docSection[nextBlockStart] == ' ' || docSection[nextBlockStart] == '\t' ||
						docSection[nextBlockStart] == '\r' || docSection[nextBlockStart] == '\n')) {
					nextBlockStart++;
				}

				// Check if next block is ```exitcode or ```maxoncstderr
				if (nextBlockStart >= docSection.Length ||
					(!docSection[nextBlockStart..].StartsWith("```exitcode") &&
					 !docSection[nextBlockStart..].StartsWith("```maxoncstderr"))) {
					// Calculate approximate line number
					int lineNum = docSectionLine + docSection[..match.Index].Count(c => c == '\n');
					errors.Add($"{basename}.md (line ~{lineNum}): maxon code block without ```exitcode or ```maxoncstderr block");
				}
			}
		}

		static string ExtractCategory(string content) {
			// Extract category from YAML frontmatter
			var categoryMatch = MyRegex().Match(content);
			if (categoryMatch.Success) {
				return categoryMatch.Groups[1].Value.Trim();
			}
			return "uncategorized";
		}

		static string ExtractDocumentationSection(string content) {
			// Find the ## Documentation section only, stop at any following ## heading
			var docMatch = MyRegex1().Match(content);

			if (docMatch.Success) {
				return docMatch.Groups[1].Value.Trim();
			}

			return string.Empty;
		}

		// Expected results are now included directly in the Documentation section using ```exitcode/```stdout blocks
		// or ```maxoncstderr blocks, and are rendered in the HTML output for users to see.

		static string ProcessExitCodeBlocks(string html) {
			// Find and combine exitcode and stdout blocks into a single expected output block
			// Pattern: <pre><code class="language-exitcode">exitCode</code></pre> followed by 
			// <pre><code class="language-stdout">content</code></pre>
			var combinedRegex = MyRegex7();

			html = combinedRegex.Replace(html, (match) => {
				var exitCode = match.Groups[1].Value.Trim();
				var stdout = match.Groups[2].Value.Trim();
				return $@"<fieldset class=""expected-output""><legend>Expected Output</legend><pre>{stdout}{(string.IsNullOrEmpty(stdout) ? "" : "\n")}Exit Code: {exitCode}</pre></fieldset>";
			});

			// Handle standalone exitcode blocks (those without stdout)
			var exitCodeRegex = MyRegex3();
			html = exitCodeRegex.Replace(html, (match) => {
				var exitCode = match.Groups[1].Value.Trim();
				return $@"<fieldset class=""expected-output""><legend>Expected Output</legend><pre>Exit Code: {exitCode}</pre></fieldset>";
			});

			return html;
		}

		static string ProcessStdoutBlocks(string html) {
			// Handle standalone stdout blocks (those without exitcode)
			// Pattern: <pre><code class="language-stdout">content</code></pre>
			var regex = MyRegex4();

			return regex.Replace(html, (match) => {
				var content = match.Groups[1].Value.Trim();
				return $@"<fieldset class=""expected-output""><legend>Expected Output</legend><pre>{content}</pre></fieldset>";
			});
		}

		static string ProcessMaxoncStderrBlocks(string html) {
			// Find and replace maxoncstderr blocks with error styling
			// Pattern: <pre><code class="language-maxoncstderr">error content</code></pre>
			var regex = MyRegex5();

			return regex.Replace(html, (match) => {
				var content = match.Groups[1].Value.Trim();
				// Return with error-output styling
				return $@"<fieldset class=""error-output""><legend>Error Output</legend><pre>{content}</pre></fieldset>";
			});
		}

		static string GenerateTitle(string name) {
			// Convert hyphenated names to title case
			return string.Join(" ", name.Split('-').Select(word =>
				char.ToUpper(word[0]) + word[1..]));
		}

		static string GetCategoryDisplayName(string category) {
			// Convert category to display name
			var displayNames = new Dictionary<string, string> {
				{ "stdlib", "Standard Library" },
				{ "math-intrinsic", "Math Intrinsics" },
				{ "operators", "Operators" },
				{ "control-flow", "Control Flow" },
				{ "types", "Types" },
				{ "diagnostics", "Diagnostics" },
				{ "declaration", "Declarations" },
				{ "statements", "Statements" },
				{ "expressions", "Expressions" },
				{ "functions", "Functions" },
				{ "namespaces", "Namespaces" },
				{ "organization", "Organization" },
				{ "compilation", "Compilation" },
				{ "optimization", "Optimization" },
				{ "interop", "Interoperability" },
				{ "type-system", "Type System" },
				{ "literals", "Literals" },
				{ "uncategorized", "Uncategorized" }
			};

			return displayNames.TryGetValue(category, out var value)
				? value : GenerateTitle(category);
		}

		static string GenerateCategoryPage(string category, List<SpecInfo> specs, MarkdownPipeline pipeline) {
			var sb = new System.Text.StringBuilder();
			var categoryDisplayName = GetCategoryDisplayName(category);

			// HTML head with styling
			sb.AppendLine("<!DOCTYPE html>");
			sb.AppendLine("<html lang=\"en\">");
			sb.AppendLine("<head>");
			sb.AppendLine("    <meta charset=\"UTF-8\">");
			sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
			sb.AppendLine($"    <title>{categoryDisplayName} - Maxon Language Documentation</title>");
			sb.AppendLine("    <link rel=\"stylesheet\" href=\"style.css\">");
			sb.AppendLine("</head>");
			sb.AppendLine("<body>");
			sb.AppendLine("    <div class=\"container\">");

			// Sidebar navigation (loaded dynamically)
			sb.AppendLine($"        <nav class=\"sidebar\" id=\"sidebar\" data-active=\"{category}\"></nav>");

			// Main content
			sb.AppendLine("        <main class=\"content\">");

			// Header
			sb.AppendLine("            <div class=\"header\">");
			sb.AppendLine($"                <h1>{categoryDisplayName}</h1>");
			sb.AppendLine("                <div class=\"breadcrumb\">");
			sb.AppendLine($"                    <a href=\"index.html\">Home</a> / {categoryDisplayName}");
			sb.AppendLine("                </div>");
			sb.AppendLine("            </div>");

			// Table of contents
			sb.AppendLine("            <div class=\"toc\">");
			sb.AppendLine("                <h2>Contents</h2>");
			sb.AppendLine("                <ul>");
			foreach (var spec in specs) {
				sb.AppendLine($"                    <li><a href=\"#{spec.Name}\">{spec.Title}</a></li>");
			}
			sb.AppendLine("                </ul>");
			sb.AppendLine("            </div>");

			// Spec sections
			foreach (var spec in specs) {
				sb.AppendLine($"            <div class=\"spec-section\" id=\"{spec.Name}\">");
				sb.AppendLine($"                <h2>{spec.Title}</h2>");
				var html = Markdown.ToHtml(spec.DocumentationContent, pipeline);
				// Process output blocks
				// First handle error output blocks (MaxoncStderr)
				html = ProcessMaxoncStderrBlocks(html);
				// Then handle exitcode blocks (which may be combined with stdout)
				html = ProcessExitCodeBlocks(html);
				// Then handle standalone stdout blocks
				html = ProcessStdoutBlocks(html);
				// Finally handle legacy output blocks
				html = MyRegex2().Replace(html, @"<fieldset class=""expected-output""><legend>Expected Output</legend><pre>$1</pre></fieldset>");
				sb.AppendLine(html);
				sb.AppendLine("            </div>");
			}

			sb.AppendLine("        </main>");
			sb.AppendLine("    </div>");
			sb.AppendLine("    <script src=\"nav.js\"></script>");
			sb.AppendLine("</body>");
			sb.AppendLine("</html>");

			return sb.ToString();
		}

		static string GenerateIndexPage(Dictionary<string, List<SpecInfo>> specsByCategory) {
			var sb = new System.Text.StringBuilder();

			sb.AppendLine("<!DOCTYPE html>");
			sb.AppendLine("<html lang=\"en\">");
			sb.AppendLine("<head>");
			sb.AppendLine("    <meta charset=\"UTF-8\">");
			sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
			sb.AppendLine("    <title>Maxon Language Documentation</title>");
			sb.AppendLine("    <link rel=\"stylesheet\" href=\"style.css\">");
			sb.AppendLine("</head>");
			sb.AppendLine("<body>");
			sb.AppendLine("    <div class=\"container\">");

			// Header
			sb.AppendLine("        <div class=\"header\">");
			sb.AppendLine("            <h1>Maxon Language Documentation</h1>");
			sb.AppendLine("            <p>Comprehensive reference for the Maxon programming language</p>");
			sb.AppendLine("        </div>");

			// Category cards
			sb.AppendLine("        <div class=\"categories\">");
			foreach (var kvp in specsByCategory.OrderBy(x => x.Key)) {
				var category = kvp.Key;
				var specs = kvp.Value;
				var displayName = GetCategoryDisplayName(category);
				var specCount = specs.Count;
				var specWord = specCount == 1 ? "spec" : "specs";

				sb.AppendLine($"            <a href=\"{category}.html\" class=\"category-card\">");
				sb.AppendLine($"                <h2>{displayName}</h2>");
				sb.AppendLine($"                <p><span class=\"spec-count\">{specCount}</span> {specWord}</p>");
				sb.AppendLine("            </a>");
			}
			sb.AppendLine("        </div>");

			sb.AppendLine("    </div>");
			sb.AppendLine("</body>");
			sb.AppendLine("</html>");

			return sb.ToString();
		}

		[GeneratedRegex(@"category:\s*(.+)", RegexOptions.IgnoreCase, "en-US")]
		private static partial Regex MyRegex();
		[GeneratedRegex(@"## Documentation\s*\r?\n(.*?)(?=\r?\n## |\z)", RegexOptions.Singleline)]
		private static partial Regex MyRegex1();
		[GeneratedRegex(@"<pre><code class=\""language-output\"">(.*?)</code></pre>", RegexOptions.Singleline)]
		private static partial Regex MyRegex2();
		[GeneratedRegex(@"<pre><code class=""language-exitcode"">(.*?)</code></pre>", RegexOptions.Singleline)]
		private static partial Regex MyRegex3();
		[GeneratedRegex(@"<pre><code class=""language-stdout"">(.*?)</code></pre>", RegexOptions.Singleline)]
		private static partial Regex MyRegex4();
		[GeneratedRegex(@"<pre><code class=""language-maxoncstderr"">(.*?)</code></pre>", RegexOptions.Singleline)]
		private static partial Regex MyRegex5();
		[GeneratedRegex(@"```maxon\s*\r?\n", RegexOptions.Multiline)]
		private static partial Regex MyRegex6();
		[GeneratedRegex(@"<pre><code class=""language-exitcode"">(.*?)</code></pre>\s*<pre><code class=""language-stdout"">(.*?)</code></pre>", RegexOptions.Singleline)]
		private static partial Regex MyRegex7();
	}
}
