// LSP Server for Maxon Language
// Provides IDE features: completion, hover, go-to-definition, etc.

pub const server = @import("server.zig");
pub const types = @import("types.zig");
pub const transport = @import("transport.zig");
pub const analyzer = @import("analyzer.zig");

/// Run the LSP server
pub const run = server.run;
