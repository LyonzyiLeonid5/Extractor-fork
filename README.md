# Extractor
A cross-platform .scs extractor for both HashFS and ZIP.


## Features
* Supports HashFS v1 and v2 as well as ZIP (including "locked" ZIP files)
* Can extract multiple archives at once
* Partial extraction
* Raw dumps
* Built-in path-finding mode for HashFS archives without directory listings
* Automatic conversion of 3nK-encoded and encrypted SII files
* Reading and executing DLL files


## Build
For x64 Windows, a standalone executable is available on the Releases page. On other platforms, install the
.NET 10 SDK and run the following:

```sh
git clone https://github.com/sk-zk/Extractor.git
cd Extractor
dotnet publish -c Release
```


## Usage
```
extractor path... [options]
```

### General options
<table>
<thead>
  <tr>
    <td><b>Short</b></td>
    <td><b>Long&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;</b></td>
    <td><b>Description</b></td>
  </tr>
</thead>
<tr>
  <td><code>-a</code></td>
  <td><code>--all</code></td>
  <td>Extracts all .scs archives in the specified directory.</td>
</tr>
<tr>
  <td><code>-d</code></td>
  <td><code>--dest</code></td>
  <td>Sets the output directory. Defaults to <code>./extracted</code>.</td>
</tr>
<tr>
  <td></td>
  <td><code>--dry-run</code></td>
  <td>Don't extract anything to disk.</td>
</tr>
<tr>
  <td><code>-f</code></td>
  <td><code>--filter</code></td>
  <td><p>Limits extraction to files whose paths match one or more of the specified filter patterns. A filter pattern can be a simple wildcard pattern, 
  where <code>?</code> matches one character and <code>*</code> matches zero or more characters, or a regex enclosed in <code>r/.../</code>.</p>
  <p>Examples:<br>
  <code>-f=*volvo_fh_2024*</code>: extract files or directories containing the string "volvo_fh_2024"<br>
  <code>-f=*volvo*,*scania*</code>: extract files or directories containing the string "volvo" or "scania"<br>
  <code>-f=/def/vehicle/truck/*/engine/*</code>: extract engine definitions for trucks</code><br>
  <code>-f=r/\.p(m[acdg]|pd)$/</code>: extract model files (.pmd, .pmg, ...)</code>
  </p>
  <p>When using regex patterns, remember to insert escape characters where necessary.</p>
  </td>
</tr>
<tr>
  <td></td>
  <td><code>--list</code></td>
  <td>Lists paths contained in the archive. Can be combined with <code>--all</code>, <code>--deep</code>, <code>--filter</code>, and <code>--partial</code>.</td>
</tr>
<tr>
  <td></td>
  <td><code>--list-all</code></td>
  <td>Lists all paths referenced by files in the archive, even if they are not contained in it.
  (Implicitly activates <code>--deep</code>.) Can be combined with <code>--all</code>, <code>--filter</code>, and <code>--partial</code>.</td>
</tr>
<tr>
  <td></td>
  <td><code>--log</code></td>
  <td>Writes a log file for debugging purposes. If no output path is specified,
  the log file will be written to <code>./_extraction.log</code>.</td>
</tr>
<tr>
  <td></td>
  <td><code>--no-update</code></td>
  <td>Don't update references to paths that had to be renamed during extraction.</td>
</tr>
<tr>
  <td><code>-p</code></td>
  <td><code>--partial</code></td>
  <td><p>Limits extraction to the comma-separated list of files and/or directories specified.</p>
  <p>Examples:<br>
  <code>-p=/locale</code><br>
  <code>-p=/def,/map</code><br>
  <code>-p=/def/world/road.sii</code></p>
  <p>When extracting a HashFS archive (without <code>--deep</code>), <b>directory traversal begins at the given paths</b>, allowing for
  extraction of known directories and files not discoverable from the top level. This makes <code>--partial</code> distinctly different
  from <code>--filter</code>. (In all other modes, extraction is limited to files whose paths begin with any of the strings given to 
  this parameter.)</p>
  </td>
</tr>
<tr>
  <td><code>-P</code></td>
  <td><code>--paths</code></td>
  <td>Same as <code>--partial</code>, but expects a text file containing paths to extract, separated by
  line breaks.</td>
</tr>
<tr>
  <td><code>-q</code></td>
  <td><code>--quiet</code></td>
  <td>Don't print paths of extracted files, and don't wait for a keypress after extraction when running outside the command line.</td>
</tr>
<tr>
  <td><code>-S</code></td>
  <td><code>--separate</code></td>
  <td>When extracting multiple archives, extract each archive to a separate directory.</td>
</tr>
<tr>
  <td><code>-s</code></td>
  <td><code>--skip-existing</code></td>
  <td>Don't overwrite existing files.</td>
</tr>
<tr>
  <td></td>
  <td><code>--tree</code></td>
  <td>Prints the archive's directory tree. Can be combined with <code>--all</code>, <code>--deep</code>, and <code>--partial</code>.</td>
</tr>
<tr>
  <td><code>-?</code>, <code>-h</code></td>
  <td><code>--help</code></td>
  <td>Prints the extractor's version and usage information.</td>
</tr>
</table>


### HashFS options
<table>
<thead>
  <tr>
    <td><b>Short</b></td>
    <td><b>Long&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;</b></td>
    <td><b>Description</b></td>
  </tr>
</thead>
<tr>
  <td></td>
  <td><code>--additional</code></td>
  <td>When using <code>--deep</code>, specifies additional start paths to search.
  Expects a text file containing paths to extract, separated by line breaks.</td>
</tr>
<tr>
  <td><code>-D</code></td>
  <td><code>--deep</code></td>
  <td>An extraction mode which scans the contained entries for referenced paths instead of traversing
  the directory tree from <code>/</code>. Use this option to extract archives without a top level directory listing.</td>
</tr>
<tr>
  <td></td>
  <td><code>--list-entries</code></td>
  <td>Lists entries contained in the archive.</td>
</tr>
<tr>
  <td><code>-r</code></td>
  <td><code>--raw</code></td>
  <td>Dumps the contained files with their hashed filenames rather than traversing
  the archive's directory tree.</td>
</tr>
<tr>
  <td></td>
  <td><code>--salt</code></td>
  <td>Ignores the salt specified in the archive header and uses the given one instead.</td>
</tr>
<tr>
  <td></td>
  <td><code>--table-at-end</code></td>
  <td>[v1 only] Ignores what the archive header says and reads the entry table from
  the end of the file.</td>
</tr>
</table>

### Plugin options

<table>
<thead>
  <tr>
    <td><b>Long&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;</b></td>
    <td><b>Description</b></td>
  </tr>
</thead>
<tr>
  <td><code>--plugin-debug</code></td>
  <td>Show debug information</td>
</tr>
<tr>
  <td><code>--plugin-load-all</code></td>
  <td>Load all available plugins regardless of their CanRun result.</td>
</tr>
<tr>
  <td><code>--plugin-load=VALUE</code></td>
  <td>Load only specific plugins by name(comma-separated). Example: --plugin-load=MyPlugin, AnotherPlugin</td>
</tr>
<tr>
  <td><code>--plugin-disable=VALUE</code></td>
  <td>Disable specific plugins by name (comma-separated).</td>
</tr>
<tr>
  <td><code>--plugin-dir=VALUE</code></td>
  <td>Load plugins from a specific directory instead of the default.</td>
</tr>
<tr>
  <td><code>--plugin-prefix=VALUE</code></td>
  <td>Load only plugins whose DLL filename starts with the given prefix.</td>
</tr>
<tr>
  <td><code>--plugin-verbose</code></td>
  <td>Show detailed information about loaded plugins.</td>
</tr>
<tr>
  <td><code>--plugin-save-output</code></td>
  <td>Save plugin output to separate files in the plugin directory.</td>
</tr>
<tr>
  <td><code>--plugin-list</code></td>
  <td>List all available plugins and exit. Use with --plugin-verbose for more details.</td>
</tr>
<tr>
  <td><code>--plugin-ignore-exit</code></td>
  <td>Ignore Environment.Exit calls from plugins.</td>
</tr>
</table>


### Examples
Normal extraction:
```sh
extractor "path\to\file.scs"
```

Extract two .scs files at once:
```sh
extractor "path\to\file1.scs" "path\to\file2.scs"
```

Extract all .scs files in a directory:
```sh
extractor "path\to\directory" --all
```

Extract `def` and `manifest.sii` only:
```sh
extractor "path\to\file.scs" --partial=/def,/manifest.sii
```

Extract model files only:
```sh
extractor "path\to\file.scs" --filter=r/\.p(m[acdg]|pd)$/
```

Extract with deep mode:
```sh
extractor "path\to\file.scs" --deep
```

Extract with deep mode when the mod is split into multiple archives:
```sh
extractor "file1.scs" "file2.scs" "file3.scs" --deep --separate
```

Alternatively:
```sh
extractor "path\to\mod\directory" --all --deep --separate
```

### Plugin Examples

Just run your plugin:
```sh
extractor.exe "file.scs" --deep --myplugin
```
View information about the plugin's work:
```sh
extractor.exe "file.scs" --deep --myplugin --plugin-debug
```
Force loading your plugin:
```sh
extractor.exe "file.scs" --deep --plugin-load=MyPlugin
```
Force load all plugins from a folder:
```sh
extractor.exe "file.scs" --deep --plugin-load-all
```
Force loading all plugins from the folder that start with My:
```sh
extractor.exe "file.scs" --deep --plugin-load-all --plugin-prefix=My
```
Force loading of your and other plugins with mandatory execution of each:
```sh
extractor.exe "file.scs" --deep --myplugin --plugin-load=OtherPlugin --plugin-ignore-exit
```


## Plugin Development

### CSPROJ Structure

```xml
<!--This is a template. You can use it for all plugins. Just rename it!-->
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\Extractor\Extractor.csproj" /> <!--REQUIRED REFERENCE!!!-->
  </ItemGroup>

  <PropertyGroup>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

### Plugin structure

```csharp
using System; //<--- REQUIRED
using System.Linq; //<--- Recommended
using System.IO; //<--- Recommended
using Extractor; //<--- REQUIRED
using TruckLib; //<--- Recommended
using TruckLib.HashFs; //<--- Recommended
using TruckLib.Sii; //<--- Recommended

namespace Extractor.Deep //namespace Extractor is REQUIRED, but I highly recommend use namespace Extractor.Deep for more useful information
{
    public class PluginName //Your plugin name
    {
        public static bool CanRun(string[] args) //<---REQURED method
        {
            return args.Any(a => a.Equals("--plugin", StringComparison.OrdinalIgnoreCase) || a.Equals("-plugin", StringComparison.OrdinalIgnoreCase)); //This is a prerequisite for running the plugin. You can configure it.
        }

        //Optional variables for configuring plugin startup. They are not required; by default, false
        public static bool RunAfterExtraction(){return true;} //<--- Runs the plugin after the extractor actions.
        public static bool IgnoreExit(){return true;} //<--- Ignores Enviroment.Exit(0). Allows the plugin to run in any case.

        public static void Run(string[] args, Extractor extractor) //<--- Required MAIN Plugin logic method
        {
          //If you use the Extractor.Deep namespace, you should be aware that missing the --deep parameter may cause the code to behave incorrectly.
          if (!args.Any(a => a.Equals("--deep", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine("Requires --deep launch parameter!");
                Console.WriteLine("If you wrote it, it means the archive is either a regular ZIP or a regular HashFs, which can be unpacked in the usual way.");
                //Stops process without --deep
                Environment.Exit(0);
            }
            //Your Code
            int entries = 0;
            foreach(var entry in entries)
            {
            }
            //Stops the entire program. But if you need to continue, you don't have to write this.
            Environment.Exit(0);
        }
    }
}
```

### Minimal plugin

```csharp
using System;
using System.Linq;
using Extractor;
using TruckLib.HashFs;

namespace Extractor.Deep
{
    public class MyPlugin
    {
        public static bool CanRun(string[] args)
        {
            return args.Any(a => a.Equals("--myplugin", StringComparison.OrdinalIgnoreCase));
        }
        public static bool RunAfterExtraction(){
          return true;
        }
        public static bool IgnoreExit(){
          return false;
        }

        public static void Run(string[] args, Extractor extractor)
        {
            if (!args.Any(a => a.Equals("--deep", StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine("Requires --deep launch parameter!");
                Console.WriteLine("If you wrote it, it means the archive is either a regular ZIP or a regular HashFs, which can be unpacked in the usual way.");
                Environment.Exit(0);
            }
            Console.WriteLine("MyPlugin started!");

            if (extractor is HashFsDeepExtractor deep)
            {
                Console.WriteLine($"Archive: {extractor.ScsPath}");
                Console.WriteLine($"Entries: {deep.Reader.Entries.Count}");
            }

            Console.WriteLine("MyPlugin finished!");
            Environment.Exit(0);
        }
    }
}
```

### Compile Your Plugin

1. Install `Microsoft .NET Framework`.
2. Open `Microsoft PowerShell` in folder with your plugin.
3. Write <code>dotnet build -c Release</code>
4. Go to `YourPlugin\bin\Release\net10.0\` and get `YourPlugin.dll`. It is your compiled plugin.
5. Then just place it in the folder with the rest of the plugins and try to run it.

### Notes

- Plugins must be in the same folder as `extractor.exe`! Or specify a different folder in the launch options.
- Not all the necessary functions for working with .scs files are available in the extractor. Try searching for them in TruckLib.
- There's no need to change or add to the standard extractor parameters. Improve yours.
- Keep in mind that you will lose a lot if you don't use --deep.
- Before you begin, please review the extractor code and the structure of what you're about to process. This will prevent you from making silly mistakes and save you time.
- Compile your plugins only into DLL files. The program won't read any other files.
