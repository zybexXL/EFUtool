using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;

namespace EFUtool
{
    enum ToolMode { Default, Create, Update, Filter, Stats }

    class Program
    {
        const string Usage = @"
Usage:
    EFUtool <[path\]index.EFU> [<path1> ... <pathN>] [options]\n

Options (Multiple -i/-x/-d switches can be used):
    -i <mask> : include files/dir mask
    -x <mask> : exclude files/dir mask
    -f        : filter EFU file (no folder update/scan)
    -s        : print EFU file statistics/info
    -p        : alternative progress indication (for logging to file)

Examples:
    Create a new EFU file with index of RootPath1 and RootPath2:
    > EFUtool index.efu RootPath1 RootPath2

    Update an existing EFU file - rescan all included folders:
    > EFUtool index.efu

    Update an existing EFU file - rescan only RootPath2, exclude EXE files:
    > EFUtool index.efu RootPath2 -x *.exe

    Update an existing EFU file - add JPG files from RootPath3:
    > EFUtool index.efu RootPath3 -i *.jpg

    Filter out RootPath2\ from EFU file:
    > EFUtool index.efu -f -x RootPath2\

    Filter out all *.tmp files and TEMP folders from EFU file:
    > EFUtool index.efu -f -x *.tmp -x \TEMP\

    Filter out all except *.jpg files of RootPath1 from EFU file:
    > EFUtool index.efu -f -i RootPath1\*.jpg

    Print stats for EFU file:
    > EFUtool index.efu -s

    Print stats for *.tmp files on EFU file:
    > EFUtool index.efu -s -i *.tmp

    Print stats for RootPath1 except *.tmp files:
    > EFUtool index.efu -s RootPath1 -x *.tmp
";

        static Dictionary<string, DirEntry> dirIndex = new Dictionary<string, DirEntry>();

        static string efuPath;
        static List<string> Roots = new List<string>();
        static List<string> include = new List<string>();
        static List<string> exclude = new List<string>();
        static ToolMode runmode = ToolMode.Default;
        internal static bool ShowProgress = true;

        static int Main(string[] args)
        {
            Console.WriteLine("EFUtool v1.0 (c) 2019 Pedro Fonseca [pbfonseca@gmail.com]\n");
            if (!ProcessCmdArgs(args))
                return 1;

            EFUfile EFU = new EFUfile(efuPath, Roots, include, exclude);

            Stopwatch sw = new Stopwatch();
            sw.Start();

            if (runmode != ToolMode.Create && !File.Exists(efuPath))
            {
                Console.WriteLine($"File not found: {efuPath}");
                    return 2;
            }

            int result = 0;
            try
            {
                switch (runmode)
                {
                    case ToolMode.Create:
                        if (!EFU.CheckRoots()) return 2;
                        result = EFU.Create();
                        break;
                    case ToolMode.Update:
                        if (!EFU.CheckRoots()) return 2;
                        result = EFU.Update();
                        break;
                    case ToolMode.Filter:
                        result = EFU.Filter();
                        break;
                    case ToolMode.Stats:
                        result = EFU.Statistics();
                        break;
                    default:
                        Console.WriteLine(Usage);
                        return 1;
                }
            }
            catch (Exception ex)
            {
                result = 99;
                Console.WriteLine($"An exception ocurred while running EFUTool:\n{ex.Message}\n");
            }

            sw.Stop();
            if (runmode == ToolMode.Create || runmode == ToolMode.Update)
            {
                string duration = sw.Elapsed.TotalMinutes > 1 ? $"{sw.Elapsed.ToString(@"h\:mm\:ss")}"
                    : sw.Elapsed.TotalSeconds > 1 ? $"{sw.Elapsed.TotalSeconds:0.0} seconds"
                    : $"{sw.ElapsedMilliseconds} ms";
                Console.WriteLine($"\nEFUtool finished in {duration}");
            }
            return result;
        }


        static bool ProcessCmdArgs(string[] args)
        {
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLower();
                if (Regex.IsMatch(arg, @"^[-/](\?|h)"))
                {
                    Console.WriteLine(Usage);
                    return false;
                }
                if (arg[0] == '-')
                {
                    if (arg == "-i" && i < args.Length + 1) include.Add(args[++i]);
                    else if (arg == "-x" && i < args.Length + 1) exclude.Add(args[++i]);
                    else if (arg == "-f") runmode = ToolMode.Filter;
                    else if (arg == "-s") runmode = ToolMode.Stats;
                    else if (arg == "-p") ShowProgress = false;
                    else
                    {
                        Console.WriteLine(Usage);
                        return false;
                    }
                }
                else
                {
                    if (efuPath == null) efuPath = args[i];
                    else Roots.Add(args[i]);
                }
            }

            if (runmode == ToolMode.Filter && include.Count == 0 && exclude.Count == 0 && Roots.Count == 0)
            {
                Console.WriteLine($"Filter mode -f needs include/exclude arguments too.");
                return false;
            }

            bool exists = File.Exists(efuPath);
            if (!exists && File.Exists($"{efuPath}.efu"))
            {
                efuPath = $"{efuPath}.efu";
                exists = true;
            }


            if (runmode == ToolMode.Default)
                runmode = exists ? ToolMode.Update : ToolMode.Create;
            if (runmode == ToolMode.Create && string.IsNullOrEmpty(Path.GetExtension(efuPath)))
                efuPath = $"{efuPath}.efu";

            if (efuPath == null || Directory.Exists(efuPath))
            {
                Console.WriteLine(Usage);
                return false;
            }

            if (runmode == ToolMode.Create && Roots.Count == 0)
            {
                Console.WriteLine("Please specify a folder path to index.");
                return false;
            }

            return true;
        }
    }
}
