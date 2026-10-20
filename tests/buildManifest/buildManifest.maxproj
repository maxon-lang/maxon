export function build() returns ExitCode
	Build.buildWithConfig(BuildConfig.create([".", "../support"], output: "buildManifest"))
	return 0
end 'build'
