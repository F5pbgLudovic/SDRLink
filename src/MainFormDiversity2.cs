using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SDRLink
{
    // ---------------------------------------------------------------- Diversité sur deux WebSDR (1.44)
    // Clic droit sur « Diversité » : « Sur deux WebSDR ». Une case de choix suit alors le bouton Diversité : le second
    // WebSDR, ouvert dans une page cachée (hors de l'écran) pendant la diversité. Seul son son sert (oreille B) ; il suit
    // la fréquence et le mode du WebSDR affiché, qui garde toutes les commandes. Volume et Mute WebSDR règlent les deux
    // oreilles. Le mélangeur cherche le retard dans les deux sens (DiversityMixer.LeadEnv) : l'un ou l'autre WebSDR peut
    // arriver en premier.
    public partial class MainForm
    {
        const int DivTwoWebSdr = 4;
        ComboBox cbUrl2;
        WebView2 web2;
        string sdr2Kind = "", sdr2M = "";
        long sdr2F;
        long div2SentF = -1;
        string div2SentFam;
        DateTime div2SentAt;
        string div2NotchSent;

        void CreateDiv2Combo()
        {
            cbUrl2 = new ComboBox();
            cbUrl2.Width = 220;
            cbUrl2.MaxDropDownItems = 30;
            cbUrl2.DropDownStyle = ComboBoxStyle.DropDown;
            cbUrl2.Margin = new Padding(3, 4, 3, 3);
            cbUrl2.Visible = false;
            cbUrl2.SelectionChangeCommitted += delegate
            {
                Favorite f = cbUrl2.SelectedItem as Favorite;
                if (f != null) BeginInvoke((Action)delegate { SetDiv2Url(f.Url); });
            };
            cbUrl2.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Enter) return;
                e.SuppressKeyPress = true;
                string t = cbUrl2.Text.Trim();
                Favorite f = settings.Favorites.Find(delegate(Favorite x) { return x.Name == t; });
                SetDiv2Url(f != null ? f.Url : t);
            };
            cbUrl2.HandleCreated += delegate { ApplyUrlMask(); };
            Tr(delegate
            {
                tips.SetToolTip(cbUrl2, Lang.T("Second WebSDR de la diversité : on n'entend que son son, dans l'autre oreille ; il suit la fréquence et le mode du WebSDR affiché",
                    "Second WebSDR for diversity: only its audio is used, in the other ear; it follows the frequency and mode of the WebSDR shown"));
            });
        }

        // Case affichée seulement quand la diversité est réglée « sur deux WebSDR ».
        void UpdateDiv2Combo()
        {
            if (cbUrl2 == null || settings == null) return;
            bool show = settings.DiversitySource == DivTwoWebSdr;
            if (cbUrl2.Visible != show) cbUrl2.Visible = show;
            if (!cbUrl2.Focused) cbUrl2.Text = Div2Name();
        }

        string Div2Name()
        {
            string u = settings.Div2Url;
            if (u == "") return "";
            Favorite f = settings.Favorites.Find(delegate(Favorite x) { return x.Matches(u); });
            return f != null ? f.Name : u;
        }

        void RefillDiv2Combo()
        {
            if (cbUrl2 == null) return;
            cbUrl2.BeginUpdate();
            cbUrl2.Items.Clear();
            foreach (Favorite f in settings.Favorites)
                if (f.List == settings.CurrentList) cbUrl2.Items.Add(f);
            cbUrl2.EndUpdate();
            cbUrl2.Text = Div2Name();
        }

        void SetDiv2Url(string url)
        {
            url = (url ?? "").Trim();
            if (url != "" && !url.Contains("://")) url = "http://" + url;
            Uri u;
            if (url != "" && (!Uri.TryCreate(url, UriKind.Absolute, out u) || (u.Scheme != "http" && u.Scheme != "https")))
            {
                ShowStatus(Lang.T("Adresse invalide : ", "Invalid address: ") + url);
                return;
            }
            settings.Div2Url = url;
            settings.Save();
            cbUrl2.Text = Div2Name();
            Log.Write("Diversité : second WebSDR " + (url == "" ? "(aucun)" : url));
            if (diversity != null && divSource == DivTwoWebSdr)
            {
                if (url == "") { StopDiversity(Lang.T("Diversité arrêtée : pas de second WebSDR", "Diversity off: no second WebSDR")); UpdateDiversityButton(); return; }
                OpenWeb2();
                ShowStatus(Lang.F("Diversité : second WebSDR « {0} » ; recherche du retard…", "Diversity: second WebSDR \"{0}\"; measuring the delay…", Div2Name()));
            }
            else ShowStatus(Lang.F("Second WebSDR de la diversité : « {0} » (bouton Diversité pour l'écouter)", "Second WebSDR for diversity: \"{0}\" (Diversity button to listen)", Div2Name()));
        }

        // Page cachée : posée hors de l'écran avec une taille normale (une page minuscule ou cachée dérange certains WebSDR).
        async void OpenWeb2()
        {
            if (webEnv == null) return;
            sdr2Kind = ""; sdr2F = 0; sdr2M = "";
            div2SentF = -1; div2SentFam = null; div2NotchSent = null;
            WebView2 w = web2;
            if (w != null && w.CoreWebView2 != null)
            {
                try { w.CoreWebView2.Navigate(settings.Div2Url); } catch (ArgumentException) { }
                return;
            }
            if (w != null) return;   // en cours de création : la navigation suivra
            w = new WebView2();
            w.Bounds = new Rectangle(-10000, -10000, Math.Max(900, web.Width), Math.Max(650, web.Height));
            w.TabStop = false;
            Controls.Add(w);
            web2 = w;
            try
            {
                await w.EnsureCoreWebView2Async(webEnv);
                if (web2 != w) { w.Dispose(); return; }
                CoreWebView2 c = w.CoreWebView2;
                await c.AddScriptToExecuteOnDocumentCreatedAsync(WebSdrBridge.Script);
                string id = WebSdrBridge.Identity(settings.Callsign);
                if (id != null) await c.AddScriptToExecuteOnDocumentCreatedAsync(id);
                if (web2 != w) { w.Dispose(); return; }
                c.WebMessageReceived += OnWeb2Message;
                c.NewWindowRequested += delegate(object s, CoreWebView2NewWindowRequestedEventArgs e) { e.Handled = true; };
                c.NavigationStarting += delegate { sdr2Kind = ""; div2SentF = -1; div2SentFam = null; div2NotchSent = null; };
                c.DOMContentLoaded += delegate { Web2Setup(); };
                c.Navigate(settings.Div2Url);
                Log.Write("Diversité : second WebSDR ouvert (page cachée) : " + settings.Div2Url);
            }
            catch (Exception ex)
            {
                Log.Write("Diversité : second WebSDR : " + ex.Message);
                ShowStatus(Lang.F("Diversité : second WebSDR impossible à ouvrir ({0})", "Diversity: cannot open the second WebSDR ({0})", ex.Message));
            }
        }

        void CloseWeb2()
        {
            WebView2 w = web2;
            web2 = null;
            sdr2Kind = "";
            if (w == null) return;
            try { Controls.Remove(w); w.Dispose(); } catch (Exception ex) { Log.Write("Diversité : fermeture du second WebSDR : " + ex.Message); }
            Log.Write("Diversité : second WebSDR fermé");
        }

        // Page du second WebSDR : son coupé (seule la dérivation sert), dérivation en marche, notch comme sur le WebSDR affiché.
        void Web2Setup()
        {
            if (web2 == null || web2.CoreWebView2 == null) return;
            web2.CoreWebView2.ExecuteScriptAsync("window.__sdrlink && window.__sdrlink.setVolume(0);");
            div2NotchSent = null;
            Div2Notch();
        }

        void Div2Notch()
        {
            if (web2 == null || web2.CoreWebView2 == null) return;
            bool on = NotchWanted;
            string s = (on ? "1" : "0") + "|" + settings.NotchLevel;
            if (s == div2NotchSent) return;
            div2NotchSent = s;
            web2.CoreWebView2.ExecuteScriptAsync("if (window.__sdrlink) { window.__sdrlink.setNotch && window.__sdrlink.setNotch(" + (on ? "true" : "false") + "," + settings.NotchLevel
                + "); window.__sdrlink.setTap && window.__sdrlink.setTap(true," + (on ? "true" : "false") + "); }");
        }

        void OnWeb2Message(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (web2 == null || sender != web2.CoreWebView2) return;
            string msg;
            try { msg = e.TryGetWebMessageAsString(); } catch { return; }
            if (msg == null) return;
            if (msg.StartsWith("pcm|"))
            {
                DiversityMixer d = diversity;
                if (d == null || divSource != DivTwoWebSdr) return;
                int a = msg.IndexOf('|'), b = msg.IndexOf('|', a + 1);
                int rate;
                if (b < 0 || !int.TryParse(msg.Substring(a + 1, b - a - 1), out rate)) return;
                byte[] pcm;
                try { pcm = Convert.FromBase64String(msg.Substring(b + 1)); }
                catch (FormatException) { return; }
                float[] f = new float[pcm.Length / 2];
                for (int i = 0; i < f.Length; i++) f[i] = (short)(pcm[2 * i] | pcm[2 * i + 1] << 8) / 32768f;
                d.FeedB(rate, f, f.Length);
                return;
            }
            string[] p = msg.Split('|');
            long hz;
            if (p.Length < 3 || !long.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out hz)) return;
            bool first = sdr2Kind == "";
            sdr2Kind = p[0];
            sdr2F = hz;
            sdr2M = p[2];
            if (first)
            {
                Log.Write("Diversité : second WebSDR détecté : " + p[0] + " " + hz + " Hz " + p[2]);
                Web2Setup();
                div2SentF = -1;
            }
            Div2Sync();
        }

        // Le second WebSDR suit la fréquence et le mode du WebSDR affiché (renvoyés au plus toutes les 3 s s'il ne les prend pas).
        void Div2Sync()
        {
            if (web2 == null || web2.CoreWebView2 == null || sdr2Kind == "" || sdrKind == "" || sdrF <= 0) return;
            Div2Notch();
            string fam = Modes.SdrFamily(sdrM);
            bool needF = Math.Abs(sdr2F - sdrF) > Tolerance;
            bool needM = fam != "" && fam != Modes.SdrFamily(sdr2M);
            if (!needF && !needM) return;
            if (sdrF == div2SentF && fam == div2SentFam && (DateTime.UtcNow - div2SentAt).TotalSeconds < 3) return;
            div2SentF = sdrF;
            div2SentFam = fam;
            div2SentAt = DateTime.UtcNow;
            web2.CoreWebView2.ExecuteScriptAsync(string.Format(CultureInfo.InvariantCulture,
                "window.__sdrlink && window.__sdrlink.set({0}, '{1}');", needF ? sdrF : 0, needM ? fam : ""));
        }
    }
}
