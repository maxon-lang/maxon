#ifndef INTRINSICS_DEFS_H
#define INTRINSICS_DEFS_H

#include <string>
#include <vector>

// Parameter definition for intrinsics
struct IntrinsicParamDef {
	std::string type;
	bool isArrayType = false;
	std::vector<std::string> allowedTypes;

	// Single type parameter
	IntrinsicParamDef(const std::string& t) : type(t) {}

	// Multiple allowed types
	static IntrinsicParamDef anyOf(std::vector<std::string> types) {
		IntrinsicParamDef p("");
		p.allowedTypes = std::move(types);
		return p;
	}

	// Array element types
	static IntrinsicParamDef arrayOf(std::vector<std::string> elemTypes) {
		IntrinsicParamDef p("");
		p.isArrayType = true;
		p.allowedTypes = std::move(elemTypes);
		return p;
	}
};

// Complete intrinsic definition
struct IntrinsicDef {
	const char* name;
	const char* returnType;
	std::vector<IntrinsicParamDef> params;
	const char* codegenMethodName;  // Name of MIRCodeGenerator method
};

// Single source of truth for all intrinsic definitions
inline std::vector<IntrinsicDef> getIntrinsicDefinitions() {
	return {
		// String intrinsics (__string_*)
		{"__string_len", "int", {{"_ManagedString"}}, "intrinsic_string_len"},
		{"__string_byte_at", "byte", {{"_ManagedString"}, {"int"}}, "intrinsic_string_byte_at"},
		{"__string_slice", "substring", {{"_ManagedString"}, {"int"}, {"int"}}, "intrinsic_string_slice"},
		{"__string_concat", "_ManagedString", {{"_ManagedString"}, {"_ManagedString"}}, "intrinsic_string_concat"},
		{"__string_make_unique", "_ManagedString", {{"_ManagedString"}}, "intrinsic_string_make_unique"},
		{"__string_set_byte", "void", {{"_ManagedString"}, {"int"}, IntrinsicParamDef::anyOf({"byte", "char"})}, "intrinsic_string_set_byte"},
		{"__string_get_refcount", "int", {{"_ManagedString"}}, "intrinsic_string_get_refcount"},
		{"__string_to_cstring", "cstring", {{"_ManagedString"}}, "intrinsic_string_to_cstring"},
		{"__string_from_chars", "_ManagedString", {IntrinsicParamDef::arrayOf({"char", "byte"}), {"int"}}, "intrinsic_string_from_chars"},

		// CString intrinsics (__cstring_*)
		{"__cstring_len", "int", {{"cstring"}}, "intrinsic_cstring_len"},
		{"__cstring_write_stdout", "int", {{"cstring"}}, "intrinsic_cstring_write_stdout"},

		// Substring intrinsics (__substring_*)
		{"__substring_len", "int", {{"substring"}}, "intrinsic_substring_len"},
		{"__substring_byte_at", "byte", {{"substring"}, {"int"}}, "intrinsic_substring_byte_at"},
		{"__substring_iter_pos", "int", {{"substring"}}, "intrinsic_substring_iter_pos"},
		{"__substring_with_iter_pos", "substring", {{"substring"}, {"int"}}, "intrinsic_substring_with_iter_pos"},
		{"__substring_to_string", "_ManagedString", {{"substring"}}, "intrinsic_substring_to_string"},
		{"__substring_slice", "substring", {{"substring"}, {"int"}, {"int"}}, "intrinsic_substring_slice"},
		{"__substring_parent_managed", "_ManagedString", {{"substring"}}, "intrinsic_substring_parent_managed"},
		{"__substring_byte_offset", "int", {{"substring"}}, "intrinsic_substring_byte_offset"},
	};
}

#endif // INTRINSICS_DEFS_H
