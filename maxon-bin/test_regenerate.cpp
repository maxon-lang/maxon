#include "test_regenerate.h"
#include "compiler.h"
#include "test_utils.h"

#include <algorithm>
#include <chrono>
#include <cstddef>
#include <cstdlib>
#include <cstring>
#include <ctime>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <iterator>
#include <map>
#include <mutex>
#include <regex>
#include <set>
#include <sstream>
#include <streambuf>
#include <string>
#include <thread>
#include <vector>

#include <sstream>

#ifdef _WIN32
#include <windows.h>
#else
#include <sys/wait.h>
#include <unistd.h>
#endif

// Structure to hold spec manifest data
struct SpecManifest {
	struct FragmentEntry {
		std::string specFile;
		std::string testName;
		int index;
	};
	std::map<std::string, FragmentEntry> fragments; // key: fragment filename
};

// Helper function to write a test fragment file
static void writeTestFragment(const std::string &outputDir, const std::string &fragmentName,
							  const std::string &code, const std::string &metadata,
							  SpecManifest &manifest, const std::string &specBaseName,
							  const std::string &testName, int index, int &totalExtracted,
							  bool isDocExample = false) {
	std::string testFileName = outputDir + "/" + fragmentName + ".test";

	std::ofstream testFile(testFileName);
	testFile << "// Test: " << fragmentName << "\n";
	testFile << code;
	testFile << "---\nN/A\n---\nN/A\n---\n";
	if (!metadata.empty()) {
		if (isDocExample) {
			// For doc examples, parse metadata and add fences for multiline values
			std::istringstream metadataStream(metadata);
			std::string line;
			std::string currentField;
			std::vector<std::string> currentLines;

			auto writeField = [&testFile](const std::string &field, std::vector<std::string> &lines) {
				// Remove trailing empty lines
				while (!lines.empty() && lines.back().empty()) {
					lines.pop_back();
				}

				if (lines.size() > 1) {
					// Multiline - needs fences
					testFile << field << " ```\n";
					for (const auto &l : lines) {
						testFile << l << "\n";
					}
					testFile << "```\n";
				} else if (lines.size() == 1) {
					// Single line
					testFile << field << " " << lines[0] << "\n";
				} else {
					// Empty value
					testFile << field << "\n";
				}
			};

			while (std::getline(metadataStream, line)) {
				// Check if this is a metadata field header
				if (line.rfind("ExitCode:", 0) == 0 || line.rfind("Args:", 0) == 0 ||
					line.rfind("Stdout:", 0) == 0 || line.rfind("Stderr:", 0) == 0 ||
					line.rfind("MaxoncStderr:", 0) == 0) {

					// Write previous field if it exists
					if (!currentField.empty()) {
						writeField(currentField, currentLines);
						currentLines.clear();
					}

					// Parse new field
					size_t colonPos = line.find(':');
					currentField = line.substr(0, colonPos + 1);
					std::string value = line.substr(colonPos + 1);

					// Trim leading whitespace from value
					size_t firstNonSpace = value.find_first_not_of(" \t");
					if (firstNonSpace != std::string::npos) {
						value = value.substr(firstNonSpace);
						if (!value.empty()) {
							currentLines.push_back(value);
						}
					}
				} else if (!currentField.empty()) {
					// Continuation line for current field
					currentLines.push_back(line);
				}
			}

			// Write last field
			if (!currentField.empty()) {
				writeField(currentField, currentLines);
			}
		} else {
			// For explicit tests, check if metadata contains multiline content that needs fencing
			std::istringstream metadataStream(metadata);
			std::string line;
			std::string output;
			bool hasMultilineField = false;

			// First pass: check if any field has multiline content
			std::string tempMetadata = metadata;
			std::istringstream checkStream(tempMetadata);
			std::string currentField;
			int lineCount = 0;

			while (std::getline(checkStream, line)) {
				if (line.rfind("ExitCode:", 0) == 0 || line.rfind("Args:", 0) == 0 ||
					line.rfind("Stdout:", 0) == 0 || line.rfind("Stderr:", 0) == 0 ||
					line.rfind("MaxoncStderr:", 0) == 0 || line.rfind("TrackAllocs:", 0) == 0) {

					if (!currentField.empty() && lineCount > 1) {
						hasMultilineField = true;
						break;
					}
					currentField = line.substr(0, line.find(':') + 1);
					lineCount = 1;
				} else if (!currentField.empty()) {
					lineCount++;
				}
			}
			if (!currentField.empty() && lineCount > 1) {
				hasMultilineField = true;
			}

			if (hasMultilineField) {
				// Parse and add fences for multiline values
				std::vector<std::string> currentLines;
				currentField.clear();

				auto writeField = [&output](const std::string &field, std::vector<std::string> &lines) {
					// Remove trailing empty lines
					while (!lines.empty() && lines.back().empty()) {
						lines.pop_back();
					}

					if (lines.size() > 1) {
						// Multiline - needs fences
						output += field + " ```\n";
						for (const auto &l : lines) {
							output += l + "\n";
						}
						output += "```\n";
					} else if (lines.size() == 1) {
						// Single line
						output += field + " " + lines[0] + "\n";
					} else {
						// Empty value
						output += field + "\n";
					}
				};

				metadataStream.clear();
				metadataStream.seekg(0);
				while (std::getline(metadataStream, line)) {
					// Check if this is a metadata field header
					if (line.rfind("ExitCode:", 0) == 0 || line.rfind("Args:", 0) == 0 ||
						line.rfind("Stdout:", 0) == 0 || line.rfind("Stderr:", 0) == 0 ||
						line.rfind("MaxoncStderr:", 0) == 0 || line.rfind("TrackAllocs:", 0) == 0) {

						// Write previous field if it exists
						if (!currentField.empty()) {
							writeField(currentField, currentLines);
							currentLines.clear();
						}

						// Parse new field
						size_t colonPos = line.find(':');
						currentField = line.substr(0, colonPos + 1);
						std::string value = line.substr(colonPos + 1);

						// Trim leading whitespace from value
						size_t firstNonSpace = value.find_first_not_of(" \t");
						if (firstNonSpace != std::string::npos) {
							value = value.substr(firstNonSpace);
							if (!value.empty()) {
								currentLines.push_back(value);
							}
						}
					} else if (!currentField.empty()) {
						// Continuation line for current field
						currentLines.push_back(line);
					}
				}

				// Write last field
				if (!currentField.empty()) {
					writeField(currentField, currentLines);
				}

				testFile << output;
			} else {
				// No multiline content, write as-is
				testFile << metadata;
			}
		}
	}
	testFile.close();

	// Add to manifest
	SpecManifest::FragmentEntry entry;
	entry.specFile = specBaseName + ".md";
	entry.testName = testName;
	entry.index = index;
	manifest.fragments[fragmentName + ".test"] = entry;

	totalExtracted++;
}

// Extract test fragments from spec files in specs/ directory
int extractSpecFragments(int verboseLevel) {
	std::string specsDir = "specs";
	std::string outputDir = "language-tests/fragments";
	std::string manifestPath = "language-tests/.spec-manifest.json";

	if (!std::filesystem::exists(specsDir)) {
		std::cerr << "Error: Directory not found: " << specsDir << std::endl;
		std::cerr << "Please create the specs/ directory with spec files." << std::endl;
		return 1;
	}

	// Delete and recreate output directory to remove orphaned fragments
	if (std::filesystem::exists(outputDir)) {
		std::filesystem::remove_all(outputDir);
	}
	std::filesystem::create_directories(outputDir);

	if (verboseLevel == 0) {
		std::cout << "Extracting test fragments from spec files..." << std::endl;
	}

	SpecManifest manifest;
	int totalFragmentsExtracted = 0;

	// Process each spec file
	for (const auto &entry : std::filesystem::directory_iterator(specsDir)) {
		if (entry.path().extension() != ".md")
			continue;

		std::string specPath = entry.path().string();
		std::string specBaseName = entry.path().stem().string();

		// Skip README.md - it contains format examples, not actual tests
		if (specBaseName == "README")
			continue;

		if (verboseLevel >= 1) {
			std::cout << "Processing " << specBaseName << ".md..." << std::endl;
		}

		std::ifstream specFile(specPath);
		if (!specFile) {
			std::cerr << "  Warning: Cannot read " << specPath << std::endl;
			continue;
		}

		std::string line;
		bool inTestSection = false;
		bool inDocSection = false;
		bool inCodeBlock = false;
		bool inTextBlock = false; // For non-executable text blocks
		std::string currentTestName;
		std::string currentCode;
		std::string currentMetadata;
		std::map<std::string, int> testNameCounts; // Track test indices per name
		bool collectingMetadata = false;
		bool inMetadataBlock = false;		 // For multi-line Stdout/Stderr
		int docExampleCount = 0;			 // Counter for unnamed doc examples
		std::string expectedOutputBlockType; // Track what type of output block is expected
		bool currentTrackAllocs = false;	 // Track --track-allocs flag for current test

		while (std::getline(specFile, line)) {
			// Check if we're entering the Documentation or Tests section
			if (line == "## Documentation") {
				inDocSection = true;
				inTestSection = false;
				continue;
			}
			if (line == "## Tests") {
				inDocSection = false;
				inTestSection = true;
				continue;
			}

			// Skip if not in Documentation or Tests section
			if (!inTestSection && !inDocSection)
				continue;

			// Check for test marker comment
			std::regex testMarkerRegex(R"(<!--\s*test:\s*([a-zA-Z0-9_.-]+)\s*-->)");
			std::smatch match;
			if (std::regex_search(line, match, testMarkerRegex)) {
				// Save previous test if exists
				if (!currentTestName.empty() && !currentCode.empty()) {
					int index = testNameCounts[currentTestName];
					std::string fragmentName = currentTestName + "." + std::to_string(index);
					writeTestFragment(outputDir, fragmentName, currentCode, currentMetadata,
									  manifest, specBaseName, currentTestName, index, totalFragmentsExtracted);
					currentCode.clear();
					currentMetadata.clear();
				}

				// Prefix test name with spec file name
				currentTestName = specBaseName + "." + match[1].str();
				testNameCounts[currentTestName]++;
				collectingMetadata = false;
				inMetadataBlock = false;
				continue;
			}

			// Check for TrackAllocs comment (must follow test marker)
			std::regex trackAllocsRegex(R"(<!--\s*TrackAllocs:\s*(true|false|yes|no|1|0)\s*-->)");
			if (std::regex_search(line, match, trackAllocsRegex)) {
				std::string val = match[1].str();
				currentTrackAllocs = (val == "true" || val == "yes" || val == "1");
				continue;
			}

			// Check for code block fence
			if (line == "```maxon") {
				// Start of maxon code block
				inCodeBlock = true;
				inTextBlock = false;
				currentCode.clear();
				expectedOutputBlockType.clear();
				continue;
			} else if (line == "```text") {
				// Start of text block (non-executable, not extracted)
				inTextBlock = true;
				inCodeBlock = false;
				continue;
			} else if (line == "```") {
				if (inTextBlock) {
					// End of text block - skip extraction
					inTextBlock = false;
					continue;
				} else if (inCodeBlock) {
					// End of code block - must be followed by output block
					inCodeBlock = false;
					continue;
				} else if (collectingMetadata && !inMetadataBlock) {
					// Start of metadata fence block
					inMetadataBlock = true;
					continue;
				} else if (inMetadataBlock) {
					// End of metadata fence block
					inMetadataBlock = false;
					// After closing an output block, we might be done or there might be more (e.g., stdout after exitcode)
					// Don't save yet, just mark that we're no longer in a block
					continue;
				}
			} else if (line == "```exitcode") {
				// Start of exitcode block (must follow maxon block)
				if (!currentCode.empty()) {
					// If in Doc section and no test name set, auto-generate one
					if (inDocSection && currentTestName.empty()) {
						docExampleCount++;
						currentTestName = specBaseName + ".doc-example-" + std::to_string(docExampleCount);
						testNameCounts[currentTestName] = 1;
					}
					// If in Tests section, test name is required
					if (inTestSection && currentTestName.empty()) {
						std::cerr << "Error: Test in " << specBaseName << ".md is missing <!-- test: name --> marker" << std::endl;
						std::cerr << "Code block starts with: " << currentCode.substr(0, currentCode.find('\n')) << std::endl;
						return 1;
					}
					expectedOutputBlockType = "exitcode";
					collectingMetadata = true;
					inMetadataBlock = true; // Reading the exitcode value
				}
				continue;
			} else if (line == "```stdout") {
				// Start of stdout block (optional, follows exitcode)
				// The exitcode should already be saved in currentMetadata
				expectedOutputBlockType = "stdout";
				inMetadataBlock = true;
				continue;
			} else if (line == "```maxoncstderr") {
				// Start of maxoncstderr block (must follow maxon block)
				if (!currentCode.empty()) {
					// If in Doc section and no test name set, auto-generate one
					if (inDocSection && currentTestName.empty()) {
						docExampleCount++;
						currentTestName = specBaseName + ".doc-example-" + std::to_string(docExampleCount);
						testNameCounts[currentTestName] = 1;
					}
					// If in Tests section, test name is required
					if (inTestSection && currentTestName.empty()) {
						std::cerr << "Error: Test in " << specBaseName << ".md is missing <!-- test: name --> marker" << std::endl;
						std::cerr << "Code block starts with: " << currentCode.substr(0, currentCode.find('\n')) << std::endl;
						return 1;
					}
					expectedOutputBlockType = "maxoncstderr";
					collectingMetadata = true;
					inMetadataBlock = true;
				}
				continue;
			}

			if (inCodeBlock || inTextBlock) {
				// Inside code block - accumulate source (but text blocks won't be extracted)
				if (inCodeBlock) {
					currentCode += line + "\n";
				}
			} else if (collectingMetadata) {
				if (inMetadataBlock) {
					// Inside metadata fence block - collect content and convert to proper field format
					if (expectedOutputBlockType == "exitcode") {
						// This is the exitcode value - save it
						if (!currentMetadata.empty() && currentMetadata.rfind("ExitCode: ", 0) != 0) {
							// We already have some metadata, append ExitCode
							currentMetadata += "ExitCode: " + line + "\n";
						} else {
							currentMetadata = "ExitCode: " + line + "\n";
						}
						// Don't set inMetadataBlock = false here, let the closing ``` handle it
					} else if (expectedOutputBlockType == "stdout") {
						// This is stdout content (multiline) - collect it
						currentMetadata += line + "\n";
					} else if (expectedOutputBlockType == "maxoncstderr") {
						// This is maxoncstderr content (multiline) - collect it
						currentMetadata += line + "\n";
					}
				} else {
					// Not in metadata block - check if we need to finish up
					// Check if we're at end of output blocks or start of new section
					if (line.empty() || line.rfind("###", 0) == 0 || line.rfind("##", 0) == 0 || line.rfind("<!--", 0) == 0 || line == "```") {
						// Save fragment if we have code and metadata
						if (!currentCode.empty() && !currentMetadata.empty()) {
							// Wrap stdout/maxoncstderr if needed
							std::string finalMetadata;
							if (expectedOutputBlockType == "stdout") {
								// Find the ExitCode line and insert Stdout after it
								size_t exitCodeEnd = currentMetadata.find('\n');
								if (exitCodeEnd != std::string::npos) {
									std::string exitCodeLine = currentMetadata.substr(0, exitCodeEnd + 1);
									std::string stdoutContent = currentMetadata.substr(exitCodeEnd + 1);
									// writeTestFragment will wrap multi-line stdout, just pass the field
									finalMetadata = exitCodeLine + "Stdout: " + stdoutContent;
								} else {
									finalMetadata = currentMetadata;
								}
							} else if (expectedOutputBlockType == "maxoncstderr") {
								// Just pass MaxoncStderr field - writeTestFragment will wrap it
								finalMetadata = "MaxoncStderr: " + currentMetadata;
							} else {
								// Just exitcode, no wrapping needed
								finalMetadata = currentMetadata;
							}

							// Prepend TrackAllocs if set
							if (currentTrackAllocs) {
								finalMetadata = "TrackAllocs: true\n" + finalMetadata;
							}

							int index = testNameCounts[currentTestName];
							std::string fragmentName = currentTestName + "." + std::to_string(index);
							writeTestFragment(outputDir, fragmentName, currentCode, finalMetadata,
											  manifest, specBaseName, currentTestName, index, totalFragmentsExtracted, inDocSection);
						}
						// Clear state
						currentCode.clear();
						currentMetadata.clear();
						currentTestName.clear();
						collectingMetadata = false;
						expectedOutputBlockType.clear();
						inMetadataBlock = false;
						currentTrackAllocs = false;
					}
				}
			}
		}

		// Save last test if exists
		if (!currentTestName.empty() && !currentCode.empty() && !currentMetadata.empty()) {
			// Prepare metadata - writeTestFragment will handle wrapping
			std::string finalMetadata;
			if (expectedOutputBlockType == "stdout") {
				// Find the ExitCode line and insert Stdout after it
				size_t exitCodeEnd = currentMetadata.find('\n');
				if (exitCodeEnd != std::string::npos) {
					std::string exitCodeLine = currentMetadata.substr(0, exitCodeEnd + 1);
					std::string stdoutContent = currentMetadata.substr(exitCodeEnd + 1);
					finalMetadata = exitCodeLine + "Stdout: " + stdoutContent;
				} else {
					finalMetadata = currentMetadata;
				}
			} else if (expectedOutputBlockType == "maxoncstderr") {
				// Just pass MaxoncStderr field - writeTestFragment will wrap it
				finalMetadata = "MaxoncStderr: " + currentMetadata;
			} else {
				// Just exitcode, no wrapping needed
				finalMetadata = currentMetadata;
			}

			// Prepend TrackAllocs if set
			if (currentTrackAllocs) {
				finalMetadata = "TrackAllocs: true\n" + finalMetadata;
			}

			int index = testNameCounts[currentTestName];
			std::string fragmentName = currentTestName + "." + std::to_string(index);
			writeTestFragment(outputDir, fragmentName, currentCode, finalMetadata,
							  manifest, specBaseName, currentTestName, index, totalFragmentsExtracted);
		}

		specFile.close();
	}

	// Write manifest file
	std::ofstream manifestFile(manifestPath);
	if (manifestFile) {
		manifestFile << "{\n";
		manifestFile << "  \"version\": \"1.0\",\n";
		manifestFile << "  \"fragments\": {\n";

		bool first = true;
		for (const auto &[fragmentName, entry] : manifest.fragments) {
			if (!first)
				manifestFile << ",\n";
			first = false;

			manifestFile << "    \"" << fragmentName << "\": {\n";
			manifestFile << "      \"spec\": \"" << entry.specFile << "\",\n";
			manifestFile << "      \"test\": \"" << entry.testName << "\",\n";
			manifestFile << "      \"index\": " << entry.index << "\n";
			manifestFile << "    }";
		}

		manifestFile << "\n  }\n";
		manifestFile << "}\n";
		manifestFile.close();

		// std::cout << "\nWrote manifest: " << manifestPath << std::endl;
	}

	std::cout << "Extraction complete: " << totalFragmentsExtracted << " fragments extracted" << std::endl;

	return 0;
}

// Helper function to regenerate a single fragment
// Returns: 0 = success, 1 = compile error (expected), 2 = unexpected error
static int regenerateSingleFragment(const std::string &testPath, const std::string &testName,
									std::string &statusMsg, int workerId = 0, int verboseLevel = 0) {
	std::ifstream inFile(testPath);
	if (!inFile) {
		statusMsg = "ERROR: Cannot read file";
		return 2;
	}

	std::string sourceCode;
	std::string line;
	while (std::getline(inFile, line)) {
		if (line == "---")
			break;
		sourceCode += line + "\n";
	}

	// Read past the old IR sections (we'll regenerate these)
	int separatorCount = 1;
	while (std::getline(inFile, line)) {
		if (line == "---") {
			separatorCount++;
			if (separatorCount == 3) {
				// Now we're at the metadata section
				break;
			}
		}
	}

	// Read and preserve metadata from the spec file
	std::string metadata;
	while (std::getline(inFile, line)) {
		metadata += line + "\n";
	}
	inFile.close();

	// Extract args and trackAllocs for compilation (if present)
	std::string args;
	bool trackAllocs = false;
	std::istringstream metaStream(metadata);
	while (std::getline(metaStream, line)) {
		if (line.rfind("Args:", 0) == 0) {
			args = line.substr(5);
			args.erase(0, args.find_first_not_of(" \t"));
		} else if (line.rfind("TrackAllocs:", 0) == 0) {
			std::string val = line.substr(12);
			val.erase(0, val.find_first_not_of(" \t"));
			trackAllocs = (val == "true" || val == "1" || val == "yes");
		}
	}

	// Use worker-specific temp files to avoid conflicts
	std::filesystem::path tempDir = "temp";
	std::filesystem::create_directories(tempDir);
	std::string workerSuffix = workerId > 0 ? ("_w" + std::to_string(workerId)) : "";
	std::string tempSource = (tempDir / ("temp_fragment" + workerSuffix + ".maxon")).string();
	std::string tempOptLL = (tempDir / ("temp-opt" + workerSuffix + ".ir")).string();
	std::string tempDebugLL = (tempDir / ("temp-debug" + workerSuffix + ".ir")).string();
	std::string tempOptExe = (tempDir / ("temp-opt" + workerSuffix + ".exe")).string();
	std::string tempDebugExe = (tempDir / ("temp-debug" + workerSuffix + ".exe")).string();

	std::ofstream tempOut(tempSource);
	if (!tempOut) {
		statusMsg = "ERROR: Cannot write temp file";
		return 2;
	}
	tempOut << sourceCode;
	tempOut.close();

	try {
		CompilationOptions optOpts;
		optOpts.inputFiles = {tempSource};
		optOpts.outputFile = tempOptLL;
		optOpts.optimize = true;
		optOpts.emitIR = true;
		optOpts.verboseLevel = 0;
		optOpts.trackAllocs = trackAllocs;

		std::string optIR;
		std::string compileError;
		std::string compileStdout;

		try {
			std::stringstream stdoutCapture;
			std::stringstream stderrCapture;
			std::streambuf *oldCout = std::cout.rdbuf(stdoutCapture.rdbuf());
			std::streambuf *oldCerr = std::cerr.rdbuf(stderrCapture.rdbuf());

			try {
				compileProgram(optOpts);
			} catch (const std::exception &e) {
				std::cout.rdbuf(oldCout);
				std::cerr.rdbuf(oldCerr);

				compileStdout = stdoutCapture.str();
				std::string stderrOutput = stderrCapture.str();

				compileError = stderrOutput;
				if (!compileError.empty() && compileError.back() != '\n') {
					compileError += '\n';
				}

				std::string exMsg = e.what();
				if (!exMsg.empty()) {
					compileError += exMsg;
					if (compileError.back() != '\n') {
						compileError += '\n';
					}
				}
				throw;
			}

			std::cout.rdbuf(oldCout);
			std::cerr.rdbuf(oldCerr);
			compileStdout = stdoutCapture.str();

			std::ifstream irFile(tempOptLL);
			if (irFile) {
				optIR = std::string(std::istreambuf_iterator<char>(irFile),
									std::istreambuf_iterator<char>());
				irFile.close();
				optIR = normalizeIR(optIR);
			}
		} catch (...) {
			// Compilation failed - check if this is expected (MaxoncStderr present)
			bool hasMaxoncStderr = metadata.find("MaxoncStderr:") != std::string::npos;

			if (!hasMaxoncStderr) {
				// Unexpected compilation failure
				statusMsg = "UNEXPECTED COMPILE ERROR: " + compileError;
				std::filesystem::remove(tempSource);
				std::filesystem::remove(tempOptLL);
				std::filesystem::remove(tempOptExe);
				return 2;
			}

			// Expected compilation failure - write N/A for IR sections, preserve metadata from spec
			std::ofstream outFile(testPath);
			outFile << sourceCode << "---\nN/A\n---\nN/A\n---\n";
			// Write preserved metadata as-is (from spec file)
			outFile << metadata;
			outFile.close();
			statusMsg = "COMPILE ERROR";

			std::filesystem::remove(tempSource);
			std::filesystem::remove(tempOptLL);
			std::filesystem::remove(tempOptExe);
			return 1;
		}

		// Generate debug IR
		CompilationOptions debugOpts;
		debugOpts.inputFiles = {tempSource};
		debugOpts.outputFile = tempDebugLL;
		debugOpts.debugInfo = true;
		debugOpts.emitIR = true;
		debugOpts.verboseLevel = 0;
		debugOpts.trackAllocs = trackAllocs;

		std::string debugIR;
		std::ifstream debugIRFile;
		compileProgram(debugOpts);

		debugIRFile.open(tempDebugLL);
		if (debugIRFile) {
			debugIR = std::string(std::istreambuf_iterator<char>(debugIRFile),
								  std::istreambuf_iterator<char>());
			debugIRFile.close();
			debugIR = normalizeIR(debugIR);
		}

		// Write fragment file with regenerated IR and preserved spec metadata
		std::ofstream testFile(testPath);
		testFile << sourceCode;
		testFile << "---\n"
				 << optIR;
		testFile << "---\n"
				 << debugIR;
		testFile << "---\n";

		// Write preserved metadata from spec (includes Args, ExitCode, Stdout, Stderr, MaxoncStderr)
		// Need to properly close any open ``` blocks
		std::string metadataTrimmed = metadata;
		// Remove trailing whitespace/newlines
		while (!metadataTrimmed.empty() && (metadataTrimmed.back() == '\n' || metadataTrimmed.back() == '\r' || metadataTrimmed.back() == ' ' || metadataTrimmed.back() == '\t')) {
			metadataTrimmed.pop_back();
		}

		testFile << metadataTrimmed;

		// Check if metadata ends with an unclosed ``` block (for Stdout/Stderr/MaxoncStderr)
		std::istringstream metaCheck(metadata);
		std::string checkLine;
		int fenceCount = 0;
		while (std::getline(metaCheck, checkLine)) {
			if (checkLine.find("```") != std::string::npos) {
				fenceCount++;
			}
		}
		// If odd number of fences, we need to close
		if (fenceCount % 2 == 1) {
			testFile << "\n```";
		}

		testFile << "\n";
		testFile.close();

		std::filesystem::remove(tempSource);
		std::filesystem::remove(tempOptLL);
		std::filesystem::remove(tempDebugLL);
		std::filesystem::remove(tempOptExe);
		std::filesystem::remove(tempDebugExe);

		statusMsg = "OK";
		return 0;
	} catch (const std::exception &e) {
		statusMsg = std::string("ERROR: ") + e.what();
		// Clean up any temp files that may have been created
		std::filesystem::remove(tempSource);
		std::filesystem::remove(tempOptLL);
		std::filesystem::remove(tempDebugLL);
		std::filesystem::remove(tempOptExe);
		std::filesystem::remove(tempDebugExe);
		return 2;
	}
}

// Regenerate a subset of fragments (used for parallel processing)
int regenerateFragmentsSubset(const std::string &outputFile, const std::vector<std::string> &testFiles, int verboseLevel) {
	std::ofstream out(outputFile);
	if (!out) {
		std::cerr << "Error: Cannot write to output file: " << outputFile << std::endl;
		return 1;
	}

	// Use process ID to generate unique worker ID
#ifdef _WIN32
	int workerId = GetCurrentProcessId() % 100000;
#else
	int workerId = getpid() % 100000;
#endif
	int failCount = 0;

	if (verboseLevel >= 1) {
		std::cerr << "[WORKER " << workerId << "] Processing " << testFiles.size() << " tests..." << std::endl;
	}

	for (size_t idx = 0; idx < testFiles.size(); ++idx) {
		const auto &testPath = testFiles[idx];
		std::filesystem::path p(testPath);
		std::string testName = p.stem().string();

		if (verboseLevel >= 1) {
			std::cerr << "[WORKER " << workerId << "] [" << (idx + 1) << "/" << testFiles.size() << "] " << testName << std::endl;
		}

		std::string statusMsg;
		int result = regenerateSingleFragment(testPath, testName, statusMsg, workerId, verboseLevel);

		if (result != 0 && result != 1) {
			failCount++;
		}

		// Replace newlines in status message to avoid parsing issues
		std::string safeStatusMsg = statusMsg;
		std::replace(safeStatusMsg.begin(), safeStatusMsg.end(), '\n', '\t');

		// Write result to output file
		out << testName << "|" << safeStatusMsg << "\n";
		out.flush();
	}

	out.close();
	return failCount > 0 ? 1 : 0;
}

int regenerateFragments(int verboseLevel) {
	try {
		auto startTime = std::chrono::high_resolution_clock::now();

		std::cout << "Regenerating test fragments..." << std::endl;

		std::vector<std::string> fragmentDirs = {
			"language-tests/fragments"};

		// Collect all test files
		std::vector<std::filesystem::path> testFiles;
		for (const auto &fragmentsDir : fragmentDirs) {
			if (!std::filesystem::exists(fragmentsDir)) {
				std::cerr << "Warning: Directory not found: " << fragmentsDir << std::endl;
				continue;
			}

			// std::cout << "\nProcessing " << fragmentsDir << "..." << std::endl;

			for (const auto &entry : std::filesystem::directory_iterator(fragmentsDir)) {
				if (entry.path().extension() == ".test") {
					testFiles.push_back(entry.path());
				}
			}
		}

		int totalTests = testFiles.size();
		if (totalTests == 0) {
			std::cout << "No test files found." << std::endl;
			return 0;
		}

		// Determine number of worker processes
		unsigned int numWorkers = std::thread::hardware_concurrency();
		if (numWorkers == 0)
			numWorkers = 4;
		if (numWorkers > 8)
			numWorkers = numWorkers / 2;

		if (numWorkers > static_cast<unsigned int>(totalTests)) {
			numWorkers = totalTests;
		}
		std::cout << "Using " << numWorkers << " parallel workers..." << std::endl;

		// Split tests into groups
		std::vector<std::vector<std::string>> testGroups(numWorkers);
		for (size_t i = 0; i < testFiles.size(); ++i) {
			testGroups[i % numWorkers].push_back(testFiles[i].string());
		}

		// Create output files for each worker
		std::vector<std::string> outputFiles(numWorkers);
		for (unsigned int i = 0; i < numWorkers; ++i) {
#ifdef _WIN32
			char tempPath[MAX_PATH];
			GetTempPathA(MAX_PATH, tempPath);
			outputFiles[i] = std::string(tempPath) + "maxon_regen_results_" + std::to_string(i) + ".tmp";
#else
			const char *tempDir = std::getenv("TMPDIR");
			if (!tempDir)
				tempDir = "/tmp";
			outputFiles[i] = std::string(tempDir) + "/maxon_regen_results_" + std::to_string(i) + ".tmp";
#endif
		}

#ifdef _WIN32
		// Launch child processes
		std::vector<PROCESS_INFORMATION> processes(numWorkers);
		std::vector<HANDLE> processHandles(numWorkers);

		// Get current executable path
		char exePath[MAX_PATH];
		GetModuleFileNameA(NULL, exePath, MAX_PATH);

		for (unsigned int i = 0; i < numWorkers; ++i) {
			if (testGroups[i].empty())
				continue;

			// Build command line for child process
			std::string cmdLine = std::string("\"") + exePath + "\" regen-fragments-subset \"" + outputFiles[i] + "\"";
			if (verboseLevel >= 2) {
				cmdLine += " -vv";
			} else if (verboseLevel >= 1) {
				cmdLine += " --verbose";
			}
			for (const auto &testFile : testGroups[i]) {
				cmdLine += " \"" + testFile + "\"";
			}

			STARTUPINFOA si = {};
			si.cb = sizeof(si);
			// Don't inherit handles to avoid stderr/stdout contention between workers
			// si.dwFlags = STARTF_USESTDHANDLES;
			// si.hStdInput = GetStdHandle(STD_INPUT_HANDLE);
			// si.hStdOutput = GetStdHandle(STD_OUTPUT_HANDLE);
			// si.hStdError = GetStdHandle(STD_ERROR_HANDLE);

			// CreateProcess requires non-const command line
			std::vector<char> cmdLineBuffer(cmdLine.begin(), cmdLine.end());
			cmdLineBuffer.push_back('\0');

			// Don't inherit handles (FALSE) to prevent handle contention
			if (!CreateProcessA(NULL, cmdLineBuffer.data(), NULL, NULL, FALSE, 0, NULL, NULL, &si, &processes[i])) {
				std::cerr << "Error: Failed to create worker process " << i << std::endl;
				return 1;
			}
			processHandles[i] = processes[i].hProcess;
		}

		// Wait for all processes to complete
		for (unsigned int i = 0; i < numWorkers; ++i) {
			if (testGroups[i].empty())
				continue;
			WaitForSingleObject(processHandles[i], INFINITE);
			CloseHandle(processes[i].hProcess);
			CloseHandle(processes[i].hThread);
		}
#else
		// POSIX implementation - fork worker processes
		std::vector<pid_t> workerPids(numWorkers, -1);

		for (unsigned int i = 0; i < numWorkers; ++i) {
			if (testGroups[i].empty())
				continue;

			pid_t pid = fork();
			if (pid == -1) {
				std::cerr << "Error: Failed to fork worker process " << i << std::endl;
				return 1;
			} else if (pid == 0) {
				// Child process - execute regen-fragments-subset
				// Build arguments - need to keep strings alive until execvp
				std::vector<std::string> argStrings;
				argStrings.push_back("maxon");
				argStrings.push_back("regen-fragments-subset");
				argStrings.push_back(outputFiles[i]);

				if (verboseLevel >= 2) {
					argStrings.push_back("-vv");
				} else if (verboseLevel >= 1) {
					argStrings.push_back("--verbose");
				}

				// Add test files
				for (const auto &testFile : testGroups[i]) {
					argStrings.push_back(testFile);
				}

				// Convert to char* array
				std::vector<char *> args;
				for (auto &str : argStrings) {
					args.push_back(const_cast<char *>(str.c_str()));
				}
				args.push_back(nullptr);

				// Execute
				execvp("maxon", args.data());

				// If execvp returns, it failed
				std::cerr << "Error: Failed to exec worker process " << i << ": " << strerror(errno) << std::endl;
				exit(1);
			} else {
				// Parent process - store child PID
				workerPids[i] = pid;
			}
		}

		// Wait for all child processes to complete
		for (unsigned int i = 0; i < numWorkers; ++i) {
			if (workerPids[i] == -1)
				continue;

			int status;
			waitpid(workerPids[i], &status, 0);

			if (WIFEXITED(status) && WEXITSTATUS(status) != 0 && verboseLevel >= 1) {
				std::cerr << "Warning: Worker " << i << " exited with code " << WEXITSTATUS(status) << std::endl;
			}
		}
#endif

		// Collect results from output files
		int successCount = 0;
		int failCount = 0;
		std::vector<std::pair<std::string, std::string>> failedFragments;

		for (unsigned int i = 0; i < numWorkers; ++i) {
			std::ifstream inFile(outputFiles[i]);
			if (!inFile)
				continue;

			std::string line;
			while (std::getline(inFile, line)) {
				size_t pos = line.find('|');
				if (pos == std::string::npos)
					continue;

				std::string testName = line.substr(0, pos);
				std::string status = line.substr(pos + 1);

				// Convert escaped newlines back
				std::replace(status.begin(), status.end(), '\t', '\n');

				if (verboseLevel >= 1) {
					std::cout << "  " << testName << "... " << status << std::endl;
				}

				if (status == "OK" || status == "COMPILE ERROR") {
					successCount++;
				} else {
					failCount++;
					failedFragments.emplace_back(testName, status);
				}
			}
			inFile.close();
			std::filesystem::remove(outputFiles[i]);
		}

		// Print failed fragments
		if (!failedFragments.empty()) {
			std::cout << "\nFailed fragments:" << std::endl;
			for (const auto &[name, status] : failedFragments) {
				std::cout << "  " << name << " - " << status << std::endl;
			}
		}

		auto endTime = std::chrono::high_resolution_clock::now();
		auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(endTime - startTime);
		double seconds = duration.count() / 1000.0;

		std::cout << "\nSummary: " << successCount << " succeeded, " << failCount << " failed, "
				  << totalTests << " total" << std::endl;
		std::cout << "Elapsed time: " << std::fixed << std::setprecision(2) << seconds << "s" << std::endl;

		// If no fragments were processed (worker processes didn't execute), this is a failure
		if (successCount == 0 && failCount == 0 && totalTests > 0) {
			std::cerr << "Error: No fragments were regenerated. This may indicate worker processes failed to run." << std::endl;
			return 1;
		}

		return failCount > 0 ? 1 : 0;
	} catch (const std::exception &e) {
		std::cerr << "Error: " << e.what() << std::endl;
		return 1;
	}
}
