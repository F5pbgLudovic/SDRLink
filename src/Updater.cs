using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SDRLink
{
    // Mise à jour automatique : SDRLink.exe de la dernière version publiée sur GitHub est téléchargé à côté de l'exe
    // utilisé, vérifié (taille, empreinte SHA-256 donnée par GitHub, numéro de version du fichier), puis l'exe utilisé
    // est renommé en « SDRLink.previous.exe » (Windows le permet même quand il tourne), le nouveau prend sa place (même nom :
    // les raccourcis marchent toujours) et
    // est lancé avec « --after-update <pid> » : il attend que l'ancien soit fermé avant de démarrer, puis supprime
    // SDRLink.previous.exe (depuis la 1.30).
    public static class Updater
    {
        public const string AssetName = "SDRLink.exe";
        public const string AfterUpdateArg = "--after-update";

        // Fichier téléchargé de la dernière version : adresse, taille et empreinte SHA-256 (null si GitHub n'en donne pas).
        public class Asset
        {
            public string Url, Sha256;
            public long Size;
        }

        // Réponse de l'API GitHub (dernière version) → fichier SDRLink.exe joint, ou null s'il n'y en a pas.
        public static Asset FindExe(string json)
        {
            try
            {
                Dictionary<string, object> rel = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(json);
                object list;
                if (rel == null || !rel.TryGetValue("assets", out list) || !(list is ArrayList)) return null;
                foreach (object o in (ArrayList)list)
                {
                    Dictionary<string, object> a = o as Dictionary<string, object>;
                    if (a == null || !string.Equals(Str(a, "name"), AssetName, StringComparison.OrdinalIgnoreCase)) continue;
                    string url = Str(a, "browser_download_url");
                    if (url == null || !url.StartsWith("https://github.com/F5pbgLudovic/SDRLink/releases/download/", StringComparison.OrdinalIgnoreCase)) return null;
                    long size = 0;
                    object s;
                    if (a.TryGetValue("size", out s) && s != null) long.TryParse(s.ToString(), out size);
                    string digest = Str(a, "digest");
                    string sha = digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest.Substring(7).ToLowerInvariant() : null;
                    if (sha != null && sha.Length != 64) sha = null;
                    return size > 0 ? new Asset { Url = url, Size = size, Sha256 = sha } : null;
                }
            }
            catch (Exception ex) { Log.Write("Mise à jour : réponse de GitHub illisible (" + ex.Message + ")"); }
            return null;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? v.ToString() : null;
        }

        // Fichier temporaire du téléchargement, dans le dossier de l'exe (le remplacement est alors un simple renommage).
        public static string DownloadPath(string exe) { return exe + ".download"; }

        public static string PreviousPath(string exe)
        {
            return Path.Combine(Path.GetDirectoryName(exe), Path.GetFileNameWithoutExtension(exe) + ".previous.exe");
        }

        // Le dossier de l'exe accepte-t-il l'écriture ? (sinon : Program Files sans droits d'administrateur…)
        public static bool CanWrite(string folder)
        {
            string probe = Path.Combine(folder, "SDRLink.write-test." + Process.GetCurrentProcess().Id + ".tmp");
            try
            {
                File.WriteAllBytes(probe, new byte[0]);
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        // Téléchargement (fil d'arrière-plan). progress(octets reçus) ; cancel() vrai = arrêt. Lève une exception en cas d'échec.
        public static void Download(string url, string dest, Action<long> progress, Func<bool> cancel)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;   // TLS 1.2
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 30000;
            req.ReadWriteTimeout = 30000;
            req.UserAgent = "SDRLink/" + Program.Version + " (F5PBG)";
            req.AllowAutoRedirect = true;   // GitHub renvoie vers son serveur de fichiers
            try
            {
                using (WebResponse resp = req.GetResponse())
                using (Stream src = resp.GetResponseStream())
                using (FileStream fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    byte[] buf = new byte[65536];
                    long total = 0;
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (cancel != null && cancel()) throw new OperationCanceledException();
                        fs.Write(buf, 0, n);
                        total += n;
                        if (progress != null) progress(total);
                    }
                }
            }
            catch
            {
                TryDelete(dest);
                throw;
            }
        }

        // Contrôle du fichier téléchargé ; renvoie null s'il est bon, sinon la raison (en clair, pour l'utilisateur).
        public static string Verify(string file, Asset asset, string version)
        {
            FileInfo fi = new FileInfo(file);
            if (!fi.Exists) return Lang.T("le fichier téléchargé a disparu (antivirus ?)", "the downloaded file has disappeared (antivirus?)");
            if (fi.Length != asset.Size)
                return Lang.F("taille incorrecte ({0} octets au lieu de {1})", "wrong size ({0} bytes instead of {1})", fi.Length, asset.Size);
            if (asset.Sha256 != null)
            {
                string sha = Sha256(file);
                if (sha != asset.Sha256) return Lang.T("empreinte SHA-256 différente de celle publiée sur GitHub", "SHA-256 fingerprint differs from the one published on GitHub");
            }
            string fv = null;
            try { fv = FileVersionInfo.GetVersionInfo(file).FileVersion; } catch { }
            Version a, b;
            if (fv == null || !Version.TryParse(fv, out a) || !Version.TryParse(version.Contains(".") ? version : version + ".0", out b)
                || a.Major != b.Major || a.Minor != b.Minor)
                return Lang.F("le fichier n'est pas SDRLink V{0}", "the file is not SDRLink V{0}", version);
            return null;
        }

        public static string Sha256(string file)
        {
            using (SHA256 h = SHA256.Create())
            using (FileStream fs = File.OpenRead(file))
            {
                StringBuilder sb = new StringBuilder();
                foreach (byte x in h.ComputeHash(fs)) sb.Append(x.ToString("x2"));
                return sb.ToString();
            }
        }

        // Remplacement : exe → previous, téléchargé → exe. Renvoie null si c'est fait, sinon la raison ;
        // en cas d'échec, l'exe d'origine est remis en place.
        public static string Install(string exe, string downloaded)
        {
            string prev = PreviousPath(exe);
            try
            {
                if (File.Exists(prev)) File.Delete(prev);
            }
            catch (Exception ex) { return Lang.F("impossible de supprimer l'ancienne copie {0} ({1})", "cannot delete the old copy {0} ({1})", Path.GetFileName(prev), ex.Message); }
            try { File.Move(exe, prev); }
            catch (Exception ex) { return Lang.F("impossible de renommer {0} ({1})", "cannot rename {0} ({1})", Path.GetFileName(exe), ex.Message); }
            try { File.Move(downloaded, exe); }
            catch (Exception ex)
            {
                Restore(exe);
                return Lang.F("impossible de mettre la nouvelle version à la place ({0})", "cannot put the new version in place ({0})", ex.Message);
            }
            return null;
        }

        // Retour à l'exe d'avant (nouvelle version introuvable ou bloquée au lancement).
        public static void Restore(string exe)
        {
            string prev = PreviousPath(exe);
            if (!File.Exists(prev)) return;
            try
            {
                if (File.Exists(exe)) File.Move(exe, DownloadPath(exe) + ".rejected");
            }
            catch { }
            try { File.Move(prev, exe); } catch (Exception ex) { Log.Write("Mise à jour : retour à l'ancienne version impossible (" + ex.Message + ")"); }
            TryDelete(DownloadPath(exe) + ".rejected");
        }

        // Au démarrage de la nouvelle version : attente de la fermeture de l'ancienne (ports, réglages, WebView2),
        // puis ménage des fichiers temporaires. Renvoie vrai si SDRLink vient d'être mis à jour.
        public static bool AfterUpdate(string[] args)
        {
            bool updated = false;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                int pid;
                if (args[i] != AfterUpdateArg || !int.TryParse(args[i + 1], out pid)) continue;
                updated = true;
                try
                {
                    using (Process p = Process.GetProcessById(pid))
                        if (!p.WaitForExit(60000)) Log.Write("Mise à jour : l'ancienne version ne s'est pas fermée en 60 s");
                }
                catch (ArgumentException) { }   // déjà fermée
                catch (Exception) { }
            }
            try
            {
                string exe = Application.ExecutablePath;
                TryDelete(DownloadPath(exe));
                TryDelete(DownloadPath(exe) + ".rejected");
                DeletePrevious(exe, updated);
            }
            catch { }
            return updated;
        }

        // Depuis la 1.30 : l'ancienne version (SDRLink.previous.exe) est supprimée du disque dès que la nouvelle a démarré
        // et que l'ancienne est fermée ; le raccourci du bureau, qui vise SDRLink.exe, lance donc toujours la nouvelle.
        // Fait à chaque démarrage (copies laissées par les mises à jour d'avant la 1.30, ou ancienne version encore ouverte
        // la fois précédente). Juste après une mise à jour, quelques essais : Windows libère le fichier peu après la fermeture.
        public static void DeletePrevious(string exe, bool justUpdated)
        {
            string prev = PreviousPath(exe);
            if (!File.Exists(prev)) return;
            for (int i = 0; i < (justUpdated ? 20 : 1); i++)
            {
                try
                {
                    File.Delete(prev);
                    Log.Write("Mise à jour : ancienne version supprimée (" + Path.GetFileName(prev) + ")");
                    return;
                }
                catch (Exception ex)
                {
                    if (i == (justUpdated ? 19 : 0)) Log.Write("Mise à jour : ancienne version " + Path.GetFileName(prev) + " pas encore supprimée (" + ex.Message + ")");
                    else Thread.Sleep(250);
                }
            }
        }

        static void TryDelete(string f)
        {
            try { if (File.Exists(f)) File.Delete(f); } catch { }
        }
    }

    // Fenêtre de téléchargement : barre de progression et bouton Annuler.
    class UpdateProgressForm : Form
    {
        readonly ProgressBar bar;
        readonly Label text;
        volatile bool cancelled;
        bool finished;
        public string Error;          // null = réussi
        public bool Cancelled { get { return cancelled; } }

        public UpdateProgressForm(Font font, string version, Updater.Asset asset, string dest)
        {
            Text = Lang.F("SDRLink : mise à jour V{0}", "SDRLink: update V{0}", version);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = font;
            text = new Label { AutoSize = true, Location = new Point(14, 16), Text = Lang.F("Téléchargement de SDRLink V{0}…", "Downloading SDRLink V{0}…", version) };
            bar = new ProgressBar { Location = new Point(14, 46), Size = new Size(440, 22), Maximum = 1000 };
            Button cancel = new Button { Text = Lang.T("Annuler", "Cancel") };
            int cw = Math.Max(110, TextRenderer.MeasureText(cancel.Text, font).Width + 30);
            cancel.SetBounds(454 - cw, 80, cw, 30);
            cancel.Click += delegate { cancelled = true; cancel.Enabled = false; };
            Controls.Add(text);
            Controls.Add(bar);
            Controls.Add(cancel);
            CancelButton = cancel;
            ClientSize = new Size(468, 124);
            Theme.Apply(this);
            // Fermeture par la croix pendant le téléchargement : comme Annuler (la fenêtre se ferme quand le fil s'arrête).
            FormClosing += delegate(object s, FormClosingEventArgs e) { if (!finished) { cancelled = true; e.Cancel = true; } };
            Shown += delegate
            {
                Thread t = new Thread(delegate()
                {
                    string err = null;
                    try
                    {
                        Updater.Download(asset.Url, dest, delegate(long got)
                        {
                            int v = (int)Math.Min(1000, got * 1000 / Math.Max(1, asset.Size));
                            try { BeginInvoke((Action)delegate { if (bar.Value != v) bar.Value = v; }); } catch { }
                        }, delegate { return cancelled; });
                        try { BeginInvoke((Action)delegate { text.Text = Lang.T("Vérification du fichier…", "Checking the file…"); }); } catch { }
                        err = Updater.Verify(dest, asset, version);
                    }
                    catch (OperationCanceledException) { err = ""; }
                    catch (Exception ex) { err = ex.Message; }
                    if (cancelled && err == null) err = "";
                    try { BeginInvoke((Action)delegate { Error = err; finished = true; DialogResult = err == null ? DialogResult.OK : DialogResult.Cancel; }); } catch { }
                });
                t.IsBackground = true;
                t.Start();
            };
        }
    }
}
