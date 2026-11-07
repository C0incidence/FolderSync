using FolderSync.Core.Abstractions;

namespace FolderSync.Core
{
    public class CompositeLogger : ILogger
    {
        private readonly ILogger[] _loggers;

        public CompositeLogger(params ILogger[] loggers)
        {
            _loggers = loggers;
        }

        public void Log(string message)
        {
            foreach(var logger in _loggers)
            {
                try
                {
                    logger.Log(message);
                }
                catch(Exception ex)
                {
                    Console.Error.WriteLine($"{logger.GetType().Name} failed: {ex.Message}");
                }
            }
        }
    }
}
