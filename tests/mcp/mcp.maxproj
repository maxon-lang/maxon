export function build() returns ExitCode
	Build.buildWithConfig(BuildConfig.create([".", "../support"], output: "mcp"))
	return 0
end 'build'
