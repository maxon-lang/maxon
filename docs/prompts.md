I would like to add the following functionality to maxon 

// match statement
var user_input = "Y"
match user_input 'input_check'
    "y" then return true
	"Y" or "y" then return true
    "n" or "N" then return false
	default then return false
end 'input_check'

// this should cause an error
var user_input = "Y"
match user_input 'input_check'
    "y" then return true
	"Y" or "y" then return true and fallthrough
    "n" or "N" then return false
	default then return false
end 'input_check'

// match expression
// no fallthrough allowed here because it doesn't make sense
var number = 2
let description = match number 'convert_number'
	1 returns "One"
	2 returns "Two"
	3 or 4 returns "More"
	default returns "Other"
end 'convert_number'


var permissions = []string

match user_role 'auth_waterfall'
	"admin" then addAdminPerimissions(permissions) and fallthrough
	"moderator" then addModeratorPermissions(permissions) and fallthrough
	"user" then addUserPermissions(permissions) and fallthrough
	default then permissions.push("view_only")
end 'auth_waterfall'

function addAdminPerimissions(permissions)
    permissions.push("delete_users")
    permissions.push("view_logs")
end 'addAdminPerimissions'

function addModeratorPermissions(permissions)
    permissions.push("ban_users")
    permissions.push("edit_posts")
end 'addModeratorPermissions'

The match should be exhaustive for enums if default is not specified.

Create a detailed plan to implement this. Create or update spec files as needed including tests. Also update any relevant documentation. Also consider how the lsp server and vscode extension need to be updated, and create tests for them if they do.


## Math instrinsics

Implement the log2 math function in the maxon standard library. Use the zig implementation as a model. It needs to be accurate.
Make sure the tests cover all the edge cases.

Create a detailed plan to implement this and write it to a file called implementation_plan.md, including a list of tests that need to be updated or added. Try to combine the tests into fewer individual cases in favor of longer tests with multiple steps.


## Code review

Extension tests should not have arbitrary delays.
