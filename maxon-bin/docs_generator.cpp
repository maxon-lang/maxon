#include "docs_generator.h"
#include "file_utils.h"
#include "logger.h"
#include <algorithm>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <md4c/md4c-html.h>
#include <regex>
#include <sstream>

namespace fs = std::filesystem;

// Read entire file content (unlike readFile from file_utils which stops at ---)
static std::string readEntireFile(const std::string &filepath) {
	std::ifstream file(filepath);
	if (!file) {
		GlobalLogger::instance().error(LogPhase::Docs, "Could not open file: ", filepath);
		return "";
	}

	std::stringstream buffer;
	buffer << file.rdbuf();
	return buffer.str();
}

// Static CSS content (copied from C# DocGen)
static const char *STYLESHEET_CSS = R"(/* Maxon Language Documentation Styles */

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
)";

// Callback for md4c to collect HTML output
static void md4c_output_callback(const MD_CHAR *text, MD_SIZE size, void *userdata) {
	std::string *output = static_cast<std::string *>(userdata);
	output->append(text, size);
}

std::string DocsGenerator::markdownToHtml(const std::string &markdown) {
	std::string html;

	// Use md4c to convert markdown to HTML
	int result = md_html(
		markdown.c_str(),
		markdown.size(),
		md4c_output_callback,
		&html,
		MD_DIALECT_GITHUB, // GitHub Flavored Markdown
		0				   // No additional HTML flags
	);

	if (result != 0) {
		GlobalLogger::instance().error(LogPhase::Docs, "Error converting markdown to HTML");
		return "";
	}

	return html;
}

std::string DocsGenerator::extractCategory(const std::string &content) {
	// Extract category from YAML frontmatter
	std::regex categoryRegex(R"(category:\s*(.+))", std::regex::icase);
	std::smatch match;

	if (std::regex_search(content, match, categoryRegex)) {
		std::string category = match[1].str();
		// Trim whitespace
		category.erase(category.find_last_not_of(" \t\r\n") + 1);
		return category;
	}

	return "uncategorized";
}

std::string DocsGenerator::extractDocumentationSection(const std::string &content) {
	// Find ## Documentation
	size_t docPos = content.find("## Documentation");
	if (docPos == std::string::npos) {
		return "";
	}

	// Move past the heading
	size_t start = content.find('\n', docPos);
	if (start == std::string::npos) {
		return "";
	}
	start++; // Skip the newline

	// Find the next ## heading (or end of file)
	size_t end = content.find("\n## ", start);
	if (end == std::string::npos) {
		// No next section, take to end of file
		end = content.size();
	}

	std::string section = content.substr(start, end - start);

	// Trim whitespace
	size_t trimStart = section.find_first_not_of(" \t\r\n");
	size_t trimEnd = section.find_last_not_of(" \t\r\n");
	if (trimStart != std::string::npos && trimEnd != std::string::npos) {
		return section.substr(trimStart, trimEnd - trimStart + 1);
	}

	return section;
}

bool DocsGenerator::validateDocumentationExamples(const std::string &content,
												  const std::string &basename,
												  std::vector<std::string> &errors) {
	// Find ## Documentation
	size_t docPos = content.find("## Documentation");
	if (docPos == std::string::npos) {
		return true; // No Documentation section
	}

	// Find the end of Documentation section (next ## or end of file)
	size_t start = docPos;
	size_t end = content.find("\n## ", start + 1);
	if (end == std::string::npos) {
		end = content.size();
	}

	std::string docSection = content.substr(start, end - start);

	// Find line number where Documentation section starts
	int docSectionLine = 1 + std::count(content.begin(), content.begin() + docPos, '\n');

	// Find all ```maxon blocks in the Documentation section
	std::regex maxonBlockRegex(R"(```maxon\s*\r?\n)");
	std::sregex_iterator iter(docSection.begin(), docSection.end(), maxonBlockRegex);
	std::sregex_iterator iterEnd;

	for (; iter != iterEnd; ++iter) {
		size_t blockStart = iter->position() + iter->length();

		// Find the closing ``` for this maxon block
		size_t blockEnd = docSection.find("```", blockStart);
		if (blockEnd == std::string::npos)
			continue;

		// Extract the code block content
		std::string codeBlock = docSection.substr(blockStart, blockEnd - blockStart);

		// Only validate blocks that contain "function main()" - these should be executable examples
		if (codeBlock.find("function main()") == std::string::npos) {
			continue; // Skip syntax examples and snippets
		}

		// Check if there's a ```exitcode or ```maxoncstderr block immediately after
		size_t nextBlockStart = blockEnd + 3;
		if (nextBlockStart >= docSection.size()) {
			// Calculate approximate line number
			int lineNum = docSectionLine + std::count(docSection.begin(),
													  docSection.begin() + iter->position(), '\n');
			errors.push_back(basename + ".md (line ~" + std::to_string(lineNum) +
							 "): maxon code block without ```exitcode or ```maxoncstderr block");
			continue;
		}

		// Skip whitespace after closing ```
		while (nextBlockStart < docSection.size() &&
			   (docSection[nextBlockStart] == ' ' || docSection[nextBlockStart] == '\t' ||
				docSection[nextBlockStart] == '\r' || docSection[nextBlockStart] == '\n')) {
			nextBlockStart++;
		}

		// Check if next block is ```exitcode or ```maxoncstderr
		if (nextBlockStart >= docSection.size() ||
			(docSection.substr(nextBlockStart, 11) != "```exitcode" &&
			 docSection.substr(nextBlockStart, 15) != "```maxoncstderr")) {
			// Calculate approximate line number
			int lineNum = docSectionLine + std::count(docSection.begin(),
													  docSection.begin() + iter->position(), '\n');
			errors.push_back(basename + ".md (line ~" + std::to_string(lineNum) +
							 "): maxon code block without ```exitcode or ```maxoncstderr block");
		}
	}

	return errors.empty();
}

std::string DocsGenerator::processExitCodeBlocks(const std::string &html) {
	std::string result = html;

	// Find and combine exitcode and stdout blocks into a single expected output block
	// Pattern: <pre><code class="language-exitcode">exitCode</code></pre> followed by
	// <pre><code class="language-stdout">content</code></pre>
	std::regex combinedRegex(
		R"(<pre><code class="language-exitcode">([\s\S]*?)</code></pre>\s*<pre><code class="language-stdout">([\s\S]*?)</code></pre>)");

	// Manually iterate and replace
	std::string output;
	std::sregex_iterator iter(result.begin(), result.end(), combinedRegex);
	std::sregex_iterator end;
	size_t lastPos = 0;

	for (; iter != end; ++iter) {
		// Append text before match
		output.append(result, lastPos, iter->position() - lastPos);

		// Process the match
		std::string exitCode = (*iter)[1].str();
		std::string stdoutStr = (*iter)[2].str();
		// Trim
		exitCode.erase(exitCode.find_last_not_of(" \t\r\n") + 1);
		stdoutStr.erase(stdoutStr.find_last_not_of(" \t\r\n") + 1);

		output += "<fieldset class=\"expected-output\"><legend>Expected Output</legend><pre>";
		output += stdoutStr;
		if (!stdoutStr.empty())
			output += "\n";
		output += "Exit Code: " + exitCode + "</pre></fieldset>";

		lastPos = iter->position() + iter->length();
	}

	// Append remaining text
	output.append(result, lastPos, result.size() - lastPos);
	result = output;

	// Handle standalone exitcode blocks (those without stdout)
	std::regex exitCodeRegex(R"(<pre><code class="language-exitcode">([\s\S]*?)</code></pre>)");

	output.clear();
	iter = std::sregex_iterator(result.begin(), result.end(), exitCodeRegex);
	lastPos = 0;

	for (; iter != end; ++iter) {
		// Append text before match
		output.append(result, lastPos, iter->position() - lastPos);

		// Process the match
		std::string exitCode = (*iter)[1].str();
		exitCode.erase(exitCode.find_last_not_of(" \t\r\n") + 1);
		output += "<fieldset class=\"expected-output\"><legend>Expected Output</legend><pre>Exit Code: " + exitCode + "</pre></fieldset>";

		lastPos = iter->position() + iter->length();
	}

	// Append remaining text
	output.append(result, lastPos, result.size() - lastPos);

	return output;
}

std::string DocsGenerator::processStdoutBlocks(const std::string &html) {
	// Handle standalone stdout blocks (those without exitcode)
	std::regex stdoutRegex(R"(<pre><code class="language-stdout">([\s\S]*?)</code></pre>)");

	std::string output;
	std::sregex_iterator iter(html.begin(), html.end(), stdoutRegex);
	std::sregex_iterator end;
	size_t lastPos = 0;

	for (; iter != end; ++iter) {
		// Append text before match
		output.append(html, lastPos, iter->position() - lastPos);

		// Process the match
		std::string content = (*iter)[1].str();
		content.erase(content.find_last_not_of(" \t\r\n") + 1);
		output += "<fieldset class=\"expected-output\"><legend>Expected Output</legend><pre>" + content + "</pre></fieldset>";

		lastPos = iter->position() + iter->length();
	}

	// Append remaining text
	output.append(html, lastPos, html.size() - lastPos);

	return output;
}

std::string DocsGenerator::processMaxoncStderrBlocks(const std::string &html) {
	// Find and replace maxoncstderr blocks with error styling
	std::regex stderrRegex(R"(<pre><code class="language-maxoncstderr">([\s\S]*?)</code></pre>)");

	std::string output;
	std::sregex_iterator iter(html.begin(), html.end(), stderrRegex);
	std::sregex_iterator end;
	size_t lastPos = 0;

	for (; iter != end; ++iter) {
		// Append text before match
		output.append(html, lastPos, iter->position() - lastPos);

		// Process the match
		std::string content = (*iter)[1].str();
		content.erase(content.find_last_not_of(" \t\r\n") + 1);
		output += "<fieldset class=\"error-output\"><legend>Error Output</legend><pre>" + content + "</pre></fieldset>";

		lastPos = iter->position() + iter->length();
	}

	// Append remaining text
	output.append(html, lastPos, html.size() - lastPos);

	return output;
}

std::string DocsGenerator::generateTitle(const std::string &name) {
	// Convert hyphenated names to title case
	std::string title;
	bool capitalize = true;

	for (char c : name) {
		if (c == '-') {
			title += ' ';
			capitalize = true;
		} else if (capitalize) {
			title += std::toupper(c);
			capitalize = false;
		} else {
			title += c;
		}
	}

	return title;
}

std::string DocsGenerator::getCategoryDisplayName(const std::string &category) {
	static const std::map<std::string, std::string> displayNames = {
		{"stdlib", "Standard Library"},
		{"math-intrinsic", "Math Intrinsics"},
		{"operators", "Operators"},
		{"control-flow", "Control Flow"},
		{"types", "Types"},
		{"diagnostics", "Diagnostics"},
		{"declaration", "Declarations"},
		{"statements", "Statements"},
		{"expressions", "Expressions"},
		{"functions", "Functions"},
		{"namespaces", "Namespaces"},
		{"organization", "Organization"},
		{"compilation", "Compilation"},
		{"optimization", "Optimization"},
		{"interop", "Interoperability"},
		{"type-system", "Type System"},
		{"literals", "Literals"},
		{"uncategorized", "Uncategorized"}};

	auto it = displayNames.find(category);
	if (it != displayNames.end()) {
		return it->second;
	}

	return generateTitle(category);
}

DocsGenerator::SpecInfo DocsGenerator::parseSpec(const std::string &filePath) {
	SpecInfo spec;

	// Read file content
	std::string content = readEntireFile(filePath);

	// Extract basename
	fs::path path(filePath);
	spec.name = path.stem().string();
	spec.title = generateTitle(spec.name);

	// Extract category and documentation
	spec.category = extractCategory(content);
	spec.documentationContent = extractDocumentationSection(content);

	return spec;
}

std::vector<DocsGenerator::SpecInfo> DocsGenerator::collectSpecs(const std::string &specsPath) {
	std::vector<SpecInfo> specs;
	std::vector<std::string> validationErrors;

	// Iterate through all .md files in specs directory
	for (const auto &entry : fs::directory_iterator(specsPath)) {
		if (!entry.is_regular_file())
			continue;

		std::string filename = entry.path().filename().string();
		std::string basename = entry.path().stem().string();

		// Skip README and status files
		if (basename == "README" ||
			filename.find("CLEANUP") != std::string::npos ||
			filename.find("IMPLEMENTATION") != std::string::npos) {
			continue;
		}

		if (entry.path().extension() != ".md")
			continue;

		// Read and validate the spec
		std::string content = readEntireFile(entry.path().string());
		validateDocumentationExamples(content, basename, validationErrors);

		// Parse the spec
		SpecInfo spec = parseSpec(entry.path().string());

		// Only add specs with documentation content
		if (!spec.documentationContent.empty()) {
			specs.push_back(spec);
		}
	}

	// Report validation errors
	if (!validationErrors.empty()) {
		Logger &logger = GlobalLogger::instance();
		logger.error(LogPhase::Docs, validationErrors.size(), " validation error(s) found:");
		for (const auto &error : validationErrors) {
			logger.error(LogPhase::Docs, "  ", error);
		}
		logger.error(LogPhase::Docs, "All maxon code blocks in Documentation sections must have corresponding ```output blocks.");
		std::exit(1); // Exit immediately like the C# version did
	}

	return specs;
}

bool DocsGenerator::createDirectoryIfNeeded(const std::string &path) {
	try {
		if (!fs::exists(path)) {
			fs::create_directories(path);
		}
		return true;
	} catch (const fs::filesystem_error &e) {
		GlobalLogger::instance().error(LogPhase::Docs, "Error creating directory ", path, ": ", e.what());
		return false;
	}
}

void DocsGenerator::generateStylesheet(const std::string &outputDir) {
	std::string path = outputDir + "/style.css";
	std::ofstream file(path);
	if (!file) {
		GlobalLogger::instance().error(LogPhase::Docs, "Error creating style.css");
		return;
	}

	file << STYLESHEET_CSS;
	file.close();
}

void DocsGenerator::generateNavigationScript(const std::string &outputDir,
											 const std::map<std::string, std::vector<SpecInfo>> &specsByCategory) {
	std::string path = outputDir + "/nav.js";
	std::ofstream file(path);
	if (!file) {
		GlobalLogger::instance().error(LogPhase::Docs, "Error creating nav.js");
		return;
	}

	file << "// Navigation data and dynamic sidebar generation\n";
	file << "const categories = {\n";

	// Get sorted category list
	std::vector<std::string> categories;
	for (const auto &kv : specsByCategory) {
		categories.push_back(kv.first);
	}
	std::sort(categories.begin(), categories.end());

	for (size_t i = 0; i < categories.size(); i++) {
		const std::string &cat = categories[i];
		std::string displayName = getCategoryDisplayName(cat);
		file << "    '" << cat << "': '" << displayName << "'";
		if (i < categories.size() - 1) {
			file << ",";
		}
		file << "\n";
	}

	file << "};\n\n";
	file << "// Build and insert sidebar navigation\n";
	file << "document.addEventListener('DOMContentLoaded', function() {\n";
	file << "    const sidebar = document.getElementById('sidebar');\n";
	file << "    if (!sidebar) return;\n\n";
	file << "    const activeCategory = sidebar.dataset.active;\n\n";
	file << "    let html = '<h2>Maxon Docs</h2><ul>';\n";
	file << "    html += '<li><a href=\"index.html\">← Home</a></li>';\n\n";
	file << "    for (const [key, displayName] of Object.entries(categories)) {\n";
	file << "        const activeClass = key === activeCategory ? ' class=\"active\"' : '';\n";
	file << "        html += `<li><a href=\"${key}.html\"${activeClass}>${displayName}</a></li>`;\n";
	file << "    }\n\n";
	file << "    html += '</ul>';\n";
	file << "    sidebar.innerHTML = html;\n";
	file << "});\n";

	file.close();

	// std::cout << "Generated nav.js" << std::endl;
}

std::string DocsGenerator::generateIndexPage(const std::map<std::string, std::vector<SpecInfo>> &specsByCategory) {
	std::ostringstream html;

	html << "<!DOCTYPE html>\n";
	html << "<html lang=\"en\">\n";
	html << "<head>\n";
	html << "    <meta charset=\"UTF-8\">\n";
	html << "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n";
	html << "    <title>Maxon Language Documentation</title>\n";
	html << "    <link rel=\"stylesheet\" href=\"style.css\">\n";
	html << "</head>\n";
	html << "<body>\n";
	html << "    <div class=\"container\">\n";

	// Header
	html << "        <div class=\"header\">\n";
	html << "            <h1>Maxon Language Documentation</h1>\n";
	html << "            <p>Comprehensive reference for the Maxon programming language</p>\n";
	html << "        </div>\n";

	// Category cards
	html << "        <div class=\"categories\">\n";

	// Get sorted categories
	std::vector<std::pair<std::string, std::vector<SpecInfo>>> sortedCategories(
		specsByCategory.begin(), specsByCategory.end());
	std::sort(sortedCategories.begin(), sortedCategories.end(),
			  [](const auto &a, const auto &b) { return a.first < b.first; });

	for (const auto &kv : sortedCategories) {
		const std::string &category = kv.first;
		const std::vector<SpecInfo> &specs = kv.second;
		std::string displayName = getCategoryDisplayName(category);
		int specCount = specs.size();
		std::string specWord = (specCount == 1) ? "spec" : "specs";

		html << "            <a href=\"" << category << ".html\" class=\"category-card\">\n";
		html << "                <h2>" << displayName << "</h2>\n";
		html << "                <p><span class=\"spec-count\">" << specCount << "</span> " << specWord << "</p>\n";
		html << "            </a>\n";
	}

	html << "        </div>\n";
	html << "    </div>\n";
	html << "</body>\n";
	html << "</html>\n";

	return html.str();
}

std::string DocsGenerator::generateCategoryPage(const std::string &category,
												const std::vector<SpecInfo> &specs) {
	std::ostringstream html;
	std::string categoryDisplayName = getCategoryDisplayName(category);

	// HTML head with styling
	html << "<!DOCTYPE html>\n";
	html << "<html lang=\"en\">\n";
	html << "<head>\n";
	html << "    <meta charset=\"UTF-8\">\n";
	html << "    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n";
	html << "    <title>" << categoryDisplayName << " - Maxon Language Documentation</title>\n";
	html << "    <link rel=\"stylesheet\" href=\"style.css\">\n";
	html << "</head>\n";
	html << "<body>\n";
	html << "    <div class=\"container\">\n";

	// Sidebar navigation (loaded dynamically)
	html << "        <nav class=\"sidebar\" id=\"sidebar\" data-active=\"" << category << "\"></nav>\n";

	// Main content
	html << "        <main class=\"content\">\n";

	// Header
	html << "            <div class=\"header\">\n";
	html << "                <h1>" << categoryDisplayName << "</h1>\n";
	html << "                <div class=\"breadcrumb\">\n";
	html << "                    <a href=\"index.html\">Home</a> / " << categoryDisplayName << "\n";
	html << "                </div>\n";
	html << "            </div>\n";

	// Table of contents
	html << "            <div class=\"toc\">\n";
	html << "                <h2>Contents</h2>\n";
	html << "                <ul>\n";
	for (const auto &spec : specs) {
		html << "                    <li><a href=\"#" << spec.name << "\">" << spec.title << "</a></li>\n";
	}
	html << "                </ul>\n";
	html << "            </div>\n";

	// Spec sections
	for (const auto &spec : specs) {
		html << "            <div class=\"spec-section\" id=\"" << spec.name << "\">\n";
		html << "                <h2>" << spec.title << "</h2>\n";

		// Convert markdown to HTML
		std::string specHtml = markdownToHtml(spec.documentationContent);

		// Process output blocks
		// First handle error output blocks (MaxoncStderr)
		specHtml = processMaxoncStderrBlocks(specHtml);
		// Then handle exitcode blocks (which may be combined with stdout)
		specHtml = processExitCodeBlocks(specHtml);
		// Then handle standalone stdout blocks
		specHtml = processStdoutBlocks(specHtml);
		// Finally handle legacy output blocks
		std::regex outputRegex(R"(<pre><code class="language-output">([\s\S]*?)</code></pre>)");

		std::string finalHtml;
		std::sregex_iterator iter(specHtml.begin(), specHtml.end(), outputRegex);
		std::sregex_iterator end;
		size_t lastPos = 0;

		for (; iter != end; ++iter) {
			finalHtml.append(specHtml, lastPos, iter->position() - lastPos);
			std::string content = (*iter)[1].str();
			finalHtml += "<fieldset class=\"expected-output\"><legend>Expected Output</legend><pre>" + content + "</pre></fieldset>";
			lastPos = iter->position() + iter->length();
		}

		finalHtml.append(specHtml, lastPos, specHtml.size() - lastPos);

		html << finalHtml;
		html << "            </div>\n";
	}

	html << "        </main>\n";
	html << "    </div>\n";
	html << "    <script src=\"nav.js\"></script>\n";
	html << "</body>\n";
	html << "</html>\n";

	return html.str();
}

int DocsGenerator::generateDocumentation() {
	Logger &logger = GlobalLogger::instance();
	logger.progress(LogPhase::Docs, "Generating documentation from specs...");

	std::string specsPath = "specs";
	std::string outputDir = "maxon-docs/Output";

	// Create output directory if needed
	if (!createDirectoryIfNeeded(outputDir)) {
		return 1;
	}

	// Clean output directory (except CSS and JS)
	try {
		for (const auto &entry : fs::directory_iterator(outputDir)) {
			std::string filename = entry.path().filename().string();
			if (filename != "style.css" && filename != "nav.js") {
				fs::remove(entry.path());
			}
		}
	} catch (const fs::filesystem_error &e) {
		logger.error(LogPhase::Docs, "Error cleaning output directory: ", e.what());
	}

	// Generate CSS file
	generateStylesheet(outputDir);

	// Collect all specs (exits with code 1 if validation fails)
	std::vector<SpecInfo> allSpecs = collectSpecs(specsPath);

	// Group by category
	std::map<std::string, std::vector<SpecInfo>> specsByCategory;
	for (const auto &spec : allSpecs) {
		specsByCategory[spec.category].push_back(spec);
	}

	// Sort specs within each category by title
	for (auto &kv : specsByCategory) {
		std::sort(kv.second.begin(), kv.second.end(),
				  [](const SpecInfo &a, const SpecInfo &b) {
					  return a.title < b.title;
				  });
	}

	// Generate navigation JavaScript
	generateNavigationScript(outputDir, specsByCategory);

	// Generate category pages
	for (const auto &kv : specsByCategory) {
		const std::string &category = kv.first;
		const std::vector<SpecInfo> &specs = kv.second;

		std::string html = generateCategoryPage(category, specs);
		std::string filename = outputDir + "/" + category + ".html";

		std::ofstream file(filename);
		if (!file) {
			logger.error(LogPhase::Docs, "Error creating ", category, ".html");
			continue;
		}

		file << html;
		file.close();
	}

	// Generate index page
	std::string indexHtml = generateIndexPage(specsByCategory);
	std::string indexPath = outputDir + "/index.html";

	std::ofstream indexFile(indexPath);
	if (!indexFile) {
		logger.error(LogPhase::Docs, "Error creating index.html");
		return 1;
	}

	indexFile << indexHtml;
	indexFile.close();

	return 0;
}
