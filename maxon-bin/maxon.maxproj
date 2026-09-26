// How this compiler is built. `maxon run build` at the repository root reaches it through
// `maxon.maxtasks`'s `Build.delegate`, and `maxon build` inside this directory runs it in place;
// both are this file, so both stamp the same version.
//
// ⛔ THE OUTPUT IS THE COMPILER'S OWN SLOT, so this REPLACES THE BINARY RUNNING IT. Once the compile
// succeeds, the compiler renames its running image to `maxon.previous` — an OS will not let a
// running executable be deleted, but will let one be renamed — and writes the new one in its place.
// A failed build leaves the running compiler in the slot, so it can build the fix.
//
// ⭐⭐ THE VERSION IS DERIVED FROM GIT AND HANDED TO `--define`, WHICH IS WHAT go's `-ldflags -X`
// DOES. A release is identified by the REF and not by a number somebody remembered to edit: a tag
// `vX.Y.Z` or a branch `release/X.Y.Z` gives the version, and anything else is `dev`. The tag is
// checked first because CI builds from one, where `rev-parse --abbrev-ref HEAD` answers `HEAD`.
// `release.sh --publish` compares the tag against what the binary reports and refuses a
// disagreement, so the two cannot drift.
//
// ⛔ THE VALUES ARE HANDED TO THE COMPILER, NEVER WRITTEN INTO A FILE. Generating a `Version.maxon`
// needs a compiler, and the compiler cannot be built without it — so a fresh checkout would have no
// `CompilerVersion` and no lane could compile at all. That file is tracked source carrying honest
// defaults, and these three names replace them.
//
// ⚠ Namespace-qualified, because a bare name is not promised to be unique: a namespace is the module
// DIRECTORY, so `Compiler.` is `maxon-bin/Compiler/`. `--define` refuses a name matching two
// declarations rather than choosing between them.

let VersionDefineName = "Compiler.CompilerVersion"
let CommitDefineName = "Compiler.CompilerCommit"
let CommitDateDefineName = "Compiler.CompilerCommitDate"
let ReleaseBranchPrefix = "release/"
let ReleaseTagPrefix = "v"
let DevVersion = "dev"
let UnknownField = "unknown"

let CompilerVariable = "MAXON_COMPILER"
let SecondStageVariable = "MAXON_SECOND_STAGE"
let VersionCommandName = "version"
let VersionLineOpen = "("
let VersionFieldSeparator = " "
let GitLineSeparator = "\n"
let ChangedFileSeparator = ", "

enum GitFailure implements Error
	noAnswer
end 'GitFailure'

enum VersionLineError implements Error
	noCommit
end 'VersionLineError'

// A missing git is not a build failure: a source tarball has no repository and must still build, so
// every caller falls back rather than refusing. It throws rather than answering "": an empty diff
// reads as an unchanged runtime, which leaves a compiler with a stale runtime of its own.
function gitAnswer(args StringArray) returns String throws GitFailure
	let result = try Subprocess.run(Executable.name("git"), arguments: args) otherwise throw GitFailure.noAnswer

	if not result.succeeded() 'gitFailed'
		throw GitFailure.noAnswer
	end 'gitFailed'

	return result.stdout.trim()
end 'gitAnswer'

// Relative to `maxon-bin/`, this manifest's working directory. From anywhere else they match
// nothing, which reads as an unchanged runtime.
function emittedRuntimePathspecs() returns StringArray
	return ["Compiler/Runtime", "Compiler/Targets/*/*Runtime*.maxon"]
end 'emittedRuntimePathspecs'

function gitAboutEmittedRuntime(command StringArray) returns String throws GitFailure
	var args = command.clone()
	args.push("--")
	args.append(emittedRuntimePathspecs())
	return try gitAnswer(args)
end 'gitAboutEmittedRuntime'

function isSet(variable String) returns bool
	_ = try Process.environmentVariable(variable) otherwise return false
	return true
end 'isSet'

function builtFromCommit(versionLine String) returns String throws VersionLineError
	let afterOpen = try versionLine.split(VersionLineOpen).get(1) otherwise throw VersionLineError.noCommit
	let commit = try afterOpen.split(VersionFieldSeparator).get(0) otherwise throw VersionLineError.noCommit

	if commit.isEmpty() or commit == UnknownField 'noCommitStamped'
		throw VersionLineError.noCommit
	end 'noCommitStamped'

	return commit
end 'builtFromCommit'

function changedFiles(listings StringArray) returns String
	var named = ""

	for listing in listings 'eachListing'
		for line in listing.split(GitLineSeparator) 'eachLine'
			let file = line.trim()

			if file.isEmpty() 'blankLine'
				continue
			end 'blankLine'

			if not named.isEmpty() 'notFirst'
				named.append(ChangedFileSeparator)
			end 'notFirst'

			named.append(file)
		end 'eachLine'
	end 'eachListing'

	return named
end 'changedFiles'

function rebuildBecause(reason String) returns bool
	printError("maxon.maxproj: asking for the compiler this writes to build it again, because {reason}\n")
	return true
end 'rebuildBecause'

// The compiler emits this runtime into every program it writes, itself included, so one build
// carries its builder's copy. Every uncertainty answers "rebuild": wrong that way costs one build,
// and wrong the other way is a compiler running a stale copy of its own runtime.
function rebuildWithOutput() returns bool
	if isSet(SecondStageVariable) 'secondStage'
		return false
	end 'secondStage'

	let builder = try Process.environmentVariable(CompilerVariable) otherwise return rebuildBecause("{CompilerVariable} names no building compiler, so the runtime it carries is unknown")
	let builderPath = try FilePath.from(builder) otherwise return rebuildBecause("{CompilerVariable} holds '{builder}', which is not a path")
	let reported = try Subprocess.run(Executable.path(builderPath), arguments: [VersionCommandName]) otherwise return rebuildBecause("{builder} could not be asked which commit it was built from")

	if not reported.succeeded() 'versionFailed'
		return rebuildBecause("`{builder} {VersionCommandName}` exited {reported.exitCode()}, so the commit it was built from is unknown")
	end 'versionFailed'

	let commit = try builtFromCommit(reported.stdout) otherwise return rebuildBecause("{builder} reports no commit it was built from")

	_ = try gitAnswer(["cat-file", "-e", "{commit}^\{commit\}"]) otherwise return rebuildBecause("the building compiler was built from {commit}, which git cannot find in this history")

	let committed = try gitAboutEmittedRuntime(["diff", "--name-only", "{commit}..HEAD"]) otherwise return rebuildBecause("git could not list the emitted-runtime commits since {commit}")
	let uncommitted = try gitAboutEmittedRuntime(["status", "--porcelain"]) otherwise return rebuildBecause("git could not list the uncommitted emitted-runtime changes")
	let changed = changedFiles([committed, uncommitted])

	if changed.isEmpty() 'runtimeUnchanged'
		return false
	end 'runtimeUnchanged'

	return rebuildBecause("the emitted runtime changed since the building compiler was built from {commit}: {changed}")
end 'rebuildWithOutput'

function afterPrefix(text String, prefix String) returns String
	let parts = text.split(prefix)

	if parts.count() < 2 'noTail'
		return DevVersion
	end 'noTail'

	return try parts.get(1) otherwise DevVersion
end 'afterPrefix'

function releaseVersion() returns String
	let tag = try gitAnswer(["describe", "--exact-match", "--tags"]) otherwise DevVersion

	if tag.startsWith(ReleaseTagPrefix) 'taggedRelease'
		return afterPrefix(tag, prefix: ReleaseTagPrefix)
	end 'taggedRelease'

	let branch = try gitAnswer(["rev-parse", "--abbrev-ref", "HEAD"]) otherwise DevVersion

	if branch.startsWith(ReleaseBranchPrefix) 'releaseBranch'
		return afterPrefix(branch, prefix: ReleaseBranchPrefix)
	end 'releaseBranch'

	return DevVersion
end 'releaseVersion'

function versionDefines(version String) returns StringArray
	let commitField = try gitAnswer(["rev-parse", "--short", "HEAD"]) otherwise UnknownField
	let dateField = try gitAnswer(["log", "-1", "--format=%cd", "--date=short"]) otherwise UnknownField

	return ["{VersionDefineName}={version}", "{CommitDefineName}={commitField}", "{CommitDateDefineName}={dateField}"]
end 'versionDefines'

export function build() returns ExitCode
	// A `dev` build is unversioned in the binary and stamps as 0.0.0.0: a binary's numeric version
	// fields exist for installers and upgrade rules, and an unreleased build has no place in that
	// ordering (`dev` is not a number, which a manifest `version` must be). `--version` still reports
	// `dev` and the commit it was built from.
	let version = releaseVersion()
	let stampedVersion = "" if version == DevVersion else version

	Build.build(".", output: ".maxon/maxon", version: stampedVersion, defines: versionDefines(version), rebuildWithOutput: rebuildWithOutput())
	return 0
end 'build'
