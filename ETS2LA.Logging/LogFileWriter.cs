namespace ETS2LA.Logging;

public class LogFileWriter
{
    private static readonly Lazy<LogFileWriter> _instance = new(() => new LogFileWriter());
    public static LogFileWriter Current => _instance.Value;

    private static readonly object fileLock = new();
    private DateTime lastWriteTime = DateTime.MinValue;

    # if WINDOWS
        private static string logFilePath = $"ets2la.log";
    # else
        private static string logFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ETS2LA", "ets2la.log");
    # endif

    private List<string> logsToWrite = new();

    public LogFileWriter()
    {
        # if LINUX
            if (!Directory.Exists(Path.GetDirectoryName(logFilePath)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(logFilePath) ?? throw new InvalidOperationException("Failed to get log file directory."));
            }
        # endif

        if (File.Exists(logFilePath))
        {
            File.Delete(logFilePath);
            File.WriteAllText(logFilePath, "");
        }
        else
        {
            File.WriteAllText(logFilePath, "");
        }

        Logger.OnLog += (logTuple) =>
        {
            string logLine = $"{logTuple.Item1} {logTuple.Item2}";
            lock (fileLock)
            {
                logsToWrite.Add(logLine);
            }
        };

        Task.Factory.StartNew(Writer, TaskCreationOptions.LongRunning);
    }

    private void Writer()
    {
        while (true)
        {
            Thread.Sleep(5000);
            Save();
        }
    }

    public void Save()
    {
        lock (fileLock)
        {
            try
            {
                if (logsToWrite.Count > 0)
                {
                    File.AppendAllLines(logFilePath, logsToWrite);
                    logsToWrite.Clear();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write log to file: {ex.Message}");
            }
        }
    }
}