using FolderSync.Core.Abstractions;

namespace FolderSync.Tests
{
    public class FakeLogger : ILogger
    {
        public List<string> Messages { get; } = new List<string>();
        public void Log(string message)
        {
            Messages.Add(message);
        }
    }
}
