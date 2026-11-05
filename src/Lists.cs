using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BrokenSpaceFinder
{
    // ------------------------------------------------------------------ Liste satırı
    class ListItem
    {
        public string Title = "", Sub = "", Size = "", Right = "", Badge = "";
        public Color Dot = Theme.Accent, BadgeColor = Theme.Sub, TitleColor = Theme.Text;
        public double Bar = -1;            // 0..1: satırın arkasına çizilen boyut payı
        public bool Checkable, Checked;
        public bool Locked = false;   // işaretlenemez (gri kutu)
        public object Tag;
    }

    // ------------------------------------------------------------------ Kaydırılabilir boyut listesi
    class SpaceList : Ctl
    {
        public readonly List<ListItem> Items = new List<ListItem>();
        public string Empty = "";
        public int RightW = 120, SizeW = 92;
        int scroll, sel = -1, hot = -1;
        bool dragThumb;
        int dragY0, dragS0;

        public event Action<ListItem> Activated, SelectedChanged, CheckChanged;
        public event Action<ListItem, Point> MenuRequested;
        public event Action BackRequested;

        public SpaceList()
        {
            Cursor = Cursors.Default;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }

        static int P(float v) { return Theme.P(v); }
        int RowH { get { return P(50); } }
        int Top0 { get { return P(4); } }
        int ContentH { get { return Items.Count * RowH + Top0 * 2; } }
        int MaxScroll { get { return Math.Max(0, ContentH - Height); } }

        public ListItem Selected { get { return sel >= 0 && sel < Items.Count ? Items[sel] : null; } }

        public void SetItems(IEnumerable<ListItem> items, bool keepScroll)
        {
            Items.Clear();
            Items.AddRange(items);
            if (!keepScroll) scroll = 0;
            sel = -1;
            hot = -1;
            Clamp();
            Invalidate();
            Fire(SelectedChanged, null);
        }

        public void Select(object tag)
        {
            for (int i = 0; i < Items.Count; i++)
                if (Items[i].Tag == tag) { sel = i; EnsureVisible(i); Invalidate(); Fire(SelectedChanged, Items[i]); return; }
        }

        void Fire(Action<ListItem> h, ListItem it) { if (h != null) h(it); }

        void Clamp() { scroll = Math.Max(0, Math.Min(scroll, MaxScroll)); }

        void EnsureVisible(int i)
        {
            int y = Top0 + i * RowH;
            if (y < scroll) scroll = y - Top0;
            else if (y + RowH > scroll + Height) scroll = y + RowH - Height + Top0;
            Clamp();
        }

        int IndexAt(int y)
        {
            int v = y + scroll - Top0;
            if (v < 0) return -1;
            int i = v / RowH;
            return i < Items.Count ? i : -1;
        }

        RectangleF CheckRect(float y) { return new RectangleF(P(14), y + (RowH - P(18)) / 2f, P(18), P(18)); }

        void Thumb(out float ty, out float th)
        {
            float track = Height - P(8);
            th = Math.Max(P(30), track * Height / (float)Math.Max(1, ContentH));
            ty = P(4) + (track - th) * scroll / Math.Max(1, MaxScroll);
        }

        protected override void OnResize(EventArgs e) { Clamp(); base.OnResize(e); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            scroll -= e.Delta * RowH * 3 / 120;
            Clamp();
            hot = IndexAt(e.Y);
            Invalidate();
            base.OnMouseWheel(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.XButton1) { if (BackRequested != null) BackRequested(); return; }
            if (e.Button == MouseButtons.Left && MaxScroll > 0 && e.X >= Width - P(14))
            {
                dragThumb = true;
                dragY0 = e.Y;
                dragS0 = scroll;
                return;
            }
            int i = IndexAt(e.Y);
            if (i < 0) return;
            var it = Items[i];
            if (e.Button == MouseButtons.Left && it.Checkable && !it.Locked && CheckRect(Top0 + i * RowH - scroll).Contains(e.Location))
            {
                it.Checked = !it.Checked;
                Fire(CheckChanged, it);
            }
            if (sel != i) { sel = i; Fire(SelectedChanged, it); }
            Invalidate();
            if (e.Button == MouseButtons.Right && MenuRequested != null) MenuRequested(it, e.Location);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragThumb)
            {
                float ty, th;
                Thumb(out ty, out th);
                float track = Height - P(8) - th;
                scroll = dragS0 + (int)((e.Y - dragY0) * MaxScroll / Math.Max(1f, track));
                Clamp();
                Invalidate();
                return;
            }
            int h = IndexAt(e.Y);
            if (h != hot) { hot = h; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragThumb = false; }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int i = IndexAt(e.Y);
            if (e.Button == MouseButtons.Left && i >= 0 && !(Items[i].Checkable && CheckRect(Top0 + i * RowH - scroll).Contains(e.Location)))
                Fire(Activated, Items[i]);
        }

        protected override bool IsInputKey(Keys k)
        {
            return k == Keys.Up || k == Keys.Down || k == Keys.PageUp || k == Keys.PageDown || k == Keys.Home || k == Keys.End ||
                   k == Keys.Enter || k == Keys.Back || k == Keys.Space || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Back) { if (BackRequested != null) BackRequested(); return; }
            if (Items.Count == 0) return;
            int page = Math.Max(1, Height / RowH - 1), n = sel;
            switch (e.KeyCode)
            {
                case Keys.Up: n = Math.Max(0, sel - 1); break;
                case Keys.Down: n = Math.Min(Items.Count - 1, sel + 1); break;
                case Keys.PageUp: n = Math.Max(0, sel - page); break;
                case Keys.PageDown: n = Math.Min(Items.Count - 1, sel + page); break;
                case Keys.Home: n = 0; break;
                case Keys.End: n = Items.Count - 1; break;
                case Keys.Enter: if (Selected != null) Fire(Activated, Selected); return;
                case Keys.Space:
                    if (Selected != null && Selected.Checkable && !Selected.Locked) { Selected.Checked = !Selected.Checked; Fire(CheckChanged, Selected); Invalidate(); }
                    return;
                case Keys.Apps:
                    if (Selected != null && MenuRequested != null) MenuRequested(Selected, new Point(P(40), Top0 + sel * RowH - scroll + RowH / 2));
                    return;
                default: return;
            }
            if (n < 0) n = 0;
            if (n != sel) { sel = n; EnsureVisible(n); Invalidate(); Fire(SelectedChanged, Items[n]); }
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, BackColor);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.Fill(g, r, P(10), Theme.Card);
            Theme.Stroke(g, r, P(10), Focused ? Theme.Mix(Theme.Border, Theme.Accent, 0.35f) : Theme.Border, 1);
            if (Items.Count == 0)
            {
                Theme.Draw(g, Empty, Font, new RectangleF(P(18), P(14), Width - P(36), Height - P(28)), Theme.Sub, StringAlignment.Near, StringAlignment.Near, true);
                return;
            }
            using (var clip = Theme.Round(new RectangleF(1, 1, Width - 2, Height - 2), P(10))) g.SetClip(clip);
            using (var small = new Font(Font.FontFamily, Font.Size - 1f))
            using (var bold = new Font(Font, FontStyle.Bold))
            using (var tiny = new Font(Font.FontFamily, Font.Size - 1.5f, FontStyle.Bold))
            {
                int first = Math.Max(0, (scroll - Top0) / RowH), last = Math.Min(Items.Count - 1, (scroll + Height) / RowH + 1);
                float rowW = Width - P(18);
                for (int i = first; i <= last; i++)
                {
                    var it = Items[i];
                    float y = Top0 + i * RowH - scroll;
                    var rr = new RectangleF(P(4), y + P(1), rowW, RowH - P(2));
                    if (i == sel) Theme.Fill(g, rr, P(8), Theme.Mix(Theme.Card, Theme.Accent, Focused ? 0.16f : 0.10f));
                    else if (i == hot) Theme.Fill(g, rr, P(8), Theme.Field);
                    if (it.Bar > 0)
                        Theme.Fill(g, new RectangleF(rr.X, rr.Y, Math.Max(P(4), (float)(rr.Width * Math.Min(1, it.Bar))), rr.Height), P(8), Color.FromArgb(34, it.Dot));

                    float x = P(14);
                    if (it.Checkable)
                    {
                        var cr = CheckRect(y);
                        Color cc = it.Locked ? Theme.Dim(Theme.Sub) : Theme.Accent;
                        if (it.Checked)
                        {
                            Theme.Fill(g, cr, P(4), cc);
                            using (var pen = new Pen(Theme.OnAccent, P(2)))
                                g.DrawLines(pen, new[] { new PointF(cr.X + cr.Width * 0.24f, cr.Y + cr.Height * 0.52f), new PointF(cr.X + cr.Width * 0.43f, cr.Y + cr.Height * 0.72f), new PointF(cr.X + cr.Width * 0.78f, cr.Y + cr.Height * 0.3f) });
                        }
                        else
                        {
                            Theme.Fill(g, cr, P(4), Theme.Field);
                            Theme.Stroke(g, cr, P(4), it.Locked ? Theme.Border : Theme.Sub, 1);
                        }
                        x += P(30);
                    }
                    float d = P(10);
                    using (var b = new SolidBrush(it.Dot)) g.FillEllipse(b, x, y + (RowH - d) / 2f, d, d);
                    x += P(22);

                    float right = Width - P(18);
                    float rw = P(RightW), sw = P(SizeW);
                    Theme.Draw(g, it.Right, small, new RectangleF(right - rw, y, rw - P(6), RowH), Theme.Sub, StringAlignment.Far, StringAlignment.Center, false);
                    Theme.Draw(g, it.Size, bold, new RectangleF(right - rw - sw, y, sw, RowH), it.Size == "" ? Theme.Sub : Theme.Text, StringAlignment.Far, StringAlignment.Center, false);
                    float textR = right - rw - sw - P(10);
                    if (it.Badge != "")
                    {
                        var bs = g.MeasureString(it.Badge, tiny);
                        float bw = bs.Width + P(12), bh = bs.Height + P(3);
                        var br = new RectangleF(textR - bw, y + (RowH - bh) / 2f, bw, bh);
                        Theme.Fill(g, br, bh / 2, Theme.Mix(it.BadgeColor, Theme.Card, 0.78f));
                        Theme.Draw(g, it.Badge, tiny, br, it.BadgeColor, StringAlignment.Center, StringAlignment.Center, false);
                        textR = br.X - P(10);
                    }
                    Theme.Draw(g, it.Title, bold, new RectangleF(x, y + P(6), Math.Max(10, textR - x), P(22)), it.TitleColor, StringAlignment.Near, StringAlignment.Center, false);
                    Theme.Draw(g, it.Sub, small, new RectangleF(x, y + P(27), Math.Max(10, textR - x), P(18)), Theme.Sub, StringAlignment.Near, StringAlignment.Center, false);
                }
            }
            g.ResetClip();
            if (MaxScroll > 0)
            {
                float ty, th;
                Thumb(out ty, out th);
                Theme.Fill(g, new RectangleF(Width - P(10), ty, P(5), th), P(3), dragThumb ? Theme.Sub : Theme.Border);
            }
        }
    }

    // ------------------------------------------------------------------ Doluluk çubuğu
    class BarSeg
    {
        public string Label;
        public long Value;
        public Color Color;
        public BarSeg(string label, long value, Color color) { Label = label; Value = value; Color = color; }
    }

    class SpaceBar : Ctl
    {
        public readonly List<BarSeg> Segs = new List<BarSeg>();
        public long Total;

        public static readonly Color[] Palette =
        {
            Theme.Accent, Color.FromArgb(88, 166, 255), Color.FromArgb(178, 132, 255), Theme.Warn,
            Color.FromArgb(255, 120, 170), Theme.Ok, Color.FromArgb(110, 200, 230),
        };

        public SpaceBar() { Cursor = Cursors.Default; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Theme.Prep(e, BackColor);
            float bh = Theme.P(14);
            var br = new RectangleF(0.5f, 0.5f, Width - 1.5f, bh);
            Theme.Fill(g, br, bh / 2, Theme.Field);
            if (Total > 0)
            {
                using (var clip = Theme.Round(br, bh / 2))
                {
                    g.SetClip(clip);
                    float x = br.X;
                    foreach (var s in Segs)
                    {
                        float w = (float)(br.Width * s.Value / (double)Total);
                        if (w <= 0) continue;
                        using (var b = new SolidBrush(s.Color)) g.FillRectangle(b, x, br.Y, Math.Max(1.5f, w), bh);
                        x += w;
                        using (var p = new Pen(Theme.Bg, 1.5f)) g.DrawLine(p, x, br.Y, x, br.Bottom);
                    }
                    g.ResetClip();
                }
            }
            using (var small = new Font(Font.FontFamily, Font.Size - 1f))
            {
                float lx = 0, ly = bh + Theme.P(9), d = Theme.P(9);
                foreach (var s in Segs)
                {
                    string t = s.Label;
                    float tw = g.MeasureString(t, small).Width;
                    if (lx + d + Theme.P(6) + tw > Width) break;
                    using (var b = new SolidBrush(s.Color)) g.FillEllipse(b, lx, ly + Theme.P(4), d, d);
                    Theme.Draw(g, t, small, new RectangleF(lx + d + Theme.P(5), ly, tw + Theme.P(4), Theme.P(18)), Theme.Sub, StringAlignment.Near, StringAlignment.Center, false);
                    lx += d + Theme.P(5) + tw + Theme.P(16);
                }
            }
        }
    }

    // ------------------------------------------------------------------ Koyu sağ tık menüsü
    class DarkColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
        public override Color MenuBorder { get { return Theme.Border; } }
        public override Color MenuItemBorder { get { return Theme.Border; } }
        public override Color MenuItemSelected { get { return Theme.Field; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Field; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Field; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
        public override Color SeparatorDark { get { return Theme.Border; } }
        public override Color SeparatorLight { get { return Theme.Card; } }
    }

    static class DarkMenu
    {
        public static ContextMenuStrip Make(Font font)
        {
            var m = new ContextMenuStrip();
            m.Renderer = new ToolStripProfessionalRenderer(new DarkColors());
            m.BackColor = Theme.Card;
            m.ForeColor = Theme.Text;
            m.ShowImageMargin = false;
            m.Font = font;
            return m;
        }

        public static void Item(ContextMenuStrip m, string text, bool enabled, bool danger, EventHandler click)
        {
            var i = new ToolStripMenuItem(text);
            i.ForeColor = !enabled ? Theme.Dim(Theme.Sub) : danger ? Theme.Danger : Theme.Text;
            i.Enabled = enabled;
            i.Padding = new Padding(Theme.P(6), Theme.P(3), Theme.P(6), Theme.P(3));
            i.Click += click;
            m.Items.Add(i);
        }

        public static void Sep(ContextMenuStrip m) { m.Items.Add(new ToolStripSeparator()); }
    }
}
