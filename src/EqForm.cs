using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SDRLink
{
    // Égaliseur 10 bandes (1.54, repris de HamRadioLink 1.9). Fenêtre non bloquante ; chaque réglage agit tout de suite sur le son.
    // Réception : son du WebSDR (bouton EQ, après Diversité). Émission : micro du PC envoyé au poste, avec noise gate,
    // compresseur et limiteur (clic droit sur PTT).
    public class EqForm : Form
    {
        static EqForm inst;
        public static void Open(Form owner, bool tx)
        {
            if (inst != null && !inst.IsDisposed) { inst.Pick(tx); inst.WindowState = FormWindowState.Normal; inst.BringToFront(); inst.Activate(); return; }
            inst = new EqForm(); inst.Pick(tx); inst.Show(owner);
        }

        static string[] PresetNames()
        {
            return new[] { Lang.T("À plat", "Flat"), Lang.T("Voix claire (présence)", "Clear voice (presence)"), Lang.T("Graves coupés", "Bass cut"),
                Lang.T("Chaleureux", "Warm"), Lang.T("Aigus adoucis", "Softened treble"), Lang.T("DX (médium renforcé)", "DX (boosted midrange)") };
        }
        static readonly double[][] Presets = {
            new double[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
            new double[] { -6, -5, -3, -1, 0, 1, 3, 4, 2, 0 },
            new double[] { -12, -10, -6, -2, 0, 0, 0, 0, 0, 0 },
            new double[] { 0, 2, 4, 3, 1, 0, -1, -2, -3, -4 },
            new double[] { 0, 0, 0, 0, 0, 0, -1, -3, -6, -9 },
            new double[] { -12, -9, -5, -1, 2, 4, 4, 2, -2, -8 } };

        bool tx = true, loading;
        FxSet cur;
        readonly RadioButton rTx, rRx; readonly CheckBox cEq, cComp, cGate; readonly TrackBar[] tb = new TrackBar[10]; readonly Label[] lv = new Label[10];
        readonly NumericUpDown nThr, nRatio, nMake, nCeil, nAtt, nRel, nGThr, nGRel; readonly Panel comp, gatePanel; readonly Plot plot; readonly Label status, gr, gs; readonly ComboBox presets;
        readonly Timer saveT = new Timer { Interval = 800 }, meterT = new Timer { Interval = 100 };

        public EqForm()
        {
            AutoScaleMode = AutoScaleMode.None; Text = Lang.T("Égaliseur 10 bandes — SDRLink", "10-band equalizer — SDRLink"); FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false;
            ClientSize = new Size(760, 720); Font = new Font("Segoe UI", 8.25f); Icon = Application.OpenForms.Count > 0 ? Application.OpenForms[0].Icon : null;
            rRx = new RadioButton { Text = Lang.T("Réception (WebSDR et poste écouté sur le PC)", "Receive (WebSDR and rig heard on the PC)"), Left = 14, Top = 10, Width = 320, Checked = true };
            rTx = new RadioButton { Text = Lang.T("Émission (micro du PC vers le poste)", "Transmit (PC microphone to the rig)"), Left = 340, Top = 10, Width = 340 };
            Controls.Add(rRx); Controls.Add(rTx); rTx.CheckedChanged += delegate { if (!loading && rTx.Checked) Pick(true); }; rRx.CheckedChanged += delegate { if (!loading && rRx.Checked) Pick(false); };
            cEq = new CheckBox { Text = Lang.T("Égaliseur actif", "Equalizer on"), Left = 14, Top = 38, Width = 200, Font = new Font(Font, FontStyle.Bold) }; Controls.Add(cEq);
            presets = new ComboBox { Left = 380, Top = 36, Width = 210, DropDownStyle = ComboBoxStyle.DropDownList };
            presets.Items.Add(Lang.T("Préréglage…", "Preset…")); foreach (string p in PresetNames()) presets.Items.Add(p); presets.SelectedIndex = 0; Controls.Add(presets);
            Button flat = new Button { Text = Lang.T("À plat", "Flat"), Left = 600, Top = 35, Width = 80, Height = 24 }; flat.Click += delegate { Apply(Presets[0]); }; Controls.Add(flat);
            presets.SelectedIndexChanged += delegate { int k = presets.SelectedIndex - 1; if (k >= 0) { Apply(Presets[k]); presets.SelectedIndex = 0; } };
            plot = new Plot(this) { Left = 10, Top = 68, Width = 740, Height = 200 }; Controls.Add(plot);

            Panel pb = new Panel { Left = 10, Top = 274, Width = 740, Height = 190 }; Controls.Add(pb);
            for (int i = 0; i < 10; i++)
            {
                int x = 14 + i * 72, k = i;
                lv[i] = new Label { Left = x, Top = 6, Width = 64, Height = 16, TextAlign = ContentAlignment.MiddleCenter, Text = "0,0 dB" }; pb.Controls.Add(lv[i]);
                TrackBar t = new TrackBar { Orientation = Orientation.Vertical, Minimum = -120, Maximum = 120, TickFrequency = 60, SmallChange = 5, LargeChange = 20, Left = x + 10, Top = 24, Width = 45, Height = 140, TickStyle = TickStyle.Both };
                t.ValueChanged += delegate { if (!loading) Changed(k, t.Value / 10.0); }; t.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Right) t.Value = 0; };
                pb.Controls.Add(t); tb[i] = t;
                int f = FxCfg.Freq[i]; pb.Controls.Add(new Label { Left = x, Top = 168, Width = 64, Height = 16, TextAlign = ContentAlignment.MiddleCenter, Text = f >= 1000 ? (f / 1000.0).ToString("0.#") + " kHz" : f + " Hz" });
            }

            gatePanel = new Panel { Left = 10, Top = 470, Width = 740, Height = 90 }; Controls.Add(gatePanel);
            cGate = new CheckBox { Text = Lang.T("Noise gate actif (coupe le micro sous le seuil)", "Noise gate on (mutes the microphone below the threshold)"), Left = 14, Top = 6, Width = 380, Font = new Font(Font, FontStyle.Bold) }; gatePanel.Controls.Add(cGate);
            gs = new Label { Left = 400, Top = 8, Width = 320, Height = 18, TextAlign = ContentAlignment.MiddleRight }; gatePanel.Controls.Add(gs);
            nGThr = Num(gatePanel, Lang.T("Seuil (dB)", "Threshold (dB)"), 14, -80, -10, 0); nGRel = Num(gatePanel, Lang.T("Fermeture (ms)", "Close time (ms)"), 134, 20, 1500, 0);

            comp = new Panel { Left = 10, Top = 566, Width = 740, Height = 90 }; Controls.Add(comp);
            cComp = new CheckBox { Text = Lang.T("Compresseur / limiteur actif", "Compressor / limiter on"), Left = 14, Top = 6, Width = 300, Font = new Font(Font, FontStyle.Bold) }; comp.Controls.Add(cComp);
            gr = new Label { Left = 400, Top = 8, Width = 320, Height = 18, TextAlign = ContentAlignment.MiddleRight }; comp.Controls.Add(gr);
            nThr = Num(comp, Lang.T("Seuil (dB)", "Threshold (dB)"), 14, -40, 0, 0); nRatio = Num(comp, Lang.T("Rapport (:1)", "Ratio (:1)"), 134, 1, 20, 1); nMake = Num(comp, Lang.T("Gain de sortie (dB)", "Output gain (dB)"), 254, 0, 24, 1);
            nCeil = Num(comp, Lang.T("Plafond limiteur (dB)", "Limiter ceiling (dB)"), 374, -12, 0, 1); nAtt = Num(comp, Lang.T("Attaque (ms)", "Attack (ms)"), 494, 1, 100, 0); nRel = Num(comp, Lang.T("Relâche (ms)", "Release (ms)"), 614, 20, 1500, 0);

            status = new Label { Left = 14, Top = 664, Width = 600, Height = 48 }; Controls.Add(status);
            Button close = new Button { Text = Lang.T("Fermer", "Close"), Left = 650, Top = 680, Width = 100, Height = 28 }; close.Click += delegate { Close(); }; Controls.Add(close);
            cEq.CheckedChanged += delegate { if (!loading) { cur.EqOn = cEq.Checked; Push(); } };
            cComp.CheckedChanged += delegate { if (!loading) { cur.CompOn = cComp.Checked; Push(); } };
            cGate.CheckedChanged += delegate { if (!loading) { cur.GateOn = cGate.Checked; Push(); } };
            foreach (NumericUpDown n in new[] { nThr, nRatio, nMake, nCeil, nAtt, nRel, nGThr, nGRel }) n.ValueChanged += delegate { Pc(); };
            saveT.Tick += delegate { saveT.Stop(); Save(); };
            meterT.Tick += delegate { ShowMeters(); }; meterT.Start();
            FormClosed += delegate { meterT.Stop(); if (saveT.Enabled) { saveT.Stop(); Save(); } inst = null; };
            Theme.Apply(this);
            foreach (Label l in lv) l.ForeColor = Theme.Dark ? Color.FromArgb(140, 215, 255) : Color.FromArgb(0, 90, 160);
        }

        static void Save() { try { if (FxCfg.S != null) FxCfg.S.Save(); } catch (Exception ex) { Log.Write("Égaliseur : enregistrement impossible : " + ex.Message); } }

        void ShowMeters()
        {
            if (!tx) return;
            gr.Text = Lang.F("Réduction : {0} dB", "Gain reduction: {0} dB", FxCfg.GrTx.ToString("0.0"));
            gs.Text = cur != null && cur.GateOn ? (FxCfg.GateOpen ? Lang.T("Gate : ouvert", "Gate: open") : Lang.T("Gate : fermé", "Gate: closed")) : "";
        }

        void Pick(bool t)
        {
            tx = t; loading = true; (t ? rTx : rRx).Checked = true;
            cur = FxCfg.Get(tx).Clone(); cEq.Checked = cur.EqOn; cComp.Checked = cur.CompOn; cGate.Checked = cur.GateOn;
            for (int i = 0; i < 10; i++) { tb[i].Value = (int)Math.Round(cur.G[i] * 10); lv[i].Text = Db(cur.G[i]); }
            SetN(nThr, cur.Thr); SetN(nRatio, cur.Ratio); SetN(nMake, cur.Makeup); SetN(nCeil, cur.Ceil); SetN(nAtt, cur.Att); SetN(nRel, cur.Rel); SetN(nGThr, cur.GateThr); SetN(nGRel, cur.GateRel);
            comp.Visible = gatePanel.Visible = tx; loading = false; plot.Invalidate(); ShowMeters();
            status.Text = tx ? Lang.T("Appliqué au micro du PC envoyé au poste : actif quand « Micro du PC vers le poste » est coché (clic droit sur PTT). Ordre : noise gate, égaliseur, compresseur, limiteur. Clic droit sur un curseur = remise à 0.",
                    "Applied to the PC microphone sent to the rig: active when \"PC microphone to the rig\" is ticked (right-click PTT). Order: noise gate, equalizer, compressor, limiter. Right-click a slider = back to 0.")
                : Lang.T("Appliqué au son du WebSDR (haut-parleur et oreille du WebSDR en diversité) et au son du poste écouté sur le PC (clic droit sur Mute TX), dès qu'on le met ; le câble des décodeurs garde le son brut. Clic droit sur un curseur = remise à 0.",
                    "Applied to the WebSDR audio (speaker and WebSDR ear in diversity) and to the rig heard on the PC (right-click Mute TRX), as soon as it is on; the decoders' cable keeps the raw audio. Right-click a slider = back to 0.");
        }
        static string Db(double g) { return (g > 0 ? "+" : "") + g.ToString("0.0") + " dB"; }
        static void SetN(NumericUpDown n, double v) { n.Value = (decimal)Math.Max((double)n.Minimum, Math.Min((double)n.Maximum, v)); }
        void Changed(int i, double g) { cur.G[i] = g; lv[i].Text = Db(g); Push(); }
        void Pc()
        {
            if (loading) return;
            cur.Thr = (double)nThr.Value; cur.Ratio = (double)nRatio.Value; cur.Makeup = (double)nMake.Value; cur.Ceil = (double)nCeil.Value; cur.Att = (double)nAtt.Value; cur.Rel = (double)nRel.Value;
            cur.GateThr = (double)nGThr.Value; cur.GateRel = (double)nGRel.Value; Push();
        }
        void Apply(double[] g) { loading = true; for (int i = 0; i < 10; i++) { cur.G[i] = g[i]; tb[i].Value = (int)Math.Round(g[i] * 10); lv[i].Text = Db(g[i]); } loading = false; Push(); }
        // l'instantané modifié devient celui du fil audio (et de la page) ; un enregistrement sur disque suit 0,8 s après le dernier geste
        void Push() { FxCfg.Store(tx, cur.Clone()); plot.Invalidate(); saveT.Stop(); saveT.Start(); }

        NumericUpDown Num(Panel p, string label, int x, int min, int max, int dec)
        {
            p.Controls.Add(new Label { Text = label, Left = x, Top = 34, Width = 116 });
            NumericUpDown n = new NumericUpDown { Left = x, Top = 54, Width = 80, Minimum = min, Maximum = max, DecimalPlaces = dec, Increment = dec > 0 ? 0.5m : 1 }; p.Controls.Add(n); return n;
        }

        // courbe de réponse de l'égaliseur : 20 Hz à 12 kHz (échelle logarithmique), ±12 dB ; curseurs : vers le haut = gain
        static readonly double Span = Math.Log10(600);
        class Plot : Control
        {
            readonly EqForm f;
            public Plot(EqForm f) { this.f = f; Tag = Theme.Custom; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics; g.Clear(Color.Black); g.SmoothingMode = SmoothingMode.AntiAlias; FxSet s = f.cur; if (s == null) return;
                float L = 44, R = Width - 12, T = 12, B = Height - 24, W = R - L, H = B - T;
                using (Pen grid = new Pen(Color.FromArgb(50, 50, 50))) using (Brush tx = new SolidBrush(Color.FromArgb(150, 150, 150)))
                {
                    for (int d = -12; d <= 12; d += 6) { float y = T + H * (12 - d) / 24f; g.DrawLine(grid, L, y, R, y); g.DrawString((d > 0 ? "+" : "") + d, Font, tx, 6, y - 7); }
                    foreach (int fr in FxCfg.Freq) { float x = L + W * (float)(Math.Log10(fr / 20.0) / Span); g.DrawLine(grid, x, T, x, B); string t = fr >= 1000 ? (fr / 1000.0).ToString("0.#") + "k" : fr.ToString(); g.DrawString(t, Font, tx, x - 8, B + 4); }
                }
                PointF[] pts = new PointF[300];
                for (int i = 0; i < pts.Length; i++) { double fr = 20 * Math.Pow(600, i / (double)(pts.Length - 1)); double db = Math.Max(-12, Math.Min(12, s.EqOn ? FxCfg.Response(s, fr, 48000) : 0)); pts[i] = new PointF(L + W * i / (pts.Length - 1), T + H * (float)((12 - db) / 24.0)); }
                using (Pen p = new Pen(f.tx ? Color.FromArgb(70, 140, 255) : Color.FromArgb(160, 215, 50), 2.2f)) g.DrawLines(p, pts);
                for (int i = 0; i < 10; i++) { float x = L + W * (float)(Math.Log10(FxCfg.Freq[i] / 20.0) / Span), y = T + H * (float)((12 - Math.Max(-12, Math.Min(12, s.G[i]))) / 24.0); g.FillEllipse(Brushes.White, x - 3, y - 3, 6, 6); }
            }
        }
    }
}
