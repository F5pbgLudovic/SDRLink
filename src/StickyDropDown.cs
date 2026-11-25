using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SDRLink
{
    // Liste déroulante qui reste ouverte (1.56, listes WebSDR, favoris et second WebSDR).
    // La liste native de Windows se ferme d'elle-même dès que la liste perd le focus : la page WebSDR (WebView2) qui le prend,
    // un rebond de la souris... Sous Windows 11 surtout, elle se refermait aussitôt ouverte. Ici, la flèche ouvre à la place
    // une liste de SDRLink, qui ne prend jamais le focus et ne se ferme que par :
    // - un nouveau clic sur la liste déroulante (sa flèche) ; un clic moins de 350 ms après l'ouverture est un rebond : ignoré ;
    // - le choix d'une ligne (clic, ou ↑ ↓ puis Entrée) ;
    // - Échap, F4 ou Alt+↑ ;
    // - la liste déroulante cachée ou désactivée, ou la fenêtre fermée.
    // Rien d'autre ne la ferme : ni la page, ni les minuteurs, ni le passage dans une autre fenêtre ; elle suit la fenêtre
    // quand celle-ci bouge. La molette fait défiler la liste ouverte.
    static class StickyDropDown
    {
        const int BounceMs = 350;
        static readonly Dictionary<ComboBox, Sticky> all = new Dictionary<ComboBox, Sticky>();

        public static void Attach(ComboBox cb)
        {
            if (!all.ContainsKey(cb)) all[cb] = new Sticky(cb);
        }

        // Vrai si la liste de cette liste déroulante est ouverte (le programme ne doit alors pas réécrire son texte).
        public static bool IsOpen(ComboBox cb)
        {
            Sticky s;
            return cb != null && (cb.DroppedDown || (all.TryGetValue(cb, out s) && s.Open));
        }

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        struct COMBOBOXINFO
        {
            public int cbSize;
            public RECT rcItem, rcButton;
            public int stateButton;
            public IntPtr hwndCombo, hwndItem, hwndList;
        }
        [DllImport("user32.dll")] static extern bool GetComboBoxInfo(IntPtr hwnd, ref COMBOBOXINFO info);

        static readonly MethodInfo onCommitted = typeof(ComboBox).GetMethod("OnSelectionChangeCommitted", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly MethodInfo onDropDown = typeof(ComboBox).GetMethod("OnDropDown", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly MethodInfo onClosed = typeof(ComboBox).GetMethod("OnDropDownClosed", BindingFlags.Instance | BindingFlags.NonPublic);

        sealed class Sticky : NativeWindow
        {
            const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104, WM_CHAR = 0x0102, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONDBLCLK = 0x0203,
                      WM_MOUSEWHEEL = 0x020A, CB_SHOWDROPDOWN = 0x014F;
            readonly ComboBox cb;
            readonly EditHook edit;
            Popup popup;
            PopList list;
            Timer follow;
            int openedTick;
            bool swallowChar;
            public bool Open;

            public Sticky(ComboBox cb)
            {
                this.cb = cb;
                edit = new EditHook(this);
                cb.HandleCreated += delegate { Hook(); };
                cb.HandleDestroyed += delegate { Close(false); edit.ReleaseHandle(); ReleaseHandle(); };
                if (cb.IsHandleCreated) Hook();
                cb.VisibleChanged += delegate { if (!cb.Visible) Close(true); };
                cb.EnabledChanged += delegate { if (!cb.Enabled) Close(true); };
                cb.Disposed += delegate { Close(false); all.Remove(cb); };
                // Liste native ouverte malgré tout (par un moyen non prévu) : remplacée par celle de SDRLink.
                cb.DropDown += delegate
                {
                    if (Open) return;
                    cb.BeginInvoke((Action)delegate { if (cb.DroppedDown) { cb.DroppedDown = false; Show(); } });
                };
            }

            void Hook()
            {
                if (Handle != IntPtr.Zero) ReleaseHandle();
                AssignHandle(cb.Handle);
                COMBOBOXINFO ci = new COMBOBOXINFO();
                ci.cbSize = Marshal.SizeOf(typeof(COMBOBOXINFO));
                if (edit.Handle != IntPtr.Zero) edit.ReleaseHandle();
                if (GetComboBoxInfo(cb.Handle, ref ci) && ci.hwndItem != IntPtr.Zero && ci.hwndItem != cb.Handle) edit.AssignHandle(ci.hwndItem);
            }

            bool OnButton(IntPtr lParam)
            {
                if (cb.DropDownStyle == ComboBoxStyle.DropDownList) return true;
                COMBOBOXINFO ci = new COMBOBOXINFO();
                ci.cbSize = Marshal.SizeOf(typeof(COMBOBOXINFO));
                if (!GetComboBoxInfo(cb.Handle, ref ci)) return true;
                long lp = (long)lParam;
                int x = (short)(lp & 0xFFFF), y = (short)((lp >> 16) & 0xFFFF);
                return x >= ci.rcButton.Left && x < ci.rcButton.Right && y >= ci.rcButton.Top && y < ci.rcButton.Bottom;
            }

            protected override void WndProc(ref Message m)
            {
                if ((m.Msg == WM_LBUTTONDOWN || m.Msg == WM_LBUTTONDBLCLK) && OnButton(m.LParam))
                {
                    if (!cb.Focused) cb.Focus();
                    if (!Open) Show();
                    else if (unchecked(Environment.TickCount - openedTick) >= BounceMs) Close(true);
                    // sinon : rebond de la souris, la liste reste ouverte
                    return;
                }
                if (m.Msg == CB_SHOWDROPDOWN && m.WParam != IntPtr.Zero) { if (!Open) Show(); m.Result = (IntPtr)1; return; }
                if (Key(ref m)) return;
                base.WndProc(ref m);
            }

            // Clavier et molette de la liste déroulante (ou de sa zone de texte) : vrai si le message est pris.
            public bool Key(ref Message m)
            {
                if (m.Msg == WM_CHAR && swallowChar) { swallowChar = false; return true; }
                bool sys = m.Msg == WM_SYSKEYDOWN;
                if (m.Msg != WM_KEYDOWN && !sys && !(Open && m.Msg == WM_MOUSEWHEEL)) return false;
                if (m.Msg == WM_MOUSEWHEEL)
                {
                    int delta = (short)(((long)m.WParam >> 16) & 0xFFFF);
                    list.TopIndex = Math.Max(0, list.TopIndex - delta / 120 * 3);
                    return true;
                }
                Keys k = (Keys)(int)(long)m.WParam;
                if (k == Keys.F4 || (sys && (k == Keys.Down || k == Keys.Up)))
                {
                    if (Open) Close(true); else Show();
                    return true;
                }
                if (!Open || sys) return false;
                int n = list.Items.Count, i = list.SelectedIndex, page = Math.Max(1, list.ClientSize.Height / Math.Max(1, list.ItemHeight) - 1);
                switch (k)
                {
                    case Keys.Escape: Close(true); swallowChar = true; return true;
                    case Keys.Enter: if (i >= 0) Pick(i); else Close(true); swallowChar = true; return true;
                    case Keys.Down: if (n > 0) list.SelectedIndex = Math.Min(n - 1, i + 1); return true;
                    case Keys.Up: if (n > 0) list.SelectedIndex = Math.Max(0, i - 1); return true;
                    case Keys.PageDown: if (n > 0) list.SelectedIndex = Math.Min(n - 1, Math.Max(0, i) + page); return true;
                    case Keys.PageUp: if (n > 0) list.SelectedIndex = Math.Max(0, i - page); return true;
                }
                return false;
            }

            void Show()
            {
                Form owner = cb.FindForm();
                if (owner == null || !cb.Visible || !cb.Enabled || cb.Items.Count == 0) return;
                if (popup == null)
                {
                    popup = new Popup();
                    list = new PopList();
                    list.Dock = DockStyle.Fill;
                    list.BorderStyle = BorderStyle.None;
                    list.IntegralHeight = false;
                    list.Picked += Pick;
                    popup.Controls.Add(list);
                    popup.Owner = owner;
                    owner.FormClosing += delegate { Close(false); };
                    follow = new Timer { Interval = 200 };
                    follow.Tick += delegate { if (!cb.Visible || !cb.Enabled || cb.IsDisposed) Close(true); else Place(); };
                }
                list.Font = cb.Font;
                if (!list.IsHandleCreated) { IntPtr h = list.Handle; }   // hauteur des lignes juste dès la 1re fois
                list.BackColor = Theme.InputBack;
                list.ForeColor = Theme.InputFore;
                popup.BackColor = Theme.Border;
                list.BeginUpdate();
                list.Items.Clear();
                foreach (object o in cb.Items) list.Items.Add(o);
                int sel = cb.SelectedIndex;
                if (sel < 0 && cb.Text != "") sel = cb.FindStringExact(cb.Text);
                list.SelectedIndex = sel < list.Items.Count ? sel : -1;
                list.EndUpdate();
                if (sel >= 0) list.TopIndex = Math.Max(0, sel - 2);
                Place();
                Open = true;
                openedTick = Environment.TickCount;
                if (!popup.Visible) popup.Show(owner);
                follow.Start();
                if (onDropDown != null) onDropDown.Invoke(cb, new object[] { EventArgs.Empty });
            }

            // Juste sous la liste déroulante (au-dessus si la place manque) ; suit la fenêtre quand elle bouge.
            void Place()
            {
                if (popup == null || cb.Parent == null) return;
                Rectangle area = Screen.FromControl(cb).WorkingArea;
                int rows = Math.Max(1, Math.Min(Math.Max(1, cb.MaxDropDownItems), list.Items.Count));
                Size size = new Size(Math.Min(Math.Max(cb.Width, cb.DropDownWidth), area.Width), rows * list.ItemHeight + 2);
                Point at = cb.Parent.PointToScreen(new Point(cb.Left, cb.Bottom));
                if (at.Y + size.Height > area.Bottom)
                {
                    int above = cb.Parent.PointToScreen(new Point(cb.Left, cb.Top)).Y - size.Height;
                    if (above >= area.Top) at.Y = above;
                    else size.Height = Math.Max(list.ItemHeight + 2, area.Bottom - at.Y);
                }
                if (at.X + size.Width > area.Right) at.X = Math.Max(area.Left, area.Right - size.Width);
                Rectangle r = new Rectangle(at, size);
                if (popup.Bounds != r) popup.Bounds = r;
            }

            void Close(bool raise)
            {
                if (!Open) return;
                Open = false;
                if (follow != null) follow.Stop();
                if (popup != null && !popup.IsDisposed) popup.Hide();
                if (raise && !cb.IsDisposed && onClosed != null) onClosed.Invoke(cb, new object[] { EventArgs.Empty });
            }

            // Ligne choisie : comme un choix dans la liste native (SelectedIndexChanged puis SelectionChangeCommitted).
            void Pick(int i)
            {
                object o = i >= 0 && i < list.Items.Count ? list.Items[i] : null;
                Close(true);
                int at = o == null ? -1 : cb.Items.IndexOf(o);
                if (at < 0) return;
                cb.SelectedIndex = at;
                if (cb.DropDownStyle == ComboBoxStyle.DropDown) cb.SelectionStart = cb.Text.Length;
                if (onCommitted != null) onCommitted.Invoke(cb, new object[] { EventArgs.Empty });
            }
        }

        // Zone de texte d'une liste déroulante modifiable (barre WebSDR) : touches F4, Alt+↓, ↑ ↓ Entrée Échap quand la liste
        // est ouverte, et molette.
        sealed class EditHook : NativeWindow
        {
            readonly Sticky owner;
            public EditHook(Sticky owner) { this.owner = owner; }
            protected override void WndProc(ref Message m)
            {
                if (owner.Key(ref m)) return;
                base.WndProc(ref m);
            }
        }

        // Fenêtre sans bordure qui ne prend jamais la main (comme les propositions de la barre WebSDR).
        sealed class Popup : Form
        {
            public Popup()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Padding = new Padding(1);   // le fond (couleur de bordure) fait le contour
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams p = base.CreateParams;
                    p.ExStyle |= 0x08000000 | 0x00000080;   // WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW
                    return p;
                }
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0021) { m.Result = (IntPtr)3; return; }   // WM_MOUSEACTIVATE → MA_NOACTIVATE
                base.WndProc(ref m);
            }
        }

        // Liste qui ne prend pas le focus : le survol surligne, un clic choisit ; l'ascenseur et la molette font défiler.
        sealed class PopList : ListBox
        {
            public event Action<int> Picked;

            protected override void WndProc(ref Message m)
            {
                const int WM_MOUSEACTIVATE = 0x0021, WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONDBLCLK = 0x0203;
                if (m.Msg == WM_MOUSEACTIVATE) { m.Result = (IntPtr)3; return; }
                if (m.Msg == WM_LBUTTONDOWN || m.Msg == WM_LBUTTONDBLCLK || m.Msg == WM_MOUSEMOVE)
                {
                    long lp = (long)m.LParam;
                    int i = IndexFromPoint(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                    if (m.Msg == WM_MOUSEMOVE) { if (i >= 0 && i != SelectedIndex) SelectedIndex = i; }
                    else
                    {
                        if (i >= 0 && Picked != null) Picked(i);
                        return;   // pas de focus pour la liste
                    }
                }
                base.WndProc(ref m);
            }
        }
    }
}
