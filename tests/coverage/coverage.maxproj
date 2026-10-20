export function build() returns ExitCode
	Build.buildWithConfig(BuildConfig.create([".", "../support"], output: "coverage"))
	return 0
end 'build'
