using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Serilog;
using Extractor.Zip;
using Extractor.Deep;
using TruckLib.HashFs;

namespace Extractor.Plugins
{
    public class PluginLoader
    {
        private readonly Options _options;
        private readonly ILogger _logger;
        private List<Type> _loadedPlugins = new();

        public PluginLoader(Options options, ILogger logger)
        {
            _options = options;
            _logger = logger;
        }

        public List<Type> LoadPlugins()
        {
            var plugins = new List<Type>();

            string baseDir = _options.PluginDirectory ?? Path.GetDirectoryName(Environment.ProcessPath) ?? "./";

            if (!Directory.Exists(baseDir))
            {
                if (_options.PluginVerbose || _options.PluginDebug)
                {
                    Console.WriteLine($"Plugin directory not found: {baseDir}");
                    _logger.Warning("[Loader]Plugin directory not found: {Directory}", baseDir);
                }
                return plugins;
            }

            var dlls = Directory.GetFiles(baseDir, "*.dll");

            if (!string.IsNullOrEmpty(_options.PluginPrefix))
            {
                dlls = dlls.Where(d => Path.GetFileName(d).StartsWith(_options.PluginPrefix, StringComparison.OrdinalIgnoreCase)).ToArray();
            }

            if (_options.PluginVerbose || _options.PluginDebug)
            {
                Console.WriteLine($"Looking for plugins in: {baseDir}");
                _logger.Information("[Loader]Looking for plugins in: {Directory}", baseDir);
                Console.WriteLine($"Found {dlls.Length} DLL files" +
                    (!string.IsNullOrEmpty(_options.PluginPrefix) ? $" (prefix: {_options.PluginPrefix})" : ""));
                _logger.Information("[Loader]Found {Count} DLL files (prefix: {Prefix})", dlls.Length, _options.PluginPrefix);
            }

            foreach (var dll in dlls)
            {
                try
                {
                    var assembly = Assembly.LoadFrom(dll);

                    foreach (var type in assembly.GetTypes())
                    {
                        var canRun = type.GetMethod("CanRun", BindingFlags.Public | BindingFlags.Static);
                        var run = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);

                        if (canRun != null && run != null)
                        {
                            bool isDisabled = _options.PluginDisableList.Contains(type.Name, StringComparer.OrdinalIgnoreCase);

                            if (isDisabled)
                            {
                                if (_options.PluginDebug || _options.PluginVerbose)
                                {
                                    Console.WriteLine($"Plugin {type.Name} is disabled via --plugin-disable");
                                    _logger.Debug("[Loader]Plugin {Plugin} is disabled", type.Name);
                                }
                                continue;
                            }

                            plugins.Add(type);

                            if (_options.PluginVerbose)
                            {
                                Console.WriteLine($"  Loaded plugin: {type.Name} from {Path.GetFileName(dll)}");
                                _logger.Information("[Loader]Loaded plugin: {Plugin} from {Dll}", type.Name, Path.GetFileName(dll));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (_options.PluginDebug)
                    {
                        Console.WriteLine($"Failed to load {Path.GetFileName(dll)}: {ex.Message}");
                        _logger.Debug(ex, "[Loader]Failed to load DLL: {Dll}", dll);
                    }
                }
            }

            if (_options.PluginVerbose || _options.PluginDebug)
            {
                Console.WriteLine($"Loaded {plugins.Count} plugin(s)");
                _logger.Information("[Loader]Loaded {Count} plugin(s)", plugins.Count);
            }

            _loadedPlugins = plugins;
            return plugins;
        }

        public List<Type> FilterPlugins(List<Type> plugins)
        {
            var result = new List<Type>();
            foreach (var plugin in plugins)
            {
                var errorBypassMethod = plugin.GetMethod("ErrorBypass", BindingFlags.Public | BindingFlags.Static);
                if (errorBypassMethod != null)
                {
                    continue;
                }

                if (_options.PluginDisableList.Contains(plugin.Name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(plugin);
            }

            return result;
        }

        public List<Type> FilterPluginsByPrefix(List<Type> plugins)
        {
            if (string.IsNullOrEmpty(_options.PluginPrefix))
                return plugins;

            var result = new List<Type>();
            foreach (var plugin in plugins)
            {
                var assemblyName = plugin.Assembly.GetName().Name;
                if (plugin.Name.StartsWith(_options.PluginPrefix, StringComparison.OrdinalIgnoreCase) ||
                    assemblyName.StartsWith(_options.PluginPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(plugin);
                }
                else if (_options.PluginDebug)
                {
                    Console.WriteLine($"[Loader] Plugin {plugin.Name} filtered out by prefix '{_options.PluginPrefix}'");
                }
            }
            return result;
        }

        public List<Type> ApplyAllFilters(List<Type> plugins)
        {
            if (plugins == null || plugins.Count == 0)
                return new List<Type>();

            var result = new List<Type>();

            foreach (var plugin in plugins)
            {
                if (!string.IsNullOrEmpty(_options.PluginPrefix))
                {
                    var assemblyName = plugin.Assembly.GetName().Name;
                    if (!plugin.Name.StartsWith(_options.PluginPrefix, StringComparison.OrdinalIgnoreCase) &&
                        !assemblyName.StartsWith(_options.PluginPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        if (_options.PluginDebug)
                            Console.WriteLine($"[Loader] Plugin {plugin.Name} filtered out by prefix '{_options.PluginPrefix}'");
                        continue;
                    }
                }

                bool isDisabled = _options.PluginDisableList.Contains(plugin.Name, StringComparer.OrdinalIgnoreCase);
                if (isDisabled)
                {
                    if (_options.PluginDebug)
                        Console.WriteLine($"[Loader] Plugin {plugin.Name} is disabled via --plugin-disable");
                    continue;
                }

                bool isInLoadList = _options.PluginLoadList.Contains(plugin.Name, StringComparer.OrdinalIgnoreCase);

                if (isInLoadList || _options.LoadAllPlugins)
                {
                    if (_options.PluginDebug)
                        Console.WriteLine($"[Loader] Plugin {plugin.Name} loaded via --plugin-load or --plugin-load-all");
                    result.Add(plugin);
                }
                else
                {
                    result.Add(plugin);
                }
            }

            return result;
        }

        public void RunPlugins(List<Type> plugins, string[] args, Extractor extractor, bool afterExtraction)
        {
            if (plugins == null || plugins.Count == 0)
                return;

            foreach (var plugin in plugins)
            {
                try
                {
                    var canRun = plugin.GetMethod("CanRun", BindingFlags.Public | BindingFlags.Static);
                    var run = plugin.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);

                    if (canRun == null || run == null)
                        continue;

                    var pluginLogger = _logger.ForContext("SourceContext", plugin.Name);

                    bool wantsAfterExtraction = false;
                    var afterMethod = plugin.GetMethod("RunAfterExtraction", BindingFlags.Public | BindingFlags.Static);

                    if (afterMethod != null)
                    {
                        try
                        {
                            wantsAfterExtraction = (bool)afterMethod.Invoke(null, null);
                        }
                        catch (Exception ex)
                        {
                            if (_options.PluginDebug)
                            {
                                Console.WriteLine($"Error checking RunAfterExtraction for {plugin.Name}: {ex.Message}");
                                _logger.Error(ex, "[Run]Error checking RunAfterExtraction for {Plugin}", plugin.Name);
                            }
                        }
                    }

                    if (afterExtraction != wantsAfterExtraction)
                        continue;

                    var errorBypassMethod = plugin.GetMethod("ErrorBypass", BindingFlags.Public | BindingFlags.Static);
                    if (errorBypassMethod != null)
                    {
                        if (_options.PluginDebug)
                        {
                            Console.WriteLine($"Plugin {plugin.Name} has ErrorBypass, skipping regular execution");
                            _logger.Information("[Run] Plugin {PluginName} has ErrorBypass, skipping regular execution", plugin.Name);
                        }
                        continue;
                    }

                    bool isInLoadList = _options.PluginLoadList.Contains(plugin.Name, StringComparer.OrdinalIgnoreCase);

                    if (!isInLoadList && !_options.LoadAllPlugins)
                    {
                        try
                        {
                            bool shouldRun = (bool)canRun.Invoke(null, new object[] { args });
                            if (!shouldRun)
                            {
                                continue;
                            }
                        }
                        catch (Exception ex)
                        {
                            if (_options.PluginDebug)
                            {
                                Console.WriteLine($"Error checking CanRun for {plugin.Name}: {ex.Message}");
                                _logger.Error(ex, "[Run]Error checking CanRun for {Plugin}", plugin.Name);
                            }
                            continue;
                        }
                    }

                    if (isInLoadList || _options.LoadAllPlugins)
                    {
                        if (_options.PluginDebug)
                        {
                            Console.WriteLine($"Plugin {plugin.Name} forced to run (override CanRun via --plugin-load or --plugin-load-all)");
                            _logger.Information("[Run] Plugin {PluginName} forced to run (override CanRun via --plugin-load or --plugin-load-all)", plugin.Name);
                        }
                    }

                    bool pluginIgnoreExit = false;
                    var ignoreExitMethod = plugin.GetMethod("IgnoreExit", BindingFlags.Public | BindingFlags.Static);

                    if (ignoreExitMethod != null)
                    {
                        try
                        {
                            pluginIgnoreExit = (bool)ignoreExitMethod.Invoke(null, null);
                        }
                        catch (Exception ex)
                        {
                            if (_options.PluginDebug)
                            {
                                Console.WriteLine($"Error checking IgnoreExit for {plugin.Name}: {ex.Message}");
                                _logger.Error(ex, "[Run]Error checking IgnoreExit for {Plugin}", plugin.Name);
                            }
                        }
                    }

                    bool shouldIgnoreExit = _options.PluginIgnoreExit || pluginIgnoreExit;

                    if (_options.PluginDebug)
                    {
                        Console.WriteLine($"Running plugin: {plugin.Name} (after: {afterExtraction}, ignoreExit: {shouldIgnoreExit})");
                        _logger.Debug("[Run]Running plugin: {Plugin} (after: {After}, ignoreExit: {IgnoreExit})",
                            plugin.Name, afterExtraction, shouldIgnoreExit);
                    }

                    var outputCapture = new StringWriter();
                    var originalOut = Console.Out;
                    var originalError = Console.Error;

                    try
                    {
                        if (_options.PluginSaveOutput)
                        {
                            Console.SetOut(outputCapture);
                            Console.SetError(outputCapture);
                        }

                        if (shouldIgnoreExit)
                        {
                            RunPluginInSeparateProcess(plugin, args, extractor, afterExtraction);
                        }
                        else
                        {
                            var parameters = run.GetParameters();

                            if (parameters.Length == 3)
                            {
                                run.Invoke(null, new object[] { args, extractor, pluginLogger });
                            }
                            else if (parameters.Length == 2)
                            {
                                run.Invoke(null, new object[] { args, extractor });
                            }
                            else
                            {
                                _logger.Warning($"Plugin {plugin.Name} has unsupported Run method signature");
                                run.Invoke(null, new object[] { args, extractor });
                            }
                        }

                        extractor.RegisterPluginRun(plugin.Name);
                    }
                    finally
                    {
                        Console.SetOut(originalOut);
                        Console.SetError(originalError);

                        if (_options.PluginSaveOutput)
                        {
                            string output = outputCapture.ToString();
                            if (!string.IsNullOrEmpty(output))
                            {
                                SavePluginOutput(plugin.Name, output);
                                Console.WriteLine(output);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (_options.PluginDebug)
                    {
                        Console.WriteLine($"Error running plugin {plugin.Name}: {ex.Message}");
                        _logger.Error(ex, "[Run]Error running plugin: {Plugin}", plugin.Name);
                        if (ex.InnerException != null)
                        {
                            Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                            _logger.Error(ex.InnerException, "[Run]Inner exception for plugin: {Plugin}", plugin.Name);
                        }
                        Console.WriteLine($"Stack trace: {ex.StackTrace}");
                        _logger.Error("[Run]Stack trace for plugin {Plugin}: {StackTrace}", plugin.Name, ex.StackTrace);
                    }
                    _logger.Error(ex, "[Run]Error running plugin: {Plugin}", plugin.Name);
                }
            }
        }

        private void RunPluginInSeparateProcess(Type plugin, string[] args, Extractor extractor, bool afterExtraction)
        {
            try
            {
                string tempFile = Path.GetTempFileName();
                try
                {
                    var data = new PluginRunData
                    {
#pragma warning disable IL3000
                        AssemblyPath = plugin.Assembly.Location,
#pragma warning restore IL3000
                        TypeName = plugin.FullName,
                        Args = args,
                        ScsPath = extractor.ScsPath,
                        AfterExtraction = afterExtraction,
                        Options = new PluginOptions
                        {
                            Destination = _options.Destination,
                            DryRun = _options.DryRun,
                            SkipIfExists = _options.SkipIfExists,
                            UseDeepExtractor = _options.UseDeepExtractor,
                            UseRawExtractor = _options.UseRawExtractor,
                            DisablePathUpdates = _options.DisablePathUpdates,
                            Salt = _options.Salt,
                            ForceEntryTableAtEnd = _options.ForceEntryTableAtEnd
                        }
                    };

                    string json = JsonSerializer.Serialize(data);
                    File.WriteAllText(tempFile, json, Encoding.UTF8);

                    string currentExe = Environment.ProcessPath;
                    string arguments = $"--plugin-runner \"{tempFile}\"";

                    var startInfo = new ProcessStartInfo
                    {
                        FileName = currentExe,
                        Arguments = arguments,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var process = new Process { StartInfo = startInfo };

                    var output = new StringBuilder();
                    var error = new StringBuilder();
                    process.OutputDataReceived += (s, e) => { if (e.Data != null) output.AppendLine(e.Data); };
                    process.ErrorDataReceived += (s, e) => { if (e.Data != null) error.AppendLine(e.Data); };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (!process.WaitForExit(60000))
                    {
                        process.Kill();
                        if (_options.PluginDebug)
                        {
                            Console.WriteLine($"Plugin {plugin.Name} timed out after 60 seconds");
                            _logger.Information("[PluginRunner] Plugin {PluginName} timed out after 60 seconds", plugin.Name);
                        }
                    }
                    else
                    {
                        string outputText = output.ToString();
                        string errorText = error.ToString();

                        Console.Write(outputText);

                        if (error.Length > 0)
                        {
                            Console.Error.Write(errorText);
                        }

                        if (_options.PluginSaveOutput)
                        {
                            string fullOutput = outputText;
                            if (!string.IsNullOrEmpty(errorText))
                            {
                                fullOutput += Environment.NewLine + "=== STDERR ===" + Environment.NewLine + errorText;
                            }
                            SavePluginOutput(plugin.Name, fullOutput);
                        }

                        if (_options.PluginDebug)
                        {
                            Console.WriteLine($"Plugin {plugin.Name} completed with exit code {process.ExitCode}");
                            _logger.Information("[PluginRunner] Plugin {PluginName} completed with exit code {ExitCode}", plugin.Name, process.ExitCode);
                        }
                    }
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
            catch (Exception ex)
            {
                if (_options.PluginDebug)
                {
                    Console.WriteLine($"Error running plugin in separate process: {ex.Message}");
                    _logger.Error(ex, "[PluginRunner]Error running plugin in separate process: {Plugin}", plugin.Name);
                }
            }
        }

        private void SavePluginOutput(string pluginName, string output)
        {
            try
            {
                string baseDir = _options.PluginDirectory ?? Path.GetDirectoryName(Environment.ProcessPath) ?? "./";
                string fileName = $"_{pluginName}_output.txt";
                string filePath = Path.Combine(baseDir, fileName);

                var content = new StringBuilder();
                content.AppendLine($"=== Plugin Output: {pluginName} ===");
                content.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                content.AppendLine(new string('=', 50));
                content.AppendLine();
                content.Append(output);
                content.AppendLine();
                content.AppendLine(new string('=', 50));
                content.AppendLine($"End of plugin output: {pluginName}");

                File.WriteAllText(filePath, content.ToString(), Encoding.UTF8);

                if (_options.PluginDebug || _options.PluginVerbose)
                {
                    Console.WriteLine($"Plugin output saved to: {filePath}");
                }
            }
            catch (Exception ex)
            {
                if (_options.PluginDebug)
                {
                    Console.WriteLine($"Failed to save plugin output for {pluginName}: {ex.Message}");
                    _logger.Error(ex, "[PluginRunner] Failed to save plugin output for {Plugin}", pluginName);
                }
            }
        }

        public void SaveDebugInfo()
        {
            try
            {
                string pluginOutputDir = _options.PluginDirectory ?? Path.GetDirectoryName(Environment.ProcessPath) ?? "./";

                _logger.Information("=== PLUGIN DEBUG INFO ===");

                _logger.Debug($"Plugin Debug Info - {DateTime.Now}");
                _logger.Debug($"Plugin directory: {pluginOutputDir}");
                _logger.Debug($"Loaded plugins: {_loadedPlugins.Count}");

                foreach (var plugin in _loadedPlugins)
                {
                    _logger.Debug($"--- {plugin.Name} ---");
                    _logger.Debug($"Assembly: {plugin.Assembly.GetName().FullName}");

                    var methods = plugin.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Where(m => m.IsPublic && m.IsStatic)
                        .Select(m => $"{m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");

                    foreach (var method in methods)
                    {
                        _logger.Debug($"  {method}");
                    }
                }

                _logger.Information("=== END OF PLUGIN DEBUG INFO ===");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save plugin debug info");
            }
        }

        public void ListPlugins(List<Type> plugins)
        {
            Console.WriteLine($"Extractor {TextUtils.GetVersionString()}\n");
            Console.WriteLine("Available plugins:\n");
            _logger.Information("Extractor {Version}", TextUtils.GetVersionString());
            _logger.Information("Available plugins:");

            if (plugins.Count == 0)
            {
                Console.WriteLine("No plugins found.");
                _logger.Information("No plugins found.");
                return;
            }

            foreach (var plugin in plugins)
            {
                var name = plugin.Name;
                var assemblyName = plugin.Assembly.GetName().Name;
                var canRunMethod = plugin.GetMethod("CanRun", BindingFlags.Public | BindingFlags.Static);

                bool? canRunResult = null;
                if (canRunMethod != null)
                {
                    try
                    {
                        canRunResult = (bool)canRunMethod.Invoke(null, new object[] { _options.RawArgs ?? Array.Empty<string>() });
                    }
                    catch { }
                }

                var runAfterMethod = plugin.GetMethod("RunAfterExtraction", BindingFlags.Public | BindingFlags.Static);
                bool runsAfter = false;
                if (runAfterMethod != null)
                {
                    try
                    {
                        runsAfter = (bool)runAfterMethod.Invoke(null, null);
                    }
                    catch { }
                }

                var ignoreExitMethod = plugin.GetMethod("IgnoreExit", BindingFlags.Public | BindingFlags.Static);
                bool ignoreExit = false;
                if (ignoreExitMethod != null)
                {
                    try
                    {
                        ignoreExit = (bool)ignoreExitMethod.Invoke(null, null);
                    }
                    catch { }
                }

                Console.WriteLine($"  {name}");
                Console.WriteLine($"    Assembly: {assemblyName}");
                Console.WriteLine($"    Runs after extraction: {runsAfter}");
                Console.WriteLine($"    Ignore Exit: {ignoreExit}");
                Console.WriteLine($"    CanRun result: {(canRunResult.HasValue ? canRunResult.Value.ToString() : "N/A")}");
                _logger.Information("Plugin: {PluginName}", name);
                _logger.Information("  Assembly: {AssemblyName}", assemblyName);
                _logger.Information("  Runs after extraction: {RunsAfter}", runsAfter);
                _logger.Information("  Ignore Exit: {IgnoreExit}", ignoreExit);
                _logger.Information("  CanRun result: {CanRunResult}", canRunResult.HasValue ? canRunResult.Value.ToString() : "N/A");

                if (_options.PluginVerbose)
                {
                    var methods = plugin.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Where(m => m.IsPublic && m.IsStatic)
                        .Select(m => m.Name);
                    Console.WriteLine($"    Methods: {string.Join(", ", methods)}");
                    _logger.Information("  Methods: {Methods}", string.Join(", ", methods));
                }
            }
        }

        public static void RunPluginFromCommandLine(string[] args)
        {
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--plugin-runner" && i + 1 < args.Length)
                    {
                        string tempFile = args[i + 1];
                        string json = File.ReadAllText(tempFile, Encoding.UTF8);
                        var data = JsonSerializer.Deserialize<PluginRunData>(json);

                        if (data != null)
                        {
                            var assembly = Assembly.LoadFrom(data.AssemblyPath);
                            var type = assembly.GetType(data.TypeName);
                            var run = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);

                            var options = new Options();
                            options.Destination = data.Options?.Destination ?? "./extracted";
                            options.DryRun = data.Options?.DryRun ?? false;
                            options.SkipIfExists = data.Options?.SkipIfExists ?? false;
                            options.UseDeepExtractor = data.Options?.UseDeepExtractor ?? false;
                            options.UseRawExtractor = data.Options?.UseRawExtractor ?? false;
                            options.DisablePathUpdates = data.Options?.DisablePathUpdates ?? false;
                            options.Salt = data.Options?.Salt;
                            options.ForceEntryTableAtEnd = data.Options?.ForceEntryTableAtEnd ?? false;

                            var extractor = CreateExtractorForPlugin(data.ScsPath, options);

                            var parameters = run.GetParameters();
                            if (parameters.Length >= 3 && parameters[2].ParameterType == typeof(ILogger))
                            {
                                var logger = new LoggerConfiguration()
                                    .WriteTo.Console()
                                    .CreateLogger();
                                var pluginLogger = logger.ForContext("SourceContext", data.TypeName);
                                run.Invoke(null, new object[] { data.Args, extractor, pluginLogger });
                            }
                            else
                            {
                                run.Invoke(null, new object[] { data.Args, extractor });
                            }
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Plugin runner error: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }

        private static Extractor CreateExtractorForPlugin(string scsPath, Options options)
        {
            if (!File.Exists(scsPath))
            {
                throw new FileNotFoundException($"File not found: {scsPath}");
            }

            char[] magic;
            using (var fs = File.OpenRead(scsPath))
            using (var r = new BinaryReader(fs, Encoding.ASCII))
            {
                magic = r.ReadChars(4);
            }

            Extractor extractor;
            if (magic.SequenceEqual(['S', 'C', 'S', '#']))
            {
                if (options.UseRawExtractor)
                    extractor = new HashFsRawExtractor(scsPath, options);
                else if (options.UseDeepExtractor)
                    extractor = new HashFsDeepExtractor(scsPath, options);
                else
                    extractor = new HashFsExtractor(scsPath, options);
            }
            else
            {
                extractor = new ZipExtractor(scsPath, options);
            }

            return extractor;
        }

        private class PluginRunData
        {
            public string AssemblyPath { get; set; }
            public string TypeName { get; set; }
            public string[] Args { get; set; }
            public string ScsPath { get; set; }
            public bool AfterExtraction { get; set; }
            public PluginOptions Options { get; set; }
        }

        private class PluginOptions
        {
            public string Destination { get; set; }
            public bool DryRun { get; set; }
            public bool SkipIfExists { get; set; }
            public bool UseDeepExtractor { get; set; }
            public bool UseRawExtractor { get; set; }
            public bool DisablePathUpdates { get; set; }
            public ushort? Salt { get; set; }
            public bool ForceEntryTableAtEnd { get; set; }
        }
    }
}