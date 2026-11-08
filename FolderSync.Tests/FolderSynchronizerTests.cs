using FolderSync.Core;
using FolderSync.Core.Abstractions;

namespace FolderSync.Tests
{
    public class FolderSynchronizerTests
    {
        private FakeLogger _logger;
        private string _testFolder;
        private string _testSourcePath;
        private string _testReplicaPath;

        [SetUp]
        public void Setup()
        {
            _testFolder = Path.GetTempPath() + Guid.NewGuid();
            _testSourcePath = Path.Combine(_testFolder, "source");
            _testReplicaPath = Path.Combine(_testFolder, "replica");

            Directory.CreateDirectory(_testSourcePath);
            Directory.CreateDirectory(_testReplicaPath);
            _logger = new FakeLogger();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_testFolder))
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(_testFolder, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(entry, FileAttributes.Normal);
                }

                Directory.Delete(_testFolder, true);
            }
        }

        [Test]
        public void Synchronize_NewFileInSource_CopiesFilesToReplica()
        {
            // Arrange
            File.WriteAllText(Path.Combine(_testSourcePath, "a.txt"), "hello, world!");
            var synchronizer = new FolderSynchronizer(_logger, _testSourcePath, _testReplicaPath);

            // Act
            synchronizer.Synchronize(CancellationToken.None);

            // Assert
            var replicaFile = Path.Combine(_testReplicaPath, "a.txt");
            Assert.That(File.Exists(replicaFile), Is.True);
            Assert.That(File.ReadAllText(replicaFile), Is.EqualTo("hello, world!"));
        }

        [Test]
        public void Synchronize_FileMissingInSource_DeletesFileFromReplica()
        {
            // Arrange
            var replicaFilePath = Path.Combine(_testReplicaPath, "a.txt");
            File.WriteAllText(replicaFilePath, "old content");
            var synchronizer = new FolderSynchronizer(_logger, _testSourcePath, _testReplicaPath);

            // Act
            synchronizer.Synchronize(CancellationToken.None);

            // Assert
            Assert.That(File.Exists(replicaFilePath), Is.False);
        }

        [TestCase("old content", "modified content")]   // Different file size: spotted by size check
        [TestCase("aaaa", "bbbb")]                      // Same file size: spotted by MD5 hash check
        public void Synchronize_FileModifiedInSource_ModifiesFileInReplica(string oldContent, string newContent)
        {
            // Arrange
            var replicaFile = Path.Combine(_testReplicaPath, "a.txt");
            var sourceFile = Path.Combine(_testSourcePath, "a.txt");
            File.WriteAllText(replicaFile, oldContent);
            File.WriteAllText(sourceFile, newContent);
            var synchronizer = new FolderSynchronizer(_logger, _testSourcePath, _testReplicaPath);

            // Act
            synchronizer.Synchronize(CancellationToken.None);

            // Assert
            Assert.That(File.ReadAllText(replicaFile), Is.EqualTo(newContent));
        }

        [Test]
        public void Synchronize_FileInNestedFolder_CopiesFileWithFolderStructure()
        {
            // Arrange
            var nestedFolderPath = Path.Combine(_testSourcePath, "nested");
            var doubleNestedFolderPath = Path.Combine(nestedFolderPath, "deeper");
            var fileInNestedFolderPath = Path.Combine(doubleNestedFolderPath, "a.txt");
            Directory.CreateDirectory(nestedFolderPath);
            Directory.CreateDirectory(doubleNestedFolderPath);
            File.WriteAllText(fileInNestedFolderPath, "hello, world!");
            var synchronizer = new FolderSynchronizer(_logger, _testSourcePath, _testReplicaPath);

            // Act
            synchronizer.Synchronize(CancellationToken.None);

            // Assert
            var replicaNestedFolderPath = Path.Combine(_testReplicaPath, "nested");
            var replicaDoubleNestedFolderPath = Path.Combine(replicaNestedFolderPath, "deeper");
            var replicaNestedFolderFilePath = Path.Combine(replicaDoubleNestedFolderPath, "a.txt");

            Assert.That(Directory.Exists(replicaNestedFolderPath), Is.True);
            Assert.That(Directory.Exists(replicaDoubleNestedFolderPath), Is.True);
            Assert.That(File.Exists(replicaNestedFolderFilePath), Is.True);
            Assert.That(File.ReadAllText(replicaNestedFolderFilePath), Is.EqualTo("hello, world!"));
        }

        [Test]
        public void Synchronize_EmptyFolderInSource_CreatesEmptyFolderInReplica()
        {
            // Arrange
            Directory.CreateDirectory(Path.Combine(_testSourcePath, "empty"));
            var synchronizer = new FolderSynchronizer(_logger, _testSourcePath, _testReplicaPath);

            // Act
            synchronizer.Synchronize(CancellationToken.None);

            // Assert
            var replicaEmptyFolder = Path.Combine(_testReplicaPath, "empty");
            Assert.That(Directory.Exists(replicaEmptyFolder), Is.True);
            Assert.That(Directory.EnumerateFileSystemEntries(replicaEmptyFolder).Any(), Is.False);
        }

        [Test]
        public void Synchronize_ExtraNestedFoldersInReplica_DeletesThemWithoutErrors()
        {
            // Arrange
            var replicaArchiveFolder = Path.Combine(_testReplicaPath, "old", "archive");
            Directory.CreateDirectory(replicaArchiveFolder);
            File.WriteAllText(Path.Combine(replicaArchiveFolder, "a.txt"), "old content");
            var synchronizer = new FolderSynchronizer(_logger, _testSourcePath, _testReplicaPath);

            // Act
            synchronizer.Synchronize(CancellationToken.None);

            // Assert
            Assert.That(Directory.Exists(Path.Combine(_testReplicaPath, "old")), Is.False);
            Assert.That(_logger.Messages, Has.None.Contains("Failed"));
        }

        [Test]
        public void Synchronize_SourceFolderMissing_LeavesReplicaUntouched()
        {
            // Arrange
            Directory.Delete(_testSourcePath, true);
            var replicaFile = Path.Combine(_testReplicaPath, "a.txt");
            File.WriteAllText(replicaFile, "backup content");
            var synchronizer = new FolderSynchronizer(_logger, _testSourcePath, _testReplicaPath);

            // Act
            synchronizer.Synchronize(CancellationToken.None);

            // Assert
            Assert.That(File.Exists(replicaFile), Is.True);
            Assert.That(File.ReadAllText(replicaFile), Is.EqualTo("backup content"));
            Assert.That(_logger.Messages, Has.Some.Contains("Source folder not found"));
        }

        [Test]
        public void Synchronize_ReadOnlyFileModifiedInSource_UpdatesFileInReplica()
        {
            // Arrange
            var replicaFile = Path.Combine(_testReplicaPath, "a.txt");
            File.WriteAllText(replicaFile, "old");
            File.SetAttributes(replicaFile, FileAttributes.ReadOnly);
            File.WriteAllText(Path.Combine(_testSourcePath, "a.txt"), "new content");
            var synchronizer = new FolderSynchronizer(_logger, _testSourcePath, _testReplicaPath);

            // Act
            synchronizer.Synchronize(CancellationToken.None);

            // Assert
            Assert.That(File.ReadAllText(replicaFile), Is.EqualTo("new content"));
            Assert.That(_logger.Messages, Has.None.Contains("Failed"));
        }

        [Test]
        public void Synchronize_ReadOnlyFileMissingInSource_DeletesFileFromReplica()
        {
            // Arrange
            var replicaFile = Path.Combine(_testReplicaPath, "a.txt");
            File.WriteAllText(replicaFile, "old");
            File.SetAttributes(replicaFile, FileAttributes.ReadOnly);
            var synchronizer = new FolderSynchronizer(_logger, _testSourcePath, _testReplicaPath);

            // Act
            synchronizer.Synchronize(CancellationToken.None);

            // Assert
            Assert.That(File.Exists(replicaFile), Is.False);
            Assert.That(_logger.Messages, Has.None.Contains("Failed"));
        }

        [Test]
        public void Synchronize_ExtraFolderWithReadOnlyFileInReplica_DeletesFolder()
        {
            // Arrange
            var replicaOldFolder = Path.Combine(_testReplicaPath, "old");
            var replicaFile = Path.Combine(replicaOldFolder, "a.txt");
            Directory.CreateDirectory(replicaOldFolder);
            File.WriteAllText(replicaFile, "old");
            File.SetAttributes(replicaFile, FileAttributes.ReadOnly);
            var synchronizer = new FolderSynchronizer(_logger, _testSourcePath, _testReplicaPath);

            // Act
            synchronizer.Synchronize(CancellationToken.None);

            // Assert
            Assert.That(Directory.Exists(replicaOldFolder), Is.False);
            Assert.That(_logger.Messages, Has.None.Contains("Failed"));
        }

        [TestCase("data", "data")]              // literally the same path
        [TestCase("data", "data/")]             // trailing separator
        public void Constructor_IdenticalPaths_ThrowsArgumentException(string sourceRelative, string replicaRelative)
        {
            // Arrange
            var sourcePath = Path.Combine(_testFolder, sourceRelative);
            var replicaPath = Path.Combine(_testFolder, replicaRelative);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => new FolderSynchronizer(_logger, sourcePath, replicaPath));
        }

        [TestCase("data", "data/backup")]       // replica inside source
        [TestCase("data/backup", "data")]       // source inside replica
        public void Constructor_NestedPaths_ThrowsArgumentException(string sourceRelative, string replicaRelative)
        {
            // Arrange
            var sourcePath = Path.Combine(_testFolder, sourceRelative);
            var replicaPath = Path.Combine(_testFolder, replicaRelative);

            // Act & Assert
            Assert.Throws<ArgumentException>(() => new FolderSynchronizer(_logger, sourcePath, replicaPath));
        }
    }
}