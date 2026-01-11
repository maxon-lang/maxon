const std = @import("std");

pub fn build(b: *std.Build) void {
    const target = b.standardTargetOptions(.{});
    // Always use Debug during development for better error messages and stack traces
    const optimize = std.builtin.OptimizeMode.Debug;
    _ = b.standardOptimizeOption(.{}); // Still accept the option but ignore it

    const exe = b.addExecutable(.{
        .name = "maxon",
        .root_module = b.createModule(.{
            .root_source_file = b.path("src/main.zig"),
            .target = target,
            .optimize = optimize,
            // Enable stack traces in debug allocator
            .strip = false,
        }),
    });

    // Add Zydis C library for disassembly
    exe.addCSourceFile(.{
        .file = b.path("src/vendor/zydis/Zydis.c"),
        .flags = &.{"-DZYDIS_STATIC_BUILD"},
    });
    exe.addIncludePath(b.path("src/vendor/zydis"));
    exe.linkLibC();

    b.installArtifact(exe);

    // Deploy maxon.exe to /bin after build using Zig code
    const install_artifact_step = &b.addInstallArtifact(exe, .{}).step;
    const deploy_step = DeployStep.create(b);
    deploy_step.step.dependOn(install_artifact_step);
    b.getInstallStep().dependOn(&deploy_step.step);

    // Generate TextMate grammar for VS Code extension (runs after deploy)
    const grammar_step = GrammarGenStep.create(b);
    grammar_step.step.dependOn(&deploy_step.step);
    b.getInstallStep().dependOn(&grammar_step.step);

    const run_cmd = b.addRunArtifact(exe);
    run_cmd.step.dependOn(b.getInstallStep());

    if (b.args) |args| {
        run_cmd.addArgs(args);
    }

    const run_step = b.step("run", "Run the compiler");
    run_step.dependOn(&run_cmd.step);

    // Test step - runs spec fragment tests
    const test_cmd = b.addRunArtifact(exe);
    test_cmd.step.dependOn(b.getInstallStep());
    test_cmd.addArgs(&.{"test"});
    // Allow any exit code - test failures shouldn't fail the build step
    test_cmd.stdio = .{ .check = .{} };
    test_cmd.has_side_effects = true;

    // Forward any additional args to the test command
    if (b.args) |args| {
        test_cmd.addArgs(args);
    }

    const test_step = b.step("test", "Run spec fragment tests");
    test_step.dependOn(&test_cmd.step);

    // Coverage step - runs tests with OpenCppCoverage to generate HTML report
    const coverage_cmd = b.addSystemCommand(&.{
        "OpenCppCoverage",
        "--sources",
        "src",
        "--export_type",
        "html:coverage-report",
        "--",
    });
    coverage_cmd.addArtifactArg(exe);
    coverage_cmd.addArgs(&.{"test"});
    coverage_cmd.step.dependOn(b.getInstallStep());

    const coverage_step = b.step("coverage", "Run tests with code coverage (requires OpenCppCoverage)");
    coverage_step.dependOn(&coverage_cmd.step);

    // Unit tests for compiler/lsp modules (run via zig test)
    const unit_tests = b.addTest(.{
        .root_module = b.createModule(.{
            .root_source_file = b.path("src/main.zig"),
            .target = target,
            .optimize = optimize,
        }),
    });
    const run_unit_tests = b.addRunArtifact(unit_tests);
    const unit_test_step = b.step("unit-test", "Run zig unit tests");
    unit_test_step.dependOn(&run_unit_tests.step);

    // LSP E2E tests - spawn server as child process and communicate via JSON-RPC
    const lsp_e2e_tests = b.addTest(.{
        .root_module = b.createModule(.{
            .root_source_file = b.path("src/lsp/e2e/lsp_e2e_tests.zig"),
            .target = target,
            .optimize = optimize,
        }),
    });
    // Tests need the compiled maxon executable to spawn
    lsp_e2e_tests.step.dependOn(b.getInstallStep());

    const run_lsp_e2e_tests = b.addRunArtifact(lsp_e2e_tests);
    run_lsp_e2e_tests.step.dependOn(b.getInstallStep());

    const lsp_e2e_test_step = b.step("test-lsp", "Run LSP E2E tests");
    lsp_e2e_test_step.dependOn(&run_lsp_e2e_tests.step);
}

const GrammarGenStep = struct {
    step: std.Build.Step,
    builder: *std.Build,

    pub fn create(b: *std.Build) *GrammarGenStep {
        const self = b.allocator.create(GrammarGenStep) catch @panic("OOM");
        self.* = .{
            .step = std.Build.Step.init(.{
                .id = .custom,
                .name = "generate TextMate grammar",
                .owner = b,
                .makeFn = make,
            }),
            .builder = b,
        };
        return self;
    }

    fn make(step: *std.Build.Step, options: std.Build.Step.MakeOptions) anyerror!void {
        const self: *GrammarGenStep = @fieldParentPtr("step", step);
        const b = self.builder;
        const allocator = b.allocator;
        _ = options;

        const build_root = b.build_root.path orelse ".";
        const grammar_path = b.pathJoin(&.{ build_root, "..", "vscode-extension", "syntaxes", "maxon.tmLanguage.json" });

        // Create directory if it doesn't exist
        const grammar_dir = std.fs.path.dirname(grammar_path) orelse return error.InvalidPath;
        std.fs.cwd().makePath(grammar_dir) catch |err| switch (err) {
            error.PathAlreadyExists => {},
            else => return err,
        };

        // Open file for writing
        const file = std.fs.createFileAbsolute(grammar_path, .{}) catch |err| {
            std.debug.print("Error creating grammar file: {}\n", .{err});
            return err;
        };
        defer file.close();

        // Generate grammar
        const grammar_generator = @import("src/grammar_generator.zig");
        grammar_generator.generateGrammar(file, allocator) catch |err| {
            std.debug.print("Error generating grammar: {}\n", .{err});
            return err;
        };
    }
};

const DeployStep = struct {
    step: std.Build.Step,
    builder: *std.Build,

    pub fn create(b: *std.Build) *DeployStep {
        const self = b.allocator.create(DeployStep) catch @panic("OOM");
        self.* = .{
            .step = std.Build.Step.init(.{
                .id = .custom,
                .name = "deploy maxon.exe",
                .owner = b,
                .makeFn = make,
            }),
            .builder = b,
        };
        return self;
    }

    fn make(step: *std.Build.Step, options: std.Build.Step.MakeOptions) anyerror!void {
        const self: *DeployStep = @fieldParentPtr("step", step);
        const b = self.builder;
        const allocator = b.allocator;
        _ = options;

        const build_root = b.build_root.path orelse ".";
        const bin_dir = b.pathJoin(&.{ build_root, "..", "bin" });
        const zig_out_dir = b.pathJoin(&.{ build_root, "zig-out", "bin" });

        const exe_path = b.pathJoin(&.{ bin_dir, "maxon.exe" });
        const old_exe_path = b.pathJoin(&.{ bin_dir, "maxon.exe.old" });
        const new_exe_path = b.pathJoin(&.{ zig_out_dir, "maxon.exe" });

        const pdb_path = b.pathJoin(&.{ bin_dir, "maxon.pdb" });
        const old_pdb_path = b.pathJoin(&.{ bin_dir, "maxon.pdb.old" });
        const new_pdb_path = b.pathJoin(&.{ zig_out_dir, "maxon.pdb" });

        // Step 1: Move old exe to .old (works even if in use on Windows)
        if (@import("builtin").os.tag == .windows) {
            // On Windows, use MoveFileEx which can move files that are in use
            const windows = std.os.windows;
            const exe_path_w = std.unicode.utf8ToUtf16LeAllocZ(allocator, exe_path) catch @panic("OOM");
            defer allocator.free(exe_path_w);
            const old_exe_path_w = std.unicode.utf8ToUtf16LeAllocZ(allocator, old_exe_path) catch @panic("OOM");
            defer allocator.free(old_exe_path_w);

            const result = windows.kernel32.MoveFileExW(exe_path_w, old_exe_path_w, windows.MOVEFILE_REPLACE_EXISTING);
            if (result == 0) {
                std.debug.print("Note: Could not move old exe\n", .{});
            }
        } else {
            std.fs.renameAbsolute(exe_path, old_exe_path) catch |err| {
                if (err != error.FileNotFound) {
                    std.debug.print("Note: Could not move old exe: {}\n", .{err});
                }
            };
        }

        // Step 2: Move old pdb to .old
        if (@import("builtin").os.tag == .windows) {
            const windows = std.os.windows;
            const pdb_path_w = std.unicode.utf8ToUtf16LeAllocZ(allocator, pdb_path) catch @panic("OOM");
            defer allocator.free(pdb_path_w);
            const old_pdb_path_w = std.unicode.utf8ToUtf16LeAllocZ(allocator, old_pdb_path) catch @panic("OOM");
            defer allocator.free(old_pdb_path_w);

            const result = windows.kernel32.MoveFileExW(pdb_path_w, old_pdb_path_w, windows.MOVEFILE_REPLACE_EXISTING);
            if (result == 0) {
                std.debug.print("Note: Could not move old pdb\n", .{});
            }
        } else {
            std.fs.renameAbsolute(pdb_path, old_pdb_path) catch |err| {
                if (err != error.FileNotFound) {
                    std.debug.print("Note: Could not move old pdb: {}\n", .{err});
                }
            };
        }

        // Step 3: Copy new exe into place
        std.fs.copyFileAbsolute(new_exe_path, exe_path, .{}) catch |err| {
            std.debug.print("Error copying exe: {}\n", .{err});
            return err;
        };

        // Step 4: Copy new pdb into place
        std.fs.copyFileAbsolute(new_pdb_path, pdb_path, .{}) catch |err| {
            std.debug.print("Error copying pdb: {}\n", .{err});
            return err;
        };

        // Step 5: Kill any running maxon.exe processes so LSP restarts with new version
        if (std.process.Child.run(.{
            .allocator = allocator,
            .argv = &.{ "taskkill", "/F", "/IM", "maxon.exe" },
        })) |_| {
            // Process killed successfully
        } else |_| {
            // Process not running or taskkill failed - not an error
        }

        // Step 6: Remove old backups
        std.fs.deleteFileAbsolute(old_exe_path) catch {};
        std.fs.deleteFileAbsolute(old_pdb_path) catch {};
    }
};
