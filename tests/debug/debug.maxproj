export function build() returns ExitCode
	Build.buildWithConfig(BuildConfig.create([".", "../support"], output: "debug"))
	return 0
end 'build'
