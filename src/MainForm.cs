using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace BrokenSpaceFinder
{
    class MainForm : Form, ISurface
    {
        readonly TitleBar titleBar = new TitleBar();
        readonly AppHeader header = new AppHeader();
        protected override void WndProc(ref Message m) { if (!Frame.Handle(this, ref m, titleBar.Height + header.Height)) base.WndProc(ref m); }
        protected override void OnActivated(EventArgs e) { base.OnActivated(e); titleBar.Invalidate(); }
        public void PaintSurface(Graphics g, Rectangle r) { Glass.Backdrop(g, ClientSize, Point.Empty, r); }
        protected override void OnPaintBackground(PaintEventArgs e) { PaintSurface(e.Graphics, e.ClipRectangle); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(true); }
        protected override void OnDeactivate(EventArgs e) { base.OnDeactivate(e); titleBar.Invalidate(); }
        readonly Settings cfg;
        readonly BufferedPanel page = new BufferedPanel();
        readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();
        public bool ExitRequested;     // kurulum paketi ya da ikinci kopya kapanmasını istedi

        PictureBox logo;
        Label lblTitle, lblSub;
        Seg segLang, segDrive, segTab;
        FlatButton btnAbout, btnAdmin;
        List<DriveInfo> drives = new List<DriveInfo>();
        readonly List<Control>[] tabCtl = { new List<Control>(), new List<Control>(), new List<Control>(), new List<Control>() };
        int tab, addTo = -1;

        // Disk haritası
        StatusBox mapInfo;
        FlatButton btnScan, btnUp, btnReveal;
        Label lblPath, secDirs, secBig;
        SpaceBar bar;
        SpaceList lstDirs, lstBig;
        DiskScanner scanner;
        DirNode root, curNode, filesOf;
        List<BigFile> bigFiles = new List<BigFile>();
        string scannedDrive = "", mapText = "";
        DateTime scanStart;

        // Temizlik
        StatusBox cleanInfo;
        SpaceList lstRules;
        FlatButton btnCalc, btnClean;
        List<Rule> rules;
        readonly Dictionary<string, bool> ruleChecks = new Dictionary<string, bool>();
        volatile bool cleanBusy, calcBusy;
        int calcGen;

        // Uygulama verileri
        StatusBox dataInfo;
        Seg segData;
        SpaceList lstData;
        FlatButton btnDataRefresh, btnDataOpen, btnDataDelete;
        List<DataFolder> dataFolders;
        volatile bool dataBusy;
        volatile string dataProgress = "";

        // Programlar
        StatusBox appInfo;
        Seg segApps;
        SpaceList lstApps;
        FlatButton btnAppRefresh, btnAppOpen, btnAppRemove;
        List<InstalledApp> apps;
        volatile bool appBusy;

        public MainForm(Settings cfg)
        {
            this.cfg = cfg;
            tab = cfg.Tab;
            Text = AppInfo.Name + " " + AppInfo.Version + (Sys.IsAdmin ? L.T(" (yönetici)", " (administrator)") : "");
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            AutoScaleMode = AutoScaleMode.None;
            Icon = Theme.AppIcon(32);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            using (var g = CreateGraphics()) Theme.S = Math.Max(1f, g.DpiX / 96f);

            page.Dock = DockStyle.Fill;
            page.BackColor = Theme.Bg;
            page.Resize += delegate { DoLayout(); };
            Controls.Add(page);
            header.Dock = DockStyle.Top;
            Controls.Add(header);
            titleBar.ShowTitle = false;
            titleBar.Dock = DockStyle.Top;
            Controls.Add(titleBar);

            BuildUi();
            InitialSize();

            tick.Interval = 250;
            tick.Tick += delegate { OnTick(); };
            tick.Start();
        }

        static int P(float v) { return Theme.P(v); }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Frame.Apply(this); }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RefreshDrives();
            SetTab(tab);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!cfg.Welcomed)
            {
                cfg.Welcomed = true;
                cfg.Save();
                BeginInvoke((MethodInvoker)delegate { using (var w = new WelcomeDialog()) w.ShowDialog(this); });
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (cleanBusy && !ExitRequested)
            {
                e.Cancel = true;
                ConfirmDialog.Info(this, L.T("Temizlik sürüyor", "Clean-up in progress"), L.T("Temizlik bitince kapatabilirsin.", "You can close the app when the clean-up finishes."));
                return;
            }
            if (scanner != null) scanner.Stop();
            ExitRequested = true;
            SavePlacement();
            WinShell.KeepAwake(false);
            base.OnFormClosing(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys key)
        {
            // Ctrl+1…4: sekmeler
            if ((key & Keys.Modifiers) == Keys.Control)
            {
                int k = (int)(key & Keys.KeyCode) - (int)Keys.D1;
                if (k >= 0 && k <= 3) { SetTab(k); return true; }
            }
            if (key == Keys.F5)
            {
                if (tab == 0 && scanner == null) StartScan();
                else if (tab == 1) Calc();
                else if (tab == 2) RefreshData();
                else if (tab == 3) RefreshApps();
                return true;
            }
            return base.ProcessCmdKey(ref msg, key);
        }

        // Kurulum paketi güncellemeden önce ya da --exit ile çağrılınca
        public void RequestExit()
        {
            if (cleanBusy) return;   // yarım kalan temizlik zarar vermez ama bitmesini beklemek daha temiz
            ExitRequested = true;
            if (scanner != null) scanner.Stop();
            Close();
        }

        public void BringToFront2()
        {
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Show();
            Activate();
            BringToFront();
        }

        void InitialSize()
        {
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            var frame = SizeFromClientSize(Size.Empty);
            MinimumSize = SizeFromClientSize(new Size(P(780), P(660)));
            ClientSize = new Size(Math.Min(P(1240), wa.Width - frame.Width), Math.Min(P(900), wa.Height - frame.Height));
            try
            {
                var p = cfg.Window.Split(',');
                if (p.Length == 5)
                {
                    var r = new Rectangle(int.Parse(p[0]), int.Parse(p[1]), int.Parse(p[2]), int.Parse(p[3]));
                    bool visible = false;
                    foreach (var sc in Screen.AllScreens)
                        if (sc.WorkingArea.IntersectsWith(new Rectangle(r.X + 40, r.Y + 10, Math.Max(1, r.Width - 80), 40))) visible = true;
                    if (visible && r.Width >= MinimumSize.Width && r.Height >= MinimumSize.Height)
                    {
                        StartPosition = FormStartPosition.Manual;
                        Bounds = r;
                        if (p[4] == "1") WindowState = FormWindowState.Maximized;
                    }
                }
            }
            catch { }
        }

        void SavePlacement()
        {
            var r = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            cfg.Window = r.X + "," + r.Y + "," + r.Width + "," + r.Height + "," + (WindowState == FormWindowState.Maximized ? "1" : "0");
            cfg.Save();
        }

        // ------------------------------------------------------------------ Arayüz kurulumu
        T Add<T>(T c) where T : Control
        {
            if (c.Font == DefaultFont || !(c is Label)) c.Font = Font;
            page.Controls.Add(c);
            if (addTo >= 0) tabCtl[addTo].Add(c);
            return c;
        }

        Label Section(string text)
        {
            var l = new Label();
            l.Text = L.Upper(text);
            l.ForeColor = Theme.Sub;
            l.BackColor = Color.Transparent;
            l.AutoSize = true;
            Add(l);
            l.Font = new Font(Font.FontFamily, 8f, FontStyle.Bold);
            return l;
        }

        FlatButton Btn(string text, BtnKind kind, EventHandler click)
        {
            var b = Add(new FlatButton(text, kind));
            b.Click += click;
            return b;
        }

        SpaceList List(string empty)
        {
            var l = Add(new SpaceList());
            l.Empty = empty;
            return l;
        }

        void BuildUi()
        {
            SuspendLayout();
            Font = new Font("Segoe UI", 9.5f);
            foreach (var t in tabCtl) t.Clear();
            addTo = -1;

            logo = new PictureBox();
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.BackColor = Color.Transparent;
            logo.Image = Theme.Logo();
            page.Controls.Add(logo);

            lblTitle = new Label();
            lblTitle.Text = AppInfo.Name;
            lblTitle.Font = new Font(Font.FontFamily, 15f, FontStyle.Bold);
            lblTitle.ForeColor = Theme.Text;
            lblTitle.AutoSize = true;
            page.Controls.Add(lblTitle);

            lblSub = new Label();
            lblSub.Text = L.T("Diskini ne dolduruyor? Bul, güvenle temizle.", "What's filling your disk? Find it, clean it safely.");
            lblSub.ForeColor = Theme.Sub;
            lblSub.AutoSize = true;
            page.Controls.Add(lblSub);

            segLang = Add(new Seg());
            segLang.Items = new[] { "TR", "EN" };
            segLang.Font = new Font("Segoe UI", 8.5f);
            segLang.Selected = Math.Max(0, Array.IndexOf(L.Codes, L.Lang));
            segLang.Changed += delegate (int i)
            {
                if (scanner != null || cleanBusy || dataBusy || appBusy || calcBusy) { segLang.Selected = Array.IndexOf(L.Codes, L.Lang); return; }
                cfg.Lang = L.Codes[i];
                cfg.Save();
                L.Set(cfg.Lang);
                BeginInvoke((MethodInvoker)RebuildUi);
            };
            btnAbout = Btn(L.T("Hakkında", "About"), BtnKind.Ghost, delegate { using (var a = new AboutDialog()) a.ShowDialog(this); });
            btnAdmin = Btn(L.T("Yönetici olarak aç", "Run as administrator"), BtnKind.Secondary, delegate { RestartAsAdmin(); });
            btnAdmin.Visible = !Sys.IsAdmin;

            segDrive = Add(new Seg());
            segDrive.Changed += delegate (int i)
            {
                if (i < 0 || i >= drives.Count) return;
                cfg.Drive = drives[i].Name;
                cfg.Save();
                OnDriveChanged();
            };
            segTab = Add(new Seg());
            segTab.Items = new[] { L.T("Disk haritası", "Disk map"), L.T("Temizlik", "Clean up"), L.T("Uygulama verileri", "App data"), L.T("Programlar", "Programs") };
            segTab.Selected = tab;
            segTab.Changed += delegate (int i) { SetTab(i); };

            // --- Disk haritası
            addTo = 0;
            mapInfo = Add(new StatusBox());
            btnScan = Btn(L.T("Diski tara", "Scan disk"), BtnKind.Primary, delegate
            {
                if (scanner != null) { scanner.Stop(); btnScan.Enabled = false; }
                else StartScan();
            });
            btnUp = Btn(L.T("↑ Üst klasör", "↑ Up"), BtnKind.Secondary, delegate { GoUp(); });
            btnReveal = Btn(L.T("Explorer'da aç", "Open in Explorer"), BtnKind.Ghost, delegate
            {
                if (filesOf != null) Sys.Reveal(filesOf.Path);
                else if (curNode != null) Sys.Reveal(curNode.Path);
            });
            btnUp.Enabled = btnReveal.Enabled = false;   // tarama bitince açılır
            lblPath = Add(new Label());
            lblPath.ForeColor = Theme.Text;
            lblPath.BackColor = Color.Transparent;
            lblPath.AutoEllipsis = true;
            lblPath.Font = new Font(Font, FontStyle.Bold);
            bar = Add(new SpaceBar());
            secDirs = Section(L.T("Klasörler", "Folders"));
            secBig = Section(L.T("En büyük dosyalar", "Largest files"));
            lstDirs = List(L.T("\"Diski tara\"ya bas. Program sürücüdeki bütün klasörleri ölçer ve en çok yer kaplayanları en üstte gösterir. 500 GB'lık bir SSD birkaç dakika sürer; diske hiçbir şey yazılmaz.",
                               "Press \"Scan disk\". The app measures every folder on the drive and shows the biggest ones on top. A 500 GB SSD takes a few minutes; nothing is written to the disk."));
            lstDirs.Activated += EnterItem;
            lstDirs.BackRequested += GoUp;
            lstDirs.MenuRequested += delegate (ListItem it, Point pt) { MapMenu(lstDirs, it, pt); };
            lstBig = List(L.T("Taramadan sonra burada sürücüdeki en büyük 300 dosya listelenir (unutulmuş videolar, kurulum dosyaları, sanal diskler…).",
                              "After a scan, the 300 largest files on the drive are listed here (forgotten videos, installers, virtual disks…)."));
            lstBig.RightW = 100;
            lstBig.Activated += delegate (ListItem it) { var b = it.Tag as BigFile; if (b != null) Sys.Reveal(b.Path); };
            lstBig.MenuRequested += delegate (ListItem it, Point pt) { MapMenu(lstBig, it, pt); };

            // --- Temizlik
            addTo = 1;
            cleanInfo = Add(new StatusBox());
            lstRules = List(L.T("Hesaplanıyor…", "Measuring…"));
            lstRules.RightW = 200;
            lstRules.CheckChanged += delegate (ListItem it) { var r = it.Tag as Rule; if (r != null) ruleChecks[r.Title] = it.Checked; UpdateCleanInfo(); };
            lstRules.Activated += delegate (ListItem it) { if (!it.Locked) { it.Checked = !it.Checked; var r = it.Tag as Rule; if (r != null) ruleChecks[r.Title] = it.Checked; lstRules.Invalidate(); UpdateCleanInfo(); } };
            lstRules.MenuRequested += delegate (ListItem it, Point pt) { RuleMenu(it, pt); };
            btnCalc = Btn(L.T("↻ Yeniden hesapla", "↻ Measure again"), BtnKind.Secondary, delegate { Calc(); });
            btnClean = Btn(L.T("Seçilenleri temizle", "Clean selected"), BtnKind.Primary, delegate { DoClean(); });

            // --- Uygulama verileri
            addTo = 2;
            dataInfo = Add(new StatusBox());
            segData = Add(new Seg());
            segData.Items = new[] { L.T("Hepsi", "All"), L.T("90+ gündür değişmeyenler", "Unchanged for 90+ days") };
            segData.Selected = 0;
            segData.Changed += delegate { FillData(); };
            btnDataRefresh = Btn(L.T("↻ Yenile", "↻ Refresh"), BtnKind.Ghost, delegate { RefreshData(); });
            lstData = List(L.T("Ölçülüyor…", "Measuring…"));
            lstData.RightW = 110;
            lstData.SelectedChanged += delegate { UpdateDataButtons(); };
            lstData.Activated += delegate (ListItem it) { var f = it.Tag as DataFolder; if (f != null) Sys.Reveal(f.Path); };
            lstData.MenuRequested += delegate (ListItem it, Point pt)
            {
                var f = it.Tag as DataFolder;
                if (f == null) return;
                var m = DarkMenu.Make(Font);
                DarkMenu.Item(m, L.T("Explorer'da aç", "Open in Explorer"), true, false, delegate { Sys.Reveal(f.Path); });
                DarkMenu.Item(m, L.T("Yolu kopyala", "Copy path"), true, false, delegate { Copy(f.Path); });
                DarkMenu.Sep(m);
                DarkMenu.Item(m, L.T("Kalıcı olarak sil…", "Delete permanently…"), !f.Protected, true, delegate { DeleteData(); });
                m.Show(lstData, pt);
            };
            btnDataOpen = Btn(L.T("Explorer'da aç", "Open in Explorer"), BtnKind.Secondary, delegate { var f = SelData(); if (f != null) Sys.Reveal(f.Path); });
            btnDataDelete = Btn(L.T("Kalıcı olarak sil…", "Delete permanently…"), BtnKind.DangerGhost, delegate { DeleteData(); });

            // --- Programlar
            addTo = 3;
            appInfo = Add(new StatusBox());
            segApps = Add(new Seg());
            segApps.Items = new[] { L.T("Boyuta göre", "By size"), L.T("Son kullanıma göre", "By last use"), L.T("Ada göre", "By name") };
            segApps.Selected = 0;
            segApps.Changed += delegate { FillApps(); };
            btnAppRefresh = Btn(L.T("↻ Yenile", "↻ Refresh"), BtnKind.Ghost, delegate { RefreshApps(); });
            lstApps = List(L.T("Kurulu programlar okunuyor…", "Reading installed programs…"));
            lstApps.RightW = 150;
            lstApps.SelectedChanged += delegate { UpdateAppButtons(); };
            lstApps.MenuRequested += delegate (ListItem it, Point pt)
            {
                var a = it.Tag as InstalledApp;
                if (a == null) return;
                var m = DarkMenu.Make(Font);
                DarkMenu.Item(m, L.T("Konumu aç", "Open location"), a.Location != "" && Directory.Exists(a.Location), false, delegate { Sys.Reveal(a.Location); });
                DarkMenu.Sep(m);
                DarkMenu.Item(m, L.T("Kaldır…", "Uninstall…"), true, true, delegate { RemoveApp(); });
                m.Show(lstApps, pt);
            };
            btnAppOpen = Btn(L.T("Konumu aç", "Open location"), BtnKind.Secondary, delegate { var a = SelApp(); if (a != null) Sys.Reveal(a.Location); });
            btnAppRemove = Btn(L.T("Kaldır…", "Uninstall…"), BtnKind.Secondary, delegate { RemoveApp(); });

            addTo = -1;
            ResumeLayout();
            ApplyTabVisibility();
            DoLayout();
        }

        void RebuildUi()
        {
            SuspendLayout();
            var old = new List<Control>();
            foreach (Control c in page.Controls) old.Add(c);
            page.Controls.Clear();
            foreach (var c in old) c.Dispose();
            Text = AppInfo.Name + " " + AppInfo.Version + (Sys.IsAdmin ? L.T(" (yönetici)", " (administrator)") : "");
            BuildUi();
            RefreshDrives();
            // Metinler dile bağlı: listeler yeniden doldurulur
            if (root != null) { mapText = ""; ShowCurrent(); FillBig(); }
            UpdateMapInfo();
            if (rules != null) { rules = null; EnsureRules(); }
            if (dataFolders != null) FillData();
            if (apps != null) FillApps();
            SetTab(tab);
            ResumeLayout();
        }

        void ApplyTabVisibility()
        {
            for (int t = 0; t < tabCtl.Length; t++)
                foreach (var c in tabCtl[t]) c.Visible = t == tab;
        }

        void SetTab(int i)
        {
            tab = Math.Max(0, Math.Min(3, i));
            if (segTab.Selected != tab) segTab.Selected = tab;
            cfg.Tab = tab;
            cfg.Save();
            ApplyTabVisibility();
            DoLayout();
            if (tab == 0) UpdateMapInfo();
            if (tab == 1) EnsureRules();
            if (tab == 2 && dataFolders == null && !dataBusy) RefreshData();
            if (tab == 3 && apps == null && !appBusy) RefreshApps();
        }

        // ------------------------------------------------------------------ Yerleşim
        // logo, ad ve dil/Hakkında düğmeleri sabit başlıkta (sayfayla birlikte oynamaz)
        void SetupHeader()
        {
            header.Logo = logo.Image;
            header.Title = lblTitle.Text;
            header.Subtitle = lblSub.Text;
            header.Font = Font;
            logo.Visible = lblTitle.Visible = lblSub.Visible = false;
            header.ClearActions();
            header.AddAction(btnAbout, P(90));
            header.AddAction(segLang, P(96));
            header.AddAction(btnAdmin, P(190));
            header.Invalidate();
        }

        void DoLayout()
        {
            if (segTab == null || btnAppRemove == null) return;
            int W = page.ClientSize.Width, H = page.ClientSize.Height;
            // geniş ekranda içerik en fazla P(1280) genişlikte ve ortada; listeler yüksekliği doldurur
            int pad = P(24), w = Math.Min(P(1280), W - pad * 2), x = (W - w) / 2, y = pad, bottom = H - pad;

            if (segLang.Parent != header) SetupHeader();

            segDrive.SetBounds(x, y, w, P(40));
            y += P(48);
            segTab.SetBounds(x, y, w, P(40));
            y += P(54);

            if (tab == 0) LayoutMap(x, y, w, bottom);
            else if (tab == 1) LayoutClean(x, y, w, bottom);
            else if (tab == 2) LayoutData(x, y, w, bottom);
            else LayoutApps(x, y, w, bottom);
            page.Invalidate();
        }

        void LayoutMap(int x, int y, int w, int bottom)
        {
            mapInfo.SetBounds(x, y, w, mapInfo.Measure(w));
            y = mapInfo.Bottom + P(12);
            btnScan.SetBounds(x, y, P(170), P(38));
            btnUp.SetBounds(x + P(180), y, P(140), P(38));
            btnReveal.SetBounds(x + P(330), y, P(170), P(38));
            y += P(48);
            lblPath.SetBounds(x, y, w, P(22));
            y += P(28);
            bar.SetBounds(x, y, w, P(44));
            y += P(50);
            if (w >= P(960))
            {
                int gap = P(20), lw = (int)(w * 0.56f), rw = w - lw - gap;
                secDirs.Location = new Point(x, y);
                secBig.Location = new Point(x + lw + gap, y);
                y += P(22);
                lstDirs.SetBounds(x, y, lw, Math.Max(P(120), bottom - y));
                lstBig.SetBounds(x + lw + gap, y, rw, Math.Max(P(120), bottom - y));
            }
            else
            {
                int h = Math.Max(P(120), (bottom - y - P(56)) / 2);
                secDirs.Location = new Point(x, y);
                y += P(22);
                lstDirs.SetBounds(x, y, w, h);
                y += h + P(12);
                secBig.Location = new Point(x, y);
                y += P(22);
                lstBig.SetBounds(x, y, w, Math.Max(P(120), bottom - y));
            }
        }

        void LayoutClean(int x, int y, int w, int bottom)
        {
            cleanInfo.SetBounds(x, y, w, cleanInfo.Measure(w));
            y = cleanInfo.Bottom + P(12);
            lstRules.SetBounds(x, y, w, Math.Max(P(150), bottom - y - P(52)));
            btnCalc.SetBounds(x, bottom - P(40), P(200), P(40));
            btnClean.SetBounds(x + w - P(300), bottom - P(40), P(300), P(40));
        }

        void LayoutData(int x, int y, int w, int bottom)
        {
            dataInfo.SetBounds(x, y, w, dataInfo.Measure(w));
            y = dataInfo.Bottom + P(12);
            segData.SetBounds(x, y, Math.Min(P(440), w - P(130)), P(36));
            btnDataRefresh.SetBounds(x + w - P(120), y + P(2), P(120), P(32));
            y += P(46);
            lstData.SetBounds(x, y, w, Math.Max(P(150), bottom - y - P(52)));
            btnDataOpen.SetBounds(x, bottom - P(40), P(180), P(40));
            btnDataDelete.SetBounds(x + w - P(200), bottom - P(40), P(200), P(40));
        }

        void LayoutApps(int x, int y, int w, int bottom)
        {
            appInfo.SetBounds(x, y, w, appInfo.Measure(w));
            y = appInfo.Bottom + P(12);
            segApps.SetBounds(x, y, Math.Min(P(480), w - P(130)), P(36));
            btnAppRefresh.SetBounds(x + w - P(120), y + P(2), P(120), P(32));
            y += P(46);
            lstApps.SetBounds(x, y, w, Math.Max(P(150), bottom - y - P(52)));
            btnAppOpen.SetBounds(x, bottom - P(40), P(180), P(40));
            btnAppRemove.SetBounds(x + w - P(180), bottom - P(40), P(180), P(40));
        }

        void OnTick()
        {
            if (scanner != null)
            {
                mapInfo.Kind = StatusKind.Active;
                mapInfo.Text = string.Format(L.T("Taranıyor… {0} dosya, {1} · {2} sn\n{3}", "Scanning… {0} files, {1} · {2} s\n{3}"),
                    Fmt.Num(Interlocked.Read(ref scanner.FilesSeen)), Fmt.Size(Interlocked.Read(ref scanner.BytesSeen)),
                    (int)(DateTime.Now - scanStart).TotalSeconds, scanner.Current);
                if (mapInfo.Height != mapInfo.Measure(mapInfo.Width)) DoLayout();
                mapInfo.Invalidate();
                WinShell.Progress(Handle, 0, WinShell.Indeterminate);
            }
            if (dataBusy && dataInfo.Text != dataProgress)
            {
                dataInfo.Kind = StatusKind.Active;
                dataInfo.Text = dataProgress;
                if (dataInfo.Height != dataInfo.Measure(dataInfo.Width)) DoLayout();
                dataInfo.Invalidate();
            }
        }

        // ------------------------------------------------------------------ Sürücüler
        string CurDrive
        {
            get { return segDrive.Selected >= 0 && segDrive.Selected < drives.Count ? drives[segDrive.Selected].RootDirectory.FullName : Sys.SystemRoot; }
        }

        void RefreshDrives()
        {
            var list = new List<DriveInfo>();
            try
            {
                foreach (var d in DriveInfo.GetDrives())
                    try { if (d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)) list.Add(d); } catch { }
            }
            catch { }
            drives = list;
            var items = new List<string>();
            int sel = -1;
            for (int i = 0; i < list.Count; i++)
            {
                var d = list[i];
                long free = 0, total = 0;
                try { free = d.TotalFreeSpace; total = d.TotalSize; } catch { }
                items.Add(d.Name.TrimEnd('\\') + "   " + Fmt.Size(free) + L.T(" boş / ", " free / ") + Fmt.Size(total));
                string want = cfg.Drive != "" ? cfg.Drive : Sys.SystemRoot;
                if (d.Name.Equals(want, StringComparison.OrdinalIgnoreCase)) sel = i;
            }
            segDrive.Items = items.ToArray();
            segDrive.Selected = sel >= 0 ? sel : 0;
        }

        void OnDriveChanged()
        {
            if (root != null && !CurDrive.Equals(scannedDrive, StringComparison.OrdinalIgnoreCase))
            {
                root = curNode = filesOf = null;
                bigFiles.Clear();
                mapText = "";
                lstDirs.SetItems(new ListItem[0], false);
                lstBig.SetItems(new ListItem[0], false);
                bar.Segs.Clear();
                bar.Invalidate();
                lblPath.Text = "";
                btnScan.Text = L.T("Diski tara", "Scan disk");
            }
            UpdateMapInfo();
        }

        void RestartAsAdmin()
        {
            if (cleanBusy) return;
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, "--elevated");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
            }
            catch (Win32Exception) { return; }   // kullanıcı UAC'de "Hayır" dedi
            ExitRequested = true;
            if (scanner != null) scanner.Stop();
            Close();
        }

        static void Copy(string s)
        {
            try { Clipboard.SetText(s); } catch { }
        }

        // ------------------------------------------------------------------ Disk haritası
        void UpdateMapInfo()
        {
            if (scanner != null || mapInfo == null) return;
            if (mapText != "") { mapInfo.Text = mapText; }
            else
            {
                mapInfo.Kind = StatusKind.Idle;
                mapInfo.Text = string.Format(L.T("{0} henüz taranmadı. Tarama sürücüdeki bütün klasörleri ölçer; diske hiçbir şey yazmaz.{1}",
                                                 "{0} hasn't been scanned yet. The scan measures every folder on the drive and never writes anything.{1}"),
                    CurDrive.TrimEnd('\\'), Sys.IsAdmin ? "" : L.T(" Yönetici olarak açarsan Windows'un korunan klasörleri de ölçülür.", " Running as administrator also measures Windows' protected folders."));
            }
            DoLayout();
        }

        void StartScan()
        {
            if (scanner != null) return;
            var s = new DiskScanner();
            scanner = s;
            string drive = CurDrive;
            scanStart = DateTime.Now;
            root = curNode = filesOf = null;
            bigFiles.Clear();
            lstDirs.SetItems(new ListItem[0], false);
            lstBig.SetItems(new ListItem[0], false);
            bar.Segs.Clear();
            bar.Invalidate();
            lblPath.Text = drive;
            btnScan.Text = L.T("Durdur", "Stop");
            btnScan.Kind = BtnKind.Secondary;
            btnScan.Invalidate();
            btnUp.Enabled = btnReveal.Enabled = false;
            segDrive.Enabled = false;
            WinShell.KeepAwake(true);
            OnTick();
            DoLayout();
            var t = new Thread(delegate ()
            {
                DirNode r = null;
                try { r = s.Scan(drive); } catch { }
                try { BeginInvoke((MethodInvoker)delegate { OnScanEnd(s, r, drive); }); } catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        void OnScanEnd(DiskScanner s, DirNode r, string drive)
        {
            scanner = null;
            WinShell.KeepAwake(false);
            WinShell.Progress(Handle, 0, WinShell.NoProgress);
            if (ExitRequested) return;
            if (Form.ActiveForm != this) WinShell.Flash(Handle);
            btnScan.Enabled = true;
            btnScan.Kind = BtnKind.Primary;
            btnScan.Text = L.T("Yeniden tara", "Scan again");
            segDrive.Enabled = true;
            if (r == null)
            {
                mapInfo.Kind = StatusKind.Warn;
                mapText = mapInfo.Text = L.T("Tarama başarısız oldu.", "The scan failed.");
                DoLayout();
                return;
            }
            root = r;
            scannedDrive = drive;
            bigFiles = s.Top();
            mapText = Summary(s.Stopped, drive);
            mapInfo.Kind = s.Stopped ? StatusKind.Warn : StatusKind.Active;
            mapInfo.Text = mapText;
            ShowNode(root, null);
            FillBig();
            RefreshDrives();
            DoLayout();
            lstDirs.Focus();
        }

        string Summary(bool stopped, string drive)
        {
            var sb = new StringBuilder();
            if (stopped) sb.Append(L.T("Tarama yarıda durduruldu; boyutlar eksik. ", "The scan was stopped early; sizes are incomplete. "));
            long total = 0, free = 0;
            try { var di = new DriveInfo(drive); total = di.TotalSize; free = di.TotalFreeSpace; } catch { }
            long used = total - free;
            sb.AppendFormat(L.T("{0}  {1} disk, {2} dolu, {3} boş. Tarama {4} dosyada {5} buldu ({6} sn).",
                                "{0}  {1} disk, {2} used, {3} free. The scan found {5} in {4} files ({6} s)."),
                drive.TrimEnd('\\'), Fmt.Size(total), Fmt.Size(used), Fmt.Size(free), Fmt.Num(root.AllFiles), Fmt.Size(root.Size),
                (int)(DateTime.Now - scanStart).TotalSeconds);
            long gap = used - root.Size;
            if (!stopped && gap > (3L << 30))
            {
                sb.Append(" ");
                sb.AppendFormat(L.T("{0} hiçbir klasörde görünmedi: Windows'un korunan klasörleri ve sistem geri yükleme noktaları.",
                                    "{0} didn't show up in any folder: Windows' protected folders and System Restore points."), Fmt.Size(gap));
                sb.Append(Sys.IsAdmin ? L.T(" Geri yükleme noktalarının alanı Windows'un \"Sistem koruması\" ayarından küçültülebilir.", " The space for restore points can be reduced in Windows' \"System Protection\" settings.")
                                      : L.T(" Yönetici olarak taramak bunu azaltır.", " Scanning as administrator reduces this."));
            }
            sb.Append("\n");
            sb.Append(L.T("Bir klasöre girmek için çift tıkla, geri dönmek için Geri tuşu. Sağ tık: Explorer'da göster, sil.",
                          "Double-click a folder to open it, Backspace to go back. Right-click: show in Explorer, delete."));
            return sb.ToString();
        }

        void ShowCurrent()
        {
            if (filesOf != null) ShowFiles(filesOf);
            else if (curNode != null) ShowNode(curNode, null);
        }

        void ShowNode(DirNode n, DirNode selectChild)
        {
            curNode = n;
            filesOf = null;
            lblPath.Text = n.Path;
            btnUp.Enabled = n.Parent != null;
            btnReveal.Enabled = true;

            var kids = new List<DirNode>(n.Kids);
            kids.Sort((a, b) => b.Size.CompareTo(a.Size));
            var items = new List<KeyValuePair<long, ListItem>>();
            for (int i = 0; i < kids.Count && i < 2000; i++)
            {
                var k = kids[i];
                var it = new ListItem();
                it.Title = k.Name;
                it.Sub = string.Format(L.T("{0} dosya · son değişiklik {1}", "{0} files · changed {1}"), Fmt.Num(k.AllFiles), Fmt.Ago(k.Newest)) +
                         (k.Denied ? L.T(" · erişim yok", " · no access") : "");
                it.Size = Fmt.Size(k.Size);
                it.Right = n.Size > 0 ? Fmt.Pct(k.Size / (double)n.Size) : "";
                it.Bar = n.Size > 0 ? k.Size / (double)n.Size : 0;
                it.Dot = i < SpaceBar.Palette.Length ? SpaceBar.Palette[i] : Theme.Sub;
                if (k.Denied && k.Size == 0) it.TitleColor = Theme.Sub;
                it.Tag = k;
                items.Add(new KeyValuePair<long, ListItem>(k.Size, it));
            }
            if (n.OwnFiles > 0)
            {
                var it = new ListItem();
                it.Title = L.T("Bu klasördeki dosyalar", "Files in this folder");
                it.Sub = Fmt.Num(n.OwnFiles) + L.T(" dosya · görmek için çift tıkla", " files · double-click to see them");
                it.Size = Fmt.Size(n.Own);
                it.Right = n.Size > 0 ? Fmt.Pct(n.Own / (double)n.Size) : "";
                it.Bar = n.Size > 0 ? n.Own / (double)n.Size : 0;
                it.Dot = Theme.Sub;
                it.Tag = "files";
                items.Add(new KeyValuePair<long, ListItem>(n.Own, it));
            }
            items.Sort((a, b) => b.Key.CompareTo(a.Key));
            var only = new List<ListItem>();
            foreach (var p in items) only.Add(p.Value);
            lstDirs.Empty = L.T("Bu klasör boş.", "This folder is empty.");
            lstDirs.SetItems(only, false);
            if (selectChild != null) lstDirs.Select(selectChild);

            bar.Segs.Clear();
            long rest = n.Size;
            for (int i = 0; i < kids.Count && i < SpaceBar.Palette.Length; i++)
            {
                if (kids[i].Size <= 0) break;
                bar.Segs.Add(new BarSeg(kids[i].Name + " " + Fmt.Size(kids[i].Size), kids[i].Size, SpaceBar.Palette[i]));
                rest -= kids[i].Size;
            }
            if (rest > 0) bar.Segs.Add(new BarSeg(L.T("Diğer ", "Other ") + Fmt.Size(rest), rest, Theme.Sub));
            bar.Total = n.Size;
            bar.Invalidate();
        }

        // Bir klasörün kendi dosyaları diskten o an okunur (taramada sadece toplamları tutulur)
        void ShowFiles(DirNode n)
        {
            filesOf = n;
            lblPath.Text = n.Path + "  ·  " + L.T("dosyalar", "files");
            btnUp.Enabled = true;
            var files = new List<FsEntry>();
            foreach (var e in Native.List(n.Path)) if (!e.IsDir) files.Add(e);
            files.Sort((a, b) => b.Size.CompareTo(a.Size));
            long max = files.Count > 0 ? Math.Max(1, files[0].Size) : 1;
            var items = new List<ListItem>();
            foreach (var f in files)
            {
                var b = new BigFile();
                b.Path = Path.Combine(n.Path, f.Name);
                b.Size = f.Size;
                b.Written = f.Written;
                var it = new ListItem();
                it.Title = f.Name;
                it.Sub = L.T("değişiklik: ", "changed: ") + Fmt.Ago(f.Written);
                it.Size = Fmt.Size(f.Size);
                it.Right = n.Own > 0 ? Fmt.Pct(f.Size / (double)n.Own) : "";
                it.Bar = f.Size / (double)max;
                it.Dot = Theme.Sub;
                it.Tag = b;
                items.Add(it);
            }
            lstDirs.Empty = L.T("Bu klasörde dosya kalmadı.", "No files left in this folder.");
            lstDirs.SetItems(items, false);
        }

        void EnterItem(ListItem it)
        {
            var k = it.Tag as DirNode;
            if (k != null)
            {
                if (k.Kids.Count > 0) ShowNode(k, null);
                else if (k.OwnFiles > 0) { curNode = k; ShowFiles(k); }
                return;
            }
            if (it.Tag is string && curNode != null) { ShowFiles(curNode); return; }
            var b = it.Tag as BigFile;
            if (b != null) Sys.Reveal(b.Path);
        }

        void GoUp()
        {
            if (filesOf != null)
            {
                var n = filesOf;
                filesOf = null;
                if (n.Kids.Count > 0) ShowNode(n, null);
                else if (n.Parent != null) ShowNode(n.Parent, n);
                return;
            }
            if (curNode != null && curNode.Parent != null) ShowNode(curNode.Parent, curNode);
        }

        void FillBig()
        {
            long max = bigFiles.Count > 0 ? Math.Max(1, bigFiles[0].Size) : 1;
            var items = new List<ListItem>();
            foreach (var b in bigFiles)
            {
                var it = new ListItem();
                it.Title = Path.GetFileName(b.Path);
                it.Sub = Path.GetDirectoryName(b.Path);
                it.Size = Fmt.Size(b.Size);
                it.Right = Fmt.Ago(b.Written);
                it.Bar = b.Size / (double)max;
                it.Dot = SpaceBar.Palette[1];
                it.Tag = b;
                items.Add(it);
            }
            lstBig.SetItems(items, true);
        }

        static string PathOf(ListItem it)
        {
            var k = it.Tag as DirNode;
            if (k != null) return k.Path;
            var b = it.Tag as BigFile;
            if (b != null) return b.Path;
            return null;
        }

        void MapMenu(SpaceList src, ListItem it, Point pt)
        {
            string path = PathOf(it);
            if (path == null) return;
            var m = DarkMenu.Make(Font);
            if (it.Tag is DirNode) DarkMenu.Item(m, L.T("Aç", "Open"), true, false, delegate { EnterItem(it); });
            DarkMenu.Item(m, L.T("Explorer'da göster", "Show in Explorer"), true, false, delegate { Sys.Reveal(path); });
            DarkMenu.Item(m, L.T("Yolu kopyala", "Copy path"), true, false, delegate { Copy(path); });
            DarkMenu.Sep(m);
            bool can = Safety.Reason(path) == null;
            DarkMenu.Item(m, L.T("Geri Dönüşüm Kutusu'na gönder", "Move to Recycle Bin"), true, false, delegate { DeletePath(path, false); });
            DarkMenu.Item(m, L.T("Kalıcı olarak sil…", "Delete permanently…"), true, can, delegate { DeletePath(path, true); });
            m.Show(src, pt);
        }

        void DeletePath(string path, bool permanent)
        {
            string why = Safety.Reason(path);
            if (why != null)
            {
                ConfirmDialog.Info(this, L.T("Bu silinemez", "This can't be deleted"), path + "\n\n" + why);
                return;
            }
            long size = Native.PathSize(path);
            string body = path + "\n" + Fmt.Size(size) + "\n\n" +
                (permanent ? L.T("Kalıcı olarak silinir, geri getirilemez. Kullanımda olan dosyalar atlanır.", "It's deleted for good and can't be restored. Files in use are skipped.")
                           : L.T("Geri Dönüşüm Kutusu'na gider; yer açılması için daha sonra kutuyu boşaltman gerekir (Temizlik sekmesi).",
                                 "It goes to the Recycle Bin; to actually free the space, empty the bin later (Clean up tab)."));
            if (!ConfirmDialog.Ask(this, permanent ? L.T("Kalıcı olarak silinsin mi?", "Delete permanently?") : L.T("Geri Dönüşüm Kutusu'na gönderilsin mi?", "Move to the Recycle Bin?"),
                                   body, permanent ? L.T("SİL", "DELETE") : null, permanent ? L.T("Kalıcı sil", "Delete") : L.T("Gönder", "Move"), permanent))
                return;

            if (!permanent)
            {
                string err = null;
                Cursor = Cursors.WaitCursor;
                try { FileOps.Recycle(path); }
                catch (OperationCanceledException) { err = ""; }
                catch (Exception ex) { err = ex.Message; }
                Cursor = Cursors.Default;
                AfterDelete(path, err == null ? size : 0, 0, err, false);
                return;
            }
            mapInfo.Kind = StatusKind.Active;
            mapInfo.Text = L.T("Siliniyor… ", "Deleting… ") + path;
            DoLayout();
            Enabled = false;
            var t = new Thread(delegate ()
            {
                int failed = 0;
                long freed = 0;
                string err = null;
                try { freed = FileOps.Delete(path, ref failed); }
                catch (Exception ex) { err = ex.Message; }
                try { BeginInvoke((MethodInvoker)delegate { Enabled = true; AfterDelete(path, freed, failed, err, true); }); } catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        void AfterDelete(string path, long freed, int failed, string err, bool permanent)
        {
            bool gone = !Directory.Exists(path) && !File.Exists(path);
            if (root != null && (gone || freed > 0))
            {
                var node = DiskScanner.Find(root, path);
                if (node != null && node != root)
                {
                    if (gone)
                    {
                        node.Parent.Kids.Remove(node);
                        DiskScanner.Subtract(node.Parent, node.Size, node.AllFiles);
                    }
                    else DiskScanner.Subtract(node, freed, 0);
                }
                else if (node == null)
                {
                    var parent = DiskScanner.Find(root, Path.GetDirectoryName(path));
                    if (parent != null && gone)
                    {
                        parent.Own = Math.Max(0, parent.Own - freed);
                        parent.OwnFiles = Math.Max(0, parent.OwnFiles - 1);
                        DiskScanner.Subtract(parent, freed, 1);
                    }
                }
                bigFiles.RemoveAll(b => !File.Exists(b.Path));
                ShowCurrent();
                FillBig();
            }
            RefreshDrives();
            if (err == "") return;   // kullanıcı Windows penceresinde vazgeçti
            var sb = new StringBuilder();
            if (err != null) sb.Append(L.T("Silinemedi: ", "Couldn't delete: ") + err);
            else
            {
                sb.AppendFormat(permanent ? L.T("Silindi: {0} boşaldı.", "Deleted: {0} freed.") : L.T("Geri Dönüşüm Kutusu'na gönderildi ({0}). Yer, kutu boşaltılınca açılır.", "Moved to the Recycle Bin ({0}). The space is freed when the bin is emptied."), Fmt.Size(freed));
                if (failed > 0) sb.AppendFormat(L.T(" {0} dosya kullanımda olduğu için atlandı.", " {0} files were in use and skipped."), Fmt.Num(failed));
            }
            sb.Append("\n").Append(path);
            mapText = sb.ToString() + "\n\n" + (mapText.Contains("\n\n") ? mapText.Substring(mapText.IndexOf("\n\n") + 2) : mapText);
            mapInfo.Kind = err != null ? StatusKind.Warn : StatusKind.Active;
            mapInfo.Text = mapText;
            DoLayout();
        }

        // ------------------------------------------------------------------ Temizlik
        void EnsureRules()
        {
            if (rules != null) return;
            rules = Rules.All();
            Calc();
        }

        ListItem RuleItem(Rule r)
        {
            var it = new ListItem();
            it.Title = r.Title;
            it.Sub = r.Desc;
            it.Badge = Rule.RiskName(r.Risk);
            it.BadgeColor = it.Dot = r.Risk == Risk.Safe ? Theme.Ok : r.Risk == Risk.Minor ? Theme.Warn : Theme.Danger;
            it.Checkable = true;
            bool c;
            it.Checked = ruleChecks.TryGetValue(r.Title, out c) ? c : r.Risk == Risk.Safe;
            it.Tag = r;
            FillRuleItem(it, r);
            return it;
        }

        void FillRuleItem(ListItem it, Rule r)
        {
            if (r.Size < 0) { it.Size = "…"; it.Right = L.T("ölçülüyor", "measuring"); return; }
            it.Size = r.Size > 0 ? Fmt.Size(r.Size) : "0";
            string blocked = r.Size > 0 ? r.Blocked() : null;
            if (blocked != null) { it.Right = blocked; it.TitleColor = Theme.Text; }
            else if (r.CustomSize != null) it.Right = r.Size > 0 ? "" : L.T("boş / kapalı", "empty / off");
            else it.Right = r.Count > 0 ? Fmt.Num(r.Count) + L.T(" öğe", " items") : L.T("temiz", "clean");
            it.Bar = -1;
        }

        void Calc()
        {
            if (rules == null) { EnsureRules(); return; }
            if (cleanBusy || calcBusy) return;
            calcBusy = true;
            int gen = ++calcGen;
            foreach (var r in rules) r.Size = -1;
            var items = new List<ListItem>();
            foreach (var r in rules) items.Add(RuleItem(r));
            lstRules.SetItems(items, true);
            btnCalc.Enabled = btnClean.Enabled = false;
            UpdateCleanInfo();
            var snapshot = new List<Rule>(rules);
            var t = new Thread(delegate ()
            {
                // Her seçenek ayrı klasörlere bakar; paralel ölçmek toplam süreyi kısaltır
                var po = new System.Threading.Tasks.ParallelOptions();
                po.MaxDegreeOfParallelism = 4;
                System.Threading.Tasks.Parallel.ForEach(snapshot, po, (r, state) =>
                {
                    if (ExitRequested) { state.Stop(); return; }
                    long s = 0;
                    int c = 0;
                    try
                    {
                        if (r.CustomSize != null) s = r.CustomSize();
                        else
                        {
                            var targets = r.Targets();
                            c = targets.Count;
                            foreach (var p in targets) s += Native.PathSize(p);
                        }
                    }
                    catch { }
                    var rr = r;
                    long ss = s;
                    int cc = c;
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate
                        {
                            if (gen != calcGen) return;
                            rr.Size = ss;
                            rr.Count = cc;
                            foreach (var it in lstRules.Items) if (it.Tag == rr) FillRuleItem(it, rr);
                            lstRules.Invalidate();
                            UpdateCleanInfo();
                        });
                    }
                    catch { state.Stop(); }
                });
                if (ExitRequested) return;
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        calcBusy = false;
                        btnCalc.Enabled = true;
                        UpdateCleanInfo();
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        List<Rule> Chosen()
        {
            var r = new List<Rule>();
            foreach (var it in lstRules.Items)
            {
                var rule = it.Tag as Rule;
                if (rule != null && it.Checked && rule.Size > 0) r.Add(rule);
            }
            return r;
        }

        void UpdateCleanInfo()
        {
            if (cleanInfo == null || cleanBusy) return;
            long total = 0, all = 0;
            var chosen = Chosen();
            foreach (var r in chosen) total += r.Size;
            if (rules != null) foreach (var r in rules) if (r.Size > 0) all += r.Size;
            cleanInfo.Kind = calcBusy ? StatusKind.Active : StatusKind.Idle;
            cleanInfo.Text = (calcBusy ? L.T("Ölçülüyor… ", "Measuring… ") : "") +
                string.Format(L.T("Seçili: {0} temizlik, toplam {1} (hepsi seçilse {2}).", "Selected: {0} items, {1} in total ({2} if all were selected)."), chosen.Count, Fmt.Size(total), Fmt.Size(all)) + "\n" +
                L.T("Yeşiller güvenli ve seçili gelir. Sarılar küçük bir yan etkiyle gelir, kırmızılar kişisel dosyalarındır; onlar senin kararın. Kullanımda olan dosyalar atlanır. Sağ tık: konumu aç, neleri sileceğini gör.",
                    "Green items are safe and come selected. Yellow ones have a small side effect, red ones are your own files; those are your call. Files in use are skipped. Right-click: open the location, see what will be deleted.");
            btnClean.Text = L.T("Seçilenleri temizle", "Clean selected") + (total > 0 ? "  ·  " + Fmt.Size(total) : "");
            btnClean.Enabled = total > 0 && !calcBusy;
            btnClean.Invalidate();
            DoLayout();
        }

        void RuleMenu(ListItem it, Point pt)
        {
            var r = it.Tag as Rule;
            if (r == null) return;
            var m = DarkMenu.Make(Font);
            bool hasPath = r.OpenPath != null && Directory.Exists(r.OpenPath);
            DarkMenu.Item(m, L.T("Konumu aç", "Open location"), hasPath, false, delegate { Sys.Reveal(r.OpenPath); });
            DarkMenu.Item(m, L.T("Neleri silecek?", "What will be deleted?"), r.Targets != null, false, delegate
            {
                List<string> t;
                try { t = r.Targets(); } catch { t = new List<string>(); }
                var sb = new StringBuilder();
                for (int i = 0; i < t.Count && i < 25; i++) sb.AppendLine(t[i]);
                if (t.Count > 25) sb.AppendLine(string.Format(L.T("… ve {0} öğe daha", "… and {0} more"), t.Count - 25));
                if (t.Count == 0) sb.Append(L.T("Silinecek bir şey yok.", "Nothing to delete."));
                ConfirmDialog.Info(this, r.Title, sb.ToString().TrimEnd());
            });
            m.Show(lstRules, pt);
        }

        void DoClean()
        {
            var chosen = Chosen();
            if (chosen.Count == 0 || cleanBusy || calcBusy) return;
            var sb = new StringBuilder();
            bool careful = false;
            long total = 0;
            foreach (var r in chosen)
            {
                total += r.Size;
                string b = r.Blocked();
                sb.Append("• ").Append(r.Title).Append(" — ").Append(Fmt.Size(r.Size));
                if (b != null) sb.Append(L.T("  (atlanacak: ", "  (skipped: ")).Append(b).Append(")");
                else if (r.Risk != Risk.Safe) sb.Append("  (").Append(Rule.RiskName(r.Risk)).Append(")");
                sb.AppendLine();
                if (r.Risk == Risk.Careful) careful = true;
            }
            sb.AppendLine();
            sb.Append(L.T("Silinenler geri getirilemez.", "Deleted files can't be restored."));
            if (!ConfirmDialog.Ask(this, string.Format(L.T("{0} temizlensin mi?", "Clean {0}?"), Fmt.Size(total)), sb.ToString(),
                                   careful ? L.T("SİL", "DELETE") : null, L.T("Temizle", "Clean"), careful))
                return;

            cleanBusy = true;
            btnClean.Enabled = btnCalc.Enabled = false;
            cleanInfo.Kind = StatusKind.Active;
            cleanInfo.Text = L.T("Temizleniyor… Kullanımda olan dosyalar atlanıyor.", "Cleaning… Files in use are skipped.");
            DoLayout();
            WinShell.Progress(Handle, 0, WinShell.Indeterminate);
            var t = new Thread(delegate ()
            {
                var report = new StringBuilder();
                long freedAll = 0;
                foreach (var r in chosen)
                {
                    string b = r.Blocked();
                    if (b != null) { report.AppendLine("–  " + r.Title + ": " + L.T("atlandı, ", "skipped, ") + b); continue; }
                    long freed = 0;
                    int failed = 0;
                    try
                    {
                        if (r.CustomRun != null)
                        {
                            long before = r.CustomSize != null ? r.CustomSize() : 0;
                            r.CustomRun();
                            long after = r.CustomSize != null ? r.CustomSize() : 0;
                            freed = Math.Max(0, before - after);
                        }
                        else foreach (var p in r.Targets()) freed += FileOps.Delete(p, ref failed);
                    }
                    catch (Exception ex) { report.AppendLine("–  " + r.Title + ": " + ex.Message); continue; }
                    freedAll += freed;
                    report.Append("✓  " + r.Title + ": " + Fmt.Size(freed));
                    if (failed > 0) report.AppendFormat(L.T(" ({0} dosya kullanımda, atlandı)", " ({0} files in use, skipped)"), Fmt.Num(failed));
                    report.AppendLine();
                }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        cleanBusy = false;
                        WinShell.Progress(Handle, 0, WinShell.NoProgress);
                        RefreshDrives();
                        if (root != null)
                        {
                            mapText = L.T("Temizlikten sonra harita eskidi; güncel hali için yeniden tara.", "The map is out of date after the clean-up; scan again for the current state.") +
                                      "\n\n" + (mapText.Contains("\n\n") ? mapText.Substring(mapText.IndexOf("\n\n") + 2) : mapText);
                            mapInfo.Kind = StatusKind.Warn;
                            mapInfo.Text = mapText;
                        }
                        ConfirmDialog.Info(this, string.Format(L.T("{0} boşaldı", "{0} freed"), Fmt.Size(freedAll)), report.ToString().TrimEnd());
                        Calc();
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);   // powercfg için UAC penceresi açılabilsin
            t.Start();
        }

        // ------------------------------------------------------------------ Uygulama verileri
        DataFolder SelData() { return lstData.Selected == null ? null : lstData.Selected.Tag as DataFolder; }

        void UpdateDataButtons()
        {
            var f = SelData();
            btnDataOpen.Enabled = f != null;
            btnDataDelete.Enabled = f != null && !f.Protected && !dataBusy;
        }

        void RefreshData()
        {
            if (dataBusy) return;
            dataBusy = true;
            btnDataRefresh.Enabled = false;
            dataProgress = L.T("Uygulama verileri ölçülüyor…", "Measuring app data…");
            OnTick();
            DoLayout();
            var t = new Thread(delegate ()
            {
                List<DataFolder> list = null;
                try
                {
                    list = DataFolders.Scan(() => ExitRequested, delegate (int d, int n)
                    {
                        dataProgress = string.Format(L.T("Uygulama verileri ölçülüyor… {0} / {1}", "Measuring app data… {0} / {1}"), d, n);
                    });
                }
                catch { list = new List<DataFolder>(); }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        dataBusy = false;
                        dataFolders = list;
                        btnDataRefresh.Enabled = true;
                        FillData();
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        void FillData()
        {
            if (dataFolders == null || lstData == null) return;
            var src = new List<DataFolder>();
            long total = 0, oldSize = 0;
            int oldCount = 0;
            foreach (var f in dataFolders)
            {
                total += f.Size;
                bool old = !f.Protected && f.DaysUnused >= 90 && f.Size > 0;
                if (old) { oldCount++; oldSize += f.Size; }
                if (segData.Selected == 0 || old) src.Add(f);
            }
            src.Sort((a, b) => b.Size.CompareTo(a.Size));
            long max = src.Count > 0 ? Math.Max(1, src[0].Size) : 1;
            var items = new List<ListItem>();
            foreach (var f in src)
            {
                var it = new ListItem();
                it.Title = f.Name;
                it.Sub = f.Where + " · " + f.Path;
                it.Size = Fmt.Size(f.Size);
                it.Right = Fmt.Ago(f.Newest);
                it.Bar = f.Size / (double)max;
                double d = f.DaysUnused;
                it.Dot = f.Protected ? Theme.Sub : d >= 365 ? Theme.Danger : d >= 90 ? Theme.Warn : Theme.Ok;
                if (f.Protected) { it.Badge = "Windows"; it.BadgeColor = Theme.Sub; }
                else if (d >= 90 && f.Size > 0) { it.Badge = L.T("kullanılmıyor olabilir", "maybe unused"); it.BadgeColor = d >= 365 ? Theme.Danger : Theme.Warn; }
                it.Tag = f;
                items.Add(it);
            }
            lstData.Empty = segData.Selected == 1 ? L.T("90 günden uzun süredir değişmeyen bir klasör yok.", "No folder has been unchanged for more than 90 days.") : L.T("Klasör bulunamadı.", "No folders found.");
            lstData.SetItems(items, false);
            dataInfo.Kind = oldCount > 0 ? StatusKind.Warn : StatusKind.Idle;
            dataInfo.Text = string.Format(L.T("Programların AppData'daki ve kullanıcı klasöründeki verileri: {0} klasör, toplam {1}. {2} tanesi ({3}) 90 günden uzun süredir hiç değişmemiş; büyük ihtimalle kaldırdığın ya da artık kullanmadığın programlardan kalma.",
                                              "Programs' data in AppData and your home folder: {0} folders, {1} in total. {2} of them ({3}) haven't changed for more than 90 days; they most likely belong to programs you removed or no longer use."),
                                          dataFolders.Count, Fmt.Size(total), oldCount, Fmt.Size(oldSize)) + "\n" +
                            L.T("Silmeden önce adına bak, emin değilsen içine göz at. Gri olanlar Windows'a ait, silinemez.", "Check the name before deleting; if unsure, look inside. Grey ones belong to Windows and can't be deleted.");
            UpdateDataButtons();
            DoLayout();
        }

        void DeleteData()
        {
            var f = SelData();
            if (f == null) return;
            string why = f.Protected ? L.T("Windows'un ve Microsoft uygulamalarının verileri; silinirse Windows bozulabilir.", "Data of Windows and Microsoft apps; deleting it can break Windows.") : Safety.Reason(f.Path);
            if (why != null) { ConfirmDialog.Info(this, L.T("Bu silinemez", "This can't be deleted"), f.Path + "\n\n" + why); return; }
            string body = f.Path + "\n" + Fmt.Size(f.Size) + " · " + L.T("son değişiklik: ", "last changed: ") + Fmt.Ago(f.Newest) + "\n\n" +
                          L.T("Bu klasör bir programın ayarlarını, girişlerini ve önbelleğini tutar. Program hâlâ kuruluysa ayarları sıfırlanır; kaldırılmış bir programdan kaldıysa hiçbir şey olmaz. Geri getirilemez.",
                              "This folder holds a program's settings, logins and cache. If the program is still installed its settings are reset; if it's left over from a removed program, nothing happens. It can't be restored.");
            if (!ConfirmDialog.Ask(this, L.T("Uygulama verisi silinsin mi?", "Delete app data?"), body, L.T("SİL", "DELETE"), L.T("Kalıcı sil", "Delete"), true)) return;
            dataBusy = true;
            UpdateDataButtons();
            dataInfo.Kind = StatusKind.Active;
            dataInfo.Text = dataProgress = L.T("Siliniyor… ", "Deleting… ") + f.Path;
            DoLayout();
            var t = new Thread(delegate ()
            {
                int failed = 0;
                long freed = FileOps.Delete(f.Path, ref failed);
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        dataBusy = false;
                        if (!Directory.Exists(f.Path)) dataFolders.Remove(f);
                        else f.Size = Math.Max(0, f.Size - freed);
                        FillData();
                        RefreshDrives();
                        dataInfo.Kind = StatusKind.Active;
                        dataInfo.Text = string.Format(L.T("{0} silindi, {1} boşaldı.", "{0} deleted, {1} freed."), f.Name, Fmt.Size(freed)) +
                                        (failed > 0 ? string.Format(L.T(" {0} dosya kullanımda olduğu için atlandı; programı kapatıp tekrar dene.", " {0} files were in use and skipped; close the program and try again."), Fmt.Num(failed)) : "");
                        DoLayout();
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        // ------------------------------------------------------------------ Programlar
        InstalledApp SelApp() { return lstApps.Selected == null ? null : lstApps.Selected.Tag as InstalledApp; }

        void UpdateAppButtons()
        {
            var a = SelApp();
            btnAppOpen.Enabled = a != null && a.Location != "" && Directory.Exists(a.Location);
            btnAppRemove.Enabled = a != null;
        }

        void RefreshApps()
        {
            if (appBusy) return;
            appBusy = true;
            btnAppRefresh.Enabled = false;
            appInfo.Kind = StatusKind.Active;
            appInfo.Text = L.T("Kurulu programlar okunuyor…", "Reading installed programs…");
            DoLayout();
            var t = new Thread(delegate ()
            {
                List<InstalledApp> list;
                try { list = Programs.Load(); } catch { list = new List<InstalledApp>(); }
                try { BeginInvoke((MethodInvoker)delegate { apps = list; FillApps(); }); } catch { return; }
                try { Programs.FillDetails(list, () => ExitRequested); } catch { }
                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        appBusy = false;
                        btnAppRefresh.Enabled = true;
                        FillApps();
                    });
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        void FillApps()
        {
            if (apps == null || lstApps == null) return;
            var src = new List<InstalledApp>(apps);
            if (segApps.Selected == 0) src.Sort((a, b) => b.Size.CompareTo(a.Size));
            else if (segApps.Selected == 1)
                src.Sort((a, b) =>
                {
                    // Tarihi bilinmeyenler en sona
                    if (a.LastRun == DateTime.MinValue && b.LastRun == DateTime.MinValue) return b.Size.CompareTo(a.Size);
                    if (a.LastRun == DateTime.MinValue) return 1;
                    if (b.LastRun == DateTime.MinValue) return -1;
                    return a.LastRun.CompareTo(b.LastRun);
                });
            else src.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));

            long max = 1, total = 0, oldSize = 0;
            int oldCount = 0;
            foreach (var a in apps)
            {
                max = Math.Max(max, a.Size);
                total += a.Size;
                if (a.LastRun != DateTime.MinValue && (DateTime.Now - a.LastRun).TotalDays > 180) { oldCount++; oldSize += a.Size; }
            }
            var items = new List<ListItem>();
            foreach (var a in src)
            {
                var it = new ListItem();
                it.Title = a.Name;
                var parts = new List<string>();
                if (a.Publisher != "") parts.Add(a.Publisher);
                if (a.Version != "") parts.Add(a.Version);
                if (a.Location != "") parts.Add(a.Location);
                it.Sub = string.Join(" · ", parts.ToArray());
                it.Size = a.Size > 0 ? Fmt.Size(a.Size) : "-";
                it.Bar = a.Size / (double)max;
                double days = a.LastRun == DateTime.MinValue ? -1 : (DateTime.Now - a.LastRun).TotalDays;
                if (days >= 0) it.Right = L.T("son: ", "last: ") + Fmt.Ago(a.LastRun);
                else if (a.Installed != DateTime.MinValue) it.Right = L.T("kuruldu: ", "installed: ") + Fmt.Ago(a.Installed);
                else it.Right = "-";
                it.Dot = days < 0 ? Theme.Sub : days > 180 ? Theme.Warn : Theme.Ok;
                if (days > 180) { it.Badge = L.T("uzun süredir açılmadı", "not opened for long"); it.BadgeColor = Theme.Warn; }
                it.Tag = a;
                items.Add(it);
            }
            lstApps.SetItems(items, true);
            appInfo.Kind = appBusy ? StatusKind.Active : oldCount > 0 ? StatusKind.Warn : StatusKind.Idle;
            appInfo.Text = string.Format(L.T("{0} program, toplam {1}. {2} tanesi 6 aydan uzun süredir açılmamış ({3}).", "{0} programs, {1} in total. {2} haven't been opened for more than 6 months ({3})."),
                                         apps.Count, Fmt.Size(total), oldCount, Fmt.Size(oldSize)) +
                           (appBusy ? L.T(" Boyutlar ve son kullanım ölçülüyor…", " Measuring sizes and last use…") : "") + "\n" +
                           L.T("Son kullanım Windows'un kayıtlarından tahmin edilir; Başlat menüsünden ya da masaüstünden açılanlarda doğrudur.", "Last use is estimated from Windows' own records; it's accurate for programs opened from the Start menu or desktop.") +
                           (Sys.IsAdmin ? "" : L.T(" Yönetici olarak açarsan daha çok program için bulunur.", " Run as administrator to find it for more programs."));
            UpdateAppButtons();
            DoLayout();
        }

        void RemoveApp()
        {
            var a = SelApp();
            if (a == null) return;
            if (!ConfirmDialog.Ask(this, L.T("Kaldırılsın mı?", "Uninstall?"),
                    a.Name + (a.Size > 0 ? " · " + Fmt.Size(a.Size) : "") + "\n\n" +
                    L.T("Programın kendi kaldırma aracı açılır; adımları oradan takip et. Bitince \"Yenile\"ye bas. Programın AppData'da bıraktığı veriler kalırsa \"Uygulama verileri\" sekmesinde görünür.",
                        "The program's own uninstaller opens; follow its steps. Press \"Refresh\" when it's done. If it leaves data behind in AppData, it shows up on the \"App data\" tab."),
                    null, L.T("Kaldırmayı başlat", "Start uninstall"), false)) return;
            try { Programs.RunUninstall(a); }
            catch (Exception ex) { ConfirmDialog.Info(this, L.T("Kaldırma aracı açılamadı", "Couldn't start the uninstaller"), ex.Message + "\n\n" + a.Uninstall); }
        }
    }
}
