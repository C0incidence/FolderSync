using FolderSync.Core.Abstractions;

namespace FolderSync.Core
{
    public class ConsoleLogger : ILogger
    {
        public void Log(string message)
        {
            Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}");
        }
    }
}
