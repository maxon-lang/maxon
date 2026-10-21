export function build() returns ExitCode
	Build.buildWithConfig(BuildConfig.create([".", "../support"], output: "targets"))
	return 0
end 'build'
