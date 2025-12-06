#include "intrinsics.h"
#include "intrinsics_defs.h"
#include "types/type_conversion.h"

IntrinsicRegistry &IntrinsicRegistry::instance() {
	static IntrinsicRegistry registry;
	return registry;
}

IntrinsicRegistry::IntrinsicRegistry() {
	for (const auto &def : getIntrinsicDefinitions()) {
		// Convert IntrinsicParamDef to IntrinsicParam
		std::vector<IntrinsicParam> params;
		for (const auto &p : def.params) {
			if (p.isAnyType) {
				// Accept any type
				params.push_back(IntrinsicParam::any());
			} else if (p.isArrayType) {
				// Array type (possibly accepting any element type if allowedTypes is empty)
				params.push_back(IntrinsicParam::arrayOf(p.allowedTypes));
			} else if (!p.allowedTypes.empty()) {
				params.push_back(IntrinsicParam::anyOf(p.allowedTypes));
			} else {
				params.push_back(IntrinsicParam(p.type));
			}
		}

		intrinsics_[def.name] = IntrinsicInfo(def.name, def.returnType, std::move(params));

		if (def.codegenMethodName) {
			codegenMethodNames_[def.name] = def.codegenMethodName;
		}
	}
}

const IntrinsicInfo *IntrinsicRegistry::lookup(const std::string &name) const {
	auto it = intrinsics_.find(name);
	if (it != intrinsics_.end()) {
		return &it->second;
	}
	return nullptr;
}

bool IntrinsicRegistry::isIntrinsic(const std::string &name) const {
	// Check if name starts with __ and is in our registry
	if (name.size() < 2 || name[0] != '_' || name[1] != '_') {
		return false;
	}
	return intrinsics_.find(name) != intrinsics_.end();
}

std::string IntrinsicRegistry::validateArgType(const IntrinsicParam &param, const std::string &actualType) const {
	// Accept any type
	if (param.isAnyType) {
		return ""; // Valid - any type accepted
	}

	if (param.isArrayType) {
		// Check if actualType is an array type
		if (!maxon::TypeConversion::isArrayType(actualType)) {
			return "expected array type, got " + actualType;
		}
		// If allowedTypes is empty, accept any array element type
		if (param.allowedTypes.empty()) {
			return ""; // Valid - any array type accepted
		}
		// Check if actualType is an array with one of the allowed element types
		std::string elementType = maxon::TypeConversion::getArrayElementType(actualType);
		for (const auto &elemType : param.allowedTypes) {
			if (elementType == elemType) {
				return ""; // Valid
			}
		}
		// Build error message
		std::string expected = "array of ";
		for (size_t i = 0; i < param.allowedTypes.size(); ++i) {
			if (i > 0)
				expected += " or ";
			expected += param.allowedTypes[i];
		}
		return "expected " + expected + ", got " + actualType;
	}

	// Multiple allowed types (non-array)
	if (!param.allowedTypes.empty()) {
		for (const auto &allowedType : param.allowedTypes) {
			if (actualType == allowedType) {
				return ""; // Valid
			}
		}
		// Build error message
		std::string expected;
		for (size_t i = 0; i < param.allowedTypes.size(); ++i) {
			if (i > 0)
				expected += " or ";
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

const char *IntrinsicRegistry::getCodegenMethodName(const std::string &name) const {
	auto it = codegenMethodNames_.find(name);
	if (it != codegenMethodNames_.end()) {
		return it->second;
	}
	return nullptr;
}
