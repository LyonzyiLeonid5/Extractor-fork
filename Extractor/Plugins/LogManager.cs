using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using System.IO;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace Extractor.Plugins
{
    public static class LogManager
    {
        public class InMemorySink : ILogEventSink
        {
            private readonly StringBuilder _builder;
            private const string OutputTemplate = "[{Timestamp:HH:mm:ss:ff} {Level:u3}] [{SourceContextName}] {Message:l}{NewLine}{Exception}";
            private readonly MessageTemplateTextFormatter _formatter;

            public static List<string> AllMessages { get; } = new List<string>();
            public static string AllLogs => string.Join("", AllMessages);

            public InMemorySink(StringBuilder builder)
            {
                _builder = builder;
                _formatter = new MessageTemplateTextFormatter(
                    OutputTemplate,
                    CultureInfo.InvariantCulture
                );
            }

            public void Emit(LogEvent logEvent)
            {
                using (var writer = new StringWriter())
                {
                    _formatter.Format(logEvent, writer);
                    var formattedMessage = writer.ToString();

                    _builder.Append(formattedMessage);
                    AllMessages.Add(formattedMessage);

                    // Добавляем в группированный лог
                    LogManager.AddLog(logEvent.Level, formattedMessage);
                }
            }
        }
        private static readonly Dictionary<LogEventLevel, List<string>> _groupedLogs = new()
        {
            { LogEventLevel.Verbose, new List<string>() },
            { LogEventLevel.Debug, new List<string>() },
            { LogEventLevel.Information, new List<string>() },
            { LogEventLevel.Warning, new List<string>() },
            { LogEventLevel.Error, new List<string>() },
            { LogEventLevel.Fatal, new List<string>() }
        };

        private static readonly object _lock = new object();

        public static void AddLog(LogEventLevel level, string message)
        {
            lock (_lock)
            {
                if (_groupedLogs.ContainsKey(level))
                {
                    _groupedLogs[level].Add(message);
                }
            }
        }

        public static List<string> GetLogsByLevel(LogEventLevel level)
        {
            lock (_lock)
            {
                return _groupedLogs.ContainsKey(level)
                    ? new List<string>(_groupedLogs[level])
                    : new List<string>();
            }
        }

        public static List<string> GetAllLogs()
        {
            lock (_lock)
            {
                var allLogs = new List<string>();
                foreach (var kvp in _groupedLogs)
                {
                    allLogs.AddRange(kvp.Value);
                }
                return allLogs;
            }
        }

        public static bool ContainsMessage(string message)
        {
            lock (_lock)
            {
                return _groupedLogs.Values.Any(list => list.Any(msg => msg.Contains(message)));
            }
        }

        public static string FindMatchingLog(string errorMessage)
        {
            lock (_lock)
            {
                foreach (var kvp in _groupedLogs)
                {
                    foreach (var log in kvp.Value)
                    {
                        if (log.Contains(errorMessage))
                        {
                            return log;
                        }
                    }
                }
                return null;
            }
        }

        public static void Clear()
        {
            lock (_lock)
            {
                foreach (var kvp in _groupedLogs)
                {
                    kvp.Value.Clear();
                }
            }
        }

        public static Dictionary<LogEventLevel, List<string>> GetGroupedLogs()
        {
            lock (_lock)
            {
                var copy = new Dictionary<LogEventLevel, List<string>>();
                foreach (var kvp in _groupedLogs)
                {
                    copy[kvp.Key] = new List<string>(kvp.Value);
                }
                return copy;
            }
        }
    }
}