export function build() returns ExitCode
	Build.buildWithConfig(BuildConfig.create([".", "../support"], output: "examples"))
	return 0
end 'build'
