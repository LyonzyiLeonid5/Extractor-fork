using MonoMod.RuntimeDetour;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using Serilog;
using Extractor.Plugins;

public static class Patcher
{
    private static List<Hook> _hooks = new List<Hook>();
    private static ILogger _logger;
    
    public static void ApplyPatch(
        string pluginName,
        Type targetType,
        string methodName,
        Type patchClass,
        string patchMethodName,
        BindingFlags methodBindingFlags = BindingFlags.NonPublic | BindingFlags.Instance)
    {
        try
        {
            _logger = PluginManager._logger;
            Console.WriteLine($"[Patcher] Applying patch from {pluginName}");
            Console.WriteLine($"[Patcher] Target: {targetType.FullName}.{methodName}");
            Console.WriteLine($"[Patcher] Patch: {patchClass.FullName}.{patchMethodName}");
            _logger?.Information("[Patcher] Applying patch from {PluginName}", pluginName);
            _logger?.Information("[Patcher] Target: {TargetType}.{MethodName}", targetType.FullName, methodName);
            _logger?.Information("[Patcher] Patch: {PatchClass}.{PatchMethodName}", patchClass.FullName, patchMethodName);

            var originalMethod = targetType.GetMethod(
                methodName,
                methodBindingFlags);
            
            if (originalMethod == null)
            {
                Console.Error.WriteLine($"[Patcher] ERROR: Method '{methodName}' not found in {targetType.FullName}");
                Console.Error.WriteLine($"[Patcher] Available methods in {targetType.Name}:");
                _logger?.Error("[Patcher] ERROR: Method '{MethodName}' not found in {TargetType}", methodName, targetType.FullName);
                _logger?.Error("[Patcher] Available methods in {TargetType}:", targetType.Name);
                foreach (var m in targetType.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    Console.Error.WriteLine($"  - {m.Name} ({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})");
                    _logger?.Error("  - {MethodName} ({Parameters})", m.Name, string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)));
                }
                throw new InvalidOperationException($"Method '{methodName}' not found in {targetType.FullName}");
            }
            
            Console.WriteLine($"[Patcher] Original method: {originalMethod.Name} ({string.Join(", ", originalMethod.GetParameters().Select(p => p.ParameterType.Name))}) -> {originalMethod.ReturnType.Name}");
            _logger?.Information("[Patcher] Original method: {MethodName} ({Parameters}) -> {ReturnType}", originalMethod.Name, string.Join(", ", originalMethod.GetParameters().Select(p => p.ParameterType.Name)), originalMethod.ReturnType.Name);
            
            var patchMethod = patchClass.GetMethod(
                patchMethodName,
                BindingFlags.Public | BindingFlags.Static);
            
            if (patchMethod == null)
            {
                Console.Error.WriteLine($"[Patcher] ERROR: Patch method '{patchMethodName}' not found in {patchClass.FullName}");
                _logger?.Error("[Patcher] ERROR: Patch method '{PatchMethodName}' not found in {PatchClass}", patchMethodName, patchClass.FullName);
                throw new InvalidOperationException($"Patch method '{patchMethodName}' not found in {patchClass.FullName}");
            }
            
            Console.WriteLine($"[Patcher] Patch method: {patchMethod.Name} ({string.Join(", ", patchMethod.GetParameters().Select(p => p.ParameterType.Name))}) -> {patchMethod.ReturnType.Name}");
            _logger?.Information("[Patcher] Patch method: {PatchMethodName} ({Parameters}) -> {ReturnType}", patchMethod.Name, string.Join(", ", patchMethod.GetParameters().Select(p => p.ParameterType.Name)), patchMethod.ReturnType.Name);
            
            var origParams = originalMethod.GetParameters();
            var patchParams = patchMethod.GetParameters();
            
            if (methodBindingFlags.HasFlag(BindingFlags.Instance))
            {
                if (patchParams.Length != origParams.Length + 1)
                {
                    Console.Error.WriteLine($"[Patcher] ERROR: Parameter count mismatch for instance method!");
                    Console.Error.WriteLine($"  Original: {origParams.Length} parameters");
                    Console.Error.WriteLine($"  Patch: {patchParams.Length} parameters (should be {origParams.Length + 1} with 'this' parameter)");
                    _logger?.Error("[Patcher] ERROR: Parameter count mismatch for instance method!");
                    _logger?.Error("[Patcher]  Original: {OriginalParams} parameters", origParams.Length);
                    _logger?.Error("[Patcher]  Patch: {PatchParams} parameters (should be {ExpectedParams} with 'this' parameter)", patchParams.Length, origParams.Length + 1);
                    throw new InvalidOperationException($"Parameter count mismatch for instance method: expected {origParams.Length + 1}, got {patchParams.Length}");
                }
                
                if (patchParams[0].ParameterType != targetType)
                {
                    Console.Error.WriteLine($"[Patcher] ERROR: First parameter of patch method must be of type {targetType.Name}");
                    Console.Error.WriteLine($"  Got: {patchParams[0].ParameterType}");
                    _logger?.Error("[Patcher] ERROR: First parameter of patch method must be of type {TargetType}", targetType.Name);
                    _logger?.Error("[Patcher]  Got: {PatchFirstParamType}", patchParams[0].ParameterType);
                    throw new InvalidOperationException($"First parameter of patch method must be of type {targetType.Name}, got {patchParams[0].ParameterType}");
                }
                
                for (int i = 0; i < origParams.Length; i++)
                {
                    if (patchParams[i + 1].ParameterType != origParams[i].ParameterType && 
                        patchParams[i + 1].ParameterType != typeof(object))
                    {
                        Console.Error.WriteLine($"[Patcher] ERROR: Parameter type mismatch at index {i + 1}!");
                        Console.Error.WriteLine($"  Original: {origParams[i].ParameterType}");
                        Console.Error.WriteLine($"  Patch: {patchParams[i + 1].ParameterType}");
                        _logger?.Error("[Patcher] ERROR: Parameter type mismatch at index {Index}!", i + 1);
                        _logger?.Error("[Patcher]  Original: {OriginalParamType}", origParams[i].ParameterType);
                        _logger?.Error("[Patcher]  Patch: {PatchParamType}", patchParams[i + 1].ParameterType);
                        throw new InvalidOperationException($"Parameter type mismatch at index {i + 1}: original {origParams[i].ParameterType}, patch {patchParams[i + 1].ParameterType}");
                    }
                }
            }
            else
            {
                if (origParams.Length != patchParams.Length)
                {
                    Console.Error.WriteLine($"[Patcher] ERROR: Parameter count mismatch!");
                    Console.Error.WriteLine($"  Original: {origParams.Length} parameters");
                    Console.Error.WriteLine($"  Patch: {patchParams.Length} parameters");
                    _logger?.Error("[Patcher] ERROR: Parameter count mismatch!");
                    _logger?.Error("[Patcher]  Original: {OriginalParams} parameters", origParams.Length);
                    _logger?.Error("[Patcher]  Patch: {PatchParams} parameters", patchParams.Length);
                    throw new InvalidOperationException($"Parameter count mismatch: original {origParams.Length}, patch {patchParams.Length}");
                }
                
                for (int i = 0; i < origParams.Length; i++)
                {
                    if (patchParams[i].ParameterType != origParams[i].ParameterType && 
                        patchParams[i].ParameterType != typeof(object))
                    {
                        Console.Error.WriteLine($"[Patcher] ERROR: Parameter type mismatch at index {i}!");
                        Console.Error.WriteLine($"  Original: {origParams[i].ParameterType}");
                        Console.Error.WriteLine($"  Patch: {patchParams[i].ParameterType}");
                        _logger?.Error("[Patcher] ERROR: Parameter type mismatch at index {Index}!", i);
                        _logger?.Error("[Patcher]  Original: {OriginalParamType}", origParams[i].ParameterType);
                        _logger?.Error("[Patcher]  Patch: {PatchParamType}", patchParams[i].ParameterType);
                        throw new InvalidOperationException($"Parameter type mismatch at index {i}: original {origParams[i].ParameterType}, patch {patchParams[i].ParameterType}");
                    }
                }
            }
            
            var hook = new Hook(originalMethod, patchMethod);
            _hooks.Add(hook);
            
            Console.WriteLine($"[Patcher] Patch applied successfully on {targetType.Name}.{methodName}!");
            _logger?.Information("[Patcher] Patch applied successfully on {TargetType}.{MethodName}!", targetType.Name, methodName);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Patcher] ERROR applying patch: {ex}");
            Console.Error.WriteLine($"[Patcher] Stack trace: {ex.StackTrace}");
            _logger?.Error(ex, "[Patcher] ERROR applying patch");
            _logger?.Error("[Patcher] Stack trace: {StackTrace}", ex.StackTrace);
            throw;
        }
    }
    
    public static void RemoveAllHooks()
    {
        foreach (var hook in _hooks)
        {
            try
            {
                hook?.Dispose();
            }
            catch { }
        }
        _hooks.Clear();
        Console.WriteLine($"[Patcher] All hooks removed");
        _logger?.Information("[Patcher] All hooks removed");
    }
}