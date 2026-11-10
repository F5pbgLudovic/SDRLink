using System;
using System.Device.Location;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;

namespace SDRLink
{
    // Position de la station pour la recherche de WebSDR (depuis la 1.30) : locator ou coordonnées tapés,
    // sinon position de l'ordinateur donnée par le service de localisation de Windows (GPS, Wi-Fi ou réseau).
    public static class GeoPosition
    {
        // Locator Maidenhead (4, 6 ou 8 caractères : JN18, JN18du, JN18du42) ou coordonnées décimales
        // (« 48.85, 2.35 », « 48,85 2,35 », « 48.85;2.35 », avec N/S/E/O/W possibles). Le centre de la case pour un locator.
        public static bool Parse(string text, out double lat, out double lon)
        {
            lat = lon = double.NaN;
            string s = (text ?? "").Trim();
            if (s == "") return false;
            if (ParseLocator(s, out lat, out lon)) return true;
            return ParseCoordinates(s, out lat, out lon);
        }

        static readonly Regex LocatorRx = new Regex("^[A-R]{2}[0-9]{2}([A-X]{2}([0-9]{2})?)?$", RegexOptions.IgnoreCase);

        public static bool ParseLocator(string text, out double lat, out double lon)
        {
            lat = lon = double.NaN;
            string s = (text ?? "").Trim().ToUpperInvariant();
            if (!LocatorRx.IsMatch(s)) return false;
            double x = (s[0] - 'A') * 20 - 180 + (s[2] - '0') * 2, y = (s[1] - 'A') * 10 - 90 + (s[3] - '0');
            double w = 2, h = 1;
            if (s.Length >= 6) { w = 2.0 / 24; h = 1.0 / 24; x += (s[4] - 'A') * w; y += (s[5] - 'A') * h; }
            if (s.Length >= 8) { w /= 10; h /= 10; x += (s[6] - '0') * w; y += (s[7] - '0') * h; }
            lon = x + w / 2;
            lat = y + h / 2;
            return true;
        }

        static bool ParseCoordinates(string s, out double lat, out double lon)
        {
            lat = lon = double.NaN;
            string[] p;
            if (s.IndexOf(';') >= 0) p = s.Split(';');
            else if (Regex.IsMatch(s, @"\S\s+\S")) p = Regex.Split(s.Replace(", ", " ").Replace(" ,", " "), @"\s+");
            else p = s.Split(',');
            if (p.Length != 2) return false;
            return Coordinate(p[0], "NS", out lat) && Coordinate(p[1], "EWO", out lon)
                && Math.Abs(lat) <= 90 && Math.Abs(lon) <= 180;
        }

        // Un nombre décimal (virgule ou point), suivi ou précédé d'une lettre de direction (S, W ou O : négatif).
        static bool Coordinate(string t, string dirs, out double v)
        {
            v = double.NaN;
            t = t.Trim().Replace(',', '.').Replace("°", "").ToUpperInvariant();
            int sign = 1;
            if (t.Length > 0 && dirs.IndexOf(t[t.Length - 1]) >= 0) { if ("SWO".IndexOf(t[t.Length - 1]) >= 0) sign = -1; t = t.Substring(0, t.Length - 1).Trim(); }
            else if (t.Length > 0 && dirs.IndexOf(t[0]) >= 0) { if ("SWO".IndexOf(t[0]) >= 0) sign = -1; t = t.Substring(1).Trim(); }
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return false;
            v *= sign;
            return true;
        }

        // Locator de 6 caractères (JN18du) d'une position.
        public static string ToLocator(double lat, double lon)
        {
            double x = Math.Max(0, Math.Min(359.9999, lon + 180)), y = Math.Max(0, Math.Min(179.9999, lat + 90));
            char a = (char)('A' + (int)(x / 20)), b = (char)('A' + (int)(y / 10));
            x %= 20; y %= 10;
            char c = (char)('0' + (int)(x / 2)), d = (char)('0' + (int)y);
            x %= 2; y %= 1;
            char e = (char)('a' + (int)(x * 12)), f = (char)('a' + (int)(y * 24));
            return new string(new[] { a, b, c, d, e, f });
        }

        // Position de l'ordinateur par le service de localisation de Windows. Bloquant (jusqu'à timeoutMs) : à appeler
        // hors du fil de l'interface. Faux si la localisation est désactivée, refusée aux applications de bureau ou trop lente.
        public static bool FromWindows(int timeoutMs, out double lat, out double lon)
        {
            lat = lon = double.NaN;
            try
            {
                using (GeoCoordinateWatcher w = new GeoCoordinateWatcher(GeoPositionAccuracy.Default))
                {
                    DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
                    if (!w.TryStart(true, TimeSpan.FromMilliseconds(timeoutMs))) return false;
                    while (DateTime.UtcNow < end)
                    {
                        GeoCoordinate c = w.Position.Location;
                        if (c != null && !c.IsUnknown) { lat = c.Latitude; lon = c.Longitude; return true; }
                        if (w.Permission == GeoPositionPermission.Denied || w.Status == GeoPositionStatus.Disabled) return false;
                        Thread.Sleep(200);
                    }
                }
            }
            catch (Exception ex) { Log.Write("Position de l'ordinateur : " + ex.Message); }
            return false;
        }
    }
}
