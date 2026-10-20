export function build() returns ExitCode
	Build.buildWithConfig(BuildConfig.create([".", "../support"], output: "profile"))
	return 0
end 'build'
