#ifndef INTRINSICS_H
#define INTRINSICS_H

#include <string>
#include <vector>
#include <map>
#include <functional>
#include <optional>

// Describes a single parameter for an intrinsic function
struct IntrinsicParam {
	std::string type;                           // Expected type (e.g., "_ManagedString", "int", "cstring")
	bool isArrayType;                           // True if the type should match array patterns (e.g., "[N]char")
	std::vector<std::string> allowedTypes;      // For multiple allowed types or array element types

	// Single type parameter
	IntrinsicParam(const std::string& t) : type(t), isArrayType(false) {}
	// Multiple allowed types or array element types
	static IntrinsicParam anyOf(const std::vector<std::string>& types) {
		IntrinsicParam p("");
		p.allowedTypes = types;
		return p;
	}
	static IntrinsicParam arrayOf(const std::vector<std::string>& elemTypes) {
		IntrinsicParam p("");
		p.isArrayType = true;
		p.allowedTypes = elemTypes;
		return p;
	}
};

// Describes an intrinsic function
struct IntrinsicInfo {
	std::string name;
	std::string returnType;
	std::vector<IntrinsicParam> params;

	IntrinsicInfo() = default;
	IntrinsicInfo(const std::string& n, const std::string& ret, std::vector<IntrinsicParam> p)
		: name(n), returnType(ret), params(std::move(p)) {}
};

// Registry for all compiler intrinsics
class IntrinsicRegistry {
public:
	static IntrinsicRegistry& instance();

	// Look up an intrinsic by name, returns nullptr if not found
	const IntrinsicInfo* lookup(const std::string& name) const;

	// Check if a name is an intrinsic (starts with __ prefix)
	bool isIntrinsic(const std::string& name) const;

	// Get all intrinsics
	const std::map<std::string, IntrinsicInfo>& getAll() const { return intrinsics_; }

	// Validate argument type against expected parameter type
	// Returns empty string if valid, error message if invalid
	std::string validateArgType(const IntrinsicParam& param, const std::string& actualType) const;

private:
	IntrinsicRegistry();
	void registerIntrinsic(const std::string& name, const std::string& returnType,
						   std::vector<IntrinsicParam> params);

	std::map<std::string, IntrinsicInfo> intrinsics_;
};

#endif // INTRINSICS_H
