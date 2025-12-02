#include "intrinsic_codegen.h"
#include "../codegen_mir.h"
#include "../intrinsics.h"

IntrinsicCodegenRegistry& IntrinsicCodegenRegistry::instance() {
	static IntrinsicCodegenRegistry registry;
	return registry;
}

IntrinsicCodegenRegistry::IntrinsicCodegenRegistry() {
	// Map method names to their implementations
	std::map<std::string, IntrinsicCodegenMethod> methodsByName = {
		// String intrinsics
		{"intrinsic_string_len", &MIRCodeGenerator::intrinsic_string_len},
		{"intrinsic_string_byte_at", &MIRCodeGenerator::intrinsic_string_byte_at},
		{"intrinsic_string_slice", &MIRCodeGenerator::intrinsic_string_slice},
		{"intrinsic_string_concat", &MIRCodeGenerator::intrinsic_string_concat},
		{"intrinsic_string_make_unique", &MIRCodeGenerator::intrinsic_string_make_unique},
		{"intrinsic_string_set_byte", &MIRCodeGenerator::intrinsic_string_set_byte},
		{"intrinsic_string_get_refcount", &MIRCodeGenerator::intrinsic_string_get_refcount},
		{"intrinsic_string_to_cstring", &MIRCodeGenerator::intrinsic_string_to_cstring},
		{"intrinsic_string_from_chars", &MIRCodeGenerator::intrinsic_string_from_chars},

		// Cstring intrinsics
		{"intrinsic_cstring_len", &MIRCodeGenerator::intrinsic_cstring_len},
		{"intrinsic_cstring_write_stdout", &MIRCodeGenerator::intrinsic_cstring_write_stdout},

		// Substring intrinsics
		{"intrinsic_substring_len", &MIRCodeGenerator::intrinsic_substring_len},
		{"intrinsic_substring_byte_at", &MIRCodeGenerator::intrinsic_substring_byte_at},
		{"intrinsic_substring_iter_pos", &MIRCodeGenerator::intrinsic_substring_iter_pos},
		{"intrinsic_substring_with_iter_pos", &MIRCodeGenerator::intrinsic_substring_with_iter_pos},
		{"intrinsic_substring_to_string", &MIRCodeGenerator::intrinsic_substring_to_string},
		{"intrinsic_substring_slice", &MIRCodeGenerator::intrinsic_substring_slice},
		{"intrinsic_substring_parent_managed", &MIRCodeGenerator::intrinsic_substring_parent_managed},
		{"intrinsic_substring_byte_offset", &MIRCodeGenerator::intrinsic_substring_byte_offset},
	};

	// Populate methods_ by looking up codegen method names from IntrinsicRegistry
	for (const auto& [intrinsicName, info] : IntrinsicRegistry::instance().getAll()) {
		const char* methodName = IntrinsicRegistry::instance().getCodegenMethodName(intrinsicName);
		if (methodName) {
			auto it = methodsByName.find(methodName);
			if (it != methodsByName.end()) {
				methods_[intrinsicName] = it->second;
			}
		}
	}
}

IntrinsicCodegenMethod IntrinsicCodegenRegistry::getMethod(const std::string& name) const {
	auto it = methods_.find(name);
	if (it != methods_.end()) {
		return it->second;
	}
	return nullptr;
}

bool IntrinsicCodegenRegistry::hasMethod(const std::string& name) const {
	return methods_.find(name) != methods_.end();
}
