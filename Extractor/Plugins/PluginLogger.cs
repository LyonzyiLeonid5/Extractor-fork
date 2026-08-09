using Serilog;
using Serilog.Events;
using System;
using System.IO;
using System.Text;

namespace Extractor.Plugins
{
    public static class PluginLogger
    {
        private static ILogger _logger;
        private static string _logFilePath;
        private static bool _isInitialized = false;
        private static readonly object _lock = new object();

        /// <summary>
        /// Инициализирует логгер для плагинов
        /// </summary>
        /// <param name="pluginName">Имя плагина</param>
        /// <param name="logDirectory">Директория для логов (по умолчанию ./logs)</param>
        public static void Initialize(string pluginName, string logDirectory = null)
        {
            lock (_lock)
            {
                if (_isInitialized)
                    return;

                try
                {
                    if (string.IsNullOrEmpty(logDirectory))
                    {
                        logDirectory = Path.Combine(AppContext.BaseDirectory, "logs", "plugins");
                    }

                    Directory.CreateDirectory(logDirectory);

                    string sanitizedName = string.Join("_", pluginName.Split(Path.GetInvalidFileNameChars()));
                    _logFilePath = Path.Combine(logDirectory, $"{sanitizedName}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");

                    var loggerConfig = new LoggerConfiguration()
                        .MinimumLevel.Debug()
                        .Enrich.FromLogContext()
                        .WriteTo.File(_logFilePath,
                            outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss:fff} {Level:u3}] {Message:l}{NewLine}{Exception}",
                            encoding: Encoding.UTF8,
                            rollingInterval: RollingInterval.Day)
                        .WriteTo.Console(
                            outputTemplate: "[{Timestamp:HH:mm:ss:fff} {Level:u3}] {Message:l}{NewLine}{Exception}");

                    _logger = loggerConfig.CreateLogger();
                    _isInitialized = true;

                    LogInformation($"=== Plugin Logger Initialized ===");
                    LogInformation($"Plugin: {pluginName}");
                    LogInformation($"Log file: {_logFilePath}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to initialize plugin logger: {ex.Message}");
                    _logger = new LoggerConfiguration()
                        .MinimumLevel.Debug()
                        .WriteTo.Console()
                        .CreateLogger();
                    _isInitialized = true;
                }
            }
        }

        public static string LogFilePath => _logFilePath;
        public static bool IsInitialized => _isInitialized;

        public static void LogInformation(string message)
        {
            if (!_isInitialized) return;
            _logger?.Information(message);
        }

        public static void LogInformation(string message, params object[] args)
        {
            if (!_isInitialized) return;
            _logger?.Information(message, args);
        }

        public static void LogError(string message)
        {
            if (!_isInitialized) return;
            _logger?.Error(message);
        }

        public static void LogError(Exception ex, string message)
        {
            if (!_isInitialized) return;
            _logger?.Error(ex, message);
        }

        public static void LogError(string message, params object[] args)
        {
            if (!_isInitialized) return;
            _logger?.Error(message, args);
        }

        public static void LogWarning(string message)
        {
            if (!_isInitialized) return;
            _logger?.Warning(message);
        }

        public static void LogWarning(string message, params object[] args)
        {
            if (!_isInitialized) return;
            _logger?.Warning(message, args);
        }

        public static void LogDebug(string message)
        {
            if (!_isInitialized) return;
            _logger?.Debug(message);
        }

        public static void LogDebug(string message, params object[] args)
        {
            if (!_isInitialized) return;
            _logger?.Debug(message, args);
        }

        public static void LogVerbose(string message)
        {
            if (!_isInitialized) return;
            _logger?.Verbose(message);
        }

        public static void LogVerbose(string message, params object[] args)
        {
            if (!_isInitialized) return;
            _logger?.Verbose(message, args);
        }

        /// <summary>
        /// Логирование сообщения с указанным уровнем
        /// </summary>
        public static void LogMessage(LogEventLevel level, string message)
        {
            if (!_isInitialized) return;
            _logger?.Write(level, message);
        }

        /// <summary>
        /// Логирование сообщения с указанным уровнем и параметрами
        /// </summary>
        public static void LogMessage(LogEventLevel level, string message, params object[] args)
        {
            if (!_isInitialized) return;
            _logger?.Write(level, message, args);
        }

        /// <summary>
        /// Закрыть и сбросить логгер
        /// </summary>
        public static void CloseAndFlush()
        {
            lock (_lock)
            {
                if (_isInitialized && _logger != null)
                {
                    LogInformation("=== Plugin Logger Closed ===");
                    Serilog.Log.CloseAndFlush();
                    _isInitialized = false;
                    _logger = null;
                }
            }
        }

        public static ILogger CreatePluginLogger(string pluginName)
        {
            try
            {
                string logDirectory = Path.Combine(AppContext.BaseDirectory, "logs", "plugins");
                Directory.CreateDirectory(logDirectory);

                string sanitizedName = string.Join("_", pluginName.Split(Path.GetInvalidFileNameChars()));
                string logFilePath = Path.Combine(logDirectory, $"{sanitizedName}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");

                return new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .Enrich.FromLogContext()
                    .WriteTo.File(logFilePath,
                        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss:fff} {Level:u3}] {Message:l}{NewLine}{Exception}",
                        encoding: Encoding.UTF8)
                    .WriteTo.Console(
                        outputTemplate: "[{Timestamp:HH:mm:ss:fff} {Level:u3}] {Message:l}{NewLine}{Exception}")
                    .CreateLogger();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to create plugin logger: {ex.Message}");
                return new LoggerConfiguration()
                    .MinimumLevel.Debug()
                    .WriteTo.Console()
                    .CreateLogger();
            }
        }
    }
}