"""Queue checks use tiny fixtures and mocked deletion; never execute real cleanup."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import unittest
from unittest.mock import patch


REPO = Path(__file__).resolve().parents[2]
SCRIPT = REPO / "tools/cleanup_temp.py"
spec = importlib.util.spec_from_file_location("cleanup_temp", SCRIPT)
cleanup = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cleanup)
WORK = REPO / ".artifacts/cleanup-queue-tests/python"


class CleanupQueueChecks(unittest.TestCase):
    def setUp(self):
        self.work = WORK / self._testMethodName
        self.work.mkdir(parents=True, exist_ok=True)
        self.database = self.work / "queue.json"
        self.database.write_text('{"version": 1, "entries": []}\n', encoding="utf-8")

    def directory(self, name):
        path = self.work / name
        path.mkdir(exist_ok=True)
        (path / "owned.txt").write_text("tiny test fixture", encoding="utf-8")
        return path

    def add(self, *paths):
        cleanup.register(self.database, [str(path) for path in paths], str(self.work), "fixture complete")

    def test_registration_deduplicates_literal_unicode_paths_without_deletion(self):
        path = self.directory("中文 [cards] with spaces")
        self.add(path)
        self.add(path)
        records = cleanup.load_database(self.database)["entries"]
        self.assertEqual([str(path)], [item["path"] for item in records])
        self.assertTrue((path / "owned.txt").is_file())

    def test_success_is_removed_but_failed_deletion_is_retained(self):
        done, busy = self.directory("success"), self.directory("busy")
        self.add(done, busy)
        with patch.object(cleanup, "remove_directory", side_effect=[None, PermissionError("in use")]) as remove:
            self.assertEqual(1, cleanup.clean(self.database))
        self.assertEqual(2, remove.call_count)
        entries = cleanup.load_database(self.database)["entries"]
        self.assertEqual([str(busy)], [item["path"] for item in entries])
        self.assertIn("in use", entries[0]["last_error"])
        self.assertTrue(done.exists())  # The test deliberately simulated, rather than performed, deletion.

    def test_already_missing_directory_removes_record_without_deletion(self):
        self.add(self.work / "never-created")
        with patch.object(cleanup, "remove_directory") as remove:
            self.assertEqual(0, cleanup.clean(self.database))
        remove.assert_not_called()
        self.assertEqual([], cleanup.load_database(self.database)["entries"])

    def test_preview_changes_neither_queue_nor_directories(self):
        path = self.directory("preview")
        self.add(path)
        before = self.database.read_bytes()
        with patch.object(cleanup, "remove_directory") as remove:
            self.assertEqual(0, cleanup.clean(self.database, dry_run=True))
        remove.assert_not_called()
        self.assertEqual(before, self.database.read_bytes())
        self.assertTrue(path.exists())

    def test_invalid_batch_is_atomic_and_cannot_escape_root(self):
        before = self.database.read_bytes()
        with self.assertRaises(ValueError):
            self.add(self.directory("valid"), self.work / ".." / "outside")
        self.assertEqual(before, self.database.read_bytes())
        for path, root in ((self.work, self.work), (REPO, REPO.parent), (REPO / "src", REPO.parent), (Path.home(), Path.home().parent)):
            with self.assertRaises(ValueError):
                cleanup.checked_target(path, root)

    def test_database_cannot_be_inside_its_cleanup_target(self):
        with self.assertRaises(ValueError):
            cleanup.register(self.database, [str(self.work)], str(WORK), "unsafe")

    def test_malformed_database_is_not_overwritten(self):
        self.database.write_text("broken JSON", encoding="utf-8")
        self.assertEqual(1, cleanup.main(["--database", str(self.database), "--add", str(self.work / "unused"), "--root", str(self.work)]))
        self.assertEqual("broken JSON", self.database.read_text(encoding="utf-8"))

    def test_concurrent_registrations_preserve_every_entry(self):
        processes = [subprocess.Popen([sys.executable, "-B", str(SCRIPT), "--database", str(self.database),
                                      "--add", str(self.work / f"worker-{index}"), "--root", str(self.work)],
                                     stdout=subprocess.PIPE, stderr=subprocess.PIPE) for index in range(6)]
        for process in processes:
            output, error = process.communicate(timeout=30)
            self.assertEqual(0, process.returncode, (output, error))
        entries = cleanup.load_database(self.database)["entries"]
        self.assertEqual(6, len(entries))
        self.assertEqual(6, len({item["path"] for item in entries}))

    def test_powershell_failure_is_reported_and_keeps_queue_record(self):
        path = self.directory("native-error")
        self.add(path)
        with patch.object(cleanup.subprocess, "run", return_value=subprocess.CompletedProcess([], 1, "", "locked")) as run:
            self.assertEqual(1, cleanup.clean(self.database))
        entries = cleanup.load_database(self.database)["entries"]
        self.assertEqual([str(path)], [item["path"] for item in entries])
        self.assertIn("locked", entries[0]["last_error"])
        self.assertEqual(str(path), run.call_args.kwargs["env"]["CARD_CLEANUP_TARGET"])

    def test_success_exit_does_not_clear_a_directory_that_still_exists(self):
        path = self.directory("still-present")
        self.add(path)
        with patch.object(cleanup.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "", "")):
            self.assertEqual(1, cleanup.clean(self.database))
        entries = cleanup.load_database(self.database)["entries"]
        self.assertEqual([str(path)], [item["path"] for item in entries])
        self.assertIn("still exists", entries[0]["last_error"])


if __name__ == "__main__":
    try:
        unittest.main()
    finally:
        if WORK.exists():
            cleanup.register(cleanup.DEFAULT_DATABASE, [str(WORK.parent)], str(REPO / ".artifacts"),
                             "Small cleanup-queue test fixtures; tests simulated deletion only")
