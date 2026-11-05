using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BrokenSpaceFinder
{
    class DirNode
    {
        public string Name = "", Path = "";
        public DirNode Parent;
        public long Size, Own;          // Own: sadece bu klasördeki dosyalar
        public int OwnFiles;
        public long AllFiles;
        public DateTime Newest = DateTime.MinValue;
        public bool Denied;
        public List<DirNode> Kids = new List<DirNode>();
    }

    class BigFile
    {
        public string Path;
        public long Size;
        public DateTime Written;
    }

    // Bütün sürücüyü klasör ağacı olarak tarar ve en büyük dosyaları tutar
    class DiskScanner
    {
        public long FilesSeen, BytesSeen;
        public volatile string Current = "";
        volatile bool stop;
        public bool Stopped { get { return stop; } }
        public void Stop() { stop = true; }

        const int TopN = 300;
        readonly object lk = new object();
        readonly List<BigFile> big = new List<BigFile>();
        long bigMin = 50L << 20;

        public DirNode Scan(string root)
        {
            var n = new DirNode();
            n.Name = root;
            n.Path = root;
            Walk(n, 0);
            return n;
        }

        void Walk(DirNode n, int depth)
        {
            if (stop) return;
            if (depth <= 2) Current = n.Path;
            bool ok;
            var entries = Native.List(n.Path, out ok);
            if (!ok) n.Denied = true;

            var kids = new List<DirNode>();
            foreach (var e in entries)
            {
                if (e.IsDir)
                {
                    if (e.IsLink) continue;   // bağlantı klasörleri başka yeri gösterir; saymak boyutu iki kez sayar
                    var k = new DirNode();
                    k.Name = e.Name;
                    k.Path = System.IO.Path.Combine(n.Path, e.Name);
                    k.Parent = n;
                    kids.Add(k);
                }
                else
                {
                    n.Own += e.Size;
                    n.OwnFiles++;
                    if (e.Written > n.Newest) n.Newest = e.Written;
                    Interlocked.Increment(ref FilesSeen);
                    Interlocked.Add(ref BytesSeen, e.Size);
                    if (e.Size >= bigMin) AddBig(n.Path, e);
                }
            }

            if (depth < 3 && kids.Count > 1)
            {
                var po = new ParallelOptions();
                po.MaxDegreeOfParallelism = 8;
                Parallel.ForEach(kids, po, k => Walk(k, depth + 1));
            }
            else
                foreach (var k in kids) Walk(k, depth + 1);

            n.Kids = kids;
            n.Size = n.Own;
            n.AllFiles = n.OwnFiles;
            foreach (var k in kids)
            {
                n.Size += k.Size;
                n.AllFiles += k.AllFiles;
                if (k.Newest > n.Newest) n.Newest = k.Newest;
            }
        }

        void AddBig(string dir, FsEntry e)
        {
            lock (lk)
            {
                if (e.Size < bigMin) return;
                var b = new BigFile();
                b.Path = Path.Combine(dir, e.Name);
                b.Size = e.Size;
                b.Written = e.Written;
                big.Add(b);
                if (big.Count >= TopN * 2) Trim();
            }
        }

        void Trim()
        {
            big.Sort((a, b) => b.Size.CompareTo(a.Size));
            if (big.Count > TopN) big.RemoveRange(TopN, big.Count - TopN);
            if (big.Count == TopN) bigMin = Math.Max(bigMin, big[big.Count - 1].Size);
        }

        public List<BigFile> Top()
        {
            lock (lk)
            {
                Trim();
                return new List<BigFile>(big);
            }
        }

        // Taranmış ağaçta bir yolu bulur (yoksa null)
        public static DirNode Find(DirNode root, string path)
        {
            if (root == null) return null;
            if (path.Equals(root.Path, StringComparison.OrdinalIgnoreCase)) return root;
            string rest = path.Substring(root.Path.Length).Trim('\\');
            var cur = root;
            foreach (var part in rest.Split('\\'))
            {
                DirNode next = null;
                foreach (var k in cur.Kids)
                    if (k.Name.Equals(part, StringComparison.OrdinalIgnoreCase)) { next = k; break; }
                if (next == null) return null;
                cur = next;
            }
            return cur;
        }

        // Silinen bir şeyin boyutu üst klasörlerden düşülür
        public static void Subtract(DirNode n, long bytes, long files)
        {
            for (var p = n; p != null; p = p.Parent)
            {
                p.Size = Math.Max(0, p.Size - bytes);
                p.AllFiles = Math.Max(0, p.AllFiles - files);
            }
        }
    }
}
