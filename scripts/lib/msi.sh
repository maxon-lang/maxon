# Facts read out of a built MSI, for every script that describes one.

# Does this MSI carry an Authenticode signature?
#
# ⭐ ASKED OF THE FILE, NEVER OF THE PIPELINE THAT PRODUCED IT. The release notes and the winget
# description tell a reader whether to expect a SmartScreen prompt, and a build where signing happened
# to be off would otherwise promise a signature that is not there.
#
# An MSI is an OLE compound file whose signature lives in a stream named `\5DigitalSignature`, and
# stream names are stored UTF-16LE in its directory. Dropping the NUL bytes makes that name findable
# with `grep`, which is the whole reason for the `tr`: the callers run on Linux too, where `signtool`
# does not exist.
#
# ⛔ `grep -c`, NEVER `grep -q`. `-q` closes the pipe at the first match, `tr` then fails writing into
# it, and under `pipefail` that failure is the pipeline's answer: a signed file reported as unsigned.
# MEASURED: v0.1.1's notes said both that the installer was signed and that it was not.
msi_is_signed() {
	tr -d '\0' < "$1" | grep -ac 'DigitalSignature' >/dev/null
}
