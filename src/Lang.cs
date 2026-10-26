namespace SDRLink
{
    // Langue de l'interface. Le journal de diagnostic reste en français.
    public static class Lang
    {
        public static bool English;

        public static string T(string fr, string en)
        {
            return English ? en : fr;
        }
    }
}
