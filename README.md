# FolderSync

A console application in C# that performs **one-way periodic synchronization** of two folders: after each run, the content of the **replica** folder exactly matches the content of the **source** folder.

All file and folder operations (creation, update, deletion) are logged both to the console and to a log file.

## Requirements

- .NET 8 SDK or later

## Build and run

```bash
dotnet build
dotnet run --project FolderSync.App -- <source> <replica> <intervalSeconds> <logFile>
```

Example:

```bash
dotnet run --project FolderSync.App -- "C:\Data\Source" "C:\Data\Replica" 30 "C:\Logs\sync.log"
```

| Argument | Description |
| --- | --- |
| `source` | Folder to synchronize from. If it does not exist, the run is skipped and logged. |
| `replica` | Folder to synchronize to. Created automatically if it does not exist. |
| `intervalSeconds` | Synchronization interval in seconds, from 1 to 86399 (less than 24 hours). |
| `logFile` | Path to the log file. Its folder is created automatically if needed. |

Press **Ctrl+C** to stop the program. The current file operation finishes, then the program exits.

### Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Program stopped normally by the user |
| `1` | Invalid arguments or startup error |

## How it works

The first synchronization runs immediately at startup, then repeats every interval. Each run:

1. Collects relative paths of all files and folders in source and replica.
2. Deletes files that exist only in the replica.
3. Deletes folders that exist only in the replica.
4. Creates folders that exist only in the source (including empty ones).
5. Compares files that exist in both folders and marks changed ones for copying.
6. Copies new and changed files to the replica.

Relative paths are used as the common key: the same file has different full paths in source and replica, but the same path relative to the folder root.

### File comparison

Files are compared from the cheapest check to the most expensive one:

1. **Size** — if sizes differ, the file has changed. No file content is read.
2. **MD5 hash** — calculated only when sizes are equal, to detect changes that keep the same size.

Unchanged files are not copied again, and nothing is logged for them.

## Design decisions

- **Source is never created.** If the source folder is missing, the run is skipped and logged. Treating a missing source as empty would delete the entire replica, which is the worst possible outcome for a backup.
- **Replica is created when missing.** It is the output of the program, so a missing replica simply means everything has to be copied.
- **Path validation at startup.** Paths are normalized (`Path.GetFullPath`, trailing separators removed). Identical paths, a replica inside the source, or a source inside the replica are rejected, because they would lead to recursive copying or deletion of the source.
- **Case sensitivity.** Paths are compared case-insensitively on Windows and macOS (default file system behavior) and case-sensitively on Linux.
- **No overlapping runs.** `PeriodicTimer` is used, so the next run never starts before the previous one has finished, even if a run takes longer than the interval.
- **Errors do not stop the program.** A failure on a single file or folder (for example, a file locked by another process) is logged, and the run continues with the next item. If a whole run fails, the error is logged and the next run is attempted after the interval.
- **Cancellation.** Ctrl+C is checked between file operations, so a file is never left half-copied.
- **Logging through an interface.** The synchronizer depends on `ILogger`. `CompositeLogger` forwards messages to the console and file loggers, and the tests use a fake logger to check what was logged.

## Project structure

```
FolderSync.App     Console application: argument parsing, logger setup, timer loop, Ctrl+C handling
FolderSync.Core    Synchronization logic and loggers
FolderSync.Tests   NUnit tests for the synchronizer
```

## Tests

```bash
dotnet test
```

Each test runs in its own temporary folder, which is deleted afterwards. The tests cover:

- new, deleted and modified files (including a modification that keeps the same file size);
- files in nested folders and empty folders;
- deletion of extra nested folders in the replica;
- a missing source folder leaving the replica untouched;
- constructor validation: identical paths, nested paths, and valid paths.

## Known limitations

- A file or folder renamed **only by letter case** on a case-insensitive system keeps its old name in the replica. Its content is still synchronized.
- If the program is stopped in the middle of a run, the replica can be partially synchronized. The next run completes the synchronization.
- Every changed file with an unchanged size is fully read to calculate its MD5 hash, which can take time for very large files.
