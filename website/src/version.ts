// ⭐⭐ **THE RELEASED VERSION, IN ONE PLACE.** The download links, the hero and the nav badge all name
// it, and they were three copies with a comment on one of them asking the reader to keep them in
// step. A release is not a moment anyone wants to be grepping for version strings.
//
// ⚠ **REWRITTEN BY `scripts/announce.sh` IN THE COMPILER REPOSITORY**, as part of publishing a
// release. Edit it by hand only to correct a mistake — the next release overwrites it either way.
//
// ⛔ **THE DOCS PAGES DELIBERATELY DO NOT USE THIS.** A shell command inside a code fence cannot
// interpolate a value, and a version pasted into one goes stale silently — so those commands are
// written against a glob (`maxon-*-x64-linux.tar.gz`) instead, which is both correct for every
// release and better for a reader who downloaded whichever one they downloaded.
export const RELEASE_VERSION = '0.1.0';
