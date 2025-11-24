#pragma once

#include <map>
#include <string>
#include <vector>

// Generate HTML documentation from spec files in specs/ directory
// This replaces the C# DocGen tool with native C++ implementation
class DocsGenerator {
  public:
	// Main entry point - generates all documentation
	static int generateDocumentation();

  private:
	struct SpecInfo {
		std::string name;
		std::string category;
		std::string documentationContent;
		std::string title;
	};

	// Scan specs directory and collect all specs
	static std::vector<SpecInfo> collectSpecs(const std::string &specsPath);

	// Parse a single spec file
	static SpecInfo parseSpec(const std::string &filePath);

	// Extract category from YAML frontmatter
	static std::string extractCategory(const std::string &content);

	// Extract Documentation section (between ## Documentation and next ##)
	static std::string extractDocumentationSection(const std::string &content);

	// Validate that maxon code blocks have output blocks
	static bool validateDocumentationExamples(const std::string &content,
											  const std::string &basename,
											  std::vector<std::string> &errors);

	// Convert markdown to HTML using md4c library
	static std::string markdownToHtml(const std::string &markdown);

	// Post-process HTML to handle output blocks
	static std::string processExitCodeBlocks(const std::string &html);
	static std::string processStdoutBlocks(const std::string &html);
	static std::string processMaxoncStderrBlocks(const std::string &html);

	// Generate HTML pages
	static std::string generateIndexPage(const std::map<std::string, std::vector<SpecInfo>> &specsByCategory);
	static std::string generateCategoryPage(const std::string &category,
											const std::vector<SpecInfo> &specs);

	// Generate static files
	static void generateStylesheet(const std::string &outputDir);
	static void generateNavigationScript(const std::string &outputDir,
										 const std::map<std::string, std::vector<SpecInfo>> &specsByCategory);

	// Utility functions
	static std::string generateTitle(const std::string &name);
	static std::string getCategoryDisplayName(const std::string &category);
	static bool createDirectoryIfNeeded(const std::string &path);
};
