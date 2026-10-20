export function build() returns ExitCode
	Build.buildWithConfig(BuildConfig.create([".", "../support"], output: "docs"))
	return 0
end 'build'
