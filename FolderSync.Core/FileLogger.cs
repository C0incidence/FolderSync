using FolderSync.Core.Abstractions;
using System.Text;

namespace FolderSync.Core
{
    public class FileLogger : ILogger
    {
        private readonly string _path;

        public FileLogger(string path)
        {
            _path = path;

            string? directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public void Log(string message)
        {
            string messageToLog = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}{Environment.NewLine}";

            byte[] data = Encoding.UTF8.GetBytes(messageToLog);

            using (FileStream fs = new FileStream(_path, FileMode.Append))
            {
                fs.Write(data, 0, data.Length);
            }
        }
    }
}
