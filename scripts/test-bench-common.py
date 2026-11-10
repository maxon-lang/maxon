import os
import stat
import subprocess
import sys
import unittest
from unittest import mock

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bench_common

SHA_NEWEST = "aaaaaaa1"
SHA_MIDDLE = "bbbbbbb2"
SHA_OLDEST = "ccccccc3"
SHA_PROTECTED = "ddddddd4"
SHA_UNUSABLE = "eeeeeee5"
SECONDS_APART = 100
AES_RELATIVE = os.path.join("bench", "aes", "aes.maxon")


def write(path, text="x"):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as fh:
        fh.write(text)


def plan_for(*shas):
    return [{"label": bench_common.ref_label(sha), "sha": sha, "compiler": None} for sha in shas]


def fake_built_tree(root, library_dirs=bench_common.LIBRARY_DIR_NAMES):
    for name in library_dirs:
        write(os.path.join(root, name, "Core.maxon"))
        write(os.path.join(root, name, "tests", "Case.maxtest"))
    for name in ("docs", "tests", "specs", "bench", "temp", ".bootstrap"):
        write(os.path.join(root, name, "file.maxtest"))
    write(os.path.join(root, *bench_common.COMPILER_SEGMENTS), "compiler")
    write(os.path.join(root, *bench_common.COMPILER_SEGMENTS) + ".mxdbg", "sidecar")
    write(os.path.join(root, "maxon-bin", "Main.maxon"))


def install(out_dir, sha, age):
    tree = os.path.join(out_dir, "built-" + sha)
    fake_built_tree(tree)
    entry = bench_common.ref_entry_of(sha, out_dir)
    os.makedirs(entry)
    bench_common.install_ref_compiler(tree, entry)
    bench_common.remove_tree(tree)
    marker = os.path.join(entry, bench_common.REF_USED_MARKER)
    os.utime(marker, (age, age))
    return entry


def present_entries(out_dir):
    return sorted(bench_common.ref_entries(out_dir))


class OutputRootTest(unittest.TestCase):
    def setUp(self):
        self.scratch = bench_common.temp_directory("test-bench-common")
        self.addCleanup(self.scratch.cleanup)
        self.out_dir = os.path.join(self.scratch.name, "out")

    def tearDown(self):
        bench_common.release_output_root(self.out_dir)

    def test_a_second_run_on_the_same_root_is_refused_naming_the_holder(self):
        code = ("import sys; sys.path.insert(0, sys.argv[1]); import bench_common; "
                "bench_common.open_output_root(sys.argv[2], 8, []); print('ready', flush=True); sys.stdin.readline()")
        holder = subprocess.Popen([sys.executable, "-c", code, os.path.dirname(os.path.abspath(__file__)), self.out_dir],
                                  stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True)
        try:
            self.assertEqual(holder.stdout.readline().strip(), "ready")

            with self.assertRaises(bench_common.UsageFailure) as refused:
                bench_common.open_output_root(self.out_dir, 8, plan_for(SHA_NEWEST))
            self.assertIn(f"pid {holder.pid}", str(refused.exception))
            self.assertFalse(os.path.exists(bench_common.ref_entry_of(SHA_NEWEST, self.out_dir)))
        finally:
            holder.communicate("\n")

        bench_common.open_output_root(self.out_dir, 8, [])

    def test_a_root_this_process_holds_is_refused_until_it_is_released(self):
        bench_common.open_output_root(self.out_dir, 8, [])
        with self.assertRaises(bench_common.UsageFailure):
            bench_common.open_output_root(self.out_dir, 8, [])

        bench_common.release_output_root(self.out_dir)
        bench_common.open_output_root(self.out_dir, 8, [])

    def test_a_run_marks_its_own_refs_used_before_pruning(self):
        os.makedirs(self.out_dir)
        install(self.out_dir, SHA_OLDEST, 1 * SECONDS_APART)
        install(self.out_dir, SHA_NEWEST, 2 * SECONDS_APART)

        bench_common.open_output_root(self.out_dir, 1, plan_for(SHA_OLDEST))

        self.assertEqual(present_entries(self.out_dir), [SHA_OLDEST])
        self.assertGreater(os.path.getmtime(os.path.join(bench_common.ref_entry_of(SHA_OLDEST, self.out_dir),
                                                         bench_common.REF_USED_MARKER)), 3 * SECONDS_APART)

    def test_a_checkout_and_the_repository_with_its_parents_are_refused_as_roots(self):
        os.makedirs(os.path.join(self.out_dir, ".git"))
        for refused in (self.out_dir, bench_common.REPO, os.path.dirname(bench_common.REPO)):
            with self.assertRaises(bench_common.UsageFailure):
                bench_common.open_output_root(refused, 8, [])

        self.assertFalse(os.path.exists(os.path.join(self.out_dir, bench_common.IGNORE_MARKER_NAME)))
        self.assertFalse(os.path.exists(os.path.join(bench_common.REPO, bench_common.LOCK_FILE_NAME)))
        inside_the_repo = os.path.join(bench_common.REPO, "temp", "bench-common-test-root-is-inside-the-repo")
        try:
            bench_common.open_output_root(inside_the_repo, 8, [])
            bench_common.release_output_root(inside_the_repo)
        finally:
            if os.path.exists(inside_the_repo):
                bench_common.remove_tree(inside_the_repo)

    def test_a_failed_build_and_a_failed_worktree_removal_are_both_reported(self):
        head = bench_common.git(bench_common.REPO, "rev-parse", "--short", "HEAD")
        entry = bench_common.ref_entry_of(head, self.out_dir)
        os.makedirs(self.out_dir)

        with mock.patch.object(bench_common, "posix_shell", return_value="bash"), \
                mock.patch.object(bench_common, "place_ref_seed", side_effect=bench_common.BuildFailure("the seed is missing")), \
                mock.patch.object(bench_common, "remove_worktree", side_effect=bench_common.BuildFailure("the tree is locked")):
            with self.assertRaises(bench_common.BuildFailure) as reported:
                bench_common.build_ref_entry(head, entry)

        self.assertIn("the seed is missing", str(reported.exception))
        self.assertIn("the tree is locked", str(reported.exception))
        bench_common.discard_ref_entry(entry)

    def test_a_failed_build_alone_is_reported_and_leaves_no_worktree(self):
        head = bench_common.git(bench_common.REPO, "rev-parse", "--short", "HEAD")
        entry = bench_common.ref_entry_of(head, self.out_dir)
        os.makedirs(self.out_dir)

        with mock.patch.object(bench_common, "posix_shell", return_value="bash"), \
                mock.patch.object(bench_common, "place_ref_seed", side_effect=bench_common.BuildFailure("the seed is missing")):
            with self.assertRaises(bench_common.BuildFailure) as reported:
                bench_common.build_ref_entry(head, entry)

        self.assertEqual(str(reported.exception), "the seed is missing")
        self.assertFalse(os.path.exists(os.path.join(entry, bench_common.REF_BUILD_TREE_NAME)))
        self.assertNotIn(os.path.basename(entry), bench_common.git(bench_common.REPO, "worktree", "list"))

    def test_a_read_only_file_is_removed_and_an_unretryable_failure_is_raised_as_it_was(self):
        victim = os.path.join(self.out_dir, "tree")
        write(os.path.join(victim, "sub", "locked.txt"))
        os.chmod(os.path.join(victim, "sub", "locked.txt"), stat.S_IREAD)
        if os.name != "nt":
            os.chmod(os.path.join(victim, "sub"), stat.S_IRUSR | stat.S_IXUSR)

        bench_common.remove_tree(victim)
        self.assertFalse(os.path.exists(victim))

        original = OSError("could not open")
        for failure in (original, (OSError, original, None)):
            with self.assertRaises(OSError) as raised:
                bench_common.writable_retry_within(victim)(os.open, victim, failure)
            self.assertIs(raised.exception, original)

        original = PermissionError("still refused")
        with self.assertRaises(PermissionError) as raised:
            bench_common.writable_retry_within(victim)(os.unlink, os.path.join(victim, "absent"), original)
        self.assertIs(raised.exception, original)

    @unittest.skipIf(os.name == "nt", "POSIX directory modes")
    def test_remove_tree_never_changes_the_mode_above_its_root(self):
        parent = os.path.join(self.scratch.name, "shared")
        victim = os.path.join(parent, "victim")
        os.makedirs(os.path.join(victim, "sub"))
        os.chmod(parent, 0o1777)
        os.chmod(os.path.join(victim, "sub"), stat.S_IRUSR | stat.S_IXUSR)
        os.chmod(victim, stat.S_IRUSR | stat.S_IXUSR)

        bench_common.remove_tree(victim)

        self.assertFalse(os.path.exists(victim))
        self.assertEqual(stat.S_IMODE(os.stat(parent).st_mode), 0o1777)

    def test_a_new_root_is_marked_and_a_marker_is_kept_as_it_is(self):
        bench_common.open_output_root(self.out_dir, bench_common.DEFAULT_KEEP_REFS, [])
        marker = os.path.join(self.out_dir, bench_common.IGNORE_MARKER_NAME)
        with open(marker, encoding="utf-8") as fh:
            self.assertEqual(fh.read(), bench_common.IGNORE_MARKER_TEXT)

        os.utime(marker, (SECONDS_APART, SECONDS_APART))
        bench_common.release_output_root(self.out_dir)
        bench_common.open_output_root(self.out_dir, bench_common.DEFAULT_KEEP_REFS, [])
        self.assertEqual(os.path.getmtime(marker), SECONDS_APART)

    def test_an_install_keeps_the_compiler_and_its_library_directories_only(self):
        os.makedirs(self.out_dir)
        entry = install(self.out_dir, SHA_NEWEST, SECONDS_APART)
        self.assertEqual(sorted(os.listdir(entry)),
                         sorted(["maxon-bin", "stdlib", "runtime", "scriptlib", bench_common.REF_USED_MARKER]))
        self.assertEqual(os.listdir(os.path.join(entry, "maxon-bin", ".maxon")), [bench_common.COMPILER_SEGMENTS[-1]])
        self.assertTrue(os.path.isfile(os.path.join(entry, "stdlib", "Core.maxon")))

    def test_a_tree_without_scriptlib_installs_and_one_without_stdlib_is_refused(self):
        os.makedirs(self.out_dir)
        tree = os.path.join(self.out_dir, "built")
        fake_built_tree(tree, library_dirs=("stdlib", "runtime"))
        entry = bench_common.ref_entry_of(SHA_NEWEST, self.out_dir)
        os.makedirs(entry)
        bench_common.install_ref_compiler(tree, entry)
        self.assertFalse(os.path.exists(os.path.join(entry, "scriptlib")))

        bare = os.path.join(self.out_dir, "bare")
        fake_built_tree(bare, library_dirs=("runtime",))
        bare_entry = bench_common.ref_entry_of(SHA_OLDEST, self.out_dir)
        os.makedirs(bare_entry)
        with self.assertRaises(bench_common.BuildFailure):
            bench_common.install_ref_compiler(bare, bare_entry)
        self.assertFalse(os.path.exists(bench_common.installed_compiler_of(bare_entry)))

    def test_the_least_recently_used_refs_beyond_the_bound_go_with_their_arm_directories(self):
        os.makedirs(self.out_dir)
        install(self.out_dir, SHA_OLDEST, 1 * SECONDS_APART)
        install(self.out_dir, SHA_MIDDLE, 2 * SECONDS_APART)
        install(self.out_dir, SHA_NEWEST, 3 * SECONDS_APART)
        for sha in (SHA_OLDEST, SHA_MIDDLE, SHA_NEWEST):
            write(os.path.join(self.out_dir, "x64", bench_common.ref_label(sha), "aes.maxon"))
        write(os.path.join(self.out_dir, "x64", "slot", "aes.maxon"))

        bench_common.prune_ref_cache(self.out_dir, 2, [])

        self.assertEqual(present_entries(self.out_dir), sorted([SHA_MIDDLE, SHA_NEWEST]))
        self.assertEqual(sorted(os.listdir(os.path.join(self.out_dir, "x64"))),
                         sorted([bench_common.ref_label(SHA_MIDDLE), bench_common.ref_label(SHA_NEWEST), "slot"]))

    def test_a_ref_the_run_uses_is_kept_and_counts_towards_the_bound(self):
        os.makedirs(self.out_dir)
        install(self.out_dir, SHA_PROTECTED, 1 * SECONDS_APART)
        install(self.out_dir, SHA_MIDDLE, 2 * SECONDS_APART)
        install(self.out_dir, SHA_NEWEST, 3 * SECONDS_APART)

        bench_common.prune_ref_cache(self.out_dir, 2, [SHA_PROTECTED])
        self.assertEqual(present_entries(self.out_dir), sorted([SHA_NEWEST, SHA_PROTECTED]))

        bench_common.prune_ref_cache(self.out_dir, 0, [SHA_PROTECTED])
        self.assertEqual(present_entries(self.out_dir), [SHA_PROTECTED])

    def test_using_a_ref_makes_it_the_most_recent(self):
        os.makedirs(self.out_dir)
        install(self.out_dir, SHA_OLDEST, 1 * SECONDS_APART)
        install(self.out_dir, SHA_NEWEST, 2 * SECONDS_APART)

        compiler = bench_common.build_compiler_from_ref(SHA_OLDEST, self.out_dir)
        self.assertEqual(compiler, bench_common.installed_compiler_of(bench_common.ref_entry_of(SHA_OLDEST, self.out_dir)))
        bench_common.prune_ref_cache(self.out_dir, 1, [])

        self.assertEqual(present_entries(self.out_dir), [SHA_OLDEST])

    def test_an_entry_without_a_compiler_is_deleted_and_unrelated_directories_are_not(self):
        os.makedirs(self.out_dir)
        install(self.out_dir, SHA_NEWEST, SECONDS_APART)
        stale = bench_common.ref_entry_of(SHA_UNUSABLE, self.out_dir)
        write(os.path.join(stale, "tree", "file.maxtest"))
        write(os.path.join(self.out_dir, "ref-notes", "keep.txt"))
        write(os.path.join(self.out_dir, "go", "main"))

        bench_common.prune_ref_cache(self.out_dir, bench_common.DEFAULT_KEEP_REFS, [])

        self.assertEqual(present_entries(self.out_dir), [SHA_NEWEST])
        self.assertTrue(os.path.isfile(os.path.join(self.out_dir, "ref-notes", "keep.txt")))
        self.assertTrue(os.path.isfile(os.path.join(self.out_dir, "go", "main")))

    def test_discarding_an_entry_removes_its_worktree_registration_too(self):
        head = bench_common.git(bench_common.REPO, "rev-parse", "--short", "HEAD")
        entry = bench_common.ref_entry_of(head, self.out_dir)
        tree = os.path.join(entry, bench_common.REF_BUILD_TREE_NAME)
        os.makedirs(entry)
        bench_common.git(bench_common.REPO, "worktree", "add", "--detach", tree, head)
        self.assertIn(os.path.basename(entry), bench_common.git(bench_common.REPO, "worktree", "list"))

        bench_common.discard_ref_entry(entry)

        self.assertFalse(os.path.exists(entry))
        self.assertNotIn(os.path.basename(entry), bench_common.git(bench_common.REPO, "worktree", "list"))

    def test_a_ref_arm_has_its_identity_from_its_sha_not_from_the_checkout_above_it(self):
        os.makedirs(self.out_dir)
        entry = install(self.out_dir, SHA_NEWEST, SECONDS_APART)
        identity = bench_common.arm_identity(plan_for(SHA_NEWEST)[0], bench_common.installed_compiler_of(entry))
        self.assertEqual((identity["head"], identity["dirty"]), (SHA_NEWEST, False))
        self.assertNotIn("checkout", identity)

    def test_a_ref_arm_compiles_the_program_its_commit_holds(self):
        head = bench_common.git(bench_common.REPO, "rev-parse", "--short", "HEAD")
        committed = subprocess.run(["git", "-C", bench_common.REPO, "cat-file", "blob", f"{head}:bench/aes/aes.maxon"],
                                   capture_output=True, check=True).stdout
        planned = plan_for(head)[0]

        path = bench_common.arm_source(planned, self.out_dir, os.path.join(bench_common.REPO, AES_RELATIVE))

        self.assertEqual(path, os.path.join(bench_common.ref_entry_of(head, self.out_dir), "sources", AES_RELATIVE))
        with open(path, "rb") as fh:
            self.assertEqual(fh.read(), committed)

        with self.assertRaises(bench_common.BuildFailure):
            bench_common.arm_source(planned, self.out_dir, os.path.join(bench_common.REPO, "bench", "aes", "absent.maxon"))


if __name__ == "__main__":
    unittest.main()
