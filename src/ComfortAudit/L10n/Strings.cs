using System.IO;
using System.Reflection;
using Jotunn.Entities;
using Jotunn.Managers;

namespace ComfortAudit.L10n
{
    /// <summary>
    /// Registers our tokens with Jotunn's LocalizationManager, which is public where the game's
    /// own Localization.AddWord is not. Lookups then go through the game's localizer, so piece
    /// and item names come back in whatever language the player runs.
    /// </summary>
    public static class Strings
    {
        public static void Register()
        {
            CustomLocalization loc = LocalizationManager.Instance.GetLocalization();

            // English is the fallback Jotunn uses for any token a language is missing, so it must
            // always be registered.
            Add(loc, "English", "ComfortAudit.L10n.en.json");
            Add(loc, "Norwegian", "ComfortAudit.L10n.nb.json");
        }

        private static void Add(CustomLocalization loc, string language, string resource)
        {
            string json = ReadEmbedded(resource);
            if (!string.IsNullOrEmpty(json))
                loc.AddJsonFile(language, json);
        }

        public static string Get(string token)
        {
            if (string.IsNullOrEmpty(token))
                return string.Empty;

            if (token[0] != '$')
                token = "$" + token;

            return Localization.instance != null
                ? Localization.instance.Localize(token)
                : token;
        }

        public static string Get(string token, params object[] args)
        {
            string s = Get(token);
            return args == null || args.Length == 0 ? s : string.Format(s, args);
        }

        private static string ReadEmbedded(string name)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream stream = asm.GetManifestResourceStream(name))
            {
                if (stream == null)
                    return null;
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }
    }
}
