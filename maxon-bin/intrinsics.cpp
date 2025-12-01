#include "intrinsics.h"

IntrinsicRegistry& IntrinsicRegistry::instance() {
	static IntrinsicRegistry registry;
	return registry;
}

IntrinsicRegistry::IntrinsicRegistry() {
	// String intrinsics (__string_*)
	registerIntrinsic("__string_len", "int", {{"_ManagedString"}});
	registerIntrinsic("__string_byte_at", "byte", {{"_ManagedString"}, {"int"}});
	registerIntrinsic("__string_slice", "substring", {{"_ManagedString"}, {"int"}, {"int"}});
	registerIntrinsic("__string_concat", "_ManagedString", {{"_ManagedString"}, {"_ManagedString"}});
	registerIntrinsic("__string_make_unique", "_ManagedString", {{"_ManagedString"}});
	registerIntrinsic("__string_set_byte", "void", {{"_ManagedString"}, {"int"}, IntrinsicParam::anyOf({"byte", "char"})});
	registerIntrinsic("__string_get_refcount", "int", {{"_ManagedString"}});
	registerIntrinsic("__string_to_cstring", "cstring", {{"_ManagedString"}});
	registerIntrinsic("__string_from_chars", "_ManagedString", {IntrinsicParam::arrayOf({"char", "byte"}), {"int"}});

	// CString intrinsics (__cstring_*)
	registerIntrinsic("__cstring_len", "int", {{"cstring"}});
	registerIntrinsic("__cstring_write_stdout", "int", {{"cstring"}});

	// Substring intrinsics (__substring_*)
	registerIntrinsic("__substring_len", "int", {{"substring"}});
	registerIntrinsic("__substring_byte_at", "byte", {{"substring"}, {"int"}});
	registerIntrinsic("__substring_iter_pos", "int", {{"substring"}});
	registerIntrinsic("__substring_with_iter_pos", "substring", {{"substring"}, {"int"}});
	registerIntrinsic("__substring_to_string", "_ManagedString", {{"substring"}});
	registerIntrinsic("__substring_slice", "substring", {{"substring"}, {"int"}, {"int"}});
}

void IntrinsicRegistry::registerIntrinsic(const std::string& name, const std::string& returnType,
										  std::vector<IntrinsicParam> params) {
	intrinsics_[name] = IntrinsicInfo(name, returnType, std::move(params));
}

const IntrinsicInfo* IntrinsicRegistry::lookup(const std::string& name) const {
	auto it = intrinsics_.find(name);
	if (it != intrinsics_.end()) {
		return &it->second;
	}
	return nullptr;
}

bool IntrinsicRegistry::isIntrinsic(const std::string& name) const {
	// Check if name starts with __ and is in our registry
	if (name.size() < 2 || name[0] != '_' || name[1] != '_') {
		return false;
	}
	return intrinsics_.find(name) != intrinsics_.end();
}

std::string IntrinsicRegistry::validateArgType(const IntrinsicParam& param, const std::string& actualType) const {
	if (param.isArrayType) {
		// Check if actualType is an array with one of the allowed element types
		// Array types look like "[N]char" or "[12]byte"
		for (const auto& elemType : param.allowedTypes) {
			if (actualType.find("]" + elemType) != std::string::npos) {
				return ""; // Valid
			}
		}
		// Build error message
		std::string expected = "array of ";
		for (size_t i = 0; i < param.allowedTypes.size(); ++i) {
			if (i > 0) expected += " or ";
			expected += param.allowedTypes[i];
		}
		return "expected " + expected + ", got " + actualType;
	}

	// Multiple allowed types (non-array)
	if (!param.allowedTypes.empty()) {
		for (const auto& allowedType : param.allowedTypes) {
			if (actualType == allowedType) {
				return ""; // Valid
			}
		}
		// Build error message
		std::string expected;
		for (size_t i = 0; i < param.allowedTypes.size(); ++i) {
			if (i > 0) expected += " or ";
			expected += param.allowedTypes[i];
		}
		return "expected " + expected + ", got " + actualType;
	}

	// Simple type match
	if (actualType != param.type) {
		return "expected " + param.type + ", got " + actualType;
	}
	return ""; // Valid
}
