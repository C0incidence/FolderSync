using FolderSync.Core.Abstractions;
using System.Security.Cryptography;

namespace FolderSync.Core
{
    public class FolderSynchronizer
    {
        private readonly ILogger _logger;
        private readonly string _sourceFolderPath;
        private readonly string _replicaFolderPath;

        // Windows and MacOS file path systems are not case-sensitive, but Linux is
        private readonly StringComparer _comparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        private readonly StringComparison _comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        public FolderSynchronizer(ILogger logger, string sourceFolderPath, string replicaFolderPath)
        {
            ArgumentException.ThrowIfNullOrEmpty(sourceFolderPath);
            ArgumentException.ThrowIfNullOrEmpty(replicaFolderPath);
            ArgumentNullException.ThrowIfNull(logger);

            var fullSourcePath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceFolderPath));
            var fullReplicaPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(replicaFolderPath));

            var sourceWithSeparator = fullSourcePath + Path.DirectorySeparatorChar;
            var replicaWithSeparator = fullReplicaPath + Path.DirectorySeparatorChar;

            if (_comparer.Equals(fullSourcePath, fullReplicaPath))
            {
                throw new ArgumentException("Source and Replica folder paths are identical.");
            }

            if (fullReplicaPath.StartsWith(sourceWithSeparator, _comparison) || fullSourcePath.StartsWith(replicaWithSeparator, _comparison))
            {
                throw new ArgumentException("Source and Replica folders can't be stored inside each other.");
            }

            _logger = logger;
            _sourceFolderPath = fullSourcePath;
            _replicaFolderPath = fullReplicaPath;
        }

        public void Synchronize(CancellationToken cancellationToken)
        {
            if (!Directory.Exists(_sourceFolderPath))
            {
                _logger.Log($"Source folder not found: {_sourceFolderPath}, skipping this synchronization.");
                return;
            }

            if (!Directory.Exists(_replicaFolderPath))
            {
                Directory.CreateDirectory(_replicaFolderPath);
                _logger.Log($"Replica folder not found: {_replicaFolderPath}, created the folder and starting the synchronization process.");
            }


            var sourceFiles = GetRelativeFilePaths(_sourceFolderPath);
            var replicaFiles = GetRelativeFilePaths(_replicaFolderPath);

            var filesToDelete = replicaFiles.Except(sourceFiles, _comparer).ToList();
            var filesToCopy = sourceFiles.Except(replicaFiles, _comparer).ToList();
            var filesToCompare = replicaFiles.Intersect(sourceFiles, _comparer).ToList();

            var sourceDirectories = GetRelativeDirectoryPaths(_sourceFolderPath);
            var replicaDirectories = GetRelativeDirectoryPaths(_replicaFolderPath);

            var directoriesToDelete = replicaDirectories.Except(sourceDirectories, _comparer).ToList();
            var directoriesToCreate = sourceDirectories.Except(replicaDirectories, _comparer).ToList();

            ExecuteForEach(filesToDelete, DeleteFile, "delete file", cancellationToken);
            ExecuteForEach(directoriesToDelete, DeleteDirectory, "delete directory", cancellationToken);
            ExecuteForEach(directoriesToCreate, CreateDirectory, "create directory", cancellationToken);
            ExecuteForEach(filesToCompare, file =>
            {
                if (!AreFilesEqual(file))
                {
                    filesToCopy.Add(file);
                }
            }, "compare file", cancellationToken);
            ExecuteForEach(filesToCopy, CopyFile, "copy file", cancellationToken);
        }

        private List<string> GetRelativeDirectoryPaths(string path)
        {
            return Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                .Select(d => Path.GetRelativePath(path, d))
                .ToList();
        }

        private List<string> GetRelativeFilePaths(string path)
        {
            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(path, f))
                .ToList();
        }
        private void CopyFile(string relativeFilePath)
        {
            string fullSourceFilePath = Path.Combine(_sourceFolderPath, relativeFilePath);
            string fullReplicaFilePath = Path.Combine(_replicaFolderPath, relativeFilePath);

            if (!File.Exists(fullSourceFilePath))
            {
                _logger.Log($"{relativeFilePath} file disappeared from the source folder before copying, skipping.");
                return;
            }

            bool existedInReplica = File.Exists(fullReplicaFilePath);

            if (existedInReplica)
            {
                ClearReadOnly(fullReplicaFilePath);
            }

            File.Copy(fullSourceFilePath, fullReplicaFilePath, overwrite: true);

            if (existedInReplica)
            {
                _logger.Log($"{relativeFilePath} file updated in replica folder.");
            }
            else
            {
                _logger.Log($"{relativeFilePath} file created in replica folder.");
            }
        }

        private void DeleteFile(string relativeFilePath)
        {
            string fullFilePath = Path.Combine(_replicaFolderPath, relativeFilePath);

            if (!File.Exists(fullFilePath))
            {
                return;
            }

            ClearReadOnly(fullFilePath);
            File.Delete(fullFilePath);
            _logger.Log($"{relativeFilePath} file has been deleted from replica folder.");
        }

        private bool AreFilesEqual(string relativeFilePath)
        {
            var fullSourceFilePath = Path.Combine(_sourceFolderPath, relativeFilePath);
            var fullReplicaFilePath = Path.Combine(_replicaFolderPath, relativeFilePath);

            if (!File.Exists(fullSourceFilePath) || !File.Exists(fullReplicaFilePath))
            {
                return false;
            }

            long sourceFileSize = new FileInfo(fullSourceFilePath).Length;
            long replicaFileSize = new FileInfo(fullReplicaFilePath).Length;

            if (sourceFileSize != replicaFileSize)
            {
                return false;
            }

            using var sourceFileStream = File.OpenRead(fullSourceFilePath);
            using var replicaFileStream = File.OpenRead(fullReplicaFilePath);

            byte[] sourceFileHash = MD5.HashData(sourceFileStream);
            byte[] replicaFileHash = MD5.HashData(replicaFileStream);

            return sourceFileHash.SequenceEqual(replicaFileHash);
        }

        private void CreateDirectory(string relativeDirectoryPath)
        {
            string fullDirectoryPath = Path.Combine(_replicaFolderPath, relativeDirectoryPath);

            Directory.CreateDirectory(fullDirectoryPath);
            _logger.Log($"{relativeDirectoryPath} directory has been created in replica folder.");
        }

        private void DeleteDirectory(string relativeDirectoryPath)
        {
            string fullDirectoryPath = Path.Combine(_replicaFolderPath, relativeDirectoryPath);

            if (!Directory.Exists(fullDirectoryPath))
            {
                return;
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(fullDirectoryPath, "*", SearchOption.AllDirectories))
            {
                ClearReadOnly(entry);
            }

            ClearReadOnly(fullDirectoryPath);
            Directory.Delete(fullDirectoryPath, true);
            _logger.Log($"{relativeDirectoryPath} directory has been deleted from replica folder.");
        }

        private void ExecuteForEach(IEnumerable<string> relativePaths, Action<string> action, string actionName, CancellationToken cancellationToken)
        {
            foreach (var relativePath in relativePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    action(relativePath);
                }
                catch (Exception ex)
                {
                    _logger.Log($"Failed to {actionName}: {relativePath}. Message: {ex.Message}");
                }
            }
        }

        private static void ClearReadOnly(string fullPath)
        {
            var attributes = File.GetAttributes(fullPath);

            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                File.SetAttributes(fullPath, attributes & ~FileAttributes.ReadOnly);
            }
        }
    }
}
