using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Serilog;

namespace Extractor.Plugins
{
    public class PluginManager
    {
        private readonly PluginLoader _loader;
        public readonly Options opt;
        public static ILogger _logger { get; private set; }
        public List<Type> plugins { get; }
        private static PluginManager _instance;
        public static PluginManager Instance => _instance;
        private static bool _extractionBlocked = false;

        public PluginManager(Options options, ILogger logger)
        {
            opt = options;
            _logger = logger.ForContext("SourceContext", "Plugins");
            _loader = new PluginLoader(options, _logger);
            plugins = LoadPlugins();
            _instance = this;

            if (opt.PluginsOnly)
            {
                _extractionBlocked = true;
                Console.WriteLine("[Plugin-Only Mode] Extraction is blocked (--plugin-only is active)");
                _logger.Information("Extraction is blocked because --plugin-only is active");
            }

            if (opt.PluginVerbose || opt.PluginDebug)
            {
                _logger.Information("[Loader]Loaded {Count} plugins: {Plugins}",
                    plugins.Count,
                    string.Join(", ", plugins.Select(p => p.Name)));
                Console.WriteLine($"Loaded {plugins.Count} plugins: {string.Join(", ", plugins.Select(p => p.Name))}");
            }
            if (opt.PluginDebug)
            {
                SaveDebugInfo();
            }
            if (opt.PrintPluginList)
            {
                ListPlugins();
            }
        }
        public PluginManager(Options options) : this(options, CreateDefaultLogger())
        {
        }

        private static ILogger CreateDefaultLogger()
        {
            return new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .CreateLogger()
                .ForContext("SourceContext", "Plugins");
        }

        public static bool IsExtractionBlocked()
        {
            return _extractionBlocked;
        }

        public static void SetExtractionBlocked(bool blocked)
        {
            _extractionBlocked = blocked;
        }

        public List<Type> LoadPlugins()
        {
            return _loader.LoadPlugins();
        }

        public List<Type> FilterPlugins(List<Type> plugins)
        {
            return _loader.FilterPlugins(plugins);
        }

        public List<Type> GetErrorBypassPlugins(List<Type> plugins)
        {
            var result = new List<Type>();

            if (plugins == null || plugins.Count == 0)
                return result;

            foreach (var plugin in plugins)
            {
                if (!string.IsNullOrEmpty(opt.PluginPrefix))
                {
                    var assemblyName = plugin.Assembly.GetName().Name;
                    if (!plugin.Name.StartsWith(opt.PluginPrefix, StringComparison.OrdinalIgnoreCase) &&
                        !assemblyName.StartsWith(opt.PluginPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        if (opt.PluginDebug)
                            Console.WriteLine($"[ErrorBypass] Plugin {plugin.Name} filtered out by prefix '{opt.PluginPrefix}'");
                        continue;
                    }
                }

                bool isDisabled = opt.PluginDisableList.Contains(plugin.Name, StringComparer.OrdinalIgnoreCase);
                if (isDisabled)
                {
                    if (opt.PluginDebug)
                        Console.WriteLine($"[ErrorBypass] Plugin {plugin.Name} is disabled via --plugin-disable");
                    continue;
                }

                var errorBypassMethod = plugin.GetMethod("ErrorBypass", BindingFlags.Public | BindingFlags.Static);
                if (errorBypassMethod == null)
                    continue;

                bool isInLoadList = opt.PluginLoadList.Contains(plugin.Name, StringComparer.OrdinalIgnoreCase);
                bool shouldLoad = opt.LoadBypassPlugins || isInLoadList || opt.LoadAllPlugins;

                if (!shouldLoad)
                {
                    if (opt.PluginDebug)
                    {
                        Console.WriteLine($"[ErrorBypass] Plugin {plugin.Name} has ErrorBypass but not loaded (use --plugin-bypass, --plugin-load, or --plugin-load-all)");
                    }
                    continue;
                }

                if (opt.PluginDebug)
                {
                    Console.WriteLine($"[ErrorBypass] Plugin {plugin.Name} passed all filters and has ErrorBypass");
                }
                _logger.Information("[ErrorBypass] Found plugin with ErrorBypass: {PluginName}", plugin.Name);
                result.Add(plugin);
            }

            if (opt.PluginDebug)
            {
                Console.WriteLine($"[ErrorBypass] Total plugins with ErrorBypass after filtering: {result.Count}");
            }

            return result;
        }

        public bool ShouldRunErrorBypassPlugins()
        {
            return opt.LoadBypassPlugins || opt.PluginLoadList.Count > 0 || opt.LoadAllPlugins;
        }

        public void RunErrorBypassPluginsIfNeeded(List<Type> plugins, Extractor extractor = null)
        {
            if (!ShouldRunErrorBypassPlugins())
            {
                if (opt.PluginDebug)
                {
                    Console.WriteLine("[ErrorBypass] Skipping - not requested (use --plugin-bypass, --plugin-load, or --plugin-load-all)");
                }
                return;
            }

            var errorBypassPlugins = GetErrorBypassPlugins(plugins);
            if (errorBypassPlugins.Count > 0)
            {
                RunErrorBypassPlugins(errorBypassPlugins, null, extractor);
            }
            else if (opt.PluginDebug)
            {
                Console.WriteLine("[ErrorBypass] No ErrorBypass plugins found or all were filtered out");
            }
        }

        public void RunErrorBypassPlugins(List<Type> plugins, Exception ex, Extractor extractor)
        {
            Console.WriteLine("[ErrorBypass] === Starting ErrorBypass Plugins ===");
            _logger.Information("[ErrorBypass] === Starting ErrorBypass Plugins ===");

            if (plugins == null || plugins.Count == 0)
            {
                Console.WriteLine("[ErrorBypass] No Plugins with ErrorBypass found. Skipping...");
                _logger.Information("[ErrorBypass] No Plugins with ErrorBypass found. Skipping...");
                return;
            }

            Console.WriteLine($"[ErrorBypass] Total {plugins.Count} found with ErrorBypass.");
            Console.WriteLine($"[ErrorBypass] List: {string.Join(", ", plugins.Select(p => p.Name))}");
            _logger.Information("[ErrorBypass] Total {Count} found with ErrorBypass. List: {PluginList}",
                plugins.Count, string.Join(", ", plugins.Select(p => p.Name)));

            var bypassPlugins = GetErrorBypassPlugins(plugins);
            Console.WriteLine($"[ErrorBypass] Plugins with ErrorBypass after filtering: {bypassPlugins.Count}");
            _logger.Information("[ErrorBypass] Plugins with ErrorBypass after filtering: {Count}", bypassPlugins.Count);

            if (bypassPlugins.Count == 0)
            {
                Console.WriteLine("[ErrorBypass] No plugins passed the filters. Skipping...");
                _logger.Information("[ErrorBypass] No plugins passed the filters. Skipping...");
                return;
            }

            foreach (var plugin in bypassPlugins)
            {
                try
                {
                    Console.WriteLine($"[ErrorBypass] Running plugin: {plugin.Name}");
                    _logger.Information("[ErrorBypass] Running plugin: {PluginName}", plugin.Name);

                    var errorBypassMethod = plugin.GetMethod("ErrorBypass", BindingFlags.Public | BindingFlags.Static);
                    var criteria = errorBypassMethod.Invoke(null, null);
                    if (criteria != null)
                    {
                        var type = criteria.GetType();
                        var librariesProp = type.GetProperty("Libraries");
                        var fileProp = type.GetProperty("File");
                        var methodProp = type.GetProperty("Method");
                        var patchMethodProp = type.GetProperty("PatchMethod");

                        var libraries = librariesProp?.GetValue(criteria) as string[];
                        var file = fileProp?.GetValue(criteria)?.ToString();
                        var method = methodProp?.GetValue(criteria)?.ToString();
                        var patchMethod = patchMethodProp?.GetValue(criteria)?.ToString();

                        Console.WriteLine($"[ErrorBypass] {plugin.Name} criteria:");
                        Console.WriteLine($"  Libraries: {(libraries != null ? string.Join(", ", libraries) : "null")}");
                        Console.WriteLine($"  File: '{file}'");
                        Console.WriteLine($"  Method: '{method}'");
                        Console.WriteLine($"  PatchMethod: '{patchMethod}'");

                        _logger.Information("[ErrorBypass] {PluginName} criteria:", plugin.Name);
                        _logger.Information("  Libraries: {Libraries}", libraries != null ? string.Join(", ", libraries) : "null");
                        _logger.Information("  File: '{File}'", file);
                        _logger.Information("  Method: '{Method}'", method);
                        _logger.Information("  PatchMethod: '{PatchMethod}'", patchMethod);
                    }

                    var runMethod = plugin.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
                    if (runMethod != null)
                    {
                        var pluginLogger = _logger.ForContext("SourceContext", plugin.Name);
                        runMethod.Invoke(null, new object[] { opt.RawArgs ?? Array.Empty<string>(), extractor, pluginLogger });
                        Console.WriteLine($"[ErrorBypass] {plugin.Name} completed successfully");
                        _logger.Information("[ErrorBypass] {PluginName} completed successfully", plugin.Name);
                    }
                    else
                    {
                        Console.WriteLine($"[ErrorBypass] {plugin.Name}: method Run not found!");
                        _logger.Information("[ErrorBypass] {PluginName}: method Run not found!", plugin.Name);
                    }
                }
                catch (Exception pluginEx)
                {
                    Console.WriteLine($"[ErrorBypass] {plugin.Name}: ERROR: {pluginEx.Message}");
                    if (pluginEx.InnerException != null)
                    {
                        Console.WriteLine($"[ErrorBypass] Inner exception: {pluginEx.InnerException.Message}");
                    }
                    _logger.Error(pluginEx, $"Error while running ErrorBypass plugin {plugin.Name}");
                }
            }

            Console.WriteLine("[ErrorBypass] === Finished ErrorBypass Plugins ===");
            _logger.Information("[ErrorBypass] === Finished ErrorBypass Plugins ===");
        }

        public void RunPlugins(List<Type> plugins, string[] args, Extractor extractor, bool afterExtraction)
        {
            _loader.RunPlugins(plugins, args, extractor, afterExtraction);
        }

        public void SaveDebugInfo()
        {
            _loader.SaveDebugInfo();
        }

        public void ListPlugins()
        {
            _loader.ListPlugins(plugins);
        }

        public static void RunPluginFromCommandLine(string[] args)
        {
            PluginLoader.RunPluginFromCommandLine(args);
        }
    }
}