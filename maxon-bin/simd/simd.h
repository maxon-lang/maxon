#ifndef SIMD_H
#define SIMD_H

/**
 * SIMD Module Header
 *
 * Single include file for all SIMD-optimized lexer/parser components.
 *
 * Usage:
 *   #include "simd/simd.h"
 *
 *   // Use SIMD lexer
 *   simd::SIMDLexer lexer(source);
 *   auto tokens = lexer.tokenize();
 *
 *   // Or use factory function with automatic dispatch
 *   auto tokens = simd::tokenize(source);
 *
 * Components:
 * - simd_platform.h:       Cross-platform SIMD intrinsics and CPU detection
 * - simd_char_class.h:     Vectorized character classification
 * - simd_keyword_matcher.h: Perfect hash keyword recognition
 * - simd_number_parser.h:   SIMD-accelerated number parsing
 * - simd_token_stream.h:    Compact token storage with string interning
 * - simd_lexer.h:           Main SIMD lexer class
 * - simd_parser_support.h:  Parser optimizations (block boundaries, lookahead)
 */

#include "simd_char_class.h"
#include "simd_keyword_matcher.h"
#include "simd_lexer.h"
#include "simd_number_parser.h"
#include "simd_parser_support.h"
#include "simd_platform.h"
#include "simd_token_stream.h"

namespace simd {

/**
 * Get SIMD capability string for diagnostics
 */
inline const char *get_simd_capability() {
	if (has_avx2()) {
		return "AVX2 (256-bit)";
	} else if (has_sse42()) {
		return "SSE4.2 (128-bit)";
	} else {
		return "Scalar (no SIMD)";
	}
}

/**
 * Check if SIMD optimizations are beneficial for the given source size
 */
inline bool should_use_simd(size_t source_size) {
	// SIMD overhead is only worthwhile for larger sources
	// Threshold determined empirically
	return has_sse42() && source_size > 512;
}

/**
 * Print CPU features (defined in simd_lexer.cpp)
 */
void print_cpu_features();

/**
 * Verify that SIMD and scalar lexers produce identical output
 * Returns true if outputs match, false otherwise with error message
 */
bool verify_lexer_correctness(const std::string &source, std::string &error_message);

/**
 * Run a full benchmark comparison between SIMD and scalar lexers
 * Prints results to stdout
 */
void run_benchmark_comparison(const std::string &source, const std::string &filename, int iterations);

} // namespace simd

#endif // SIMD_H
