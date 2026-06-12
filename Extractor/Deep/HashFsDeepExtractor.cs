using Serilog;
using Sprache;
using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TruckLib.HashFs;
using static Extractor.ConsoleUtils;
using static Extractor.PathUtils;
using static Extractor.TextUtils;

namespace Extractor.Deep
{
    /// <summary>
    /// A HashFS extractor which scans entry contents for paths before extraction to simplify
    /// the extraction of archives which lack dictory listings.
    /// </summary>
    public class HashFsDeepExtractor : HashFsExtractor
    {
        /// <summary>
        /// The directory to which files whose paths were not discovered
        /// will be written.
        /// </summary>
        private const string DumpDirectory = "_unknown";

        /// <summary>
        /// The directory to which decoy files will be written.
        /// </summary>
        private const string DecoyDirectory = "_decoy";

        /// <summary>
        /// The number of files whose paths were not discovered and therefore have been
        /// dumped to <see cref="DumpDirectory"/>.
        /// </summary>
        private int numDumped;

        private HashFsPathFinder finder;

        private bool hasSearchedForPaths;

        private readonly ILogger logger;

        public HashFsDeepExtractor(string scsPath, Options opt) : base(scsPath, opt)
        {
            logger = Log.ForContext<HashFsDeepExtractor>();
        }

        /// <inheritdoc/>
        public override void Extract(string outputRoot)
        {
            Console.Error.WriteLine("Searching for paths ...");
            FindPaths();
            Extract(finder.FoundFiles.Order().ToArray(), outputRoot, false);
        }

        public void Extract(IList<string> foundFiles, string outputRoot, bool ignoreMissing)
        {
            bool startPathsSet = !opt.StartPaths.SequenceEqual(["/"]);

            substitutions = DeterminePathSubstitutions(foundFiles);
            LogPathSubstitutions();

            // Extract regular files
            var filteredFoundFiles = foundFiles
                .Where(p =>
                {
                    if (startPathsSet && !opt.StartPaths.Any(p.StartsWith))
                        return false;
                    return MatchesFilters(opt.Filters, p);
                })
                .Order()
                .ToArray();
            logger.Information("[{ScsName}] Extracting {NumPaths} files with known paths", 
                ScsName, filteredFoundFiles.Length);
            ExtractFiles(filteredFoundFiles, outputRoot, ignoreMissing);

            // Extract decoy files
            var foundDecoyFiles = finder.FoundDecoyFiles
                .Where(p =>
                {
                    if (startPathsSet && !opt.StartPaths.Any(p.StartsWith))
                        return false;
                    return MatchesFilters(opt.Filters, p);
                })
                .Order()
                .ToArray();
            if (foundDecoyFiles.Length > 0)
            {
                logger.Information("[{ScsName}] Extracting {NumDecoyPaths} decoy files", 
                    ScsName, foundDecoyFiles.Length);
            }
            var decoyDestination = Path.Combine(outputRoot, DecoyDirectory);
            foreach (var decoyFile in foundDecoyFiles)
            {
                ExtractFile(decoyFile, decoyDestination);
            }

            // Extract files whose paths could not be recovered
            if (!(startPathsSet || opt.Filters?.Count > 0))
            {
                DumpUnrecovered(outputRoot, foundFiles.Concat(foundDecoyFiles));
            }

            WriteRenamedSummary(outputRoot);
            WriteModifiedSummary(outputRoot);
        }

        public (HashSet<string> FoundFiles, HashSet<string> ReferencedFiles) FindPaths()
        {
            logger.Information("[{ScsName}] Searching archive contents for paths", ScsName);
            if (!hasSearchedForPaths)
            {
                finder = new HashFsPathFinder(Reader, opt.AdditionalStartPaths, junkEntries);
                finder.Find();
                hasSearchedForPaths = true;
            }
            logger.Information("[{ScsName}] Found {NumFoundFiles} paths, {NumReferencedFiles} referenced paths", 
                ScsName, finder.FoundFiles.Count, finder.ReferencedFiles.Count);
            return (finder.FoundFiles, finder.ReferencedFiles);
        }

        public HashFsPathFinder FindPaths(AssetLoader multiModWrapper)
        {
            finder = new HashFsPathFinder(Reader, opt.AdditionalStartPaths, junkEntries, multiModWrapper);
            finder.Find();
            hasSearchedForPaths = true;
            return finder;
        }

        /// <summary>
        /// Extracts files whose paths were not discovered.
        /// </summary>
        /// <param name="destination">The root output directory.</param>
        /// <param name="foundFiles">All discovered paths.</param>
        private void DumpUnrecovered(string destination, IEnumerable<string> foundFiles)
        {
            var notRecovered = Reader.Entries.Values
                .Where(e => !e.IsDirectory)
                .Except(foundFiles.Select(f =>
                {
                    if (Reader.EntryExists(f) != EntryType.NotFound)
                    {
                        return Reader.GetEntry(f);
                    }
                    junkEntries.TryGetValue(Reader.HashPath(f), out var retval);
                    return retval;
                }))
                .Except(junkEntries.Values)
                .Except(maybeJunkEntries.Values)
                .ToArray();

            if (notRecovered.Length == 0)
            {
                return;
            }
            
            logger.Information("[{ScsName}] Dumping {NumUnrecovered} files with unknown paths", 
                ScsName, notRecovered.Length);
            HashSet<ulong> visitedOffsets = [];

            var outputDir = Path.Combine(destination, DumpDirectory);

            foreach (var entry in notRecovered)
            {
                /*if (junkEntries.ContainsKey(entry.Hash) ||
                    maybeJunkEntries.ContainsKey(entry.Hash))
                {
                    continue;
                }*/

                var hashHexStr = entry.Hash.ToString("x16");

                byte[] fileBuffer;
                try
                {
                    fileBuffer = Reader.Extract(entry, "")[0];
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Unable to dump {hashHexStr}: {ex.Message}");
                    logger.Error(ex, "[{ScsName}] Unable to dump {HashStr} ({Offset})", ScsName, hashHexStr, entry.Offset);
                    numFailed++;
                    continue;
                }

                var fileType = FileTypeHelper.Infer(fileBuffer);
                var extension = FileTypeHelper.FileTypeToExtension(fileType);
                var fileName = hashHexStr + extension;
                var outputPath = Path.Combine(outputDir, fileName);
                Console.WriteLine($"Dumping {fileName} ...");
                if (!Overwrite && File.Exists(outputPath))
                {
                    numSkipped++;
                }
                else
                {
                    logger.Verbose("[{ScsName}] Dumping {FileName}", ScsName, fileName);
                    ExtractToDisk(entry, $"/{DumpDirectory}/{fileName}", outputPath);
                    numDumped++;
                }
            }
        }

        public override void PrintPaths(bool includeAll)
        {
            logger.Information("[{ScsName}] Searching archive contents for paths", ScsName);
            var finder = new HashFsPathFinder(Reader);
            finder.Find();
            var paths = (includeAll 
                ? finder.FoundFiles.Union(finder.ReferencedFiles) 
                : finder.FoundFiles).Order();

            PrintPathsMatchingFilters(paths, opt.StartPaths, opt.Filters);
        }

        public override void PrintExtractionResult()
        {
            Console.WriteLine($"{numExtracted} extracted " +
                $"({renamedFiles.Count} renamed, {modifiedFiles.Count} modified, {numDumped} dumped), " +
                $"{numSkipped} skipped, {numJunk} junk, {numFailed} failed");
            logger.Information("[{ScsName}] {NumExtracted} extracted " +
                "({NumRenamed} renamed, {NumModified} modified, {NumDumped} dumped), " +
                "{NumSkipped} skipped, {NumJunk} junk, {NumFailed} failed",
                ScsName, numExtracted, renamedFiles.Count, modifiedFiles.Count, 
                numDumped, numSkipped, numJunk, numFailed);
            PrintRenameSummary(renamedFiles.Count, modifiedFiles.Count);
        }

        public override List<Tree.Directory> GetDirectoryTree()
        {
            logger.Information("[{ScsName}] Searching archive contents for paths", ScsName);
            var finder = new HashFsPathFinder(Reader);
            finder.Find();

            var trees = opt.StartPaths
                .Select(startPath => PathListToTree(startPath, finder.FoundFiles))
                .ToList();
            return trees;
        }
    }
}
