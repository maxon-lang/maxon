export function build() returns ExitCode
	Build.buildWithConfig(BuildConfig.create([".", "../support"], output: "lsp"))
	return 0
end 'build'
