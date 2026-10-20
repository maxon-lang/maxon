export function build() returns ExitCode
	Build.buildWithConfig(BuildConfig.create([".", "../support"], output: "execute"))
	return 0
end 'build'
