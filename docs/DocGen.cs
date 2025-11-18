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

							var testName = Path.GetFileName(filename).Replace(".md", "") + "." + testCount;
							var testPath = Path.GetFullPath("../language-tests/doc-fragments/" + testName + ".test");
							
							// Write temporary fragment with N/A for IR
							File.WriteAllText(testPath, (codeBlock.Trim() + "\n---\nN/A\n---\n" + expectedResults).TrimEnd() + "\n");
							
							// Regenerate fragment with proper IR using create-test-fragment.ps1
							var scriptPath = Path.GetFullPath("../create-test-fragment.ps1");
							var psi = new ProcessStartInfo {
								FileName = "powershell.exe",
								Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -TestName \"{testName}\" -SourceFile \"{testPath}\"",
								WorkingDirectory = Path.GetFullPath(".."),
								UseShellExecute = false,
								CreateNoWindow = true,
								RedirectStandardOutput = true,
								RedirectStandardError = true
							};
							
							using (var process = Process.Start(psi)) {
								process?.WaitForExit();
							}
							
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
