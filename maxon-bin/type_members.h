#ifndef TYPE_MEMBERS_H
#define TYPE_MEMBERS_H

#include <string>
#include <vector>

// Information about a type member (method or property)
// This is the shared definition used by both the compiler and LSP
struct TypeMemberInfo {
	std::string name;		   // Member name (e.g., "length", "toUpper")
	bool isMethod;			   // true = method (needs parens), false = property
	std::string returnType;	   // Return type
	std::string signature;	   // For methods: "(arg1 type, arg2 type)"
	std::string documentation; // Description for LSP hover/completion

	TypeMemberInfo(const std::string &n, bool method, const std::string &ret,
				   const std::string &sig = "", const std::string &doc = "")
		: name(n), isMethod(method), returnType(ret), signature(sig), documentation(doc) {}
};

// Get type members for built-in array type
// Note: Arrays use .count() method from stdlib, not built-in properties
inline std::vector<TypeMemberInfo> getArrayTypeMembers() {
	return {};
}

// Check if a member name is valid for an array type
// Returns the member's return type, or empty string if not valid
// Note: Arrays use .count() method from stdlib, not built-in properties
inline std::string getArrayMemberType(const std::string &memberName) {
	// No built-in array properties - use stdlib methods like count() instead
	return "";
}

#endif // TYPE_MEMBERS_H
