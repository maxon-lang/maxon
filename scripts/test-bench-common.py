import contextlib
import io
import os
import platform
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

    def test_a_ref_arm_leaves_out_a_companion_its_commit_lacks(self):
        head = bench_common.git(bench_common.REPO, "rev-parse", "--short", "HEAD")
        planned = plan_for(head)[0]
        held = os.path.join(bench_common.REPO, AES_RELATIVE)
        lacking = os.path.join(bench_common.REPO, "bench", "aes", "absent.maxon")

        staged = bench_common.arm_companions(planned, self.out_dir, [held, lacking])

        self.assertEqual(staged, [os.path.join(bench_common.ref_entry_of(head, self.out_dir), "sources", AES_RELATIVE)])

    def test_a_checkout_arm_refuses_a_companion_its_checkout_lacks(self):
        slot = {"label": bench_common.SLOT_LABEL, "sha": None, "compiler": bench_common.SLOT_COMPILER}

        with self.assertRaises(bench_common.BuildFailure):
            bench_common.arm_companions(slot, self.out_dir, [os.path.join(bench_common.REPO, "bench", "aes", "absent.maxon")])

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


CLOCK_NANOS = 20
FLOOR_TICKS = bench_common.RESOLVABLE_TICKS


def maxon_runnable(name, compiler=None):
    return {"name": name, "kind": bench_common.KIND_MAXON, "compiler": compiler or name}


def go_runnable(name):
    return {"name": name, "kind": bench_common.KIND_GO}


def seen_by(*shapes_and_names):
    return {"row": {shape: set(names) for shape, names in shapes_and_names}}


SHAPE = (8, 100, 5, "7")
OTHER_SHAPE = (8, 100, 5, "8")


class ParseOutputTest(unittest.TestCase):
    def test_rows_and_the_clock_line_are_parsed(self):
        results, resolutions = bench_common.parse_output("arm", "clock 25\nrow 8 100 5 400000 7\r\nother 0 1 5 90 123\n")

        self.assertEqual(resolutions, [25])
        self.assertEqual(results["row"], {"bytes": 8, "iterations": 100, "repeats": 5, "nanos": 400000, "digest": "7"})
        self.assertEqual(list(results), ["row", "other"])

    def test_every_malformed_output_is_a_verification_failure(self):
        for stdout in ("", "clock 25\n", "row 8 100 5 400000\n", "row 8 100 5 400000 7 9\n", "row 8 x 5 400000 7\n",
                       "row 8 100 5 400000 7\nrow 8 100 5 400000 7\n", "clock x\nrow 8 100 5 400000 7\n", "clock 0\nrow 8 100 5 400000 7\n"):
            with self.subTest(stdout=stdout), self.assertRaises(bench_common.VerificationFailure):
                bench_common.parse_output("arm", stdout)

    def test_two_clock_lines_are_both_returned_for_the_caller_to_refuse(self):
        _, resolutions = bench_common.parse_output("arm", "clock 25\nclock 30\nrow 8 100 5 400000 7\n")

        self.assertEqual(resolutions, [25, 30])


class CheckAgreementTest(unittest.TestCase):
    def check(self, seen, runnables):
        bench_common.check_agreement(seen, runnables, ["row"])

    def test_arms_that_print_one_shape_agree(self):
        self.check(seen_by((SHAPE, ["a", "go"])), [maxon_runnable("a"), go_runnable("go")])

    def test_a_different_digest_is_refused_naming_the_arms(self):
        with self.assertRaisesRegex(bench_common.VerificationFailure, "disagree on row.*go"):
            self.check(seen_by((SHAPE, ["a"]), (OTHER_SHAPE, ["go"])), [maxon_runnable("a"), go_runnable("go")])

    def test_a_go_arm_that_prints_no_row_is_refused(self):
        with self.assertRaisesRegex(bench_common.VerificationFailure, "printed no row"):
            self.check(seen_by((SHAPE, ["a"])), [maxon_runnable("a"), go_runnable("go")])

    def test_a_control_compiler_built_before_the_workload_may_omit_it(self):
        self.check(seen_by((SHAPE, ["slot", "go"])), [maxon_runnable("slot"), maxon_runnable("ref"), go_runnable("go")])

    def test_every_tier_arm_of_a_control_compiler_may_omit_it(self):
        runnables = [maxon_runnable("slot-aes", "slot"), maxon_runnable("slot-software", "slot"),
                     maxon_runnable("ref-aes", "ref"), maxon_runnable("ref-software", "ref"), go_runnable("go")]

        self.check(seen_by((SHAPE, ["slot-aes", "slot-software", "go"])), runnables)

    def test_a_tier_arm_that_drops_a_row_its_compiler_prints_on_another_tier_is_refused(self):
        runnables = [maxon_runnable("slot-aes", "slot"), maxon_runnable("slot-software", "slot"), go_runnable("go")]

        with self.assertRaisesRegex(bench_common.VerificationFailure, "slot-software.*printed no row"):
            self.check(seen_by((SHAPE, ["slot-aes", "go"])), runnables)

    def test_a_workload_no_maxon_arm_printed_is_refused(self):
        with self.assertRaisesRegex(bench_common.VerificationFailure, "printed no row"):
            self.check(seen_by((SHAPE, ["go"])), [maxon_runnable("slot"), go_runnable("go")])


class CheckResolutionTest(unittest.TestCase):
    def check(self, nanos, resolution):
        bench_common.check_resolution({("row", "arm"): nanos}, {"arm": resolution}, ["row"], "in the programs")

    def test_a_row_at_the_floor_passes(self):
        self.check(FLOOR_TICKS * CLOCK_NANOS, CLOCK_NANOS)

    def test_a_row_under_the_floor_is_refused_naming_where_to_raise_it(self):
        with self.assertRaisesRegex(bench_common.VerificationFailure, "arm timed row at .* ns.*Raise row's base iterations in the programs"):
            self.check(FLOOR_TICKS * CLOCK_NANOS - 1, CLOCK_NANOS)

    def test_an_arm_with_no_clock_line_is_not_gated(self):
        self.check(1, None)


class CollectTest(unittest.TestCase):
    def rounds(self, outputs):
        pending = {name: list(rounds) for name, rounds in outputs.items()}
        return lambda runnable: pending[runnable["name"]].pop(0)

    def row(self, nanos, digest="7"):
        return {"row": {"bytes": 8, "iterations": 100, "repeats": 5, "nanos": nanos, "digest": digest}}

    def collect(self, outputs, runnables, runs=2):
        with mock.patch.object(bench_common, "run_once", self.rounds(outputs)):
            return bench_common.collect(runnables, runs, 0, "in the programs")

    def test_each_cell_is_its_arms_best_time_and_each_resolution_its_largest(self):
        outputs = {"a": [(self.row(900000), [30]), (self.row(800000), [40])], "go": [(self.row(700000), [25]), (self.row(750000), [25])]}

        best, seen, order, resolutions = self.collect(outputs, [maxon_runnable("a"), go_runnable("go")])

        self.assertEqual(best, {("row", "a"): 800000, ("row", "go"): 700000})
        self.assertEqual(resolutions, {"a": 40, "go": 25})
        self.assertEqual(order, ["row"])
        self.assertEqual(seen, {"row": {SHAPE: {"a", "go"}}})

    def test_a_digest_that_differs_between_arms_fails_the_collection(self):
        outputs = {"a": [(self.row(900000), [30])], "go": [(self.row(700000, digest="8"), [25])]}

        with self.assertRaises(bench_common.VerificationFailure):
            self.collect(outputs, [maxon_runnable("a"), go_runnable("go")], runs=1)

    def test_a_go_arm_with_two_clock_lines_fails_the_collection(self):
        outputs = {"go": [(self.row(900000), [30, 31])]}

        with self.assertRaisesRegex(bench_common.VerificationFailure, "2 'clock resolutionNanos' lines"):
            self.collect(outputs, [go_runnable("go")], runs=1)

    def test_a_row_too_short_for_its_clock_fails_the_collection(self):
        outputs = {"go": [(self.row(FLOOR_TICKS * 25 - 1), [25])]}

        with self.assertRaises(bench_common.VerificationFailure):
            self.collect(outputs, [go_runnable("go")], runs=1)


class PrintTablesTest(unittest.TestCase):
    BEST = {("row", "slot-aes"): 400000, ("row", "slot-software"): 800000, ("row", "go"): 200000, ("row", "go-software"): 400000}
    SEEN = {"row": {SHAPE: {"slot-aes", "slot-software", "go", "go-software"}}}
    NAMES = ["slot-aes", "slot-software", "go", "go-software"]

    def printed(self, ratio_tables):
        out = io.StringIO()
        with contextlib.redirect_stdout(out):
            bench_common.print_tables(["row"], self.BEST, self.SEEN, self.NAMES, bench_common.NANOS_PER_OP, ratio_tables)
        return out.getvalue()

    def test_the_throughput_table_shows_every_arm_in_MB_per_second_for_a_byte_workload(self):
        text = self.printed([])

        self.assertIn("slot-software", text)
        self.assertIn("2.0 MB/s", text)
        self.assertIn("4.0 MB/s", text)

    def test_a_table_of_pairs_shows_each_ratio_against_its_own_partner(self):
        text = self.printed(bench_common.ratio_table_of_pairs([("slot-aes", "go"), ("slot-software", "go-software")]))

        self.assertIn("slot-aes vs go", text)
        self.assertIn("slot-software vs go-software", text)
        self.assertRegex(text, r"row\s+2\.00\s+2\.00\n")

    def test_tables_per_maxon_arm_show_every_go_arm(self):
        text = self.printed(bench_common.ratio_tables_per_maxon_arm(["slot-aes"], ["go", "go-software"]))

        self.assertIn("ratios, slot-aes time / Go time", text)
        self.assertRegex(text, r"row\s+2\.00\s+1\.00\n")


class SmallHelpersTest(unittest.TestCase):
    def test_a_note_is_one_table_cell(self):
        self.assertEqual(bench_common.note_cell("a | b\n  c"), "a / b c")

    def test_a_row_prefix_names_the_run(self):
        context = {"today": "2026-01-02", "head": "abc", "dirty": True, "host": "Linux x86_64, cpu", "runs": 5}

        self.assertEqual(bench_common.row_prefix(context), ["2026-01-02", "abc", "dirty", "Linux x86_64, cpu", "5"])
        self.assertEqual(bench_common.row_prefix(context, lane="x64")[3], "x64")

    def test_every_log_header_carries_the_shared_bullets_and_its_own_table(self):
        header = bench_common.log_header("Title", "Intro.", ["An extra bullet."], "Columns: notes.", ("date", "note"))

        self.assertTrue(header.startswith("# Title\n\nIntro.\n\n- **Rows are appended by the tool"))
        self.assertIn("- **Rows rot.**", header)
        self.assertIn("- An extra bullet.\n\nColumns: notes.\n\n| date | note |\n|---|---|\n", header)

    def test_the_go_release_is_read_from_the_version_line(self):
        self.assertEqual(bench_common.go_release("go version go1.24.7 linux/amd64"), "go1.24.7")
        self.assertEqual(bench_common.go_release("go version devel go1.25-abc Mon linux/amd64"), "go1.25-abc")

    def test_the_host_description_names_the_machine_and_the_cpu(self):
        description = bench_common.host_description()

        self.assertTrue(description.startswith(f"{platform.system()} {platform.machine()}"))
        self.assertEqual(description, ", ".join(part for part in [f"{platform.system()} {platform.machine()}", bench_common.cpu_model()] if part))

    def test_the_go_build_stamp_covers_the_shared_package(self):
        scratch = bench_common.temp_directory("test-bench-common-go")
        self.addCleanup(scratch.cleanup)
        program = os.path.join(scratch.name, "program")
        shared = os.path.join(scratch.name, "shared")
        write(os.path.join(program, "main.go"), "package main")
        write(os.path.join(shared, "shared.go"), "package shared")
        stamps = []

        def build(go, env, flags, package, cwd, exe, stamp, what):
            stamps.append((stamp["sources"], what))
            return exe

        with mock.patch.object(bench_common, "REPO", scratch.name), mock.patch.object(bench_common, "go_query", lambda *a: "go1"), \
                mock.patch.object(bench_common, "go_target_settings", lambda *a: {}), mock.patch.object(bench_common, "build_go_cached", build):
            bench_common.build_go_variants("go", program, scratch.name, {}, "", {"default": []}, dependency_dirs=[shared], target_label=" on x64")
            write(os.path.join(shared, "shared.go"), "package shared // edited")
            bench_common.build_go_variants("go", program, scratch.name, {}, "", {"default": []}, dependency_dirs=[shared], target_label=" on x64")

        self.assertEqual(set(stamps[0][0]), {"program/main.go", "shared/shared.go"})
        self.assertNotEqual(stamps[0][0], stamps[1][0])
        self.assertEqual(stamps[0][1], "the default variant on x64")


if __name__ == "__main__":
    unittest.main()
