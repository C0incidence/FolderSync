using FolderSync.Core.Abstractions;
using System.Security.Cryptography;

namespace FolderSync.Core
{
    public class FolderSynchronizer
    {
        private readonly ILogger _logger;
        private readonly string _sourceFolderPath;
        private readonly string _replicaFolderPath;

        public FolderSynchronizer(ILogger logger, string sourceFolderPath, string replicaFolderPath)
        {
            ArgumentException.ThrowIfNullOrEmpty(sourceFolderPath);
            ArgumentException.ThrowIfNullOrEmpty(replicaFolderPath);
            ArgumentNullException.ThrowIfNull(logger);

            _logger = logger;
            _sourceFolderPath = sourceFolderPath;
            _replicaFolderPath = replicaFolderPath;
        }

        public void Synchronize(CancellationToken cancellationToken)
        {
            if (!Directory.Exists(_sourceFolderPath))
            {
                _logger.Log($"Source folder not found: {_sourceFolderPath}, terminating the synchronization process.");
                return;
            }

            if (!Directory.Exists(_replicaFolderPath))
            {
                Directory.CreateDirectory(_replicaFolderPath);
                _logger.Log($"Replica folder not found: {_replicaFolderPath}, creating the folder and starting the synchronization process.");
            }

            var sourceFiles = GetRelativeFilePaths(_sourceFolderPath);
            var replicaFiles = GetRelativeFilePaths(_replicaFolderPath);

            var filesToDelete = replicaFiles.Except(sourceFiles).ToList();
            var filesToCopy = sourceFiles.Except(replicaFiles).ToList();
            var filesToCompare = replicaFiles.Intersect(sourceFiles).ToList();

            var sourceDirectories = GetRelativeDirectoryPaths(_sourceFolderPath);
            var replicaDirectories = GetRelativeDirectoryPaths(_replicaFolderPath);

            var directoriesToDelete = replicaDirectories.Except(sourceDirectories).ToList();
            var directoriesToCopy = sourceDirectories.Except(replicaDirectories).ToList();

            foreach (var directory in directoriesToCopy)
            {
                CreateDirectory(directory);
            }

            foreach (var file in filesToDelete)
            {
                DeleteFile(file);
            }

            foreach (var file in filesToCompare)
            {
                if (!AreFilesEqual(file))
                {
                    filesToCopy.Add(file);
                }
            }

            foreach (var file in filesToCopy)
            {
                CopyFile(file);
            }

            foreach (var directory in directoriesToDelete)
            {
                DeleteDirectory(directory);
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
