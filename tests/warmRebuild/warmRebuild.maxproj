export function build() returns ExitCode
	Build.buildWithConfig(BuildConfig.create([".", "../support"], output: "warmRebuild"))
	return 0
end 'build'
