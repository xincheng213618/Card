"""Register disposable directories; run without options to clean the text queue.

Agents use --add or --list only. The user runs the cleanup command manually.
Deletion uses PowerShell -LiteralPath on Windows and never scans for candidates.
"""
from __future__ import annotations

import argparse
import base64
from contextlib import contextmanager
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import shutil
import stat
import subprocess
import sys
import time


REPO = Path(__file__).resolve().parents[1]
DEFAULT_DATABASE = Path(__file__).with_name("cleanup_queue.json")


def absolute_path(value: str | Path) -> Path:
    path = Path(value)
    if not path.is_absolute() or any(char in str(path) for char in "*?"):
        raise ValueError(f"Expected an absolute, literal path: {value}")
    return Path(os.path.abspath(path))


def is_link(path: Path) -> bool:
    try:
        info = path.lstat()
    except FileNotFoundError:
        return False
    return stat.S_ISLNK(info.st_mode) or bool(
        getattr(info, "st_file_attributes", 0) & stat.FILE_ATTRIBUTE_REPARSE_POINT
    )


def checked_target(value: str | Path, root_value: str | Path) -> Path:
    path, root = absolute_path(value), absolute_path(root_value)
    for item in (path, *path.parents, root, *root.parents):
        if is_link(item):
            raise ValueError(f"Linked paths are not cleanup targets: {item}")
    path, root = path.resolve(), root.resolve()
    protected = {Path(path.anchor), Path.home().resolve(), REPO}
    if root in protected or path == root or not path.is_relative_to(root):
        raise ValueError(f"Target must be strictly inside a dedicated temporary/output root: {path}")
    if any(item == path or item.is_relative_to(path) for item in protected):
        raise ValueError(f"Refusing a source, home or drive ancestor: {path}")
    if path.is_relative_to(REPO) and not any(path.is_relative_to(REPO / name) for name in (".artifacts", ".codex-artifacts", "artifacts")):
        raise ValueError(f"Only disposable artifact directories may be cleaned inside the repository: {path}")
    return path


@contextmanager
def database_lock(database: Path):
    """A persistent one-byte lock protects atomic queue updates across processes."""
    with database.with_suffix(database.suffix + ".lock").open("a+b") as handle:
        if handle.seek(0, os.SEEK_END) == 0:
            handle.write(b"0")
            handle.flush()
        deadline = time.monotonic() + 15
        while True:
            try:
                handle.seek(0)
                if os.name == "nt":
                    import msvcrt
                    msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
                else:
                    import fcntl
                    fcntl.flock(handle.fileno(), fcntl.LOCK_EX | fcntl.LOCK_NB)
                break
            except OSError:
                if time.monotonic() >= deadline:
                    raise RuntimeError(f"Cleanup queue is busy: {database}")
                time.sleep(0.1)
        try:
            yield
        finally:
            handle.seek(0)
            if os.name == "nt":
                msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(handle.fileno(), fcntl.LOCK_UN)


def load_database(database: Path) -> dict:
    if not database.exists():
        return {"version": 1, "entries": []}
    data = json.loads(database.read_text(encoding="utf-8-sig"))
    if not isinstance(data, dict) or data.get("version") != 1 or not isinstance(data.get("entries"), list):
        raise ValueError(f"Invalid cleanup database: {database}")
    for entry in data["entries"]:
        if not isinstance(entry, dict) or not all(isinstance(entry.get(key), str) for key in ("path", "root", "reason")):
            raise ValueError(f"Invalid cleanup entry in {database}")
    return data


def save_database(database: Path, data: dict) -> None:
    staging = database.with_suffix(database.suffix + ".tmp")
    with staging.open("w", encoding="utf-8", newline="\n") as handle:
        json.dump(data, handle, ensure_ascii=False, indent=2)
        handle.write("\n")
        handle.flush()
        os.fsync(handle.fileno())
    os.replace(staging, database)


def register(database: Path, paths: list[str], root: str, reason: str, lock_root: str | None = None) -> int:
    targets = [checked_target(path, root) for path in paths]
    if any(database.resolve().is_relative_to(target) for target in targets):
        raise ValueError("A cleanup directory cannot contain its own database.")
    root_path = absolute_path(root).resolve()
    lock_path = absolute_path(lock_root).resolve() if lock_root else None
    if lock_path and any(target != lock_path and not target.is_relative_to(lock_path) for target in targets):
        raise ValueError("The verification lock must belong to the target or an ancestor.")
    # Validate the whole batch before updating, so an invalid path never partly registers.
    with database_lock(database):
        data = load_database(database)
        by_path = {os.path.normcase(str(absolute_path(entry["path"]))): entry for entry in data["entries"]}
        for target in targets:
            key = os.path.normcase(str(target))
            entry = by_path.get(key)
            if entry is None:
                entry = {"path": str(target), "added_at": datetime.now(timezone.utc).isoformat()}
                data["entries"].append(entry)
                by_path[key] = entry
            entry.update(root=str(root_path), reason=reason)
            if lock_path:
                entry["lock_root"] = str(lock_path)
            else:
                entry.pop("lock_root", None)
            entry.pop("last_error", None)
        save_database(database, data)
    return len(targets)


REMOVE_SCRIPT = r"""
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$lease = $null
try {
    $target = (Resolve-Path -LiteralPath $env:CARD_CLEANUP_TARGET).Path
    $root = (Resolve-Path -LiteralPath $env:CARD_CLEANUP_ROOT).Path.TrimEnd('\')
    if (-not $target.Equals($env:CARD_CLEANUP_TARGET, [StringComparison]::OrdinalIgnoreCase) -or
        -not $target.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Resolved cleanup target escaped its registered root.'
    }
    $item = Get-Item -LiteralPath $target -Force
    if (-not $item.PSIsContainer) { throw 'The registered target is not a directory.' }
    for ($ancestor = $item; $null -ne $ancestor; $ancestor = $ancestor.Parent) {
        if ($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked cleanup path refused.' }
    }
    $linked = Get-ChildItem -LiteralPath $target -Recurse -Force |
        Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint } | Select-Object -First 1
    if ($null -ne $linked) { throw "Linked child refused: $($linked.FullName)" }
    $consumers = @(Get-CimInstance Win32_Process | Where-Object {
        $_.ProcessId -ne $PID -and (
            ($_.ExecutablePath -and $_.ExecutablePath.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase)) -or
            ($_.CommandLine -and $_.CommandLine.IndexOf($target, [StringComparison]::OrdinalIgnoreCase) -ge 0))
    })
    if ($consumers.Count -gt 0) { throw "Directory is in use by process IDs: $($consumers.ProcessId -join ', ')" }
    if ($env:CARD_CLEANUP_LOCK_ROOT) {
        $lockRoot = (Resolve-Path -LiteralPath $env:CARD_CLEANUP_LOCK_ROOT).Path
        $lockPath = Join-Path $lockRoot '.test-changed.lock'
        if (Test-Path -LiteralPath $lockPath) {
            $lease = [IO.File]::Open($lockPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            # Retired whole roots contain their lock; reusable bin/obj/renders keep the lock held.
            if ($lockRoot.Equals($target, [StringComparison]::OrdinalIgnoreCase)) { $lease.Dispose(); $lease = $null }
        }
    }
    Remove-Item -LiteralPath $target -Recurse -Force
    if (Test-Path -LiteralPath $target) { throw 'Directory still exists after cleanup.' }
}
catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }
finally { if ($null -ne $lease) { $lease.Dispose() } }
"""


def remove_directory(entry: dict) -> None:
    target = checked_target(entry["path"], entry["root"])
    if entry.get("lock_root"):
        lock_root = absolute_path(entry["lock_root"]).resolve()
        if target != lock_root and not target.is_relative_to(lock_root):
            raise ValueError("Verification lock is outside the target's ancestors.")
    info = target.stat()  # Access failures must not be mistaken for an absent directory.
    if not stat.S_ISDIR(info.st_mode):
        raise ValueError(f"Not a directory: {target}")
    if os.name != "nt":
        raise RuntimeError("This workspace cleanup command requires Windows PowerShell.")
    shell = shutil.which("pwsh") or shutil.which("powershell")
    if not shell:
        raise RuntimeError("PowerShell was not found.")
    environment = os.environ.copy()
    environment.update(CARD_CLEANUP_TARGET=str(target), CARD_CLEANUP_ROOT=str(absolute_path(entry["root"]).resolve()),
                       CARD_CLEANUP_LOCK_ROOT=entry.get("lock_root", ""))
    encoded = base64.b64encode(REMOVE_SCRIPT.encode("utf-16-le")).decode("ascii")
    result = subprocess.run([shell, "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded],
                            env=environment, capture_output=True, text=True, encoding="utf-8", errors="replace",
                            creationflags=subprocess.CREATE_NO_WINDOW)
    if result.returncode:
        raise RuntimeError(result.stderr.strip() or result.stdout.strip() or "PowerShell cleanup failed.")
    try:
        target.lstat()
    except FileNotFoundError:
        return
    raise RuntimeError(f"Directory still exists after cleanup: {target}")


def clean(database: Path, *, dry_run: bool = False) -> int:
    failures = 0
    with database_lock(database):
        data = load_database(database)
        for entry in list(data["entries"]):
            try:
                target = checked_target(entry["path"], entry["root"])
                if database.resolve().is_relative_to(target):
                    raise ValueError("A cleanup directory cannot contain its own database.")
                if dry_run:
                    print(f"[preview] {target} | {entry['reason']}")
                    continue
                try:
                    target.lstat()
                except FileNotFoundError:
                    print(f"[already absent] {target}")
                else:
                    remove_directory(entry)
                    print(f"[deleted] {target}")
                data["entries"].remove(entry)
            except (OSError, ValueError, RuntimeError) as error:
                failures += 1
                entry["last_error"] = str(error)
                print(f"[retained] {entry['path']} | {error}", file=sys.stderr)
            if not dry_run:
                save_database(database, data)  # Preserve progress even if later entries fail or execution stops.
        print(f"Pending: {len(data['entries'])}; failed: {failures}")
    return 1 if failures else 0


def main(argv: list[str] | None = None) -> int:
    for stream in (sys.stdout, sys.stderr):
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--database", type=Path, default=DEFAULT_DATABASE, help="UTF-8 JSON text database")
    actions = parser.add_mutually_exclusive_group()
    actions.add_argument("--add", nargs="+", metavar="DIRECTORY", help="Register exact owned paths; does not delete")
    actions.add_argument("--list", action="store_true", help="Read the pending queue without changes")
    actions.add_argument("--dry-run", action="store_true", help="Validate and preview without deletion or queue changes")
    parser.add_argument("--root", help="Verified temporary/output root containing the registered directories")
    parser.add_argument("--reason", default="Task-owned temporary output is no longer needed")
    parser.add_argument("--lock-root", help="Verification output root containing .test-changed.lock")
    args = parser.parse_args(argv)
    if args.add and not args.root:
        parser.error("--add requires --root")
    database = args.database.absolute()
    try:
        if args.add:
            count = register(database, args.add, args.root, args.reason, args.lock_root)
            print(f"Registered {count} path(s): {database}")
            return 0
        if args.list:
            with database_lock(database):
                for entry in load_database(database)["entries"]:
                    print(f"{entry['path']} | {entry['reason']}")
                    if entry.get("last_error"):
                        print(f"  Last error: {entry['last_error']}")
            return 0
        return clean(database, dry_run=args.dry_run)
    except (OSError, ValueError, RuntimeError) as error:
        print(f"Cleanup queue error: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
