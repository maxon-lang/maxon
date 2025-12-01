#ifndef INTRINSIC_CODEGEN_H
#define INTRINSIC_CODEGEN_H

#include "../ast.h"
#include "../mir/mir.h"
#include <map>
#include <string>

// Forward declaration
class MIRCodeGenerator;

// Member function pointer type for intrinsic codegen
using IntrinsicCodegenMethod = mir::MIRValue* (MIRCodeGenerator::*)(CallExprAST*);

// Registry mapping intrinsic names to their codegen member methods
class IntrinsicCodegenRegistry {
public:
	static IntrinsicCodegenRegistry& instance();

	// Get the codegen method for an intrinsic, returns nullptr if not found
	IntrinsicCodegenMethod getMethod(const std::string& name) const;

	// Check if an intrinsic has codegen registered
	bool hasMethod(const std::string& name) const;

private:
	IntrinsicCodegenRegistry();
	std::map<std::string, IntrinsicCodegenMethod> methods_;
};

#endif // INTRINSIC_CODEGEN_H
