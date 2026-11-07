using FolderSync.Core.Abstractions;
using System.Security.Cryptography;

namespace FolderSync.Core
{
    public class FolderSynchronizer
    {
        private readonly ILogger _logger;
        private readonly string _sourceFolderPath;
        private readonly string _replicaFolderPath;

        // Windows file path system is not case-sensitive, but Linux is
        private StringComparer _comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        private StringComparison _comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

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
            var directoriesToCopy = sourceDirectories.Except(replicaDirectories, _comparer).ToList();

            foreach (var directory in directoriesToCopy)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    CreateDirectory(directory);
                }
                catch (Exception ex)
                {
                    _logger.Log($"Failed to create a directory: {directory}. Message: {ex.Message}");
                }
            }

            foreach (var file in filesToDelete)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    DeleteFile(file);
                }
                catch (Exception ex)
                {
                    _logger.Log($"Failed to delete a file: {file}. Message: {ex.Message}");
                }
            }

            foreach (var file in filesToCompare)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (!AreFilesEqual(file))
                    {
                        filesToCopy.Add(file);
                    }
                }
                catch (Exception ex) 
                {
                    _logger.Log($"Failed to compare a file: {file}. Message: {ex.Message}");
                }
            }

            foreach (var file in filesToCopy)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    CopyFile(file);
                }
                catch (Exception ex)
                {
                    _logger.Log($"Failed to copy a file: {file}. Message: {ex.Message}");
                }
            }

            foreach (var directory in directoriesToDelete)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    DeleteDirectory(directory);
                }
                catch (Exception ex)
                {
                    _logger.Log($"Failed to delete a directory: {directory}. Message: {ex.Message}");
                }
            }
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
                return;
            }

            File.Copy(fullSourceFilePath, fullReplicaFilePath, overwrite: true);
            _logger.Log($"{relativeFilePath} file copied to replica folder.");
        }   

        private void DeleteFile(string relativeFilePath)
        {
            string fullFilePath = Path.Combine(_replicaFolderPath, relativeFilePath);

            if (!File.Exists(fullFilePath))
            {
                return;
            }

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

            if(sourceFileSize != replicaFileSize)
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
            Directory.Delete(fullDirectoryPath, true);
            _logger.Log($"{relativeDirectoryPath} directory has been deleted from replica folder.");
        }

    }
}
