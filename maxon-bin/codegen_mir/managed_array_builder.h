#ifndef MANAGED_ARRAY_BUILDER_H
#define MANAGED_ARRAY_BUILDER_H

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
 * ManagedArrayBuilder - Helper class for managed array code generation
 *
 * This class provides a clean API for generating MIR code that manipulates
 * Maxon's managed array types. It encapsulates the low-level struct layouts
 * and common patterns for:
 * - Array allocation with proper header/refcount handling
 * - Field extraction from managed array structs
 * - Scope-based cleanup tracking
 * - Reference counting operations (retain/release)
 * - Element access with stride calculations
 *
 * ## Memory Layout
 *
 * __ManagedArrayData<T>: { _buffer ptr, _len i32, _capacity i32 }
 *   - Field 0: _buffer - pointer to array element data
 *   - Field 1: _len - current number of elements
 *   - Field 2: _capacity - 0 = stack allocated, >0 = heap allocated
 *
 * Heap-allocated buffers use this layout:
 * ```
 * +----------+----------+----------------+
 * | refcount | dataSize | element data   |
 * | (i32)    | (i32)    | ...            |
 * +----------+----------+----------------+
 * ^          ^          ^
 * |          |          +-- data pointer (returned by _managed_array_alloc)
 * |          +-- offset 4: data size in bytes
 * +-- offset 0: refcount starts at 1
 * ```
 *
 * ## Usage Example
 *
 * ```cpp
 * ManagedArrayBuilder mab(codegen, "int");
 *
 * // Get array fields
 * auto dataPtr = mab.getDataPtr(managedPtr);
 * auto len = mab.getLength(managedPtr);
 *
 * // Allocate new buffer with tracking
 * auto newBuffer = mab.allocateBuffer(capacityVal, "array grow");
 *
 * // Get element pointer
 * auto elemPtr = mab.getElementPtr(dataPtr, index);
 *
 * // Track for cleanup
 * mab.trackHeapArray("arr", structAlloca, "int", true);
 * ```
 */
class ManagedArrayBuilder {
  public:
	/**
	 * Construct a ManagedArrayBuilder for arrays of the given element type.
	 * @param gen The code generator to use
	 * @param elementType The element type name (e.g., "int", "float", "MyStruct")
	 */
	ManagedArrayBuilder(MIRCodeGenerator &gen, const std::string &elementType);

	// ========== Type Accessors ==========

	/** Get the __ManagedArrayData<T> struct type */
	mir::MIRType *getManagedArrayDataType();

	/** Get the array<T> struct type (wrapper with iterIndex) */
	mir::MIRType *getArrayStructType();

	/** Get the MIR type for the element */
	mir::MIRType *getElementMIRType();

	/** Get element size in bytes */
	int getElementSize();

	/** Get the element type name */
	const std::string &getElementTypeName() const { return elementType_; }

	// ========== Field Extraction ==========

	/**
	 * Get the buffer pointer field (field 0) from __ManagedArrayData.
	 * @param managedPtr Pointer to the __ManagedArrayData struct
	 * @param name Optional name for the resulting value
	 * @return MIRValue representing the buffer pointer
	 */
	mir::MIRValue *getDataPtr(mir::MIRValue *managedPtr, const std::string &name = "arr.buffer");

	/**
	 * Get the length field (field 1) from __ManagedArrayData.
	 * @param managedPtr Pointer to the __ManagedArrayData struct
	 * @param name Optional name for the resulting value
	 * @return MIRValue representing the length (i32)
	 */
	mir::MIRValue *getLength(mir::MIRValue *managedPtr, const std::string &name = "arr.len");

	/**
	 * Get the capacity field (field 2) from __ManagedArrayData.
	 * Capacity of 0 indicates stack-allocated buffer.
	 * @param managedPtr Pointer to the __ManagedArrayData struct
	 * @param name Optional name for the resulting value
	 * @return MIRValue representing the capacity (i32)
	 */
	mir::MIRValue *getCapacity(mir::MIRValue *managedPtr, const std::string &name = "arr.cap");

	/**
	 * Check if array buffer is heap-allocated (capacity > 0).
	 * Capacity values: 0 = stack, >0 = heap allocated
	 * @param managedPtr Pointer to the __ManagedArrayData struct
	 * @param name Optional name for the resulting value
	 * @return MIRValue representing a boolean (i1) - true if heap allocated
	 */
	mir::MIRValue *isHeapAllocated(mir::MIRValue *managedPtr, const std::string &name = "is.heap");

	// ========== Field Modification ==========

	/**
	 * Set the buffer pointer field (field 0) of __ManagedArrayData.
	 * @param managedPtr Pointer to the __ManagedArrayData struct
	 * @param dataPtr The new buffer pointer value
	 */
	void setDataPtr(mir::MIRValue *managedPtr, mir::MIRValue *dataPtr);

	/**
	 * Set the length field (field 1) of __ManagedArrayData.
	 * @param managedPtr Pointer to the __ManagedArrayData struct
	 * @param length The new length value
	 */
	void setLength(mir::MIRValue *managedPtr, mir::MIRValue *length);

	/**
	 * Set the capacity field (field 2) of __ManagedArrayData.
	 * @param managedPtr Pointer to the __ManagedArrayData struct
	 * @param capacity The new capacity value
	 */
	void setCapacity(mir::MIRValue *managedPtr, mir::MIRValue *capacity);

	// ========== Allocation ==========

	/**
	 * Allocate a new array buffer with managed header.
	 * The buffer includes an 8-byte header (refcount + data_size).
	 *
	 * @param numElements Number of elements to allocate
	 * @param tag A descriptive tag for memory tracking
	 * @return Pointer to the data area (after the header)
	 */
	mir::MIRValue *allocateBuffer(mir::MIRValue *numElements, const std::string &tag);

	/**
	 * Allocate a stack-local __ManagedArrayData struct.
	 * @param name Name for the alloca
	 * @return Pointer to the allocated struct
	 */
	mir::MIRValue *allocateManagedStruct(const std::string &name = "managed.array.alloc");

	/**
	 * Allocate a stack-local array<T> struct (with iterIndex).
	 * @param name Name for the alloca
	 * @return Pointer to the allocated struct
	 */
	mir::MIRValue *allocateArrayStruct(const std::string &name = "array.alloc");

	// ========== Struct Population ==========

	/**
	 * Populate all fields of __ManagedArrayData struct.
	 * @param structPtr Pointer to the struct to populate
	 * @param dataPtr Buffer pointer to store in field 0
	 * @param length Length value to store in field 1
	 * @param capacity Capacity value to store in field 2
	 */
	void populateManagedStruct(mir::MIRValue *structPtr, mir::MIRValue *dataPtr, mir::MIRValue *length,
							   mir::MIRValue *capacity);

	// ========== Element Access ==========

	/**
	 * Get pointer to element at given index.
	 * Handles stride calculation based on element size.
	 * @param dataPtr Pointer to the array data buffer
	 * @param index Element index (i32)
	 * @param name Optional name for the resulting value
	 * @return Pointer to the element
	 */
	mir::MIRValue *getElementPtr(mir::MIRValue *dataPtr, mir::MIRValue *index, const std::string &name = "elem.ptr");

	// ========== Reference Counting ==========

	/**
	 * Emit code to release array buffer if heap-allocated.
	 * This decrements the refcount and frees memory if refcount reaches 0.
	 * Does nothing for stack-allocated arrays.
	 *
	 * @param managedPtr Pointer to __ManagedArrayData struct
	 * @param tag Tag for memory tracking
	 */
	void emitReleaseIfHeap(mir::MIRValue *managedPtr, const std::string &tag);

	/**
	 * Emit code to retain (increment refcount) array buffer if heap-allocated.
	 * Does nothing for stack-allocated arrays.
	 *
	 * @param managedPtr Pointer to __ManagedArrayData struct
	 */
	void emitRetainIfHeap(mir::MIRValue *managedPtr);

	/**
	 * Emit unconditional release call on a data pointer.
	 * Use this when you know the pointer is heap-allocated.
	 *
	 * @param dataPtr Pointer to array data (from _managed_array_alloc)
	 * @param tag Tag for memory tracking
	 */
	void emitRelease(mir::MIRValue *dataPtr, const std::string &tag);

	/**
	 * Emit unconditional retain call on a data pointer.
	 * Use this when you know the pointer is heap-allocated.
	 *
	 * @param dataPtr Pointer to array data
	 */
	void emitRetain(mir::MIRValue *dataPtr);

	// ========== Scope Tracking ==========

	/**
	 * Track a heap-allocated array for cleanup at scope exit.
	 * @param name Descriptive name for the allocation
	 * @param structAlloca Alloca for the array struct
	 * @param elementType Element type name
	 * @param isDynamic True if the buffer can be reallocated (grow)
	 */
	void trackHeapArray(const std::string &name, mir::MIRValue *structAlloca, const std::string &elementType,
						bool isDynamic);

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
	 * @param name Function name (e.g., "_managed_array_alloc")
	 * @param returnType Return type
	 * @param paramTypes Parameter types
	 * @return The function declaration/definition
	 */
	mir::MIRFunction *getOrDeclareFunction(const std::string &name, mir::MIRType *returnType,
										   const std::vector<mir::MIRType *> &paramTypes);

  private:
	MIRCodeGenerator &gen_;
	mir::MIRModule *module_;
	std::string elementType_;
};

#endif // MANAGED_ARRAY_BUILDER_H
