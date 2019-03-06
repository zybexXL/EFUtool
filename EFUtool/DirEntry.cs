using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace EFUtool
{
    public class DirEntry
    {
        public bool Verified;       // disk access done
        public bool Changed;        // disk timestamp mismatch
        public bool Exists;

        public string Path;         // fullpath
        public long Size;
        public DateTime Modified;
        public DateTime Created;
        public FileAttributes Attributes;
        public List<DirEntry> Contents;
        public DirEntry Parent;

        public bool isFolder { get { return Attributes.HasFlag(FileAttributes.Directory); } }
        public bool isFile { get { return !isFolder; } }


        public DirEntry(DirEntry parent, string path, long size, DateTime modified, DateTime created, FileAttributes attributes)
        {
            Parent = parent;
            Path = path;
            Size = size;
            Modified = modified;
            Created = created;
            Attributes = attributes;
        }

        public DirEntry(DirEntry parent, DirectoryInfo dir)
        {
            Parent = parent;
            Path = dir.FullName;
            Size = 0;
            Modified = dir.LastWriteTime;
            Created = dir.CreationTime;
            Attributes = dir.Attributes;
            Exists = dir.Exists;
            Verified = true;
        }

        public DirEntry(DirEntry parent, FileData dir)
        {
            Parent = parent;
            Path = dir.FullPath;
            Size = dir.Size;
            Modified = dir.LastWriteTime;
            Created = dir.CreationTime;
            Attributes = dir.Attributes;
            Exists = true;
            Verified = true;
        }

        public void Add(DirEntry entry)
        {
            if (Contents == null) Contents = new List<DirEntry>();
            entry.Parent = this;
            Contents.Add(entry);
        }

        public override string ToString()
        {
            return isFolder ? $"[dir] {Path}" : $"{Path}";
        }
    }
}
