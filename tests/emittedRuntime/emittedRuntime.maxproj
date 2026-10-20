export function build() returns ExitCode
	Build.buildWithConfig(BuildConfig.create([".", "../support"], output: "emittedRuntime"))
	return 0
end 'build'
