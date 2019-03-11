using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EFUtool
{

    public class EFUfile
    {
        Dictionary<string, DirEntry> dirIndex = new Dictionary<string, DirEntry>();
        List<string> roots = new List<string>();
        List<string> include = new List<string>();
        List<string> exclude = new List<string>();
        List<string> iargs = new List<string>();
        List<string> xargs = new List<string>();

        int dirCount = 0;
        int fileCount = 0;
        long totalSize = 0;
        int maxDepth = 0;
        int exDirCount = 0;
        int exFileCount = 0;
        long exSize = 0;
        Dictionary<string, Tuple<int,long>> extensionCount = new Dictionary<string, Tuple<int, long>>();

        string EFUpath;

        public EFUfile(string path, List<string> rootList, List<string> included, List<string> excluded)
        {
            EFUpath = path;
            roots = rootList;
            iargs = included;
            xargs = excluded;
            include = preparePatterns(included);
            exclude = preparePatterns(excluded);
        }

        bool CheckRoots()
        {
            for (int i = 0; i < roots.Count; i++)
            {
                string path = Path.GetFullPath(roots[i]);                // warning: case is not corrected
                DirectoryInfo di = new DirectoryInfo(path);
                if (!di.Exists)
                {
                    Console.WriteLine($"Folder not found: {path}");
                    return false;
                }
                DirEntry entry = new DirEntry(null, di);
                entry.Verified = true;
                entry.Changed = true;
                entry.Exists = di.Exists;
                dirIndex[path.ToLower()] = entry;
            }
            return true;
        }

        #region include/exclude pattern check
        // convert -i  and -x masks to regex patterns
        string preparePattern(string mask)
        {
            string pattern = null;
            if (mask.StartsWith("regex:"))
                pattern = mask.Replace("regex:", "");
            else
                pattern = Regex.Escape(mask).Replace("\\*", ".*").Replace("\\?", "\\.");

            if (pattern.StartsWith(".*") && pattern.Length > 2) pattern = pattern.Substring(2);
            if (pattern.EndsWith(".*") && pattern.Length > 2) pattern = pattern.Substring(0, pattern.Length - 2);
            return pattern;
        }

        List<string> preparePatterns(List<string> masks)
        {
            List<string> patterns = new List<string>();
            foreach (var p in masks)
                patterns.Add(preparePattern(p));
            return patterns;
        }

        bool isIncluded(string path, bool isFolder)
        {
            if (include.Count == 0 && exclude.Count == 0)
                return true;

            foreach (var x in exclude)
            {
                if (isFolder && !x.Contains(@"\\"))      // excluding folders requires mask to contain a slash
                    continue;
                if (Regex.IsMatch(path, x, RegexOptions.IgnoreCase))
                    return false;
            }

            if (include.Count == 0) return true;
            bool defaultIncludeFolders = true;
            foreach (var x in include)
            {
                if (x.Contains(@"\\")) defaultIncludeFolders = false;
                if (isFolder && !x.Contains(@"\\"))      // including folders requires mask to contain a slash
                    continue;
                if (Regex.IsMatch(path, x, RegexOptions.IgnoreCase))
                    return true;
            }
            return isFolder && defaultIncludeFolders;      // folders are included by default, unless excluded above
        }

        #endregion

        #region save/load args to EFU file
        public void SaveEFUArgs(StreamWriter efu, DateTime creationDate)
        {
            string args = Util.Base64Encode(string.Join("\r", iargs) + "\n" + string.Join("\r", xargs));
            int attr = 1 + 2 + 4 + 64 + 256; // hidden, system, readonly, Device, Offline
            efu.WriteLine($"\"EFU:\\Args\\{args}\",0,{DateTime.Now.ToFileTime()},{creationDate.ToFileTime()},{attr}");
        }

        public bool ParseSavedArgs(string line, bool load, out DateTime cdate)
        {
            cdate = DateTime.Now;
            Match m = Regex.Match(line, @"^""EFU:\\Args\\([^""]+)"",\d+,(\d+),(\d+),(\d+)$", RegexOptions.IgnoreCase);
            if (!m.Success) return false;
            if (int.Parse(m.Groups[4].Value) != 1 + 2 + 4 + 64 + 256) return false;

            string args = Util.Base64Decode(m.Groups[1].Value);
            if (!args.Contains("\n")) return false;

            DateTime mdate = DateTime.FromFileTime(long.Parse(m.Groups[2].Value));
            cdate = DateTime.FromFileTime(long.Parse(m.Groups[3].Value));

            string[] includes = args.Split('\n')[0].Split(new char[] { '\r' }, StringSplitOptions.RemoveEmptyEntries);
            string[] excludes = args.Split('\n')[1].Split(new char[] { '\r' }, StringSplitOptions.RemoveEmptyEntries);
            args = "";
            foreach(var i in includes) {
                args += $"-i {i} ";
                if (!iargs.Contains(i, StringComparer.InvariantCultureIgnoreCase)) iargs.Add(i);
                if (load)
                {
                    string pattern = preparePattern(i);
                    if (!include.Contains(pattern))
                        include.Add(pattern);
                }
            }
            foreach (var x in excludes)
            {
                args += $"-x {x} ";
                if (!xargs.Contains(x, StringComparer.InvariantCultureIgnoreCase)) xargs.Add(x);
                if (load)
                {
                    string pattern = preparePattern(x);
                    if (!exclude.Contains(pattern))
                        exclude.Add(pattern);
                }
            }

            if (string.IsNullOrEmpty(args)) args = "(none)";
            //string ignored = load ? "" : " [ignored]";
            Console.WriteLine($"  Created on {cdate}, last modified on {mdate}");
            Console.WriteLine($"  Stored args: {args}");

            return true;
        }

        public bool RestoreSavedArgs(bool load, out DateTime created)
        {
            created = DateTime.Now;
            try
            {
                using (StreamReader sr = new StreamReader(EFUpath, Encoding.UTF8, false))
                {
                    sr.ReadLine();  // header
                    string line = sr.ReadLine();  // args
                    return ParseSavedArgs(line, load, out created);
                }
            }
            catch { }
            return false;
        }

        #endregion

        #region public entrypoints

        // create new EFU
        public int Create()
        {
            if (!CheckRoots()) return 2;

            Console.WriteLine($"Creating EFU file: {Path.GetFileName(EFUpath)}\n");

            // create tmp EFU file
            string newEFU = Path.ChangeExtension(EFUpath, ".tmp");
            StreamWriter swEFU = new StreamWriter(newEFU, false);
            swEFU.WriteLine("Filename,Size,Date Modified,Date Created,Attributes");
            if (Program.saveArgs)
                SaveEFUArgs(swEFU, DateTime.Now);

            Console.WriteLine($"Scanning and indexing folders");
            EFUScanFolder(swEFU);

            Console.WriteLine($"Finalizing EFU index");
            EFUFinalize(swEFU);

            swEFU.Close();
            Console.WriteLine("Replacing EFU file");
            try
            {
                File.Delete(EFUpath);
                File.Move(newEFU, EFUpath);
            }
            catch
            {
                Console.WriteLine("Error: failed to replace the existing EFU file, please check permissions or if the file in in use.");
                return 3;
            }

            Console.WriteLine($"\nContents:  {Util.FormatSize(totalSize)} in {fileCount:n0} files, {dirCount:n0} folders [{maxDepth} depth]");
            return 0;
        }

        // update existing EFU
        public int Update()
        {
            if (!CheckRoots()) return 2;

            Console.WriteLine($"Updating EFU file: {Path.GetFileName(EFUpath)}");
            bool saveArgs = RestoreSavedArgs(true, out DateTime created);

            Console.WriteLine($"\nScanning current EFU index");
            EFULoad();

            Console.WriteLine($"Finding changed folders");
            EFUFindChangedFolders();

            // create tmp EFU file
            string newEFU = Path.ChangeExtension(EFUpath, ".tmp");
            StreamWriter swEFU = new StreamWriter(newEFU, false, Encoding.UTF8, 65536);
            swEFU.WriteLine("Filename,Size,Date Modified,Date Created,Attributes");
            if (saveArgs || Program.saveArgs)
                SaveEFUArgs(swEFU, created);

            Console.WriteLine($"Reindexing unchanged folders");
            EFUReindexUnchangedEntries(swEFU);

            Console.WriteLine($"Scanning new/modified folders");
            EFUScanFolder(swEFU);

            Console.WriteLine("Updating directory sizes");
            var roots = dirIndex.Values.Where(d => d.Parent == null).ToList();
            foreach (var dir in roots)
            {
                if (dir.Exists)
                    getSubSize(dir);
            }

            Console.WriteLine($"Finalizing EFU index");
            EFUFinalize(swEFU);

            swEFU.Close();
            Console.WriteLine("Replacing EFU file");
            try
            {
                File.Delete(EFUpath);
                File.Move(newEFU, EFUpath);
            }
            catch
            {
                Console.WriteLine("Error: failed to replace the existing EFU file, please check permissions or if the file in in use.");
                return 3;
            }

            Console.WriteLine($"\nContents:  {Util.FormatSize(totalSize)} in {fileCount:n0} files, {dirCount:n0} folders [{maxDepth} depth]");
            return 0;
        }

        // print EFU stats
        public int Statistics()
        {
            Console.WriteLine($"Compiling EFU statistics: {Path.GetFileName(EFUpath)}");
            RestoreSavedArgs(false, out DateTime created);
            Console.WriteLine();

            if (roots.Count > 0)
                include.AddRange(preparePatterns(roots));       // treat Roots as includes

            getStats();

            Console.WriteLine($"Contents:  {Util.FormatSize(totalSize),12}  {fileCount,12:n0} files  {dirCount,10:n0} folders [{maxDepth} depth]");
            if (exclude.Count >0 || include.Count > 0)
                Console.WriteLine($"Excluded:  {Util.FormatSize(exSize),12}  {exFileCount,12:n0} files  {exDirCount,10:n0} folders");

            if (extensionCount.Count > 0)
            {
                var extF = extensionCount.OrderByDescending(x => x.Value.Item1).Take(10);
                var extS = extensionCount.OrderByDescending(x => x.Value.Item2).Take(10);
                int width = Math.Max(extF.Max(x => x.Key.Length), extS.Max(x => x.Key.Length));

                Console.WriteLine("\nTop 10 extensions by file count:\n" + new string('-', 34 + width));
                foreach (var x in extF)
                    Console.WriteLine($"{x.Key.PadRight(width)}   {x.Value.Item1,10:n0} files   {Util.FormatSize(x.Value.Item2),12}");

                Console.WriteLine("\nTop 10 extensions by size:\n" + new string('-', 34 + width));
                foreach (var x in extS)
                    Console.WriteLine($"{x.Key.PadRight(width)}   {x.Value.Item1,10:n0} files   {Util.FormatSize(x.Value.Item2),12}");
            }
            return 0;
        }

        // remove entries from existing EFU
        public int Filter()
        {
            Console.WriteLine($"Filtering EFU file: {Path.GetFileName(EFUpath)}");
            bool saveArgs = RestoreSavedArgs(false, out DateTime created);

            if (roots.Count > 0)
                exclude.AddRange(preparePatterns(roots));       // treat Roots as exclude

            Console.WriteLine($"\nScanning current EFU index");
            EFULoad();

            foreach (var dir in dirIndex.Values)
                dir.Exists = dir.Verified = true;

            // create tmp EFU file
            string newEFU = Path.ChangeExtension(EFUpath, ".tmp");
            StreamWriter swEFU = new StreamWriter(newEFU, false, Encoding.UTF8, 65536);
            swEFU.WriteLine("Filename,Size,Date Modified,Date Created,Attributes");
            if (saveArgs || Program.saveArgs)
                SaveEFUArgs(swEFU, created);

            Console.WriteLine($"Applying filter");
            EFUFilter(swEFU);

            Console.WriteLine("Updating directory sizes");
            var rootDirs = dirIndex.Values.Where(d => d.Parent == null).ToList();
            foreach (var dir in rootDirs)
            {
                if (dir.Exists)
                    getSubSize(dir);
            }

            Console.WriteLine($"Finalizing EFU index");
            EFUFinalize(swEFU);

            swEFU.Close();
            Console.WriteLine("Replacing EFU file");
            try
            {
                File.Delete(EFUpath);
                File.Move(newEFU, EFUpath);
            }
            catch {
                Console.WriteLine("Error: failed to replace the existing EFU file, please check permissions or if the file in in use.");
                return 3;
            }

            Console.WriteLine($"\nContents:  {Util.FormatSize(totalSize),12}  {fileCount,12:n0} files  {dirCount,10:n0} folders [{maxDepth} depth]");
            if (exclude.Count > 0 || include.Count > 0)
                Console.WriteLine($"Excluded:  {Util.FormatSize(exSize),12}  {exFileCount,12:n0} files  {exDirCount,10:n0} folders");

            return 0;
        }

        #endregion

        // checks each known folder timestamp to determine if it has changed
        void EFUFindChangedFolders()
        {
            int curr = 0;
            int count = dirIndex.Count;
            int changed = 0;
            int missing = 0;
            DirEntry prev = null;

            int lastpc = -1;
            foreach (var entry in dirIndex.Values)
            {
                int pc = ++curr * 100 / count;
                if (Program.ShowProgress && (pc != lastpc || curr % 100 == 0))
                {
                    string log = $"  {curr}/{count} ({pc}%) {entry.Path}";
                    if (log.Length > Console.BufferWidth - 1)
                        log = log.Substring(0, Console.BufferWidth - 4) + "...";
                    Console.Write($"{log.PadRight(Console.BufferWidth - 1)}\r");
                    lastpc = pc;
                }

                entry.Verified = true;
                if (prev != null && !prev.Exists && entry.Path.StartsWith(prev.Path))      // if parent doesn't exist, this one doesn't either
                {
                    missing++;
                    continue;
                }

                prev = entry;
                var di = new DirectoryInfo(entry.Path);
                if (di.Exists)
                {
                    entry.Exists = true;
                    if (di.LastWriteTime != entry.Modified)
                    {
                        entry.Changed = true;
                        changed++;
                    }
                }
                else
                    missing++;
            }
            if (Program.ShowProgress) Console.Write($"\r{new string(' ', Console.BufferWidth - 1)}\r");
            Console.WriteLine($"  {count} folders checked, {changed} changed and {missing} missing");
        }

        // folder scan/indexing - uses EFUScanFolderRecursive
        void EFUScanFolder(StreamWriter newEFU)
        {
            var dirs = dirIndex.Values.Where(d => d.Verified && d.Exists && d.Changed).ToList();
            foreach (var dir in dirs)
                EFUScanFolderRecursive(newEFU, dir);

            if (Program.ShowProgress) Console.Write($"\r{new string(' ', Console.BufferWidth-1)}\r");
        }

        // recursive folder scan/indexing (new/modified folders)
        int dirnum = 0;
        void EFUScanFolderRecursive(StreamWriter newEFU, DirEntry dir)
        {
            if (Program.ShowProgress && dirnum++ % 100 == 0)
            {
                string log = $"  Scanning: {dir.Path}";
                if (log.Length > Console.BufferWidth - 1)
                    log = log.Substring(0, Console.BufferWidth - 4) + "...";
                Console.Write($"{log.PadRight(Console.BufferWidth - 1)}\r");
            }

            int depth = dir.Path.Length < 3 ? 0 : dir.Path.Length - dir.Path.Substring(2).Replace("\\", "").Length;
            if (depth > maxDepth)
                maxDepth = depth;

            FileData[] contents = FastDirectory.GetFiles(dir.Path, "*.*", SearchOption.TopDirectoryOnly);
            var subs = contents.Where(e => e.Attributes.HasFlag(FileAttributes.Directory)).ToArray();
            var files = contents.Where(e => !e.Attributes.HasFlag(FileAttributes.Directory)).ToArray();

            foreach (var f in files)
            {
                if (isIncluded(f.FullPath, false))
                {
                    newEFU.WriteLine($"\"{f.FullPath}\",{f.Size},{f.LastWriteTime.ToFileTime()},{f.CreationTime.ToFileTime()},{(int)f.Attributes}");
                    dir.Size += f.Size;
                    totalSize += f.Size;
                    fileCount++;
                }
                else
                {
                    exFileCount++;
                    exSize += f.Size;
                }
            }

            foreach (var s in subs)
            {
                if (dirIndex.ContainsKey(s.FullPath.ToLower()))     // skip folders already indexed
                    continue;

                if (isIncluded(s.FullPath, true))
                {
                    DirEntry sub = new DirEntry(dir, s) { Changed = true };
                    dir.Add(sub);
                    dirIndex[s.FullPath.ToLower()] = sub;
                    EFUScanFolderRecursive(newEFU, sub);
                    dir.Size += sub.Size;
                }
                else
                    exDirCount++;
            }
        }

        // recursively update total folder size
        // before calling this, each folder size is assumed to be just the sum of sizes of the files on that folder (not including subfolders)
        long getSubSize(DirEntry dir)
        {
            if (dir.Contents != null)
                foreach (var sub in dir.Contents)
                    if (sub.Exists)
                        dir.Size += getSubSize(sub);
            return dir.Size;
        }

        // writes folder info to EFU file
        void EFUFinalize(StreamWriter newEFU)
        {
            foreach (var dir in dirIndex.Values)
            {
                if (dir.Exists)
                {
                    dirCount++;
                    
                    int depth = dir.Path.Length < 3 ? 0 : dir.Path.Length - dir.Path.Substring(2).Replace("\\", "").Length;
                    if (depth > maxDepth)
                        maxDepth = depth;

                    newEFU.WriteLine($"\"{dir.Path}\",{dir.Size},{dir.Modified.ToFileTime()},{dir.Created.ToFileTime()},{(int)dir.Attributes}");
                }
            }
        }

        // copies file entries of unmodified folders from existing EFU to new EFU
        void EFUReindexUnchangedEntries(StreamWriter newEFU)
        {
            long lines = 0;
            int lastpc = -1;

            using (StreamReader sr = new StreamReader(EFUpath, Encoding.UTF8, false, 1 << 20))
            {
                sr.ReadLine();  // header
                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine();
                    ++lines;

                    int pc = (int)(100 * sr.BaseStream.Position / sr.BaseStream.Length);
                    if (Program.ShowProgress && (lines % 10000 == 0 || pc != lastpc))
                        Console.Write($"  Done {pc}%\r");
                    lastpc = pc;

                    DirEntry file = ParseEntry(line, false);
                    if (file.isFolder)
                        continue;
                    else
                    {
                        if (isIncluded(file.Path, false))      // process included/excluded files
                        {
                            var folder = Path.GetDirectoryName(file.Path);
                            if (dirIndex.TryGetValue(folder.ToLower(), out var subdir) && subdir.Verified)
                            {
                                if (!subdir.Exists) continue;
                                if (!subdir.Changed)
                                {
                                    fileCount++;
                                    totalSize += file.Size;
                                    subdir.Size += file.Size;
                                    newEFU.WriteLine(line);
                                }
                            }
                        }
                        else
                        {
                            exFileCount++;
                            exSize += file.Size;
                        }
                    }
                }
            }
            if (Program.ShowProgress) Console.Write("                    \r");
        }

        // loads an EFU file, keeping only directory info and stats (files are ignored)
        void EFULoad(bool Checked = false)
        {
            long lines = 0;
            int dirs = 0;
            int files = 0;
            long size = 0;
            int depth = 0;
            int lastpc = -1;
            
            using (StreamReader sr = new StreamReader(EFUpath, Encoding.UTF8, false, 1 << 20))
            {
                sr.ReadLine();  // header
                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine();
                    ++lines;

                    int pc = (int)(100 * sr.BaseStream.Position / sr.BaseStream.Length);
                    if (Program.ShowProgress && (lines % 10000 == 0 || pc != lastpc))
                        Console.Write($"  Loaded {pc}%\r");
                    lastpc = pc;

                    DirEntry dir = ParseEntry(line, true);      // dirs only
                    if (dir != null)
                    {
                        int fd = dir.Path.Length - dir.Path.Substring(1).Replace("\\", "").Length;
                        if (fd > depth)
                            depth = fd;

                        dirs++;
                        if (isIncluded(dir.Path, true))     // filter
                        {
                            dirIndex[dir.Path.ToLower()] = dir;
                            DirEntry parDir = null;
                            var parent = Path.GetDirectoryName(dir.Path);
                            if (parent != null && dirIndex.TryGetValue(parent.ToLower(), out parDir))
                                parDir.Add(dir);
                            if (parDir == null)
                                size += dir.Size;   // root folder size

                            dir.Size = 0;   // will be recalculated
                        }
                        else
                            exDirCount++;       // size is added during elsewhere
                    }
                    else
                        files++;
                }
            }
            //if (Program.ShowProgress) Console.Write("                    \r");
            Console.WriteLine($"  Contents:  {Util.FormatSize(size)} in {files:n0} files, {dirs:n0} folders [{depth} depth]");
        }

        // copies entries that pass exclude filter from existing EFU to new EFU
        void EFUFilter(StreamWriter newEFU)
        {
            long lines = 0;
            int lastpc = -1;

            using (StreamReader sr = new StreamReader(EFUpath, Encoding.UTF8, false, 1 << 20))
            {
                sr.ReadLine();  // header
                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine();
                    ++lines;

                    int pc = (int)(100 * sr.BaseStream.Position / sr.BaseStream.Length);
                    if (Program.ShowProgress && (lines % 10000 == 0 || pc != lastpc))
                        Console.Write($"  Done {pc}%\r");
                    lastpc = pc;

                    DirEntry file = ParseEntry(line, false);
                    if (file.isFolder)
                        continue;
                    else
                    {
                        var folder = Path.GetDirectoryName(file.Path);
                        if (folder != null && dirIndex.TryGetValue(folder.ToLower(), out var subdir))
                        {
                            if (isIncluded(file.Path, file.isFolder))
                            {
                                fileCount++;
                                totalSize += file.Size;
                                subdir.Size += file.Size;
                                newEFU.WriteLine(line);
                                continue;
                            }
                        }
                        exFileCount++;
                        exSize += file.Size;
                    }
                }
            }
            if (Program.ShowProgress) Console.Write("                    \r");
        }

        // loads an EFU file, keeping only directory info and stats (files are ignored)
        void getStats()
        {
            long lines = 0;
            int lastpc = -1;
            using (StreamReader sr = new StreamReader(EFUpath, Encoding.UTF8, false, 1 << 20))
            {
                sr.ReadLine();  // header
                while (!sr.EndOfStream)
                {
                    string line = sr.ReadLine();
                    ++lines;

                    int pc = (int)(100 * sr.BaseStream.Position / sr.BaseStream.Length);
                    if (Program.ShowProgress && (lines % 10000 == 0 || pc != lastpc))
                        Console.Write($"  Loaded {pc}%\r");
                    lastpc = pc;

                    DirEntry dir = ParseEntry(line, false);      // dirs only
                    if (isIncluded(dir.Path, dir.isFolder))
                    {
                        if (dir.isFolder)
                        {
                            dirCount++;
                            int depth = dir.Path.Length < 3 ? 0 : dir.Path.Length - dir.Path.Substring(2).Replace("\\", "").Length;
                            if (depth > maxDepth)
                                maxDepth = depth;
                        }
                        else
                        {
                            fileCount++;
                            totalSize += dir.Size;

                            string ext = (Path.GetExtension(dir.Path)??"").ToLower();
                            if (extensionCount.TryGetValue(ext, out var count))
                                extensionCount[ext] = new Tuple<int, long>(count.Item1 + 1, count.Item2 + dir.Size);
                            else
                                extensionCount[ext] = new Tuple<int, long>(1, dir.Size);
                        }
                    }
                    else
                    {

                        if (dir.isFolder) exDirCount++;
                        else
                        {
                            exFileCount++;
                            exSize += dir.Size;
                        }
                    }
                }
            }
            if (Program.ShowProgress) Console.Write("                    \r");
        }

        // uses string.split (2x faster than regex for this task)
        DirEntry ParseEntry(string line, bool dirsOnly = false)
        {
            if (dirsOnly && (line.EndsWith(",0") || line.EndsWith(",32")))
                return null;

            var items = line.Split(',');
            if (items.Length >= 5)
            {
                FileAttributes attr = (FileAttributes)int.Parse(items[items.Length - 1]);
                if (dirsOnly && !attr.HasFlag(FileAttributes.Directory))
                    return null;

                string path = items.Length == 5 ? items[0].Trim('\"') : string.Join(",", items.Take(items.Length - 4).ToList()).Trim('\"');
                long size = long.Parse(items[items.Length - 4]);
                DateTime mdate = DateTime.FromFileTime(long.Parse(items[items.Length - 3]));
                DateTime cdate = DateTime.FromFileTime(long.Parse(items[items.Length - 2]));

                return new DirEntry(null, path, size, mdate, cdate, attr);
            }
            return null;
        }

        // get existing DirEntry or create new one
        DirEntry getDirEntry(string path)
        {
            string key = path?.ToLower();
            if (dirIndex.TryGetValue(key, out DirEntry entry))
                return entry;

            string root = Path.GetPathRoot(path).TrimEnd('\\');
            string dir = Path.GetDirectoryName(path)?.TrimEnd('\\') ?? "";
            //string name = path == root ? root : Path.GetFileName(path);

            DirEntry de = new DirEntry(getDirEntry(dir), dir, 0, DateTime.MinValue, DateTime.MinValue, 0);
            dirIndex[key] = de;

            return de;
        }
    }
}
