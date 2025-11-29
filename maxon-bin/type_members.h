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
// Note: Static arrays (e.g., [5]int) only have .length
// Dynamic arrays/slices would have .capacity too, but we don't distinguish yet
inline std::vector<TypeMemberInfo> getArrayTypeMembers() {
	return {
		{"length", false, "int", "", "Number of elements in the array"}};
}

// Check if a member name is valid for an array type
// Returns the member's return type, or empty string if not valid
inline std::string getArrayMemberType(const std::string &memberName) {
	if (memberName == "length") {
		return "int";
	}
	// Note: .capacity is NOT supported on static arrays
	// When we add dynamic arrays/slices, we'll need to distinguish
	return "";
}

#endif // TYPE_MEMBERS_H
