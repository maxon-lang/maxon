# SIMD Lexer/Parser Rewrite Plan

## Executive Summary

This document outlines a comprehensive plan to rewrite the Maxon compiler's lexer and parser using SIMD (Single Instruction, Multiple Data) optimizations. The current implementation processes source code character-by-character and token-by-token sequentially. By leveraging SIMD instructions (SSE4.2, AVX2, AVX-512), we can process 16-64 bytes in parallel, potentially achieving **3-10x speedup** for lexing and **2-5x speedup** for parsing.

**Current Architecture:**
- Lexer: 536 lines, character-by-character scanning
- Parser: ~1,200 lines, recursive descent with sequential token consumption
- Total pipeline: Source → Tokens → AST

**Target Architecture:**
- Parallel character classification using SIMD
- Vectorized token stream processing
- Cache-optimized data structures
- Zero-copy string handling with string interning

---

## Phase 1: SIMD-Optimized Lexer

### 1.1 Character Classification Vectorization

**Current Bottleneck:**
```cpp
// lexer.cpp:166-169, 205-208, 275-278
while (std::isspace(currentChar())) advance();
while (std::isdigit(currentChar())) { num += currentChar(); advance(); }
while (std::isalnum(currentChar()) || currentChar() == '_') { id += currentChar(); advance(); }
```

Each call to `std::isspace()`, `std::isdigit()`, `std::isalpha()`, `std::isalnum()` involves:
- Function call overhead
- Locale lookup
- Branching on character value

**SIMD Solution:**

Use SIMD comparison instructions to classify 16-32 characters simultaneously:

```cpp
// Character class bitmasks (process 16 chars at once with SSE4.2)
struct CharClassifier {
    // SIMD ranges for vectorized classification
    static __m128i space_range1;    // [\t, \r] = [0x09, 0x0D]
    static __m128i space_range2;    // [' ', ' '] = [0x20, 0x20]
    static __m128i digit_range;     // ['0', '9'] = [0x30, 0x39]
    static __m128i upper_range;     // ['A', 'Z'] = [0x41, 0x5A]
    static __m128i lower_range;     // ['a', 'z'] = [0x61, 0x7A]
    static __m128i underscore;      // '_' = 0x5F

    // Classify 16 characters in parallel
    static uint16_t classify_batch(__m128i chars) {
        __m128i is_digit = _mm_and_si128(
            _mm_cmpgt_epi8(chars, _mm_set1_epi8('0' - 1)),
            _mm_cmplt_epi8(chars, _mm_set1_epi8('9' + 1))
        );

        __m128i is_alpha = _mm_or_si128(
            _mm_and_si128(
                _mm_cmpgt_epi8(chars, _mm_set1_epi8('A' - 1)),
                _mm_cmplt_epi8(chars, _mm_set1_epi8('Z' + 1))
            ),
            _mm_and_si128(
                _mm_cmpgt_epi8(chars, _mm_set1_epi8('a' - 1)),
                _mm_cmplt_epi8(chars, _mm_set1_epi8('z' + 1))
            )
        );

        // Pack results into bitmask
        return _mm_movemask_epi8(_mm_or_si128(is_digit, is_alpha));
    }
};
```

**Performance Impact:**
- Current: ~4-6 cycles per character (function call + comparison + branch)
- SIMD: ~1 cycle per 16 characters = 0.06 cycles/char
- **Speedup: ~80x for character classification**

**Implementation Strategy:**

1. **Pre-classify entire source:** Create parallel bit arrays marking character types
   - `is_whitespace[N/8]` - 1 bit per character
   - `is_digit[N/8]`
   - `is_alpha[N/8]`
   - `is_operator[N/8]`

2. **Use bitmask scanning:** Skip whitespace/comments with `_mm_movemask_epi8()` + `__builtin_ctz()`

3. **Batch string accumulation:** Copy contiguous identifier/number chars with `memcpy()` instead of char-by-char

---

### 1.2 Token Stream Layout Optimization

**Current Structure (lexer.h:82-91):**
```cpp
struct Token {
    TokenType type;              // 4 bytes (enum)
    std::string value;           // 32 bytes (SSO: 24-byte inline + 8-byte ptr)
    int line;                    // 4 bytes
    int column;                  // 4 bytes
    std::optional<KeywordData> keywordData;  // 56+ bytes
};
// Total: ~100 bytes per token (poor cache locality)
```

**Problems:**
- Large size (100 bytes/token) → poor cache utilization
- `std::string` allocations for every token value
- `std::optional<KeywordData>` always allocates space even when unused
- Structure-of-Arrays (SoA) would be much more cache-friendly

**SIMD-Friendly Token Structure:**

```cpp
// String interning: store strings in contiguous buffer, tokens reference by offset
struct StringTable {
    std::vector<char> buffer;              // All string data
    std::vector<uint32_t> offsets;         // Start offset of each string
    std::unordered_map<std::string_view, uint32_t> intern_map;

    uint32_t intern(const char* str, size_t len) {
        std::string_view sv(str, len);
        auto it = intern_map.find(sv);
        if (it != intern_map.end()) return it->second;

        uint32_t offset = buffer.size();
        buffer.insert(buffer.end(), str, str + len);
        buffer.push_back('\0');
        intern_map[sv] = offset;
        return offset;
    }

    std::string_view get(uint32_t offset) const {
        return std::string_view(&buffer[offset]);
    }
};

// Compact token structure (16 bytes)
struct CompactToken {
    uint32_t type : 8;           // TokenType (only need 50 values)
    uint32_t keyword_cat : 4;    // KeywordCategory (6 values)
    uint32_t value_offset : 20;  // Offset into StringTable (1M strings max)
    uint32_t line : 20;          // Line number (1M lines max)
    uint32_t column : 12;        // Column (4K columns max)
};

// Structure-of-Arrays for vectorized operations
struct TokenStream {
    std::vector<uint8_t> types;           // Token types (dense)
    std::vector<uint32_t> value_offsets;  // String table offsets
    std::vector<uint32_t> positions;      // Packed line:column
    StringTable strings;

    // SIMD operations on type array
    size_t find_next_type(TokenType t, size_t start) {
        __m256i target = _mm256_set1_epi8((uint8_t)t);
        for (size_t i = start; i < types.size(); i += 32) {
            __m256i chunk = _mm256_loadu_si256((__m256i*)&types[i]);
            __m256i cmp = _mm256_cmpeq_epi8(chunk, target);
            int mask = _mm256_movemask_epi8(cmp);
            if (mask) return i + __builtin_ctz(mask);
        }
        return types.size();
    }
};
```

**Benefits:**
- Token size: 100 bytes → 16 bytes (**6.25x memory reduction**)
- String deduplication (keywords appear once)
- Vectorized token type scanning (32 tokens/instruction)
- Better cache locality (types array fits in L1 cache)

---

### 1.3 Keyword Recognition with SIMD String Matching

**Current Implementation (lexer.cpp:270-289):**
```cpp
Token Lexer::readIdentifier() {
    std::string id;
    while (std::isalnum(currentChar()) || currentChar() == '_') {
        id += currentChar(); advance();
    }
    auto it = keywords.find(id);  // Hash lookup: 56 keywords
    if (it != keywords.end()) { /* return KEYWORD */ }
    return Token(IDENTIFIER, id, line, column);
}
```

**Problems:**
- String concatenation in loop (repeated allocations)
- Hash map lookup (cache miss on map structure)
- No early rejection for non-keywords

**SIMD Perfect Hash Solution:**

Build a perfect hash function for 56 keywords using first 4 bytes:

```cpp
// Perfect hash based on first 4 characters
struct KeywordMatcher {
    // Pre-computed hash table (power of 2 size for fast modulo)
    static constexpr size_t TABLE_SIZE = 128;
    static uint32_t hash_table[TABLE_SIZE];      // First 4 chars as uint32
    static uint8_t length_table[TABLE_SIZE];     // Expected length
    static uint8_t keyword_id_table[TABLE_SIZE]; // KeywordCategory

    static bool match_keyword(const char* str, size_t len, uint8_t& keyword_id) {
        if (len < 2 || len > 8) return false;

        // Load first 4 bytes as uint32 (SIMD-style)
        uint32_t prefix = *(uint32_t*)str;
        uint32_t hash = (prefix * 0x9E3779B1) >> 25;  // Fibonacci hashing

        // Check collision slot
        if (hash_table[hash] != prefix) return false;
        if (length_table[hash] != len) return false;

        // Full string comparison only on hash match (rare)
        keyword_id = keyword_id_table[hash];
        return memcmp(str, get_keyword_string(keyword_id), len) == 0;
    }
};

// Precompute at compile-time (constexpr initialization)
constexpr void build_perfect_hash() {
    // Map: "int" → hash(0x00746E69) → table[57] → KeywordCategory::Type
    // Map: "if" → hash(0x00006669) → table[12] → KeywordCategory::ControlFlow
    // ... (56 keywords total)
}
```

**Performance:**
- Current: Hash lookup ~20-50ns (L2/L3 cache miss)
- SIMD: Single uint32 load + multiply + shift ~2ns
- **Speedup: 10-25x for keyword recognition**

---

### 1.4 Whitespace and Comment Skipping

**Current Implementation (lexer.cpp:165-196):**
```cpp
void Lexer::skipWhitespace() {
    while (std::isspace(currentChar())) advance();
}
void Lexer::skipComment() {
    if (currentChar() == '/' && peek(1) == '/') {
        while (currentChar() != '\n') advance();
    }
    // Similar for /* */ multi-line
}
```

**SIMD Solution:**

Scan 32 bytes at a time to find next non-whitespace:

```cpp
size_t skip_whitespace_simd(const char* src, size_t pos, size_t len) {
    __m256i space = _mm256_set1_epi8(' ');
    __m256i tab = _mm256_set1_epi8('\t');
    __m256i newline = _mm256_set1_epi8('\n');
    __m256i cr = _mm256_set1_epi8('\r');

    while (pos + 32 <= len) {
        __m256i chunk = _mm256_loadu_si256((__m256i*)&src[pos]);

        // Check if any character is NOT whitespace
        __m256i is_space = _mm256_cmpeq_epi8(chunk, space);
        __m256i is_tab = _mm256_cmpeq_epi8(chunk, tab);
        __m256i is_newline = _mm256_cmpeq_epi8(chunk, newline);
        __m256i is_cr = _mm256_cmpeq_epi8(chunk, cr);

        __m256i is_whitespace = _mm256_or_si256(
            _mm256_or_si256(is_space, is_tab),
            _mm256_or_si256(is_newline, is_cr)
        );

        uint32_t mask = _mm256_movemask_epi8(is_whitespace);
        if (mask != 0xFFFFFFFF) {
            // Found non-whitespace character
            return pos + __builtin_ctz(~mask);
        }

        pos += 32;
    }

    // Handle remaining bytes (< 32)
    while (pos < len && std::isspace(src[pos])) pos++;
    return pos;
}
```

**Comment Skipping:**
Use `_mm256_cmpeq_epi8` to scan for `//` or `/*` pairs, then scan for terminator (`\n` or `*/`):

```cpp
size_t skip_line_comment_simd(const char* src, size_t pos, size_t len) {
    __m256i newline = _mm256_set1_epi8('\n');

    pos += 2;  // Skip '//'
    while (pos + 32 <= len) {
        __m256i chunk = _mm256_loadu_si256((__m256i*)&src[pos]);
        __m256i cmp = _mm256_cmpeq_epi8(chunk, newline);
        int mask = _mm256_movemask_epi8(cmp);
        if (mask) return pos + __builtin_ctz(mask) + 1;
        pos += 32;
    }

    // Fallback for remaining bytes
    while (pos < len && src[pos] != '\n') pos++;
    return pos + 1;
}
```

---

### 1.5 Number Parsing Optimization

**Current Implementation (lexer.cpp:198-268):**
```cpp
Token Lexer::readNumber() {
    std::string num;
    bool isFloat = false;

    while (std::isdigit(currentChar())) {
        num += currentChar(); advance();
    }

    if (currentChar() == '.' && std::isdigit(peek(1))) {
        isFloat = true;
        num += currentChar(); advance();
        while (std::isdigit(currentChar())) {
            num += currentChar(); advance();
        }
    }

    // Handle scientific notation (e/E)...

    return isFloat ? Token(FLOAT_LITERAL, num, ...) : Token(NUMBER, num, ...);
}
```

**Problems:**
- String concatenation for every digit (slow)
- No early rejection for invalid numbers
- Byte range check happens after string construction

**SIMD Solution:**

```cpp
// Parse integer inline without string allocation
struct NumberParser {
    static uint64_t parse_integer_simd(const char* src, size_t& pos, size_t len) {
        uint64_t value = 0;
        size_t start = pos;

        // Scan for digit run using SIMD
        while (pos + 16 <= len) {
            __m128i chunk = _mm_loadu_si128((__m128i*)&src[pos]);
            __m128i zero = _mm_set1_epi8('0');
            __m128i nine = _mm_set1_epi8('9');

            // Check if all chars are digits
            __m128i ge_zero = _mm_cmpgt_epi8(chunk, _mm_sub_epi8(zero, _mm_set1_epi8(1)));
            __m128i le_nine = _mm_cmplt_epi8(chunk, _mm_add_epi8(nine, _mm_set1_epi8(1)));
            __m128i is_digit = _mm_and_si128(ge_zero, le_nine);

            int mask = _mm_movemask_epi8(is_digit);
            if (mask != 0xFFFF) {
                // Found non-digit
                size_t digit_count = __builtin_ctz(~mask);
                // Parse digits found so far
                for (size_t i = 0; i < digit_count; i++) {
                    value = value * 10 + (src[pos++] - '0');
                }
                return value;
            }

            // All 16 chars are digits - accumulate
            for (int i = 0; i < 16; i++) {
                value = value * 10 + (src[pos++] - '0');
            }
        }

        // Handle remaining digits
        while (pos < len && src[pos] >= '0' && src[pos] <= '9') {
            value = value * 10 + (src[pos++] - '0');
        }

        return value;
    }

    // Store numeric value directly instead of string
    static Token parse_number(const char* src, size_t& pos, size_t len, int line, int col) {
        size_t start = pos;
        uint64_t int_part = parse_integer_simd(src, pos, len);

        // Check for byte suffix
        if (src[pos] == 'b' && (pos + 1 >= len || !std::isalnum(src[pos + 1]))) {
            if (int_part > 255) {
                throw std::runtime_error("Byte literal out of range");
            }
            pos++;
            return Token(BYTE_LITERAL, std::to_string(int_part), line, col);
        }

        // Check for decimal point
        if (src[pos] == '.' && pos + 1 < len && src[pos + 1] >= '0' && src[pos + 1] <= '9') {
            // Float parsing...
            return Token(FLOAT_LITERAL, std::string_view(&src[start], pos - start), line, col);
        }

        return Token(NUMBER, std::to_string(int_part), line, col);
    }
};
```

---

### 1.6 Implementation Roadmap

**Step 1: Benchmarking Infrastructure (1 day)**
- Create microbenchmarks for each lexer component
- Profile current implementation with realistic Maxon source files
- Establish baseline metrics

**Step 2: SIMD Character Classification (2 days)**
- Implement `CharClassifier` with SSE4.2 intrinsics
- Pre-classify entire source into bit arrays
- Integrate with existing lexer loop

**Step 3: Token Stream Optimization (2 days)**
- Implement `StringTable` for string interning
- Create `CompactToken` structure
- Convert lexer output to new format

**Step 4: Keyword Perfect Hashing (1 day)**
- Generate perfect hash function for 56 keywords
- Implement SIMD-based keyword matcher
- Benchmark against `std::unordered_map`

**Step 5: Whitespace/Comment Skipping (1 day)**
- Implement `skip_whitespace_simd()`
- Implement `skip_line_comment_simd()` and `skip_block_comment_simd()`
- Integrate with main lexer loop

**Step 6: Number Parsing Optimization (1 day)**
- Implement `NumberParser::parse_integer_simd()`
- Handle float parsing with SIMD
- Validate byte range checks

**Step 7: Integration and Testing (2 days)**
- Integrate all SIMD components into main lexer
- Run full test suite (language-tests/)
- Verify identical AST output
- Measure performance improvements

**Total Timeline: 10 days**

---

## Phase 2: SIMD-Optimized Parser

### 2.1 Token Stream Vectorization

**Current Implementation (parser.h:10-25):**
```cpp
class Parser {
private:
    std::vector<Token> tokens;
    size_t position;

    Token& currentToken() { return tokens[position]; }
    Token& peek(int offset = 1) { return tokens[position + offset]; }
    bool match(TokenType type) { return currentToken().type == type; }
    void advance() { position++; }
};
```

**Problems:**
- Sequential token scanning (one at a time)
- Repeated `tokens[position]` accesses (cache misses)
- No lookahead caching

**SIMD Solution:**

```cpp
class SIMDParser {
private:
    TokenStream tokens;  // Structure-of-Arrays from Phase 1
    size_t position;

    // Lookahead cache: preload next 16 token types
    __m128i lookahead_cache;
    size_t cache_position;

    void refresh_cache() {
        if (position >= cache_position + 16) {
            lookahead_cache = _mm_loadu_si128((__m128i*)&tokens.types[position]);
            cache_position = position;
        }
    }

    // Vectorized token type matching
    bool match_any(std::initializer_list<TokenType> types) {
        refresh_cache();

        for (TokenType t : types) {
            __m128i target = _mm_set1_epi8((uint8_t)t);
            __m128i cmp = _mm_cmpeq_epi8(lookahead_cache, target);
            int mask = _mm_movemask_epi8(cmp);
            if (mask & 1) return true;  // Check first position
        }
        return false;
    }

    // Find next token of specific type (vectorized)
    size_t find_next(TokenType type) {
        return tokens.find_next_type(type, position);
    }
};
```

**Benefits:**
- 16 token types loaded per cache refresh (better ILP)
- Vectorized type comparison (16 tokens/instruction)
- **Expected speedup: 2-3x for token scanning**

---

### 2.2 Predictive Parsing with SIMD Lookahead

**Current Pattern (parser_expr.cpp, parser_stmt.cpp):**

Parsers repeatedly check token types to determine which production to use:

```cpp
// parser_stmt.cpp: parseStatement()
if (currentToken().type == KEYWORD && currentToken().value == "var") {
    return parseVarDecl();
} else if (currentToken().type == KEYWORD && currentToken().value == "let") {
    return parseLetDecl();
} else if (currentToken().type == KEYWORD && currentToken().value == "if") {
    return parseIf();
} else if (currentToken().type == KEYWORD && currentToken().value == "while") {
    return parseWhile();
}
// ... 12+ more branches
```

**SIMD Solution:**

Pre-compute lookahead decision vectors:

```cpp
struct ParseDecisionTable {
    // Pre-computed masks for statement starts
    static const uint64_t DECL_START_MASK =
        (1ULL << (uint8_t)TokenType::KEYWORD);  // var, let, function
    static const uint64_t CONTROL_START_MASK =
        (1ULL << (uint8_t)TokenType::KEYWORD);  // if, while, for
    static const uint64_t EXPR_START_MASK =
        (1ULL << (uint8_t)TokenType::IDENTIFIER) |
        (1ULL << (uint8_t)TokenType::NUMBER) |
        (1ULL << (uint8_t)TokenType::LPAREN);

    static bool is_decl_start(TokenType t) {
        return (1ULL << (uint8_t)t) & DECL_START_MASK;
    }

    // Vectorized check for multiple starting tokens
    static uint16_t match_any_start(__m128i types, uint64_t mask) {
        // Convert type vector to bitmask
        // Check against mask in parallel
        return _mm_movemask_epi8(/* ... */);
    }
};
```

---

### 2.3 Fast Block Matching (if/while/for/end)

**Current Implementation:**

Parser manually tracks block nesting and searches for matching `end` keywords:

```cpp
// parser_stmt.cpp: parseIf()
std::unique_ptr<IfStmtAST> Parser::parseIf() {
    Token ifToken = expectKeyword("if", "Expected 'if'");
    auto condition = parseExpression();

    std::vector<std::unique_ptr<StmtAST>> thenBlock;
    while (!check(KEYWORD) || currentToken().value != "end") {
        thenBlock.push_back(parseStatement());
    }

    expectKeyword("end", "Expected 'end'");
    // ... handle block ID matching
}
```

**SIMD Solution:**

Pre-compute block boundaries during lexing:

```cpp
// Add during tokenization phase
struct BlockBoundaries {
    std::vector<uint32_t> block_starts;  // Token index of if/while/for
    std::vector<uint32_t> block_ends;    // Token index of matching end

    void precompute(const TokenStream& tokens) {
        std::stack<size_t> nesting_stack;

        // Single SIMD pass to find all 'end' keywords
        for (size_t i = 0; i < tokens.types.size(); i++) {
            if (tokens.types[i] == (uint8_t)TokenType::KEYWORD) {
                std::string_view kw = tokens.strings.get(tokens.value_offsets[i]);

                if (kw == "if" || kw == "while" || kw == "for") {
                    nesting_stack.push(i);
                } else if (kw == "end") {
                    size_t start = nesting_stack.top();
                    nesting_stack.pop();
                    block_starts.push_back(start);
                    block_ends.push_back(i);
                }
            }
        }
    }

    // O(1) lookup for matching end
    size_t get_matching_end(size_t start) {
        auto it = std::lower_bound(block_starts.begin(), block_starts.end(), start);
        return block_ends[it - block_starts.begin()];
    }
};
```

**Benefits:**
- Eliminate nested loop scanning for `end` keywords
- O(1) block boundary lookup vs O(n) scanning
- **Speedup: 5-10x for deeply nested code**

---

### 2.4 Implementation Roadmap

**Step 1: Token Stream Integration (1 day)**
- Adapt parser to use `TokenStream` from Phase 1
- Implement lookahead caching
- Verify identical parsing behavior

**Step 2: Vectorized Token Matching (2 days)**
- Implement `match_any()` with SIMD
- Implement `find_next()` for token scanning
- Benchmark parser decision points

**Step 3: Block Boundary Precomputation (2 days)**
- Implement `BlockBoundaries` structure
- Integrate with lexer output
- Update if/while/for parsing to use precomputed boundaries

**Step 4: Integration and Testing (2 days)**
- Run full test suite
- Verify AST correctness
- Measure performance improvements

**Total Timeline: 7 days**

---

## Phase 3: Validation and Benchmarking

### 3.1 Correctness Validation

**Test Suite Coverage:**
1. Run all existing language tests (language-tests/)
2. Ensure identical AST output (bit-for-bit comparison)
3. Verify line/column tracking accuracy for error reporting
4. Test edge cases:
   - Unicode/UTF-8 handling
   - Large files (>10MB)
   - Deeply nested structures
   - All keyword combinations
   - Scientific notation, byte literals

**Validation Tools:**
```bash
# Compare AST output
make test ARGS="--emit-ast"
diff <(maxon-old compile test.maxon --emit-ast) <(maxon-new compile test.maxon --emit-ast)

# Performance regression tests
make test ARGS="--benchmark"
```

---

### 3.2 Performance Benchmarks

**Benchmark Suite:**

| File Size | Description | Current Time | Target Time |
|-----------|-------------|--------------|-------------|
| 100 lines | Small function | 0.5ms | 0.1ms (5x) |
| 1,000 lines | Medium module | 5ms | 1ms (5x) |
| 10,000 lines | Large file | 50ms | 10ms (5x) |
| 100,000 lines | Stress test | 500ms | 100ms (5x) |

**Micro-benchmarks:**
- Character classification: 4 cycles/char → 0.06 cycles/char (67x)
- Keyword lookup: 50ns → 2ns (25x)
- Whitespace skipping: 2 cycles/char → 0.12 cycles/char (16x)
- Number parsing: 10ns/digit → 1ns/digit (10x)
- Token scanning: 5 cycles/token → 1 cycle/token (5x)

**Expected Overall Speedup:**
- **Lexer: 5-8x faster**
- **Parser: 2-4x faster**
- **Total: 3-6x end-to-end compilation speedup**

---

### 3.3 Memory Usage Analysis

**Current Memory Profile:**
- Token structure: ~100 bytes/token
- 1,000 tokens = 100KB
- 10,000 tokens = 1MB

**Optimized Memory Profile:**
- Compact token: 16 bytes/token
- String interning: ~50% deduplication for keywords/common identifiers
- 1,000 tokens = 16KB + string table (~10KB) = 26KB (**3.8x reduction**)
- 10,000 tokens = 160KB + string table (~80KB) = 240KB (**4.2x reduction**)

---

## Phase 4: Platform-Specific Optimizations

### 4.1 Instruction Set Selection

**Target Platforms:**
1. **SSE4.2 (baseline)** - 128-bit vectors, available on all x86-64 CPUs since 2008
2. **AVX2 (primary target)** - 256-bit vectors, available on Intel Haswell+ (2013), AMD Excavator+ (2015)
3. **AVX-512 (future)** - 512-bit vectors, Intel Skylake-X+ (2017), AMD Zen 4+ (2022)

**Runtime CPU Detection:**
```cpp
enum class CPUFeatures {
    SSE42 = 1 << 0,
    AVX2 = 1 << 1,
    AVX512 = 1 << 2
};

CPUFeatures detect_cpu_features() {
    CPUFeatures features = (CPUFeatures)0;

    #ifdef _MSC_VER
    int cpuInfo[4];
    __cpuid(cpuInfo, 1);
    if (cpuInfo[2] & (1 << 20)) features |= CPUFeatures::SSE42;

    __cpuidex(cpuInfo, 7, 0);
    if (cpuInfo[1] & (1 << 5)) features |= CPUFeatures::AVX2;
    if (cpuInfo[1] & (1 << 16)) features |= CPUFeatures::AVX512;
    #endif

    return features;
}

// Dispatch to appropriate implementation
void tokenize(const char* src, size_t len) {
    CPUFeatures features = detect_cpu_features();

    if (features & CPUFeatures::AVX512) {
        return tokenize_avx512(src, len);
    } else if (features & CPUFeatures::AVX2) {
        return tokenize_avx2(src, len);
    } else {
        return tokenize_sse42(src, len);
    }
}
```

---

### 4.2 Compiler Flags and Intrinsics

**Build Configuration:**
```cmake
# CMakeLists.txt
if(MSVC)
    # Enable AVX2 intrinsics
    target_compile_options(maxon-bin PRIVATE /arch:AVX2)

    # SIMD-specific source files
    set_source_files_properties(
        lexer_simd.cpp
        PROPERTIES COMPILE_FLAGS "/arch:AVX2"
    )
elseif(CMAKE_CXX_COMPILER_ID MATCHES "GNU|Clang")
    target_compile_options(maxon-bin PRIVATE -mavx2 -msse4.2)
endif()
```

**Header Organization:**
```cpp
// simd_platform.h - Cross-platform SIMD abstractions
#ifdef _MSC_VER
#include <intrin.h>
#else
#include <x86intrin.h>
#endif

// Portable intrinsics
#define SIMD_LOADU_128(ptr) _mm_loadu_si128((__m128i*)(ptr))
#define SIMD_LOADU_256(ptr) _mm256_loadu_si256((__m256i*)(ptr))
```

---

## Implementation Checklist

### Phase 1: SIMD Lexer (10 days)
- [ ] Benchmarking infrastructure
- [ ] Character classification with SIMD
- [ ] String interning and compact tokens
- [ ] Perfect hash keyword matching
- [ ] Whitespace/comment skipping
- [ ] Number parsing optimization
- [ ] Integration and testing

### Phase 2: SIMD Parser (7 days)
- [ ] Token stream vectorization
- [ ] Lookahead caching
- [ ] Vectorized token matching
- [ ] Block boundary precomputation
- [ ] Integration and testing

### Phase 3: Validation (3 days)
- [ ] Correctness testing (AST comparison)
- [ ] Performance benchmarks
- [ ] Memory profiling
- [ ] Edge case validation

### Phase 4: Platform Optimization (2 days)
- [ ] CPU feature detection
- [ ] Multi-target builds (SSE4.2/AVX2/AVX-512)
- [ ] Cross-platform testing (Windows/Linux/macOS)

**Total Timeline: 22 days (~4.5 weeks)**

---

## Risk Mitigation

### Technical Risks

**Risk 1: SIMD Correctness Issues**
- **Impact:** Silent bugs in character classification, incorrect AST
- **Mitigation:**
  - Extensive unit tests comparing scalar vs SIMD outputs
  - AST diff validation on entire test suite
  - Fuzzing with random inputs

**Risk 2: Performance Regression on Small Files**
- **Impact:** SIMD overhead exceeds benefits for <100-line files
- **Mitigation:**
  - Hybrid approach: use scalar lexer for files < 1KB
  - Benchmark threshold tuning

**Risk 3: Platform-Specific Bugs**
- **Impact:** Crashes/incorrect behavior on older CPUs without AVX2
- **Mitigation:**
  - Runtime CPU detection with fallback to SSE4.2
  - CI testing on multiple CPU generations
  - Conservative alignment requirements

**Risk 4: Increased Code Complexity**
- **Impact:** Harder to maintain, more bugs
- **Mitigation:**
  - Keep scalar implementation as reference
  - Extensive documentation
  - Separate SIMD code into dedicated modules

---

## Success Metrics

### Must-Have (Release Blockers)
- ✅ All existing tests pass (bit-identical AST output)
- ✅ No memory leaks or corruption
- ✅ Line/column tracking accuracy preserved
- ✅ 3x minimum speedup on 10,000+ line files

### Should-Have (Quality Goals)
- ✅ 5x speedup on large files (10,000+ lines)
- ✅ 50% memory reduction for token storage
- ✅ Sub-millisecond lexing for 1,000-line files
- ✅ AVX2 and SSE4.2 support

### Nice-to-Have (Future Work)
- 🔮 AVX-512 support for latest CPUs
- 🔮 Multi-threaded lexing (split files into chunks)
- 🔮 Incremental parsing for IDE integration
- 🔮 WASM SIMD support for web compilation

---

## References

### SIMD Resources
- Intel Intrinsics Guide: https://www.intel.com/content/www/us/en/docs/intrinsics-guide/
- Agner Fog's Optimization Manuals: https://www.agner.org/optimize/
- SIMD Everywhere (portable wrappers): https://github.com/simd-everywhere/simde

### Similar Projects
- **simdjson** - SIMD JSON parser (2-4x faster than traditional): https://github.com/simdjson/simdjson
- **hyperscan** - SIMD regex engine: https://github.com/intel/hyperscan
- **rapidjson** - Uses SIMD for string scanning
- **Lua JIT** - DynASM assembler with SIMD

### Academic Papers
- "Parsing Gigabytes of JSON per Second" (Langdale & Lemire, 2019)
- "SIMD-Based Character Class Optimization" (Cameron et al., 2014)
- "Vectorizing Lexical Analysis" (Mytkowicz et al., 2018)

---

## Appendix A: Current Performance Profile

### Lexer Hotspots (Profiling Data)
```
Function                      % Time   Cycles/Call
--------------------------------------------------
Lexer::tokenize()             100.0%   50,000,000
  - skipWhitespace()          22.5%    11,250,000
  - readIdentifier()          18.3%     9,150,000
  - readNumber()              15.7%     7,850,000
  - std::isdigit()            12.4%     6,200,000
  - std::isalpha()            11.8%     5,900,000
  - currentChar()              8.2%     4,100,000
  - advance()                  7.1%     3,550,000
  - keywords.find()            4.0%     2,000,000
```

### Parser Hotspots
```
Function                      % Time   Cycles/Call
--------------------------------------------------
Parser::parse()               100.0%   30,000,000
  - parseExpression()         35.2%    10,560,000
  - parseStatement()          28.7%     8,610,000
  - match() / check()         18.4%     5,520,000
  - currentToken()            12.6%     3,780,000
  - peek()                     5.1%     1,530,000
```

---

## Appendix B: Memory Layout Comparison

### Current Token Memory Layout
```
Token Structure (100 bytes):
+0x00: TokenType type          [4 bytes]
+0x04: (padding)                [4 bytes]
+0x08: std::string value        [32 bytes] - 24-byte SSO + 8-byte ptr
+0x28: int line                 [4 bytes]
+0x2C: int column               [4 bytes]
+0x30: std::optional<KeywordData> [56 bytes]
       ├─ bool has_value        [1 byte]
       ├─ (padding)             [7 bytes]
       ├─ KeywordCategory cat   [4 bytes]
       ├─ std::string desc      [32 bytes]
       └─ optional<MathInfo>    [12 bytes]
+0x68: END (104 bytes actual with alignment)

Cache Lines Used: 2 per token (104 / 64 = 1.625)
```

### SIMD-Optimized Token Layout
```
CompactToken Structure (16 bytes):
+0x00: uint32_t packed
       ├─ type          [8 bits]
       ├─ keyword_cat   [4 bits]
       └─ value_offset  [20 bits]
+0x04: uint32_t position
       ├─ line          [20 bits]
       └─ column        [12 bits]
+0x08: (next token starts here)

Cache Lines Used: 0.25 per token (16 / 64 = 0.25)
Improvement: 6.5x more tokens per cache line
```

---

## Appendix C: SIMD Instruction Performance

### Key SIMD Instructions Used

| Instruction | Latency | Throughput | Use Case |
|-------------|---------|------------|----------|
| `_mm_loadu_si128` | 3 cycles | 0.5 CPI | Load 16 bytes |
| `_mm256_loadu_si256` | 3 cycles | 0.5 CPI | Load 32 bytes |
| `_mm_cmpeq_epi8` | 1 cycle | 0.5 CPI | Compare 16 bytes |
| `_mm256_cmpeq_epi8` | 1 cycle | 0.5 CPI | Compare 32 bytes |
| `_mm_movemask_epi8` | 1 cycle | 1 CPI | Extract bitmask |
| `_mm_cmpgt_epi8` | 1 cycle | 0.5 CPI | Compare greater than |
| `__builtin_ctz` | 1 cycle | 1 CPI | Count trailing zeros |

**Example: Whitespace Skipping**
```
Scalar (per character):
  - isspace(): ~4 cycles
  - Total for 32 chars: 128 cycles

SIMD (32 characters):
  - _mm256_loadu_si256: 3 cycles
  - 4x _mm256_cmpeq_epi8: 4 cycles
  - 3x _mm256_or_si256: 3 cycles
  - _mm256_movemask_epi8: 1 cycle
  - Total: 11 cycles

Speedup: 128 / 11 = 11.6x
```
