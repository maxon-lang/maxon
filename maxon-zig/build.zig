const std = @import("std");

pub fn build(b: *std.Build) void {
    const target = b.standardTargetOptions(.{});
    const optimize = b.standardOptimizeOption(.{});

    const exe = b.addExecutable(.{
        .name = "maxon-zig",
        .root_module = b.createModule(.{
            .root_source_file = b.path("src/main.zig"),
            .target = target,
            .optimize = optimize,
        }),
    });

    b.installArtifact(exe);

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
