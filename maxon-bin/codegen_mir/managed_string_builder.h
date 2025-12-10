#ifndef MANAGED_STRING_BUILDER_H
#define MANAGED_STRING_BUILDER_H

#include "../mir/mir.h"
#include "../mir/mir_builder.h"
#include <string>
#include <vector>

// Forward declarations
class MIRCodeGenerator;

namespace mir {
class MIRModule;
class MIRValue;
class MIRType;
class MIRFunction;
} // namespace mir

/**
 * ManagedStringBuilder - Helper class for managed string code generation
 *
 * This class provides a clean API for generating MIR code that manipulates
 * Maxon's managed string types. It encapsulates the low-level struct layouts
 * and common patterns for:
 * - String allocation with proper header/refcount handling
 * - Field extraction from managed string structs
 * - Scope-based cleanup tracking
 * - Reference counting operations (retain/release)
 *
 * ## Memory Layout
 *
 * Heap-allocated strings use this layout:
 * ```
 * +--------+--------+------+------+
 * |refcount|data_sz | data | null |
 * | (i32)  | (i32)  |      | term |
 * +--------+--------+------+------+
 * ^        ^        ^
 * |        |        +-- data pointer (returned by _managed_string_alloc)
 * |        +-- offset 4: data size stored here
 * +-- offset 0: refcount starts at 1
 * ```
 *
 * The __ManagedStringData struct holds:
 * - Field 0: data (ptr) - points to string data (or inline SSO data)
 * - Field 1: length (i32) - number of bytes in string
 * - Field 2: capacity (i32) - capacity for heap strings, -1 for SSO
 *
 * ## Usage Example
 *
 * ```cpp
 * ManagedStringBuilder msb(codegen);
 *
 * // Get string fields
 * auto dataPtr = msb.getDataPtr(managedPtr);
 * auto len = msb.getLength(managedPtr);
 *
 * // Allocate new buffer with tracking
 * auto newBuffer = msb.allocateBuffer(capacityVal, "string concat");
 *
 * // Track for cleanup
 * msb.trackHeapString("result", newBuffer);
 * ```
 */
class ManagedStringBuilder {
  public:
	/**
	 * Construct a ManagedStringBuilder for use with the given code generator.
	 * The builder holds a reference to the generator's module, builder, etc.
	 */
	explicit ManagedStringBuilder(MIRCodeGenerator &gen);

	// ========== Type Accessors ==========

	/** Get the __ManagedStringData struct type */
	mir::MIRType *getManagedStringType();

	/** Get the substring struct type */
	mir::MIRType *getSubstringType();

	/** Get the cstring struct type */
	mir::MIRType *getCstringType();

	// ========== Field Extraction ==========

	/**
	 * Get the data pointer field (field 0) from a __ManagedStringData struct.
	 * @param managedPtr Pointer to the __ManagedStringData struct
	 * @param name Optional name for the resulting value
	 * @return MIRValue representing the data pointer
	 */
	mir::MIRValue *getDataPtr(mir::MIRValue *managedPtr, const std::string &name = "data.ptr");

	/**
	 * Get the length field (field 1) from a __ManagedStringData struct.
	 * @param managedPtr Pointer to the __ManagedStringData struct
	 * @param name Optional name for the resulting value
	 * @return MIRValue representing the length (i32)
	 */
	mir::MIRValue *getLength(mir::MIRValue *managedPtr, const std::string &name = "len");

	/**
	 * Get the capacity field (field 2) from a __ManagedStringData struct.
	 * A capacity of -1 indicates SSO (small string optimization).
	 * @param managedPtr Pointer to the __ManagedStringData struct
	 * @param name Optional name for the resulting value
	 * @return MIRValue representing the capacity (i32)
	 */
	mir::MIRValue *getCapacity(mir::MIRValue *managedPtr, const std::string &name = "cap");

	/**
	 * Check if a string is heap-allocated (capacity > 0).
	 * Capacity values: -1 = SSO, 0 = constant string, >0 = heap allocated
	 * @param managedPtr Pointer to the __ManagedStringData struct
	 * @param name Optional name for the resulting value
	 * @return MIRValue representing a boolean (i1) - true if heap allocated
	 */
	mir::MIRValue *isHeapAllocated(mir::MIRValue *managedPtr, const std::string &name = "is.heap");

	// ========== Allocation ==========

	/**
	 * Allocate a new string buffer with the managed header.
	 * The buffer includes an 8-byte header (refcount + data_size) and is
	 * properly tracked for memory debugging.
	 *
	 * @param capacity The number of bytes to allocate for data (excluding header)
	 * @param tag A descriptive tag for memory tracking
	 * @return Pointer to the data area (after the header)
	 */
	mir::MIRValue *allocateBuffer(mir::MIRValue *capacity, const std::string &tag);

	/**
	 * Allocate a stack-local __ManagedStringData struct.
	 * @param name Name for the alloca
	 * @return Pointer to the allocated struct
	 */
	mir::MIRValue *allocateManagedStruct(const std::string &name = "managed.alloc");

	/**
	 * Allocate a stack-local cstring struct.
	 * @param name Name for the alloca
	 * @return Pointer to the allocated struct
	 */
	mir::MIRValue *allocateCstringStruct(const std::string &name = "cstring.alloc");

	/**
	 * Allocate a stack-local substring struct.
	 * @param name Name for the alloca
	 * @return Pointer to the allocated struct
	 */
	mir::MIRValue *allocateSubstringStruct(const std::string &name = "substring.alloc");

	// ========== Struct Population ==========

	/**
	 * Populate all fields of a __ManagedStringData struct.
	 * @param structPtr Pointer to the struct to populate
	 * @param dataPtr Data pointer to store in field 0
	 * @param length Length value to store in field 1
	 * @param capacity Capacity value to store in field 2
	 */
	void populateManagedStruct(mir::MIRValue *structPtr, mir::MIRValue *dataPtr, mir::MIRValue *length,
							   mir::MIRValue *capacity);

	/**
	 * Populate all fields of a cstring struct.
	 * @param structPtr Pointer to the cstring struct
	 * @param dataPtr Data pointer (field 0)
	 * @param length Length value (field 1)
	 * @param managedPtr Original managed string pointer for refcount (field 2)
	 */
	void populateCstringStruct(mir::MIRValue *structPtr, mir::MIRValue *dataPtr, mir::MIRValue *length,
							   mir::MIRValue *managedPtr);

	/**
	 * Populate all fields of a substring struct.
	 * Substring layout: { _parentManaged ptr, _ptr ptr, _len i32, _iterPos i32 }
	 * @param structPtr Pointer to the substring struct
	 * @param parentManaged Parent managed string pointer (field 0)
	 * @param dataPtr Data pointer into parent's buffer (field 1)
	 * @param length Length in bytes (field 2)
	 * @param iterPos Iterator position in grapheme clusters (field 3)
	 */
	void populateSubstringStruct(mir::MIRValue *structPtr, mir::MIRValue *parentManaged, mir::MIRValue *dataPtr,
								 mir::MIRValue *length, mir::MIRValue *iterPos);

	// ========== Reference Counting ==========

	/**
	 * Emit code to release a string buffer if it's heap-allocated.
	 * This decrements the refcount and frees memory if refcount reaches 0.
	 * Does nothing for SSO strings.
	 *
	 * @param managedPtr Pointer to __ManagedStringData struct (or data pointer)
	 * @param tag Tag for memory tracking
	 */
	void emitReleaseIfHeap(mir::MIRValue *managedPtr, const std::string &tag);

	/**
	 * Emit code to retain (increment refcount) a string buffer if heap-allocated.
	 * Does nothing for SSO strings.
	 *
	 * @param managedPtr Pointer to __ManagedStringData struct
	 */
	void emitRetainIfHeap(mir::MIRValue *managedPtr);

	/**
	 * Emit unconditional release call on a data pointer.
	 * Use this when you know the pointer is heap-allocated.
	 *
	 * @param dataPtr Pointer to string data (from _managed_string_alloc)
	 * @param tag Tag for memory tracking
	 */
	void emitRelease(mir::MIRValue *dataPtr, const std::string &tag);

	/**
	 * Emit unconditional retain call on a data pointer.
	 * Use this when you know the pointer is heap-allocated.
	 *
	 * @param dataPtr Pointer to string data
	 */
	void emitRetain(mir::MIRValue *dataPtr);

	// ========== Scope Tracking ==========

	/**
	 * Track a heap-allocated string for cleanup at scope exit.
	 * @param name Descriptive name for the allocation
	 * @param dataPtrAlloca Alloca holding the data pointer
	 */
	void trackHeapString(const std::string &name, mir::MIRValue *dataPtrAlloca);

	/**
	 * Track a cstring for cleanup at scope exit.
	 * @param name Descriptive name for the cstring
	 * @param cstringAlloca Alloca holding the cstring struct
	 */
	void trackCstring(const std::string &name, mir::MIRValue *cstringAlloca);

	/**
	 * Track a substring for cleanup at scope exit.
	 * @param name Descriptive name for the substring
	 * @param substringAlloca Alloca holding the substring struct
	 */
	void trackSubstring(const std::string &name, mir::MIRValue *substringAlloca);

	// ========== Utility ==========

	/**
	 * Create a global string constant for use as a tag.
	 * @param name Internal name for the global
	 * @param content The string content
	 * @return Pointer to the global string
	 */
	mir::MIRValue *createTag(const std::string &name, const std::string &content);

	/**
	 * Get or declare a runtime function by name.
	 * @param name Function name (e.g., "_managed_string_alloc")
	 * @param returnType Return type
	 * @param paramTypes Parameter types
	 * @return The function declaration/definition
	 */
	mir::MIRFunction *getOrDeclareFunction(const std::string &name, mir::MIRType *returnType,
										   const std::vector<mir::MIRType *> &paramTypes);

  private:
	MIRCodeGenerator &gen_;
	mir::MIRModule *module_;
};

#endif // MANAGED_STRING_BUILDER_H
