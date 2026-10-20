export function build() returns ExitCode
	Build.buildWithConfig(BuildConfig.create([".", "../support"], output: "dap"))
	return 0
end 'build'
