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
    public class PluginManager
    {
        private readonly Options _options;
        private readonly ILogger _logger;
        private List<Type> _loadedPlugins = new();

        public PluginManager(Options options, ILogger logger)
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
                Console.WriteLine($"Found {dlls.Length} DLL files" +
                    (!string.IsNullOrEmpty(_options.PluginPrefix) ? $" (prefix: {_options.PluginPrefix})" : ""));
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
                                    _logger.Debug("Plugin {Plugin} is disabled", type.Name);
                                }
                                continue;
                            }

                            plugins.Add(type);

                            if (_options.PluginVerbose)
                            {
                                Console.WriteLine($"  Loaded plugin: {type.Name} from {Path.GetFileName(dll)}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (_options.PluginDebug)
                    {
                        Console.WriteLine($"Failed to load {Path.GetFileName(dll)}: {ex.Message}");
                        _logger.Debug(ex, "Failed to load DLL: {Dll}", dll);
                    }
                }
            }

            if (_options.PluginVerbose || _options.PluginDebug)
            {
                Console.WriteLine($"Loaded {plugins.Count} plugin(s)");
            }

            _loadedPlugins = plugins;
            return plugins;
        }

        public List<Type> FilterPlugins(List<Type> plugins)
        {
            var filtered = new List<Type>(plugins);

            if (_options.PluginDisableList.Count > 0)
            {
                filtered = filtered.Where(p => !_options.PluginDisableList.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }

            return filtered;
        }

        public void RunPlugins(List<Type> plugins, string[] args, Extractor extractor, bool afterExtraction)
        {
            if (plugins == null || plugins.Count == 0)
                return;

            string pluginOutputDir = _options.PluginDirectory ?? Path.GetDirectoryName(Environment.ProcessPath) ?? "./";

            foreach (var plugin in plugins)
            {
                try
                {
                    var canRun = plugin.GetMethod("CanRun", BindingFlags.Public | BindingFlags.Static);
                    var run = plugin.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);

                    if (canRun == null || run == null)
                        continue;

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
                            }
                            continue;
                        }
                    }

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
                            }
                        }
                    }

                    if (afterExtraction != wantsAfterExtraction)
                        continue;

                    if (isInLoadList || _options.LoadAllPlugins)
                    {
                        if (_options.PluginDebug)
                        {
                            Console.WriteLine($"Plugin {plugin.Name} forced to run (override CanRun via --plugin-load or --plugin-load-all)");
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
                            }
                        }
                    }

                    bool shouldIgnoreExit = _options.PluginIgnoreExit || pluginIgnoreExit;

                    if (_options.PluginDebug)
                    {
                        Console.WriteLine($"Running plugin: {plugin.Name} (after: {afterExtraction}, ignoreExit: {shouldIgnoreExit})");
                        _logger.Debug("Running plugin: {Plugin} (after: {After}, ignoreExit: {IgnoreExit})",
                            plugin.Name, afterExtraction, shouldIgnoreExit);
                    }

                    TextWriter originalOut = null;
                    TextWriter originalError = null;
                    StreamWriter fileWriter = null;
                    bool isOutputRedirected = false;

                    if (_options.PluginSaveOutput)
                    {
                        originalOut = Console.Out;
                        originalError = Console.Error;

                        string outputFile = Path.Combine(pluginOutputDir, $"plugin_{plugin.Name}_{(afterExtraction ? "after" : "before")}.txt");

                        string directory = Path.GetDirectoryName(outputFile);
                        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        fileWriter = new StreamWriter(outputFile);
                        Console.SetOut(fileWriter);
                        Console.SetError(fileWriter);
                        isOutputRedirected = true;
                    }

                    try
                    {
                        if (shouldIgnoreExit)
                        {
                            RunPluginInSeparateProcess(plugin, args, extractor, afterExtraction);
                        }
                        else
                        {
                            run.Invoke(null, new object[] { args, extractor });
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_options.PluginDebug)
                        {
                            Console.WriteLine($"Plugin {plugin.Name} exception: {ex.Message}");
                        }
                    }
                    finally
                    {
                        if (isOutputRedirected && fileWriter != null)
                        {
                            try
                            {
                                fileWriter.Flush();
                                fileWriter.Close();
                                fileWriter.Dispose();
                            }
                            catch { }

                            try
                            {
                                if (originalOut != null)
                                    Console.SetOut(originalOut);
                                if (originalError != null)
                                    Console.SetError(originalError);
                            }
                            catch { }
                        }
                    }

                    extractor.RegisterPluginRun(plugin.Name);
                }
                catch (Exception ex)
                {
                    if (_options.PluginDebug)
                    {
                        Console.WriteLine($"Error running plugin {plugin.Name}: {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                        }
                        Console.WriteLine($"Stack trace: {ex.StackTrace}");
                    }
                    _logger.Error(ex, "Error running plugin: {Plugin}", plugin.Name);
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
                        AssemblyPath = plugin.Assembly.Location,
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
                        }
                    }
                    else
                    {
                        if (output.Length > 0)
                        {
                            Console.Write(output.ToString());
                        }
                        if (error.Length > 0)
                        {
                            Console.Error.Write(error.ToString());
                        }

                        if (_options.PluginDebug)
                        {
                            Console.WriteLine($"Plugin {plugin.Name} completed with exit code {process.ExitCode}");
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
                }
            }
        }

        public void SaveDebugInfo()
        {
            try
            {
                string pluginOutputDir = _options.PluginDirectory ?? Path.GetDirectoryName(Environment.ProcessPath) ?? "./";
                string debugFile = Path.Combine(pluginOutputDir, "_plugin_debug.txt");

                string directory = Path.GetDirectoryName(debugFile);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var writer = new StreamWriter(debugFile);
                writer.WriteLine($"Plugin Debug Info - {DateTime.Now}");
                writer.WriteLine($"Plugin directory: {pluginOutputDir}");
                writer.WriteLine($"Loaded plugins: {_loadedPlugins.Count}");
                writer.WriteLine();

                foreach (var plugin in _loadedPlugins)
                {
                    writer.WriteLine($"--- {plugin.Name} ---");
                    writer.WriteLine($"Assembly: {plugin.Assembly.GetName().FullName}");

                    var methods = plugin.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Where(m => m.IsPublic && m.IsStatic)
                        .Select(m => $"{m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"))})");

                    foreach (var method in methods)
                    {
                        writer.WriteLine($"  {method}");
                    }
                    writer.WriteLine();
                }

                Console.WriteLine($"Plugin debug info saved to: {debugFile}");
                _logger.Information("Plugin debug info saved to: {File}", debugFile);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save plugin debug info");
            }
        }

        public void ListPlugins()
        {
            Console.WriteLine($"Extractor {TextUtils.GetVersionString()}\n");
            Console.WriteLine("Available plugins:\n");

            var plugins = LoadPlugins();

            if (plugins.Count == 0)
            {
                Console.WriteLine("No plugins found.");
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

                if (_options.PluginVerbose)
                {
                    var methods = plugin.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .Where(m => m.IsPublic && m.IsStatic)
                        .Select(m => m.Name);
                    Console.WriteLine($"    Methods: {string.Join(", ", methods)}");
                }
                Console.WriteLine();
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
                            run.Invoke(null, new object[] { data.Args, extractor });
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

            if (magic.SequenceEqual(['S', 'C', 'S', '#']))
            {
                if (options.UseRawExtractor)
                    return new HashFsRawExtractor(scsPath, options);
                else if (options.UseDeepExtractor)
                    return new HashFsDeepExtractor(scsPath, options);
                else
                    return new HashFsExtractor(scsPath, options);
            }
            else
            {
                return new ZipExtractor(scsPath, options);
            }
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