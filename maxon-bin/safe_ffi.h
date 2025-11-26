/**
 * Safe FFI Implementation for Maxon
 *
 * This file implements subprocess isolation for extern function calls.
 * All extern functions are executed in a separate worker process, with
 * arguments passed via shared memory and synchronization via semaphores.
 *
 * Architecture:
 *
 * Main Process                    FFI Worker Process
 * ============                    ==================
 *     |                                  |
 *     |   [first extern call]            |
 *     |-- Init shared memory region ---->|  (lazy init)
 *     |-- Init semaphores -------------->|  (lazy init)
 *     |-- Spawn worker process --------->| (starts on demand)
 *     |                                  |
 *     |   [extern call]                  |
 *     |-- Serialize args to shm -------->|
 *     |-- Signal request semaphore ----->|
 *     |                                  |-- Deserialize args
 *     |                                  |-- Call extern function
 *     |                                  |-- Serialize result
 *     |<- Signal response semaphore -----|
 *     |-- Deserialize result             |
 *     |                                  |
 *     |   [program exit]                 |
 *     |-- Signal shutdown --------------->|
 *     |                                  |-- Exit
 *
 * Shared Memory Layout (4KB default):
 *
 * Offset  Size    Field
 * ------  ----    -----
 * 0       4       Magic number (0x4D584649 = "MXFI")
 * 4       4       Version (1)
 * 8       4       Request type: 0=idle, 1=call, 2=shutdown
 * 12      4       Function ID (index in extern function table)
 * 16      4       Argument count
 * 20      4       Return type code (0=void, 1=int, 2=float, 3=ptr, etc.)
 * 24      4       Status: 0=idle, 1=processing, 2=success, 3=error
 * 28      4       Error code (if status==error)
 * 32      N       Serialized arguments
 * 32+N    M       Serialized return value
 *
 * Serialization Format:
 * Each value is prefixed with a 1-byte type tag:
 *   0x01 = i32 (4 bytes, little-endian)
 *   0x02 = f64 (8 bytes, IEEE 754)
 *   0x03 = bool (1 byte)
 *   0x04 = ptr (8 bytes, opaque handle - NOT dereferenceable)
 *   0x05 = char (1 byte)
 *   0x00 = void (no data)
 */

#ifndef SAFE_FFI_H
#define SAFE_FFI_H

#include <cstdint>
#include <string>
#include <vector>

namespace safeffi {

// Shared memory magic number: "MXFI" in little-endian
constexpr uint32_t MAGIC = 0x4946584D;
constexpr uint32_t VERSION = 1;
constexpr size_t DEFAULT_SHM_SIZE = 4096;
constexpr uint32_t DEFAULT_TIMEOUT_MS = 30000; // 30 seconds

// Request types
enum class RequestType : uint32_t {
	Idle = 0,
	Call = 1,
	Shutdown = 2
};

// Status codes
enum class Status : uint32_t {
	Idle = 0,
	Processing = 1,
	Success = 2,
	Error = 3
};

// Type tags for serialization
enum class TypeTag : uint8_t {
	Void = 0x00,
	Int32 = 0x01,
	Float64 = 0x02,
	Bool = 0x03,
	Ptr = 0x04,
	Char = 0x05
};

// Error codes
enum class ErrorCode : uint32_t {
	None = 0,
	WorkerCrashed = 1,
	Timeout = 2,
	SerializationError = 3,
	UnknownFunction = 4,
	InvalidArguments = 5
};

// Shared memory header
struct ShmHeader {
	uint32_t magic;
	uint32_t version;
	uint32_t requestType;
	uint32_t functionId;
	uint32_t argCount;
	uint32_t returnType;
	uint32_t status;
	uint32_t errorCode;
	// Data follows at offset 32
};

static_assert(sizeof(ShmHeader) == 32, "ShmHeader must be 32 bytes");

// Function descriptor for the extern function table
struct ExternFunctionDesc {
	std::string name;
	std::vector<TypeTag> paramTypes;
	TypeTag returnType;
};

} // namespace safeffi

#endif // SAFE_FFI_H
