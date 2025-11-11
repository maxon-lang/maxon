using Markdig;

namespace Docs {
	internal class Program {
		static void Main(string[] args) {
			var inputPath = "Content";

			if (Directory.Exists("Output") == false) {
				Directory.CreateDirectory("Output");
			}

		foreach (FileInfo file in new DirectoryInfo("Output").EnumerateFiles()) {
			file.Delete();
		}

		foreach (FileInfo file in new DirectoryInfo("../language-tests/doc-fragments").EnumerateFiles()) {
			file.Delete();
		}

		var _pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseBootstrap().Build();			foreach (var filename in Directory.EnumerateFiles(inputPath, "*.md")) {
				var document = File.ReadAllText(filename);
				var result = Markdown.ToHtml(document, _pipeline);
				File.WriteAllText("Output/" + Path.GetFileName(filename) + ".html", result);

				var file = File.OpenText(filename);
				var inCodeBlock = false;
				var codeBlock = "";
				var expectedResults = "";
				var testCount = 1;

				while (true) {
					var line = file.ReadLine();
					if (line == null) {
						break;
					}
					if (inCodeBlock) {
						if (line.StartsWith("~~~")) {
							inCodeBlock = false;

							File.WriteAllText("../language-tests/doc-fragments/" + Path.GetFileName(filename).Replace(".md", "") + "." + testCount + ".test", codeBlock.Trim() + "\n---\nN/A\n---\n" + expectedResults);
							testCount++;
							expectedResults = "";
							codeBlock = "";
							continue;
						}
						if (line.StartsWith("ExitCode: ")) {
							expectedResults += line + "\n";
						} else {
							codeBlock += line + "\n";
						}
					} else {
						if (line.StartsWith("~~~")) {
							inCodeBlock = true;
						}
					}
				}
			}
		}
	}
}
