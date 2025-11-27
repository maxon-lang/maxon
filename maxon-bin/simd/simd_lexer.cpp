/**
 * SIMD Lexer/Parser Implementation
 *
 * This file contains the implementation of SIMD-optimized lexer and parser
 * components. Most functionality is in headers for inlining.
 */

#include "../lexer.h"
#include "simd.h"

#include <chrono>
#include <cstdio>
#include <iomanip>
#include <iostream>
#include <sstream>

namespace simd {

// Static initialization of KeywordMatcher
// This ensures the keyword table is built before first use
namespace {
struct KeywordMatcherInit {
	KeywordMatcherInit() {
		KeywordMatcher::initialize();
	}
};

// Global instance triggers initialization
static KeywordMatcherInit keyword_matcher_init;
} // namespace

// CPU feature detection result (cached)
static CPUFeature cached_features = CPUFeature::None;
static bool features_detected = false;

CPUFeature get_cached_cpu_features() {
	if (!features_detected) {
		cached_features = detect_cpu_features();
		features_detected = true;
	}
	return cached_features;
}

// Print CPU feature report (for debugging/diagnostics)
void print_cpu_features() {
	CPUFeature features = get_cached_cpu_features();

	printf("CPU Features:\n");
	printf("  SSE2:     %s\n", has_feature(features, CPUFeature::SSE2) ? "YES" : "no");
	printf("  SSE3:     %s\n", has_feature(features, CPUFeature::SSE3) ? "YES" : "no");
	printf("  SSSE3:    %s\n", has_feature(features, CPUFeature::SSSE3) ? "YES" : "no");
	printf("  SSE4.1:   %s\n", has_feature(features, CPUFeature::SSE41) ? "YES" : "no");
	printf("  SSE4.2:   %s\n", has_feature(features, CPUFeature::SSE42) ? "YES" : "no");
	printf("  AVX:      %s\n", has_feature(features, CPUFeature::AVX) ? "YES" : "no");
	printf("  AVX2:     %s\n", has_feature(features, CPUFeature::AVX2) ? "YES" : "no");
	printf("  AVX-512F: %s\n", has_feature(features, CPUFeature::AVX512F) ? "YES" : "no");
	printf("  POPCNT:   %s\n", has_feature(features, CPUFeature::POPCNT) ? "YES" : "no");
	printf("  BMI1:     %s\n", has_feature(features, CPUFeature::BMI1) ? "YES" : "no");
	printf("  BMI2:     %s\n", has_feature(features, CPUFeature::BMI2) ? "YES" : "no");
}

// Benchmark result structure
struct BenchmarkResult {
	double total_ms;
	size_t token_count;
	size_t source_bytes;
	double tokens_per_ms;
	double mb_per_sec;
	double avg_time_per_iteration_us;
};

// Benchmark SIMD lexer
BenchmarkResult benchmark_simd_lexer(const std::string &source, int iterations) {
	BenchmarkResult result = {};
	result.source_bytes = source.size();

	// Warm up
	{
		SIMDLexer lexer(source);
		auto tokens = lexer.tokenize();
		result.token_count = tokens.size();
	}

	// Benchmark
	auto start = std::chrono::high_resolution_clock::now();

	for (int i = 0; i < iterations; ++i) {
		SIMDLexer lexer(source);
		auto tokens = lexer.tokenize();
		(void)tokens; // Prevent optimization
	}

	auto end = std::chrono::high_resolution_clock::now();
	auto duration = std::chrono::duration_cast<std::chrono::microseconds>(end - start);

	result.total_ms = duration.count() / 1000.0;
	result.tokens_per_ms = (result.token_count * iterations) / result.total_ms;
	result.mb_per_sec = (result.source_bytes * iterations) / (result.total_ms * 1000.0);
	result.avg_time_per_iteration_us = static_cast<double>(duration.count()) / iterations;

	return result;
}

// Benchmark scalar lexer
BenchmarkResult benchmark_scalar_lexer(const std::string &source, int iterations) {
	BenchmarkResult result = {};
	result.source_bytes = source.size();

	// Warm up
	{
		Lexer lexer(source);
		auto tokens = lexer.tokenize();
		result.token_count = tokens.size();
	}

	// Benchmark
	auto start = std::chrono::high_resolution_clock::now();

	for (int i = 0; i < iterations; ++i) {
		Lexer lexer(source);
		auto tokens = lexer.tokenize();
		(void)tokens; // Prevent optimization
	}

	auto end = std::chrono::high_resolution_clock::now();
	auto duration = std::chrono::duration_cast<std::chrono::microseconds>(end - start);

	result.total_ms = duration.count() / 1000.0;
	result.tokens_per_ms = (result.token_count * iterations) / result.total_ms;
	result.mb_per_sec = (result.source_bytes * iterations) / (result.total_ms * 1000.0);
	result.avg_time_per_iteration_us = static_cast<double>(duration.count()) / iterations;

	return result;
}

// Verify that SIMD and scalar lexers produce identical output
bool verify_lexer_correctness(const std::string &source, std::string &error_message) {
	// Tokenize with both lexers
	std::vector<Token> simd_tokens;
	std::vector<Token> scalar_tokens;

	try {
		SIMDLexer simd_lexer(source);
		simd_tokens = simd_lexer.tokenize();
	} catch (const std::exception &e) {
		error_message = "SIMD lexer threw exception: " + std::string(e.what());
		return false;
	}

	try {
		Lexer scalar_lexer(source);
		scalar_tokens = scalar_lexer.tokenize();
	} catch (const std::exception &e) {
		error_message = "Scalar lexer threw exception: " + std::string(e.what());
		return false;
	}

	// Compare token counts
	if (simd_tokens.size() != scalar_tokens.size()) {
		std::ostringstream ss;
		ss << "Token count mismatch: SIMD=" << simd_tokens.size()
		   << ", Scalar=" << scalar_tokens.size();
		error_message = ss.str();
		return false;
	}

	// Compare each token
	for (size_t i = 0; i < simd_tokens.size(); ++i) {
		const auto &st = simd_tokens[i];
		const auto &ot = scalar_tokens[i];

		if (st.type != ot.type || st.value != ot.value ||
			st.line != ot.line || st.column != ot.column) {
			std::ostringstream ss;
			ss << "Token mismatch at index " << i << ":\n"
			   << "  SIMD:   type=" << static_cast<int>(st.type)
			   << " value='" << st.value << "' line=" << st.line << " col=" << st.column << "\n"
			   << "  Scalar: type=" << static_cast<int>(ot.type)
			   << " value='" << ot.value << "' line=" << ot.line << " col=" << ot.column;
			error_message = ss.str();
			return false;
		}
	}

	error_message = "";
	return true;
}

// Run complete benchmark comparison
void run_benchmark_comparison(const std::string &source, const std::string &filename, int iterations) {
	std::cout << "\n";
	std::cout << "========================================\n";
	std::cout << "Lexer Benchmark: " << filename << "\n";
	std::cout << "========================================\n";
	std::cout << "Source size: " << source.size() << " bytes\n";
	std::cout << "Iterations:  " << iterations << "\n";
	std::cout << "\n";

	// Print CPU features
	print_cpu_features();
	std::cout << "\nSIMD Mode: " << get_simd_capability() << "\n\n";

	// Verify correctness first
	std::string error_message;
	bool correct = verify_lexer_correctness(source, error_message);
	if (!correct) {
		std::cout << "ERROR: Lexer output mismatch!\n";
		std::cout << error_message << "\n";
		return;
	}
	std::cout << "Correctness: VERIFIED (SIMD and scalar produce identical output)\n\n";

	// Run benchmarks
	std::cout << "Running scalar lexer benchmark...\n";
	BenchmarkResult scalar_result = benchmark_scalar_lexer(source, iterations);

	std::cout << "Running SIMD lexer benchmark...\n";
	BenchmarkResult simd_result = benchmark_simd_lexer(source, iterations);

	// Calculate speedup
	double speedup = scalar_result.total_ms / simd_result.total_ms;

	// Print results
	std::cout << "\n";
	std::cout << "Results:\n";
	std::cout << "----------------------------------------\n";
	std::cout << std::fixed << std::setprecision(2);

	std::cout << "                    Scalar       SIMD\n";
	std::cout << "Total time (ms):    " << std::setw(8) << scalar_result.total_ms
			  << "   " << std::setw(8) << simd_result.total_ms << "\n";
	std::cout << "Avg time (µs):      " << std::setw(8) << scalar_result.avg_time_per_iteration_us
			  << "   " << std::setw(8) << simd_result.avg_time_per_iteration_us << "\n";
	std::cout << "Tokens/ms:          " << std::setw(8) << scalar_result.tokens_per_ms
			  << "   " << std::setw(8) << simd_result.tokens_per_ms << "\n";
	std::cout << "MB/sec:             " << std::setw(8) << scalar_result.mb_per_sec
			  << "   " << std::setw(8) << simd_result.mb_per_sec << "\n";
	std::cout << "----------------------------------------\n";
	std::cout << "Token count:        " << simd_result.token_count << "\n";
	std::cout << "Speedup:            " << speedup << "x\n";
	std::cout << "========================================\n";
}

} // namespace simd
