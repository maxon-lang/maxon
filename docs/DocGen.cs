using Markdig;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DocGen {
	internal class SpecInfo {
		public string Name { get; set; } = "";
		public string Category { get; set; } = "uncategorized";
		public string DocumentationContent { get; set; } = "";
		public string Title { get; set; } = "";
	}

	internal class Program {
		static void Main() {
			var inputPath = "../specs";
			var docFragmentsPath = "../language-tests/doc-fragments";

			if (Directory.Exists("Output") == false) {
				Directory.CreateDirectory("Output");
			}

			foreach (FileInfo file in new DirectoryInfo("Output").EnumerateFiles()) {
				file.Delete();
			}

			// Create doc-fragments directory if it doesn't exist
			if (Directory.Exists(docFragmentsPath) == false) {
				Directory.CreateDirectory(docFragmentsPath);
			}

			// Clear existing doc-fragments
			foreach (FileInfo file in new DirectoryInfo(docFragmentsPath).EnumerateFiles("*.test")) {
				file.Delete();
			}

			var pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseBootstrap().Build();

			// Collect all specs and group by category
			var specsByCategory = new Dictionary<string, List<SpecInfo>>();
			int totalExamplesExtracted = 0;

			foreach (var filename in Directory.EnumerateFiles(inputPath, "*.md")) {
				var basename = Path.GetFileNameWithoutExtension(filename);
				
				// Skip README and status files
				if (basename.Equals("README", StringComparison.OrdinalIgnoreCase) ||
				    basename.Contains("CLEANUP") || basename.Contains("IMPLEMENTATION")) {
					continue;
				}

				var content = File.ReadAllText(filename);
				
				var spec = new SpecInfo {
					Name = basename,
					Category = ExtractCategory(content),
					DocumentationContent = ExtractDocumentationSection(content),
					Title = GenerateTitle(basename)
				};

				if (!string.IsNullOrWhiteSpace(spec.DocumentationContent)) {
					if (!specsByCategory.ContainsKey(spec.Category)) {
						specsByCategory[spec.Category] = new List<SpecInfo>();
					}
					specsByCategory[spec.Category].Add(spec);

					// Extract code examples from documentation
					int examplesExtracted = ExtractCodeExamples(spec.DocumentationContent, basename, docFragmentsPath);
					totalExamplesExtracted += examplesExtracted;
				}
			}

			// Sort specs within each category by name
			foreach (var category in specsByCategory.Keys) {
				specsByCategory[category].Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.Ordinal));
			}

			// Generate category pages
			foreach (var kvp in specsByCategory.OrderBy(x => x.Key)) {
				var category = kvp.Key;
				var specs = kvp.Value;
				
				var htmlContent = GenerateCategoryPage(category, specs, specsByCategory.Keys.OrderBy(x => x).ToList(), pipeline);
				var filename = $"Output/{category}.html";
				File.WriteAllText(filename, htmlContent);
				Console.WriteLine($"Generated {category}.html ({specs.Count} specs)");
			}

			// Generate index page
			var indexContent = GenerateIndexPage(specsByCategory);
			File.WriteAllText("Output/index.html", indexContent);
			Console.WriteLine("Generated index.html");

			if (totalExamplesExtracted > 0) {
				Console.WriteLine($"Extracted {totalExamplesExtracted} code examples to doc-fragments/");
			}
		}

		static string ExtractCategory(string content) {
			// Extract category from YAML frontmatter
			var categoryMatch = Regex.Match(content, @"category:\s*(.+)", RegexOptions.IgnoreCase);
			if (categoryMatch.Success) {
				return categoryMatch.Groups[1].Value.Trim();
			}
			return "uncategorized";
		}

		static string ExtractDocumentationSection(string content) {
			// Find the ## Documentation section only, stop at any following ## heading
			var docMatch = Regex.Match(content, @"## Documentation\s*\r?\n(.*?)(?=\r?\n## |\z)", RegexOptions.Singleline);
			
			if (docMatch.Success) {
				return docMatch.Groups[1].Value.Trim();
			}
			
			return string.Empty;
		}

		static int ExtractCodeExamples(string documentationContent, string specName, string outputPath) {
			// Find Example/Examples sections and extract maxon code blocks from them
			var exampleSections = Regex.Matches(documentationContent, @"###\s+Examples?\s*\r?\n(.*?)(?=\r?\n### |\z)", RegexOptions.Singleline | RegexOptions.IgnoreCase);
			
			int exampleNumber = 1;
			foreach (Match sectionMatch in exampleSections) {
				var sectionContent = sectionMatch.Groups[1].Value;
				
				// Find all maxon code blocks within this section
				var codeBlocks = Regex.Matches(sectionContent, @"```maxon\s*\r?\n(.*?)```", RegexOptions.Singleline);
				
				foreach (Match codeMatch in codeBlocks) {
					var code = codeMatch.Groups[1].Value.Trim();
					
					// Skip empty code blocks
					if (string.IsNullOrWhiteSpace(code)) {
						continue;
					}

					// Skip code blocks that don't look like complete programs (no function definition)
					if (!code.Contains("function ")) {
						continue;
					}

					// Create the .test file format
					var testContent = code + "\n\n---\n; IR will be generated by maxon regen-fragments\n---\n; Debug IR will be generated by maxon regen-fragments\n";
					
					var filename = Path.Combine(outputPath, $"{specName}.{exampleNumber}.test");
					File.WriteAllText(filename, testContent);
					exampleNumber++;
				}
			}

			return exampleNumber - 1;
		}

		static string GenerateTitle(string name) {
			// Convert hyphenated names to title case
			return string.Join(" ", name.Split('-').Select(word => 
				char.ToUpper(word[0]) + word.Substring(1)));
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

			return displayNames.ContainsKey(category) 
				? displayNames[category] 
				: GenerateTitle(category);
		}

		static string GenerateCategoryPage(string category, List<SpecInfo> specs, List<string> allCategories, MarkdownPipeline pipeline) {
			var sb = new System.Text.StringBuilder();
			var categoryDisplayName = GetCategoryDisplayName(category);

			// HTML head with styling
			sb.AppendLine("<!DOCTYPE html>");
			sb.AppendLine("<html lang=\"en\">");
			sb.AppendLine("<head>");
			sb.AppendLine("    <meta charset=\"UTF-8\">");
			sb.AppendLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
			sb.AppendLine($"    <title>{categoryDisplayName} - Maxon Language Documentation</title>");
			sb.AppendLine("    <style>");
			sb.AppendLine(@"        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif;
            line-height: 1.6;
            margin: 0;
            padding: 0;
            background-color: #f5f5f5;
        }
        .container {
            display: flex;
            min-height: 100vh;
        }
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
        .content {
            margin-left: 250px;
            padding: 40px;
            flex: 1;
            max-width: 900px;
        }
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
    ");
			sb.AppendLine("    </style>");
			sb.AppendLine("</head>");
			sb.AppendLine("<body>");
			sb.AppendLine("    <div class=\"container\">");
			
			// Sidebar navigation
			sb.AppendLine("        <nav class=\"sidebar\">");
			sb.AppendLine("            <h2>Maxon Docs</h2>");
			sb.AppendLine("            <ul>");
			sb.AppendLine("                <li><a href=\"index.html\">← Home</a></li>");
			foreach (var cat in allCategories) {
				var activeClass = cat == category ? " class=\"active\"" : "";
				sb.AppendLine($"                <li><a href=\"{cat}.html\"{activeClass}>{GetCategoryDisplayName(cat)}</a></li>");
			}
			sb.AppendLine("            </ul>");
			sb.AppendLine("        </nav>");
			
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
				sb.AppendLine(html);
				sb.AppendLine("            </div>");
			}
			
			sb.AppendLine("        </main>");
			sb.AppendLine("    </div>");
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
			sb.AppendLine("    <style>");
			sb.AppendLine(@"        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif;
            line-height: 1.6;
            margin: 0;
            padding: 0;
            background-color: #f5f5f5;
        }
        .container {
            max-width: 1000px;
            margin: 0 auto;
            padding: 40px 20px;
        }
        .header {
            background-color: white;
            padding: 40px;
            margin-bottom: 40px;
            border-radius: 8px;
            box-shadow: 0 2px 4px rgba(0,0,0,0.1);
            text-align: center;
        }
        .header h1 {
            margin: 0 0 10px 0;
            color: #2c3e50;
            font-size: 2.5em;
        }
        .header p {
            color: #7f8c8d;
            font-size: 1.1em;
        }
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
    ");
			sb.AppendLine("    </style>");
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
	}
}
