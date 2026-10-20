export function build() returns ExitCode
	Build.buildWithConfig(BuildConfig.create([".", "../support"], output: "cli"))
	return 0
end 'build'
