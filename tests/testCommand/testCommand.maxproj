export function build() returns ExitCode
	Build.buildWithConfig(BuildConfig.create([".", "../support"], output: "testCommand"))
	return 0
end 'build'
