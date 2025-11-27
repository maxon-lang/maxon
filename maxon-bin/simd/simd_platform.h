#ifndef SIMD_PLATFORM_H
#define SIMD_PLATFORM_H

/**
 * SIMD Platform Abstraction Header
 *
 * Provides cross-platform SIMD intrinsics (SSE4.2, AVX2, AVX-512)
 * with runtime CPU feature detection and fallback support.
 */

#include <cstddef>
#include <cstdint>

// Platform-specific intrinsic headers
#ifdef _MSC_VER
#include <intrin.h>
#define SIMD_FORCE_INLINE __forceinline
#define SIMD_ALIGN(n) __declspec(align(n))
#else
#include <x86intrin.h>
#define SIMD_FORCE_INLINE __attribute__((always_inline)) inline
#define SIMD_ALIGN(n) __attribute__((aligned(n)))
#endif

// CPU Feature flags
enum class CPUFeature : uint32_t {
	None = 0,
	SSE2 = 1 << 0,
	SSE3 = 1 << 1,
	SSSE3 = 1 << 2,
	SSE41 = 1 << 3,
	SSE42 = 1 << 4,
	AVX = 1 << 5,
	AVX2 = 1 << 6,
	AVX512F = 1 << 7,
	AVX512BW = 1 << 8,
	POPCNT = 1 << 9,
	BMI1 = 1 << 10,
	BMI2 = 1 << 11
};

// Enable bitwise operations on CPUFeature
inline CPUFeature operator|(CPUFeature a, CPUFeature b) {
	return static_cast<CPUFeature>(static_cast<uint32_t>(a) | static_cast<uint32_t>(b));
}

inline CPUFeature operator&(CPUFeature a, CPUFeature b) {
	return static_cast<CPUFeature>(static_cast<uint32_t>(a) & static_cast<uint32_t>(b));
}

inline CPUFeature &operator|=(CPUFeature &a, CPUFeature b) {
	a = a | b;
	return a;
}

inline bool has_feature(CPUFeature features, CPUFeature test) {
	return (static_cast<uint32_t>(features) & static_cast<uint32_t>(test)) != 0;
}

/**
 * Detect CPU features at runtime
 */
inline CPUFeature detect_cpu_features() {
	CPUFeature features = CPUFeature::None;

#ifdef _MSC_VER
	int cpuInfo[4];

	// Get feature flags from CPUID
	__cpuid(cpuInfo, 1);

	if (cpuInfo[3] & (1 << 26))
		features |= CPUFeature::SSE2;
	if (cpuInfo[2] & (1 << 0))
		features |= CPUFeature::SSE3;
	if (cpuInfo[2] & (1 << 9))
		features |= CPUFeature::SSSE3;
	if (cpuInfo[2] & (1 << 19))
		features |= CPUFeature::SSE41;
	if (cpuInfo[2] & (1 << 20))
		features |= CPUFeature::SSE42;
	if (cpuInfo[2] & (1 << 23))
		features |= CPUFeature::POPCNT;
	if (cpuInfo[2] & (1 << 28))
		features |= CPUFeature::AVX;

	// Extended feature flags
	__cpuidex(cpuInfo, 7, 0);

	if (cpuInfo[1] & (1 << 3))
		features |= CPUFeature::BMI1;
	if (cpuInfo[1] & (1 << 5))
		features |= CPUFeature::AVX2;
	if (cpuInfo[1] & (1 << 8))
		features |= CPUFeature::BMI2;
	if (cpuInfo[1] & (1 << 16))
		features |= CPUFeature::AVX512F;
	if (cpuInfo[1] & (1 << 30))
		features |= CPUFeature::AVX512BW;

#elif defined(__GNUC__) || defined(__clang__)
	// Use built-in CPU detection on GCC/Clang
	__builtin_cpu_init();

	if (__builtin_cpu_supports("sse2"))
		features |= CPUFeature::SSE2;
	if (__builtin_cpu_supports("sse3"))
		features |= CPUFeature::SSE3;
	if (__builtin_cpu_supports("ssse3"))
		features |= CPUFeature::SSSE3;
	if (__builtin_cpu_supports("sse4.1"))
		features |= CPUFeature::SSE41;
	if (__builtin_cpu_supports("sse4.2"))
		features |= CPUFeature::SSE42;
	if (__builtin_cpu_supports("popcnt"))
		features |= CPUFeature::POPCNT;
	if (__builtin_cpu_supports("avx"))
		features |= CPUFeature::AVX;
	if (__builtin_cpu_supports("avx2"))
		features |= CPUFeature::AVX2;
	if (__builtin_cpu_supports("bmi"))
		features |= CPUFeature::BMI1;
	if (__builtin_cpu_supports("bmi2"))
		features |= CPUFeature::BMI2;
	if (__builtin_cpu_supports("avx512f"))
		features |= CPUFeature::AVX512F;
	if (__builtin_cpu_supports("avx512bw"))
		features |= CPUFeature::AVX512BW;
#endif

	return features;
}

/**
 * Get cached CPU features (initialized once on first call)
 */
inline CPUFeature get_cpu_features() {
	static CPUFeature features = detect_cpu_features();
	return features;
}

// ============================================================================
// SIMD Type Abstractions
// ============================================================================

// 128-bit vector types (SSE)
using simd128_t = __m128i;

// 256-bit vector types (AVX2)
using simd256_t = __m256i;

// ============================================================================
// SIMD Load/Store Operations
// ============================================================================

// Unaligned loads
SIMD_FORCE_INLINE simd128_t simd_loadu_128(const void *ptr) {
	return _mm_loadu_si128(reinterpret_cast<const simd128_t *>(ptr));
}

SIMD_FORCE_INLINE simd256_t simd_loadu_256(const void *ptr) {
	return _mm256_loadu_si256(reinterpret_cast<const simd256_t *>(ptr));
}

// Aligned loads (ptr must be 16/32-byte aligned)
SIMD_FORCE_INLINE simd128_t simd_load_128(const void *ptr) {
	return _mm_load_si128(reinterpret_cast<const simd128_t *>(ptr));
}

SIMD_FORCE_INLINE simd256_t simd_load_256(const void *ptr) {
	return _mm256_load_si256(reinterpret_cast<const simd256_t *>(ptr));
}

// Unaligned stores
SIMD_FORCE_INLINE void simd_storeu_128(void *ptr, simd128_t v) {
	_mm_storeu_si128(reinterpret_cast<simd128_t *>(ptr), v);
}

SIMD_FORCE_INLINE void simd_storeu_256(void *ptr, simd256_t v) {
	_mm256_storeu_si256(reinterpret_cast<simd256_t *>(ptr), v);
}

// ============================================================================
// SIMD Broadcast Operations
// ============================================================================

SIMD_FORCE_INLINE simd128_t simd_set1_epi8_128(int8_t v) {
	return _mm_set1_epi8(v);
}

SIMD_FORCE_INLINE simd256_t simd_set1_epi8_256(int8_t v) {
	return _mm256_set1_epi8(v);
}

SIMD_FORCE_INLINE simd128_t simd_set1_epi32_128(int32_t v) {
	return _mm_set1_epi32(v);
}

SIMD_FORCE_INLINE simd256_t simd_set1_epi32_256(int32_t v) {
	return _mm256_set1_epi32(v);
}

SIMD_FORCE_INLINE simd128_t simd_setzero_128() {
	return _mm_setzero_si128();
}

SIMD_FORCE_INLINE simd256_t simd_setzero_256() {
	return _mm256_setzero_si256();
}

// ============================================================================
// SIMD Comparison Operations
// ============================================================================

// Equal comparison (byte-wise)
SIMD_FORCE_INLINE simd128_t simd_cmpeq_epi8_128(simd128_t a, simd128_t b) {
	return _mm_cmpeq_epi8(a, b);
}

SIMD_FORCE_INLINE simd256_t simd_cmpeq_epi8_256(simd256_t a, simd256_t b) {
	return _mm256_cmpeq_epi8(a, b);
}

// Greater than comparison (signed byte-wise)
SIMD_FORCE_INLINE simd128_t simd_cmpgt_epi8_128(simd128_t a, simd128_t b) {
	return _mm_cmpgt_epi8(a, b);
}

SIMD_FORCE_INLINE simd256_t simd_cmpgt_epi8_256(simd256_t a, simd256_t b) {
	return _mm256_cmpgt_epi8(a, b);
}

// Less than comparison (implemented as reversed greater than)
SIMD_FORCE_INLINE simd128_t simd_cmplt_epi8_128(simd128_t a, simd128_t b) {
	return _mm_cmplt_epi8(a, b);
}

SIMD_FORCE_INLINE simd256_t simd_cmplt_epi8_256(simd256_t a, simd256_t b) {
	// AVX2 doesn't have _mm256_cmplt_epi8, use reversed gt
	return _mm256_cmpgt_epi8(b, a);
}

// ============================================================================
// SIMD Logical Operations
// ============================================================================

SIMD_FORCE_INLINE simd128_t simd_and_128(simd128_t a, simd128_t b) {
	return _mm_and_si128(a, b);
}

SIMD_FORCE_INLINE simd256_t simd_and_256(simd256_t a, simd256_t b) {
	return _mm256_and_si256(a, b);
}

SIMD_FORCE_INLINE simd128_t simd_or_128(simd128_t a, simd128_t b) {
	return _mm_or_si128(a, b);
}

SIMD_FORCE_INLINE simd256_t simd_or_256(simd256_t a, simd256_t b) {
	return _mm256_or_si256(a, b);
}

SIMD_FORCE_INLINE simd128_t simd_xor_128(simd128_t a, simd128_t b) {
	return _mm_xor_si128(a, b);
}

SIMD_FORCE_INLINE simd256_t simd_xor_256(simd256_t a, simd256_t b) {
	return _mm256_xor_si256(a, b);
}

SIMD_FORCE_INLINE simd128_t simd_andnot_128(simd128_t a, simd128_t b) {
	return _mm_andnot_si128(a, b);
}

SIMD_FORCE_INLINE simd256_t simd_andnot_256(simd256_t a, simd256_t b) {
	return _mm256_andnot_si256(a, b);
}

// ============================================================================
// SIMD Mask Operations
// ============================================================================

// Extract bitmask from comparison result (1 bit per byte)
SIMD_FORCE_INLINE uint16_t simd_movemask_epi8_128(simd128_t v) {
	return static_cast<uint16_t>(_mm_movemask_epi8(v));
}

SIMD_FORCE_INLINE uint32_t simd_movemask_epi8_256(simd256_t v) {
	return static_cast<uint32_t>(_mm256_movemask_epi8(v));
}

// ============================================================================
// SIMD Arithmetic Operations
// ============================================================================

SIMD_FORCE_INLINE simd128_t simd_add_epi8_128(simd128_t a, simd128_t b) {
	return _mm_add_epi8(a, b);
}

SIMD_FORCE_INLINE simd256_t simd_add_epi8_256(simd256_t a, simd256_t b) {
	return _mm256_add_epi8(a, b);
}

SIMD_FORCE_INLINE simd128_t simd_sub_epi8_128(simd128_t a, simd128_t b) {
	return _mm_sub_epi8(a, b);
}

SIMD_FORCE_INLINE simd256_t simd_sub_epi8_256(simd256_t a, simd256_t b) {
	return _mm256_sub_epi8(a, b);
}

// ============================================================================
// Bit Manipulation Utilities
// ============================================================================

// Count trailing zeros (position of first set bit from LSB)
SIMD_FORCE_INLINE uint32_t count_trailing_zeros(uint32_t v) {
	if (v == 0)
		return 32;
#ifdef _MSC_VER
	unsigned long index;
	_BitScanForward(&index, v);
	return index;
#else
	return __builtin_ctz(v);
#endif
}

SIMD_FORCE_INLINE uint32_t count_trailing_zeros64(uint64_t v) {
	if (v == 0)
		return 64;
#ifdef _MSC_VER
	unsigned long index;
	_BitScanForward64(&index, v);
	return static_cast<uint32_t>(index);
#else
	return __builtin_ctzll(v);
#endif
}

// Count leading zeros
SIMD_FORCE_INLINE uint32_t count_leading_zeros(uint32_t v) {
	if (v == 0)
		return 32;
#ifdef _MSC_VER
	unsigned long index;
	_BitScanReverse(&index, v);
	return 31 - index;
#else
	return __builtin_clz(v);
#endif
}

// Population count (number of set bits)
SIMD_FORCE_INLINE uint32_t popcount32(uint32_t v) {
#ifdef _MSC_VER
	return __popcnt(v);
#else
	return __builtin_popcount(v);
#endif
}

SIMD_FORCE_INLINE uint32_t popcount64(uint64_t v) {
#ifdef _MSC_VER
	return static_cast<uint32_t>(__popcnt64(v));
#else
	return __builtin_popcountll(v);
#endif
}

// ============================================================================
// SIMD Character Class Helpers
// ============================================================================

/**
 * Check if character is in range [low, high] inclusive
 * Returns 0xFF for chars in range, 0x00 otherwise
 */
SIMD_FORCE_INLINE simd128_t simd_in_range_128(simd128_t chars, char low, char high) {
	// chars >= low && chars <= high
	// Equivalent to: chars - low <= high - low (using unsigned subtraction)
	simd128_t offset = simd_set1_epi8_128(static_cast<int8_t>(low));
	simd128_t range = simd_set1_epi8_128(static_cast<int8_t>(high - low));

	// Subtract low from all chars (underflow wraps for chars < low)
	simd128_t shifted = simd_sub_epi8_128(chars, offset);

	// Check if shifted <= range (using unsigned comparison trick)
	// For unsigned: a <= b is equivalent to NOT(a > b)
	// But we use: max_unsigned(a,b) == b which is: a <= b
	simd128_t max_val = _mm_max_epu8(shifted, range);
	return simd_cmpeq_epi8_128(max_val, range);
}

SIMD_FORCE_INLINE simd256_t simd_in_range_256(simd256_t chars, char low, char high) {
	simd256_t offset = simd_set1_epi8_256(static_cast<int8_t>(low));
	simd256_t range = simd_set1_epi8_256(static_cast<int8_t>(high - low));

	simd256_t shifted = simd_sub_epi8_256(chars, offset);
	simd256_t max_val = _mm256_max_epu8(shifted, range);
	return simd_cmpeq_epi8_256(max_val, range);
}

/**
 * Check if any byte in the vector matches the given character
 */
SIMD_FORCE_INLINE bool simd_contains_128(simd128_t v, char c) {
	simd128_t target = simd_set1_epi8_128(c);
	simd128_t cmp = simd_cmpeq_epi8_128(v, target);
	return simd_movemask_epi8_128(cmp) != 0;
}

SIMD_FORCE_INLINE bool simd_contains_256(simd256_t v, char c) {
	simd256_t target = simd_set1_epi8_256(c);
	simd256_t cmp = simd_cmpeq_epi8_256(v, target);
	return simd_movemask_epi8_256(cmp) != 0;
}

// ============================================================================
// SIMD Dispatch Helpers
// ============================================================================

/**
 * Get the optimal SIMD width for the current CPU
 * Returns 32 for AVX2, 16 for SSE
 */
inline size_t get_optimal_simd_width() {
	CPUFeature features = get_cpu_features();
	if (has_feature(features, CPUFeature::AVX2)) {
		return 32;
	}
	return 16;
}

/**
 * Check if AVX2 is available
 */
inline bool has_avx2() {
	return has_feature(get_cpu_features(), CPUFeature::AVX2);
}

/**
 * Check if SSE4.2 is available
 */
inline bool has_sse42() {
	return has_feature(get_cpu_features(), CPUFeature::SSE42);
}

#endif // SIMD_PLATFORM_H
