using Spectre.Console;
using System.Runtime.CompilerServices;
using System.IO;

namespace ETS2LA.Logging;

public static class Logger
{
    public static IAnsiConsole Console { get; }

    // These are used to detect when the same log line is printed back to back
    // (ie. someone is logging a warning in Tick())
    public static string lastLine = "";
    public static int repeatCount = 0;
    private static bool hasConsole = false;

    private static readonly object logLock = new();

    public static event Action<Tuple<string, string>>? OnLog;
    static Logger()
    {
        Console = AnsiConsole.Console;
        try { Console.Write("Console initialized"); hasConsole = true; }
        catch { }
        var logFileWriter = LogFileWriter.Current;
    }

    public static string GetTimestamp()
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        return $"[grey]{timestamp}[/]";
    }

    private static string GetSourceInfo(string filePath, int lineNumber)
    {
        var folderName = Path.GetFileName(Path.GetDirectoryName(filePath) ?? "");
        var fileName = Path.GetFileName(filePath);
        return $"[underline grey]{folderName}/{fileName}:{lineNumber}[/]";
    }

    // Called from each of the log functions if the same
    // message is logged again.
    public static void PrintRepeating(string message)
    {
        if (!hasConsole) return;
        
        bool canMoveCursor = !System.Console.IsOutputRedirected;

        if (repeatCount == 0 || !canMoveCursor)
        {
            Console.MarkupLine($"{GetTimestamp()} [grey][[^^^]][/]");
        }
        else
        {
            Console.Cursor.MoveUp(1);
            Console.Write("\r");
            Console.MarkupLine($"{GetTimestamp()} [grey][[^^^]] x{repeatCount + 1}[/]");
        }
        repeatCount++;
    }

    private static void WriteLogLine(
        string level, 
        string message, 
        string color, 
        string filePath, 
        int lineNumber, 
        Exception ex = null
    )
    {
        // overlay + log file want plain text
        string plainMessage;
        try
        {
            plainMessage = Markup.Remove(message);
        }
        catch
        {
            plainMessage = message;
        }
        OnLog?.Invoke(Tuple.Create(level, plainMessage));

        if (hasConsole)
        {
            var source = GetSourceInfo(filePath, lineNumber);
            var timestamp = GetTimestamp();
            var levelTag = $"[{color}][[{level}]][/]";

            // Two columns, one on the left and one on the right
            // for the source info.
            var table = new Table().HideHeaders().HideRowSeparators().Border(TableBorder.None).Expand();
            table.AddColumn(new TableColumn("").NoWrap());
            table.AddColumn(new TableColumn("").NoWrap().Alignment(Justify.Right));

            try
            {
                table.AddRow(
                    new Markup($"{timestamp} {levelTag} {message}"),
                    new Markup(source)
                );
            } catch (Exception markupEx)
            {
                // If something goes wrong with the markup (eg. message contains invalid markup)
                // just print the message without markup instead of crashing the logger.
                Console.WriteLine($"{timestamp} [{level}] {plainMessage} ({source})");
                Console.WriteLine($"Error while writing log line: {markupEx}");
                return;
            }

            Console.Write(table);
            if (ex != null)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }

    public static void Debug(
        string message,
        [CallerFilePath] string filePath = "", 
        [CallerLineNumber] int lineNumber = 0
    )
    {
        lock (logLock)
        {
            if (message == lastLine)
            {
                PrintRepeating(message);
                return;
            }

            WriteLogLine("DBG", message, "grey", filePath, lineNumber);
            lastLine = message;
            repeatCount = 0;
        }
    }
    public static void Info(
        string message,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0
    )
    {
        lock (logLock)
        {
            if (message == lastLine)
            {
                PrintRepeating(message);
                return;
            }

            WriteLogLine("INF", message, "blue", filePath, lineNumber);
            lastLine = message;
            repeatCount = 0;
        }
    }

    public static void Warn(
        string message,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0
    )
    {
        lock (logLock)
        {
            if (message == lastLine)
            {
                PrintRepeating(message);
                return;
            }

            WriteLogLine("WRN", message, "yellow", filePath, lineNumber);
            lastLine = message;
            repeatCount = 0;
        }
    }

    public static void Error(
        string message,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0
    )
    {
        lock (logLock)
        {
            if (message == lastLine)
            {
                PrintRepeating(message);
                return;
            }

            WriteLogLine("ERR", message, "red", filePath, lineNumber);
            lastLine = message;
            repeatCount = 0;
        }
    }

    public static void Fatal(
        Exception ex, string message,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0
    )
    {
        lock (logLock)
        {
            if (message == lastLine)
            {
                PrintRepeating(message);
                return;
            }

            WriteLogLine("FTL", message, "bold red", filePath, lineNumber, ex);
            lastLine = message;
            repeatCount = 0;
        }
    }

    public static void Success(
        string message,
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0
    )
    {
        lock (logLock)
        {
            if (message == lastLine)
            {
                PrintRepeating(message);
                return;
            }

            WriteLogLine("OKK", message, "bold green", filePath, lineNumber);
            lastLine = message;
            repeatCount = 0;
        }
    }
}
