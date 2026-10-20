export function build() returns ExitCode
	Build.buildWithConfig(BuildConfig.create([".", "../support"], output: "parallelCompile"))
	return 0
end 'build'
