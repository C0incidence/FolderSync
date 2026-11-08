using FolderSync.Core;
using FolderSync.Core.Abstractions;

namespace FolderSync.App
{
    internal class Program
    {
        private const int MaxIntervalSeconds = 24 * 60 * 60;

        static async Task Main(string[] args)
        {
            if (args.Length != 4)
            {
                Console.WriteLine("Incorrect number of arguments. Usage: FolderSync.App <source> <replica> <intervalSeconds> <logFile> . Terminating the program");
                return;
            }

            var sourceFolderPath = args[0];
            var replicaFolderPath = args[1];
            var timerInterval = args[2];
            var logFilePath = args[3];

            using var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            if (!int.TryParse(timerInterval, out int interval) || interval <= 0 || interval >= MaxIntervalSeconds)
            {
                Console.WriteLine("Interval must be a positive number, but less than 24 hours.");
                return;
            }

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));
            CompositeLogger compositeLogger;
            FolderSynchronizer synchronizer;

            try
            {
                ConsoleLogger consoleLogger = new ConsoleLogger();
                FileLogger fileLogger = new FileLogger(logFilePath);
                compositeLogger = new CompositeLogger(consoleLogger, fileLogger);
                synchronizer = new FolderSynchronizer(compositeLogger, sourceFolderPath, replicaFolderPath);
            }
            catch(Exception ex)
            {
                Console.WriteLine($"Program has been stopped by the following error: {ex.Message}");
                return;
            }

            try
            {
                Synchronize(synchronizer, cts.Token, compositeLogger);

                while (await timer.WaitForNextTickAsync(cts.Token)) 
                {
                    Synchronize(synchronizer, cts.Token, compositeLogger);
                }
            }
            catch (OperationCanceledException)
            {
                compositeLogger.Log($"Program has been stopped by user.");
            }
        }

        static void Synchronize(FolderSynchronizer synchronizer, CancellationToken token, ILogger logger)
        {
            try
            {
                synchronizer.Synchronize(token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.Log($"Synchronization failed with following error: {ex.Message}.");
            }
        }
    }
}
