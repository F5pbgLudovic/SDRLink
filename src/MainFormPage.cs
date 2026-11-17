using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace SDRLink
{
    // ---------------------------------------------------------------- Sur la page WebSDR (1.43)
    // Trois ronds posés sur la page (partage, +, −), comme dans PhoneSDRLink : clic gauche = action, clic droit = position
    // (quatre coins, milieu haut, milieu bas ; en ligne ou en colonne). + et − règlent le zoom de la page, retenu par WebSDR.
    // Sans zoom retenu, une page OpenWebRX dont le panneau de commande déborde sous le bandeau du haut est réduite juste ce
    // qu'il faut pour le voir en entier. Clic droit sur la page : masquer l'adresse du WebSDR (étoiles dans la case).
    public partial class MainForm
    {
        PageRoundButton btnShare, btnZoomIn, btnZoomOut;
        Control pageHost;
        bool urlMaskHooked;
        CoreWebView2Environment webEnv;
        System.Windows.Forms.Timer fitTimer;
        int fitPasses;
        bool pageZoomNav, pageZoomSetting;
        string zoomHost = "";

        static readonly double[] ZoomSteps = { 0.25, 0.33, 0.5, 0.67, 0.75, 0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0 };
        public const double ZoomMin = 0.25, ZoomMax = 3.0, FitMin = 0.5;

        void CreatePageButtons(Control host)
        {
            pageHost = host;
            btnShare = new PageRoundButton(PageRoundButton.Glyph.Share);
            btnZoomIn = new PageRoundButton(PageRoundButton.Glyph.Plus);
            btnZoomOut = new PageRoundButton(PageRoundButton.Glyph.Minus);
            btnShare.Click += delegate { ShareFrequency(); };
            btnZoomIn.Click += delegate { StepPageZoom(+1); };
            btnZoomOut.Click += delegate { StepPageZoom(-1); };
            foreach (PageRoundButton b in PageButtons())
            {
                ContextMenuStrip m = new ContextMenuStrip();
                m.Opening += delegate(object s, System.ComponentModel.CancelEventArgs e) { e.Cancel = false; FillPageButtonsMenu((ContextMenuStrip)s); };
                m.Items.Add("-");
                b.ContextMenuStrip = m;
                host.Controls.Add(b);
            }
            Tr(delegate
            {
                string right = Lang.T(" (clic droit : position des boutons)", " (right-click: position of the buttons)");
                tips.SetToolTip(btnShare, Lang.T("Partager la fréquence : copie dans le presse-papiers un lien vers ce WebSDR, sur cette fréquence et ce mode", "Share the frequency: copies to the clipboard a link to this WebSDR, on this frequency and mode") + right);
                tips.SetToolTip(btnZoomIn, Lang.T("Agrandir la page du WebSDR", "Zoom in the WebSDR page") + right);
                tips.SetToolTip(btnZoomOut, Lang.T("Réduire la page du WebSDR", "Zoom out the WebSDR page") + right);
            });
            host.Resize += delegate { PlacePageButtons(); if (FitAllowed()) StartFit(1, 800); };
            PlacePageButtons();
        }

        PageRoundButton[] PageButtons() { return new PageRoundButton[] { btnShare, btnZoomIn, btnZoomOut }; }

        // Position : place 0 bas à droite, 1 bas à gauche, 2 haut à droite, 3 haut à gauche, 4 milieu haut, 5 milieu bas ;
        // + PageBtnHorizontal : en ligne (sinon en colonne).
        public const int PageBtnHorizontal = 8, PageBtnPlaces = 6, PageBtnDefault = 5 + PageBtnHorizontal;

        void PlacePageButtons()
        {
            if (pageHost == null || btnShare == null) return;
            float k;
            using (Graphics g = CreateGraphics()) k = g.DpiX / 96f;
            int d = (int)Math.Round(38 * k), gap = (int)Math.Round(6 * k), m = (int)Math.Round(10 * k);
            int pos = settings.PageBtnPos, place = pos & 7;
            bool horiz = (pos & PageBtnHorizontal) != 0;
            PageRoundButton[] bs = PageButtons();
            int n = bs.Length, w = horiz ? n * d + (n - 1) * gap : d, h = horiz ? d : n * d + (n - 1) * gap;
            int W = pageHost.ClientSize.Width, H = pageHost.ClientSize.Height;
            int x = place == 1 || place == 3 ? m : place >= 4 ? (W - w) / 2 : W - w - m - SystemInformation.VerticalScrollBarWidth;
            int y = place == 2 || place == 3 || place == 4 ? m : H - h - m;
            for (int i = 0; i < n; i++)
            {
                bs[i].SetBounds(x + (horiz ? i * (d + gap) : 0), y + (horiz ? 0 : i * (d + gap)), d, d);
                bs[i].BringToFront();
            }
        }

        static string PlaceName(int place)
        {
            switch (place)
            {
                case 0: return Lang.T("Bas à droite", "Bottom right");
                case 1: return Lang.T("Bas à gauche", "Bottom left");
                case 2: return Lang.T("Haut à droite", "Top right");
                case 3: return Lang.T("Haut à gauche", "Top left");
                case 4: return Lang.T("Milieu haut", "Top centre");
                default: return Lang.T("Milieu bas", "Bottom centre");
            }
        }

        void FillPageButtonsMenu(ContextMenuStrip m)
        {
            m.Items.Clear();
            ToolStripMenuItem title = new ToolStripMenuItem(Lang.T("Position des boutons :", "Position of the buttons:"));
            title.Enabled = false;
            m.Items.Add(title);
            foreach (bool horiz in new bool[] { false, true })
                for (int place = 0; place < PageBtnPlaces; place++)
                {
                    int pos = place + (horiz ? PageBtnHorizontal : 0);
                    string text = PlaceName(place) + " — " + (horiz ? Lang.T("horizontal", "horizontal") : Lang.T("vertical", "vertical"));
                    if (pos == PageBtnDefault) text += " " + Lang.T("(position d'origine)", "(original position)");
                    ToolStripMenuItem it = new ToolStripMenuItem(text);
                    it.Checked = settings.PageBtnPos == pos;
                    it.Click += delegate { settings.PageBtnPos = pos; settings.Save(); PlacePageButtons(); };
                    m.Items.Add(it);
                }
            m.Items.Add(new ToolStripSeparator());
            double stored;
            bool manual = zoomHost != "" && settings.PageZooms.TryGetValue(zoomHost, out stored);
            ToolStripMenuItem auto = new ToolStripMenuItem(Lang.F("Zoom de la page : automatique (maintenant {0})", "Page zoom: automatic (now {0})", ZoomText(web.ZoomFactor)));
            auto.Checked = !manual;
            auto.Enabled = zoomHost != "";
            auto.Click += delegate { ResetPageZoom(); };
            m.Items.Add(auto);
            m.Items.Add(HideUrlMenuItem());
        }

        ToolStripMenuItem HideUrlMenuItem()
        {
            ToolStripMenuItem it = new ToolStripMenuItem(Lang.T("Masquer l'adresse du WebSDR", "Hide the WebSDR address"));
            it.Checked = settings.HideUrl;
            it.Click += delegate { SetHideUrl(!settings.HideUrl); };
            return it;
        }

        static string ZoomText(double z)
        {
            return Math.Round(z * 100).ToString(CultureInfo.InvariantCulture) + " %";
        }

        // ---------------------------------------------------------------- Zoom de la page

        void InitPageTools(CoreWebView2Environment env, CoreWebView2 core)
        {
            webEnv = env;
            fitTimer = new System.Windows.Forms.Timer();
            fitTimer.Tick += delegate { fitTimer.Stop(); FitPage(); };
            core.NavigationStarting += delegate(object s, CoreWebView2NavigationStartingEventArgs e) { if (!e.IsRedirected) pageZoomNav = true; };
            core.ContentLoading += delegate { ApplyHostZoom(); };
            core.NavigationCompleted += delegate { ApplyHostZoom(); pageZoomNav = false; };
            web.ZoomFactorChanged += delegate
            {
                // Ctrl + molette dans la page : zoom choisi à la main, retenu pour ce WebSDR comme avec + et −.
                if (pageZoomSetting || pageZoomNav || zoomHost == "") return;
                double z = Math.Max(ZoomMin, Math.Min(ZoomMax, web.ZoomFactor));
                double old;
                if (settings.PageZooms.TryGetValue(zoomHost, out old) && Math.Abs(old - z) < 0.005) return;
                settings.PageZooms[zoomHost] = z;
                settings.Save();
                ShowStatus(Lang.F("Page du WebSDR : {0}", "WebSDR page: {0}", ZoomText(z)));
            };
            core.ContextMenuRequested += OnPageContextMenu;
        }

        static string HostKey(string url)
        {
            Uri u;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out u)) return "";
            if (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps) return "";
            return u.Authority.ToLowerInvariant();
        }

        void SetPageZoom(double z)
        {
            z = Math.Max(ZoomMin, Math.Min(ZoomMax, z));
            if (Math.Abs(web.ZoomFactor - z) < 0.001) return;
            pageZoomSetting = true;
            try { web.ZoomFactor = z; }
            finally { pageZoomSetting = false; }
        }

        // Nouvelle page : le zoom retenu pour ce WebSDR, sinon 100 % (puis l'ajustement automatique d'OpenWebRX).
        void ApplyHostZoom()
        {
            if (web.CoreWebView2 == null) return;
            string host = HostKey(web.CoreWebView2.Source);
            bool changed = host != zoomHost;
            zoomHost = host;
            double z;
            if (host != "" && settings.PageZooms.TryGetValue(host, out z)) SetPageZoom(z);
            else if (changed || pageZoomNav) SetPageZoom(1.0);
        }

        void StepPageZoom(int dir)
        {
            if (web.CoreWebView2 == null || zoomHost == "") return;
            double z = web.ZoomFactor, next = z;
            if (dir > 0) { foreach (double s in ZoomSteps) if (s > z + 0.005) { next = s; break; } }
            else for (int i = ZoomSteps.Length - 1; i >= 0; i--) if (ZoomSteps[i] < z - 0.005) { next = ZoomSteps[i]; break; }
            SetPageZoom(next);
            settings.PageZooms[zoomHost] = web.ZoomFactor;
            settings.Save();
            fitTimer.Stop();
            ShowStatus(Lang.F("Page du WebSDR : {0} (retenu pour ce WebSDR ; clic droit sur un bouton : zoom automatique)", "WebSDR page: {0} (kept for this WebSDR; right-click a button: automatic zoom)", ZoomText(web.ZoomFactor)));
        }

        void ResetPageZoom()
        {
            if (zoomHost == "") return;
            settings.PageZooms.Remove(zoomHost);
            settings.Save();
            SetPageZoom(1.0);
            ShowStatus(Lang.T("Zoom de la page : automatique", "Page zoom: automatic"));
            if (FitAllowed()) StartFit(2, 300);
        }

        // Ajustement automatique : seulement OpenWebRX (son panneau de commande est calé en bas et passe sous le bandeau du
        // haut quand la fenêtre est trop basse), et seulement sans zoom choisi à la main pour ce WebSDR.
        bool FitAllowed()
        {
            return sdrKind.StartsWith("owrx", StringComparison.Ordinal) && zoomHost != "" && !settings.PageZooms.ContainsKey(zoomHost) && web.CoreWebView2 != null;
        }

        void StartFit(int passes, int delayMs)
        {
            if (fitTimer == null) return;
            fitPasses = passes;
            fitTimer.Stop();
            fitTimer.Interval = delayMs;
            fitTimer.Start();
        }

        const string FitScript = "(function(){var p=document.getElementById('openwebrx-panel-receiver'),c=document.getElementById('webrx-canvas-container');" +
            "if(!p||!c||!p.offsetParent)return '';var r=p.getBoundingClientRect();" +
            "return Math.round(c.getBoundingClientRect().top)+'|'+Math.round(r.height)+'|'+window.innerHeight;})()";

        async void FitPage()
        {
            if (!FitAllowed()) return;
            string host = zoomHost, r;
            try { r = await web.CoreWebView2.ExecuteScriptAsync(FitScript); }
            catch { return; }
            if (!FitAllowed() || host != zoomHost) return;
            string[] p = (r ?? "").Trim('"').Split('|');
            int top, h, inner;
            if (p.Length == 3 && int.TryParse(p[0], out top) && int.TryParse(p[1], out h) && int.TryParse(p[2], out inner) && h > 100 && inner > 100)
            {
                double z = web.ZoomFactor, fit = FitZoom(z, top, h, inner);
                if (Math.Abs(fit - z) >= 0.02)
                {
                    SetPageZoom(fit);
                    Log.Write("Page OpenWebRX ajustée : " + ZoomText(fit) + " (bandeau " + top + ", panneau " + h + ", hauteur " + inner + ")");
                    if (fit < z) ShowStatus(Lang.F("Page du WebSDR réduite à {0} pour voir tout son panneau de commande (+ et − pour choisir)", "WebSDR page reduced to {0} to show its whole control panel (+ and − to choose)", ZoomText(fit)));
                }
            }
            if (--fitPasses > 0) StartFit(fitPasses, 2500);
        }

        // Zoom qui loge le panneau (hauteur h) sous le bandeau (bas à top), dans la hauteur visible inner (pixels de la page
        // au zoom z) : jamais au-dessus de 100 %, jamais sous FitMin.
        public static double FitZoom(double z, int top, int h, int inner)
        {
            double fit = z * inner / (top + h + 12.0);
            fit = Math.Floor(fit * 100) / 100;
            return Math.Max(FitMin, Math.Min(1.0, fit));
        }

        // ---------------------------------------------------------------- Partage

        // Lien de partage de PhoneSDRLink (page GitHub Pages) : l'appli Android l'ouvre, un PC propose d'écouter dans le navigateur.
        public const string ShareBase = "https://f5pbgludovic.github.io/PhoneSDRLink/s/";

        public static string BuildShareLink(string url, long hz, string mode, string kind)
        {
            string k = kind.StartsWith("owrx", StringComparison.Ordinal) ? "owrx" : kind == "websdr" || kind == "kiwi" ? kind : "";
            if (k == "owrx") { int hash = url.IndexOf('#'); if (hash >= 0) url = url.Substring(0, hash); }
            string s = ShareBase + "?u=" + Uri.EscapeDataString(url) + "&f=" + hz.ToString(CultureInfo.InvariantCulture) + "&m=" + mode;
            if (k != "") s += "&k=" + k;
            return s;
        }

        public static string ShareMode(string sdrMode)
        {
            string fam = Modes.SdrFamily(sdrMode);
            return fam == "" ? "USB" : fam.ToUpperInvariant();
        }

        void ShareFrequency()
        {
            CoreWebView2 core = web.CoreWebView2;
            string url = core != null ? core.Source : "";
            if (sdrKind == "" || sdrF <= 0 || HostKey(url) == "") { ShowStatus(Lang.T("Aucun WebSDR à partager", "No WebSDR to share")); return; }
            string mode = ShareMode(sdrM);
            string title = (core.DocumentTitle ?? "").Trim();
            if (title == "") title = new Uri(url).Host;
            string khz = (sdrF / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
            string text = "📻 " + Lang.F("{0} kHz {1} sur {2}", "{0} kHz {1} on {2}", khz, mode, title) + "\r\n" + BuildShareLink(url, sdrF, mode, sdrKind);
            for (int i = 0; ; i++)
            {
                try { Clipboard.SetText(text); break; }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    if (i >= 4) { ShowStatus(Lang.T("Presse-papiers occupé : lien non copié", "Clipboard busy: link not copied")); return; }
                    System.Threading.Thread.Sleep(50);
                }
            }
            Log.Write("Lien de partage copié : " + khz + " kHz " + mode);
            ShowStatus(Lang.F("Lien de partage copié dans le presse-papiers ({0} kHz {1}) : à coller dans un message", "Share link copied to the clipboard ({0} kHz {1}): paste it into a message", khz, mode));
        }

        // ---------------------------------------------------------------- Adresse masquée

        // Clic droit sur la page : « Masquer l'adresse du WebSDR » en tête du menu (le même clic droit la fait revenir).
        void OnPageContextMenu(object sender, CoreWebView2ContextMenuRequestedEventArgs e)
        {
            if (webEnv == null) return;
            try
            {
                CoreWebView2ContextMenuItem it = webEnv.CreateContextMenuItem(Lang.T("Masquer l'adresse du WebSDR", "Hide the WebSDR address"), null, CoreWebView2ContextMenuItemKind.CheckBox);
                it.IsChecked = settings.HideUrl;
                it.CustomItemSelected += delegate { BeginInvoke((Action)delegate { SetHideUrl(!settings.HideUrl); }); };
                IList<CoreWebView2ContextMenuItem> items = e.MenuItems;
                items.Insert(0, it);
                items.Insert(1, webEnv.CreateContextMenuItem("", null, CoreWebView2ContextMenuItemKind.Separator));
            }
            catch (Exception ex) { Log.Write("Menu de la page : " + ex.Message); }
        }

        void SetHideUrl(bool hide)
        {
            settings.HideUrl = hide;
            settings.Save();
            ApplyUrlMask();
            ShowStatus(hide ? Lang.T("Adresse du WebSDR masquée (clic droit sur la page pour la revoir)", "WebSDR address hidden (right-click the page to show it again)")
                : Lang.T("Adresse du WebSDR affichée", "WebSDR address shown"));
        }

        // Étoiles dans la case : sa zone de texte passe en mode mot de passe (caractère *). L'adresse reste dans la case
        // (Aller, favoris, navigation inchangés), sa flèche ouvre la liste comme avant, et l'adresse ne peut plus être copiée.
        void ApplyUrlMask()
        {
            if (cbUrl == null) return;
            if (!urlMaskHooked)
            {
                urlMaskHooked = true;
                cbUrl.HandleCreated += delegate { ApplyUrlMask(); };   // nouvelle zone de texte (thème, taille du texte…)
                translators.Add(UrlMaskTip);
            }
            if (!cbUrl.IsHandleCreated) return;
            IntPtr edit = UrlEditHandle();
            if (edit == IntPtr.Zero) return;
            SendMessage(edit, EM_SETPASSWORDCHAR, settings.HideUrl ? (IntPtr)'*' : IntPtr.Zero, IntPtr.Zero);
            InvalidateRect(edit, IntPtr.Zero, true);
            UrlMaskTip();
        }

        void UrlMaskTip()
        {
            tips.SetToolTip(cbUrl, settings.HideUrl ? Lang.T("Adresse masquée : la flèche ouvre toujours la liste (clic droit sur la page du WebSDR pour revoir l'adresse)", "Address hidden: the arrow still opens the list (right-click the WebSDR page to show the address again)") : "");
        }

        IntPtr UrlEditHandle()
        {
            ComboInfo ci = new ComboInfo();
            ci.cbSize = Marshal.SizeOf(typeof(ComboInfo));
            return GetComboBoxInfo(cbUrl.Handle, ref ci) ? ci.hwndItem : IntPtr.Zero;
        }

        const int EM_SETPASSWORDCHAR = 0xCC;
        [StructLayout(LayoutKind.Sequential)]
        struct ComboInfo { public int cbSize; public int il, it, ir, ib, bl, bt, br, bb; public int stateButton; public IntPtr hwndCombo, hwndItem, hwndList; }
        [DllImport("user32.dll")] static extern bool GetComboBoxInfo(IntPtr hwnd, ref ComboInfo info);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool InvalidateRect(IntPtr h, IntPtr r, bool erase);
    }

    // Rond sur la page : fond sombre, signe blanc dessiné (partage, plus, moins).
    public class PageRoundButton : Control
    {
        public enum Glyph { Share, Plus, Minus }
        readonly Glyph glyph;
        bool hot, down;

        public PageRoundButton(Glyph g)
        {
            glyph = g;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            BackColor = Color.FromArgb(40, 40, 40);
            Tag = Theme.Custom;
        }

        public Glyph Kind { get { return glyph; } }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            using (GraphicsPath p = new GraphicsPath()) { p.AddEllipse(0, 0, Width, Height); Region = new Region(p); }
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        // Clic gauche seulement (le clic droit ouvre le menu des positions).
        protected override void OnClick(EventArgs e)
        {
            MouseEventArgs me = e as MouseEventArgs;
            if (me != null && me.Button != MouseButtons.Left) return;
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = down ? Color.FromArgb(90, 90, 90) : hot ? Color.FromArgb(70, 70, 70) : Color.FromArgb(40, 40, 40);
            g.Clear(fill);
            float w = Width, h = Height, cx = w / 2f, cy = h / 2f, s = Math.Min(w, h);
            using (Pen ring = new Pen(Color.FromArgb(200, 200, 200), Math.Max(1f, s / 30f))) g.DrawEllipse(ring, 1, 1, w - 3, h - 3);
            using (Pen pen = new Pen(Color.White, Math.Max(2f, s / 12f)))
            using (Brush br = new SolidBrush(Color.White))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                if (glyph == Glyph.Plus || glyph == Glyph.Minus)
                {
                    float a = s * 0.24f;
                    g.DrawLine(pen, cx - a, cy, cx + a, cy);
                    if (glyph == Glyph.Plus) g.DrawLine(pen, cx, cy - a, cx, cy + a);
                }
                else
                {
                    // Trois points reliés : l'icône habituelle du partage.
                    float r = s * 0.085f, dx = s * 0.17f, dy = s * 0.17f;
                    PointF p1 = new PointF(cx + dx, cy - dy), p2 = new PointF(cx - dx, cy), p3 = new PointF(cx + dx, cy + dy);
                    using (Pen thin = new Pen(Color.White, Math.Max(1.5f, s / 20f)))
                    {
                        g.DrawLine(thin, p2, p1);
                        g.DrawLine(thin, p2, p3);
                    }
                    foreach (PointF p in new PointF[] { p1, p2, p3 }) g.FillEllipse(br, p.X - r, p.Y - r, 2 * r, 2 * r);
                }
            }
        }
    }
}
