export function build() returns ExitCode
	Build.buildWithConfig(BuildConfig.create([".", "../support"], output: "specHarness"))
	return 0
end 'build'
