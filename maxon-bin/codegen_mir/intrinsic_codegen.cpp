#include "intrinsic_codegen.h"
#include "../codegen_mir.h"

IntrinsicCodegenRegistry& IntrinsicCodegenRegistry::instance() {
	static IntrinsicCodegenRegistry registry;
	return registry;
}

IntrinsicCodegenRegistry::IntrinsicCodegenRegistry() {
	// String intrinsics
	methods_["__string_len"] = &MIRCodeGenerator::intrinsic_string_len;
	methods_["__string_byte_at"] = &MIRCodeGenerator::intrinsic_string_byte_at;
	methods_["__string_slice"] = &MIRCodeGenerator::intrinsic_string_slice;
	methods_["__string_concat"] = &MIRCodeGenerator::intrinsic_string_concat;
	methods_["__string_make_unique"] = &MIRCodeGenerator::intrinsic_string_make_unique;
	methods_["__string_set_byte"] = &MIRCodeGenerator::intrinsic_string_set_byte;
	methods_["__string_get_refcount"] = &MIRCodeGenerator::intrinsic_string_get_refcount;
	methods_["__string_to_cstring"] = &MIRCodeGenerator::intrinsic_string_to_cstring;
	methods_["__string_from_chars"] = &MIRCodeGenerator::intrinsic_string_from_chars;

	// Cstring intrinsics
	methods_["__cstring_len"] = &MIRCodeGenerator::intrinsic_cstring_len;
	methods_["__cstring_write_stdout"] = &MIRCodeGenerator::intrinsic_cstring_write_stdout;

	// Substring intrinsics
	methods_["__substring_len"] = &MIRCodeGenerator::intrinsic_substring_len;
	methods_["__substring_byte_at"] = &MIRCodeGenerator::intrinsic_substring_byte_at;
	methods_["__substring_iter_pos"] = &MIRCodeGenerator::intrinsic_substring_iter_pos;
	methods_["__substring_with_iter_pos"] = &MIRCodeGenerator::intrinsic_substring_with_iter_pos;
	methods_["__substring_to_string"] = &MIRCodeGenerator::intrinsic_substring_to_string;
	methods_["__substring_slice"] = &MIRCodeGenerator::intrinsic_substring_slice;
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
