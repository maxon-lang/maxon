/**
 * Unit tests for the 'maxon build' command
 *
 * Tests multi-file project discovery, compilation, and execution.
 * Uses the build-test-project/ directory containing:
 *   - lib.maxon: exports helper(x int) int returning x * 2
 *   - main.maxon: calls helper(21), expected exit code 42
 */

#include <catch_amalgamated.hpp>
#include <cstdlib>
#include <filesystem>
#include <fstream>

namespace fs = std::filesystem;

//==============================================================================
// Test Fixtures and Helpers
//==============================================================================

namespace {

// Get the maxon executable path
fs::path getMaxonPath() {
	// Tests run from maxon-bin/tests/build/, maxon is in maxon/bin/
	fs::path testDir = fs::current_path();

#ifdef _WIN32
	fs::path maxonPath = testDir / ".." / ".." / ".." / "bin" / "maxon.exe";
#else
	fs::path maxonPath = testDir / ".." / ".." / ".." / "bin" / "maxon";
#endif

	if (fs::exists(maxonPath)) {
		return fs::canonical(maxonPath);
	}

	FAIL("Could not locate maxon executable");
	return {};
}

// Path to the test project relative to the test executable's working directory
fs::path getTestProjectPath() {
	// Tests run from maxon-bin/tests/build/, test project is in maxon-bin/tests/build-test-project/
	fs::path testDir = fs::current_path();

	// Try relative to build directory first
	fs::path projectPath = testDir / ".." / "build-test-project";
	if (fs::exists(projectPath)) {
		return fs::canonical(projectPath);
	}

	// Try relative to tests directory
	projectPath = testDir / "build-test-project";
	if (fs::exists(projectPath)) {
		return fs::canonical(projectPath);
	}

	// Try from maxon-bin/tests
	projectPath = testDir.parent_path() / "build-test-project";
	if (fs::exists(projectPath)) {
		return fs::canonical(projectPath);
	}

	FAIL("Could not locate build-test-project directory");
	return {};
}

// Clean up any artifacts from previous test runs
void cleanupArtifacts(const fs::path &projectPath) {
	fs::path binDir = projectPath / "bin";
	if (fs::exists(binDir)) {
		fs::remove_all(binDir);
	}
}

// Run a command and return its exit code
int runCommand(const std::string &command) {
#ifdef _WIN32
	return std::system(command.c_str());
#else
	int result = std::system(command.c_str());
	// On Unix, system() returns the wait status, extract exit code
	if (WIFEXITED(result)) {
		return WEXITSTATUS(result);
	}
	return -1;
#endif
}

} // namespace

//==============================================================================
// Build Command Tests
//==============================================================================

TEST_CASE("Build: maxon build compiles multi-file project", "[build]") {
	fs::path projectPath = getTestProjectPath();
	fs::path maxonPath = getMaxonPath();
	cleanupArtifacts(projectPath);

	// Save current directory and change to test project
	fs::path originalDir = fs::current_path();
	fs::current_path(projectPath);

	// Run maxon build
	std::string command = "\"" + maxonPath.string() + "\" build";
	int buildResult = runCommand(command);

	// Restore original directory
	fs::current_path(originalDir);

	REQUIRE(buildResult == 0);

	// Verify executable was created in bin/
	fs::path exePath = projectPath / "bin" / "testapp.exe";
	REQUIRE(fs::exists(exePath));

	// Clean up
	cleanupArtifacts(projectPath);
}

TEST_CASE("Build: compiled executable runs correctly", "[build][e2e]") {
	fs::path projectPath = getTestProjectPath();
	fs::path maxonPath = getMaxonPath();
	cleanupArtifacts(projectPath);

	// Save current directory and change to test project
	fs::path originalDir = fs::current_path();
	fs::current_path(projectPath);

	// Run maxon build
	std::string buildCommand = "\"" + maxonPath.string() + "\" build";
	int buildResult = runCommand(buildCommand);

	// Restore original directory
	fs::current_path(originalDir);

	REQUIRE(buildResult == 0);

	// Verify executable was created
	fs::path exePath = projectPath / "bin" / "testapp.exe";
	REQUIRE(fs::exists(exePath));

	// Run the executable and verify exit code
	// main.maxon returns helper(21) + double(5) where helper returns x * 2
	// So expected exit code is 21 * 2 + 5 * 2 = 42 + 10 = 52
	std::string execCommand = "\"" + exePath.string() + "\"";
	int exitCode = runCommand(execCommand);
	REQUIRE(exitCode == 52);

	// Clean up
	cleanupArtifacts(projectPath);
}
