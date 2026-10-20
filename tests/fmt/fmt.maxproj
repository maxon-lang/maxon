export function build() returns ExitCode
	Build.buildWithConfig(BuildConfig.create([".", "../support"], output: "fmt"))
	return 0
end 'build'
