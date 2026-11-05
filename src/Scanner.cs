using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BrokenSpaceFinder
{
    class DirNode
    {
        public string Name = "";
        public DirNode Parent;
        // Yol saklanmaz, üst klasörlerden üretilir: 200 binden fazla klasörde onlarca MB tasarruf
        public string Path { get { return Parent == null ? Name : System.IO.Path.Combine(Parent.Path, Name); } }
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

        static readonly List<DirNode> NoKids = new List<DirNode>(0);
        static readonly int Threads = Math.Max(4, Math.Min(16, Environment.ProcessorCount * 2));
        [ThreadStatic] static List<FsEntry> scratch;

        public DirNode Scan(string root)
        {
            var n = new DirNode();
            n.Name = root;
            Walk(n, root, 0);
            return n;
        }

        void Walk(DirNode n, string path, int depth)
        {
            if (stop) return;
            if (depth <= 2) Current = path;
            var entries = scratch ?? (scratch = new List<FsEntry>(256));
            entries.Clear();
            if (!Native.Read(path, entries)) n.Denied = true;

            List<DirNode> kids = null;
            List<string> kidPaths = null;
            long bytes = 0, newest = 0;
            int files = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.IsDir)
                {
                    if (e.IsLink) continue;   // bağlantı klasörleri başka yeri gösterir; saymak boyutu iki kez sayar
                    var k = new DirNode();
                    k.Name = e.Name;
                    k.Parent = n;
                    if (kids == null) { kids = new List<DirNode>(); kidPaths = new List<string>(); }
                    kids.Add(k);
                    kidPaths.Add(System.IO.Path.Combine(path, e.Name));
                }
                else
                {
                    bytes += e.Size;
                    files++;
                    if (e.Time > newest) newest = e.Time;
                    if (e.Size >= bigMin) AddBig(path, e);
                }
            }
            entries.Clear();   // alt klasörler aynı listeyi kullanır
            n.Own = bytes;
            n.OwnFiles = files;
            n.Newest = Native.Local(newest);
            Interlocked.Add(ref FilesSeen, files);
            Interlocked.Add(ref BytesSeen, bytes);

            if (kids == null) { n.Kids = NoKids; n.Size = n.Own; n.AllFiles = n.OwnFiles; return; }
            kids.TrimExcess();

            if (depth < 4 && kids.Count > 1)
            {
                var po = new ParallelOptions();
                po.MaxDegreeOfParallelism = Threads;
                Parallel.For(0, kids.Count, po, i => Walk(kids[i], kidPaths[i], depth + 1));
            }
            else
                for (int i = 0; i < kids.Count; i++) Walk(kids[i], kidPaths[i], depth + 1);

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
