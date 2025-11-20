using Markdig;
using System.Diagnostics;

namespace DocGen {
	internal class Program {
		static void Main() {
			var inputPath = "Content";

		if (Directory.Exists("Output") == false) {
			Directory.CreateDirectory("Output");
		}

		foreach (FileInfo file in new DirectoryInfo("Output").EnumerateFiles()) {
			file.Delete();
		}		var _pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseBootstrap().Build();			foreach (var filename in Directory.EnumerateFiles(inputPath, "*.md")) {
				var document = File.ReadAllText(filename);
				var result = Markdown.ToHtml(document, _pipeline);
				File.WriteAllText("Output/" + Path.GetFileName(filename) + ".html", result);
			}
		}
	}
}
