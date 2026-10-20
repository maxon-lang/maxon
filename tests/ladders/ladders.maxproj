export function build() returns ExitCode
	Build.buildWithConfig(BuildConfig.create([".", "../support"], output: "ladders"))
	return 0
end 'build'
