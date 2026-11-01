using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json;

namespace UnderveilModLoader
{
    public static class ModTextExtensions
    {
        internal const char ArgSeparator = '\u001F';

        // Packs the arguments into the key instead of resolving the text, because the game
        // looks a key up on every display and the text must follow the current language.
        public static string Format(this string key, params string[] args)
        {
            return key + ArgSeparator + string.Join(ArgSeparator.ToString(), args);
        }
    }

    internal static class ModText
    {
        private const string FallbackLocale = "en";

        private sealed class Bundle
        {
            internal ModEntry Owner;
            internal Dictionary<string, Dictionary<string, string>> Locales;
        }

        private static readonly List<Bundle> bundles = new List<Bundle>();

        // Throws on a broken file before anything is registered, so a mod is never left half loaded.
        internal static void Load(ModEntry entry)
        {
            string langDir = Path.Combine(entry.Directory, "assets", "lang");
            if (!Directory.Exists(langDir))
            {
                return;
            }
            Bundle bundle = new Bundle
            {
                Owner = entry,
                Locales = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
            };
            foreach (string localeDir in Directory.GetDirectories(langDir))
            {
                bundle.Locales[Path.GetFileName(localeDir)] = ReadLocale(entry, localeDir);
            }
            bundles.Add(bundle);
        }

        // A mod that failed to load must not keep providing text.
        internal static void Unload(ModEntry entry) => bundles.RemoveAll(b => b.Owner == entry);

        internal static bool TryGet(string key, out string text)
        {
            return TryGet(LocalizationHelper.CurrentLocaleCode, key, out text)
                || TryGet(FallbackLocale, key, out text);
        }

        private static bool TryGet(string locale, string key, out string text)
        {
            foreach (Bundle bundle in bundles)
            {
                if (bundle.Locales.TryGetValue(locale, out Dictionary<string, string> texts)
                    && texts.TryGetValue(key, out text) && text != null)
                {
                    return true;
                }
            }
            text = null;
            return false;
        }

        private static Dictionary<string, string> ReadLocale(ModEntry entry, string localeDir)
        {
            Dictionary<string, string> texts = new Dictionary<string, string>();
            foreach (string file in Directory.GetFiles(localeDir, "*.json"))
            {
                // An empty file deserializes to null and simply has no keys.
                Dictionary<string, string> read = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(file));
                if (read == null)
                {
                    continue;
                }
                string prefix = $"{entry.Id}.{Path.GetFileNameWithoutExtension(file)}.";
                foreach (KeyValuePair<string, string> pair in read)
                {
                    texts[prefix + pair.Key] = pair.Value;
                }
            }
            return texts;
        }
    }

    // Every game text lookup goes through this method, so mod text needs no other hook.
    [HarmonyPatch(typeof(LocalizationHelper), "GetRawInternal")]
    internal static class GetRawInternalPatch
    {
        private static bool Prefix(string key, object[] args, ref bool found, ref string __result)
        {
            string[] parts = key.Split(ModTextExtensions.ArgSeparator);
            if (!ModText.TryGet(parts[0], out string text))
            {
                return true;
            }
            found = true;
            __result = Format(text, args != null && args.Length > 0 ? args : parts.Skip(1).ToArray<object>());
            return false;
        }

        private static string Format(string text, object[] args)
        {
            if (args.Length == 0)
            {
                return text;
            }
            try
            {
                return string.Format(text, args);
            }
            catch (FormatException)
            {
                return text;
            }
        }
    }
}
