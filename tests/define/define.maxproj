export function build() returns ExitCode
	Build.buildWithConfig(BuildConfig.create([".", "../support"], output: "define"))
	return 0
end 'build'
