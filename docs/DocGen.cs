using Markdig;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace DocGen {
	internal class Program {
		static void Main() {
			// Read from specs directory instead of Content
			var inputPath = "../specs";

			if (Directory.Exists("Output") == false) {
				Directory.CreateDirectory("Output");
			}

			foreach (FileInfo file in new DirectoryInfo("Output").EnumerateFiles()) {
				file.Delete();
			}

			var _pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseBootstrap().Build();

			foreach (var filename in Directory.EnumerateFiles(inputPath, "*.md")) {
				var basename = Path.GetFileNameWithoutExtension(filename);
				
				// Skip README files
				if (basename.Equals("README", StringComparison.OrdinalIgnoreCase)) {
					continue;
				}

				var content = File.ReadAllText(filename);
				
				// Extract only the Documentation section
				var docSection = ExtractDocumentationSection(content);
				
				if (!string.IsNullOrWhiteSpace(docSection)) {
					var result = Markdown.ToHtml(docSection, _pipeline);
					File.WriteAllText("Output/" + basename + ".md.html", result);
					Console.WriteLine($"Generated {basename}.md.html");
				}
			}
		}

		static string ExtractDocumentationSection(string content) {
			// Find the ## Documentation section
			var docMatch = Regex.Match(content, @"## Documentation\s*\r?\n(.*?)(?=\r?\n## |\z)", RegexOptions.Singleline);
			
			if (docMatch.Success) {
				return docMatch.Groups[1].Value.Trim();
			}
			
			return string.Empty;
		}
	}
}
