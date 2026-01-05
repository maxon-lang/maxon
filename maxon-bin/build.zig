const std = @import("std");

pub fn build(b: *std.Build) void {
    const target = b.standardTargetOptions(.{});
    const optimize = b.standardOptimizeOption(.{});

    const exe = b.addExecutable(.{
        .name = "maxon",
        .root_module = b.createModule(.{
            .root_source_file = b.path("src/main.zig"),
            .target = target,
            .optimize = optimize,
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

    // Deploy maxon.exe to /bin after build
    // On Windows, we need to: move old binary, copy new, kill running processes, remove old
    const bin_path = b.path("../bin/maxon.exe").getPath(b);
    const old_bin_path = b.path("../bin/maxon.exe.old").getPath(b);

    // Step 1: Move existing binary out of the way (ignore errors if it doesn't exist)
    const move_old_cmd = b.addSystemCommand(&.{ "cmd", "/c", "if", "exist", bin_path, "move", "/Y", bin_path, old_bin_path });

    // Step 2: Copy new binary to bin directory
    const copy_cmd = b.addSystemCommand(&.{ "cmd", "/c", "copy", "/Y" });
    copy_cmd.addArtifactArg(exe);
    copy_cmd.addArg(bin_path);
    copy_cmd.step.dependOn(&move_old_cmd.step);

    // Step 3: Kill any running maxon.exe processes (so VSCode LSP restarts)
    const kill_cmd = b.addSystemCommand(&.{ "cmd", "/c", "taskkill", "/F", "/IM", "maxon.exe", "2>nul", "||", "echo", "." });
    kill_cmd.step.dependOn(&copy_cmd.step);

    // Step 4: Remove old binary
    const remove_old_cmd = b.addSystemCommand(&.{ "cmd", "/c", "del", "/F", "/Q", old_bin_path, "2>nul", "||", "echo", "." });
    remove_old_cmd.step.dependOn(&kill_cmd.step);

    // Make the deploy step depend on the install artifact, then hook it into install
    b.getInstallStep().dependOn(&remove_old_cmd.step);

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
}
