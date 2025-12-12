#include "intrinsic_codegen.h"
#include "../codegen_mir.h"
#include "../intrinsics.h"

IntrinsicCodegenRegistry &IntrinsicCodegenRegistry::instance() {
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

		// File I/O intrinsics
		{"intrinsic_read_file", &MIRCodeGenerator::intrinsic_read_file},
		{"intrinsic_write_file", &MIRCodeGenerator::intrinsic_write_file},
		{"intrinsic_write_file_binary", &MIRCodeGenerator::intrinsic_write_file_binary},

		// Directory intrinsics
		{"intrinsic_list_directory", &MIRCodeGenerator::intrinsic_list_directory},
		{"intrinsic_is_directory", &MIRCodeGenerator::intrinsic_is_directory},

		// Process intrinsics
		{"intrinsic_execute_process", &MIRCodeGenerator::intrinsic_execute_process},

		// Substring intrinsics
		{"intrinsic_substring_len", &MIRCodeGenerator::intrinsic_substring_len},
		{"intrinsic_substring_byte_at", &MIRCodeGenerator::intrinsic_substring_byte_at},
		{"intrinsic_substring_iter_pos", &MIRCodeGenerator::intrinsic_substring_iter_pos},
		{"intrinsic_substring_with_iter_pos", &MIRCodeGenerator::intrinsic_substring_with_iter_pos},
		{"intrinsic_substring_to_string", &MIRCodeGenerator::intrinsic_substring_to_string},
		{"intrinsic_substring_slice", &MIRCodeGenerator::intrinsic_substring_slice},
		{"intrinsic_substring_parent_managed", &MIRCodeGenerator::intrinsic_substring_parent_managed},
		{"intrinsic_substring_byte_offset", &MIRCodeGenerator::intrinsic_substring_byte_offset},

		// Array intrinsics
		{"intrinsic_array_len", &MIRCodeGenerator::intrinsic_array_len},
		{"intrinsic_array_capacity", &MIRCodeGenerator::intrinsic_array_capacity},
		{"intrinsic_array_set_length", &MIRCodeGenerator::intrinsic_array_set_length},
		{"intrinsic_array_grow", &MIRCodeGenerator::intrinsic_array_grow},
		{"intrinsic_array_set_at", &MIRCodeGenerator::intrinsic_array_set_at},
		{"intrinsic_array_shift_right", &MIRCodeGenerator::intrinsic_array_shift_right},
		{"intrinsic_array_shift_left", &MIRCodeGenerator::intrinsic_array_shift_left},

		// Managed array intrinsics (new struct-based layout)
		{"intrinsic_managed_array_len", &MIRCodeGenerator::intrinsic_managed_array_len},
		{"intrinsic_managed_array_capacity", &MIRCodeGenerator::intrinsic_managed_array_capacity},
		{"intrinsic_managed_array_set_length", &MIRCodeGenerator::intrinsic_managed_array_set_length},
		{"intrinsic_managed_array_set_capacity", &MIRCodeGenerator::intrinsic_managed_array_set_capacity},
		{"intrinsic_managed_array_grow", &MIRCodeGenerator::intrinsic_managed_array_grow},
		{"intrinsic_managed_array_set_at", &MIRCodeGenerator::intrinsic_managed_array_set_at},
		{"intrinsic_managed_array_get_at", &MIRCodeGenerator::intrinsic_managed_array_get_at},
		{"intrinsic_managed_array_shift_right", &MIRCodeGenerator::intrinsic_managed_array_shift_right},
		{"intrinsic_managed_array_shift_left", &MIRCodeGenerator::intrinsic_managed_array_shift_left},
	};

	// Populate methods_ by looking up codegen method names from IntrinsicRegistry
	for (const auto &[intrinsicName, info] : IntrinsicRegistry::instance().getAll()) {
		const char *methodName = IntrinsicRegistry::instance().getCodegenMethodName(intrinsicName);
		if (methodName) {
			auto it = methodsByName.find(methodName);
			if (it != methodsByName.end()) {
				methods_[intrinsicName] = it->second;
			}
		}
	}
}

IntrinsicCodegenMethod IntrinsicCodegenRegistry::getMethod(const std::string &name) const {
	auto it = methods_.find(name);
	if (it != methods_.end()) {
		return it->second;
	}
	return nullptr;
}

bool IntrinsicCodegenRegistry::hasMethod(const std::string &name) const {
	return methods_.find(name) != methods_.end();
}
