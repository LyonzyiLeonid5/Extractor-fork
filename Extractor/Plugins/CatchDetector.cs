using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Serilog;
using Serilog.Events;

namespace Extractor.Plugins
{
    public static class CatchDetector
    {
        private static ILogger _logger;
        private static bool _isInitialized = false;

        public static void Initialize(ILogger logger)
        {
            if (_isInitialized) return;
            
            _logger = logger;
            
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
            
            _isInitialized = true;
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;
            if (ex != null)
            {
                DisplayError(ex);
                _logger?.Fatal(ex, "Unhandled exception");
            }
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            DisplayError(e.Exception);
            _logger?.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        }

        private static void OnFirstChanceException(object sender, FirstChanceExceptionEventArgs e)
        {
            _logger?.Debug(e.Exception, "First chance exception");
        }

        public static void DisplayError(Exception ex)
        {
            if (ex == null) return;

            Console.WriteLine("\n=== ОШИБКА ===");
            
            string matchingLog = LogManager.FindMatchingLog(ex.Message);
            
            if (!string.IsNullOrEmpty(matchingLog))
            {
                Console.WriteLine(matchingLog);
            }
            else
            {
                Console.WriteLine(ex.ToString());
            }

            var allLogs = LogManager.GetAllLogs();
            var additionalLogs = allLogs.Where(log => !log.Contains(ex.Message)).ToList();
            
            if (additionalLogs.Any())
            {
                Console.WriteLine("\n=== ДОПОЛНИТЕЛЬНЫЕ ЛОГИ ===");
                foreach (var log in additionalLogs.Take(10))
                {
                    Console.WriteLine(log);
                }
                if (additionalLogs.Count > 10)
                {
                    Console.WriteLine($"... и еще {additionalLogs.Count - 10} сообщений");
                }
            }
            
            Console.WriteLine("====================\n");
        }

        public static void DisplayErrorByLevel(Exception ex, LogEventLevel level)
        {
            if (ex == null) return;

            Console.WriteLine($"\n=== ОШИБКА (уровень: {level}) ===");
            
            var logsByLevel = LogManager.GetLogsByLevel(level);
            string matchingLog = logsByLevel.FirstOrDefault(log => log.Contains(ex.Message));
            
            if (!string.IsNullOrEmpty(matchingLog))
            {
                Console.WriteLine(matchingLog);
            }
            else
            {
                Console.WriteLine(ex.ToString());
            }

            var additionalLogs = logsByLevel.Where(log => !log.Contains(ex.Message)).ToList();
            if (additionalLogs.Any())
            {
                Console.WriteLine($"\n=== ДОПОЛНИТЕЛЬНЫЕ ЛОГИ (уровень: {level}) ===");
                foreach (var log in additionalLogs.Take(5))
                {
                    Console.WriteLine(log);
                }
            }
            
            Console.WriteLine("==========================\n");
        }

        public static void Shutdown()
        {
            if (!_isInitialized) return;
            
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;
            
            _isInitialized = false;
            Console.WriteLine("[ERROR HANDLER] Глобальный перехват ошибок отключен");
        }
    }
}