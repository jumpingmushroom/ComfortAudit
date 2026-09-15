using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;

namespace ComfortAudit.Core
{
    /// <summary>
    /// Relative material cost weights, so "10 wood" does not rank equal to "10 iron".
    ///
    /// Any weight is an opinion, so this is deliberately soft: unknown materials score 1.0, which
    /// degrades the ranking to raw item count — defensible and fully explainable. Cost is only
    /// ever a tie-break behind comfort gain, so a wrong weight never promotes a worse upgrade
    /// above a better one.
    ///
    /// Keys are item localization tokens ("$item_wood"), which are stable across languages.
    /// An optional BepInEx/config/ComfortAudit.costs.json overrides the shipped defaults.
    /// </summary>
    public static class CostTable
    {
        public const float DefaultWeight = 1.0f;
        private const string OverrideFileName = "ComfortAudit.costs.json";

        private static Dictionary<string, float> _weights;

        /// <summary>Tokens seen on real pieces that had no entry — surfaced by the console command.</summary>
        private static readonly HashSet<string> Unknown = new HashSet<string>();

        public static void Load()
        {
            _weights = new Dictionary<string, float>(StringComparer.Ordinal);
            Unknown.Clear();

            Parse(ReadEmbedded("ComfortAudit.L10n.costs.json"));

            string overridePath = Path.Combine(Paths.ConfigPath, OverrideFileName);
            if (File.Exists(overridePath))
            {
                try
                {
                    Parse(File.ReadAllText(overridePath));
                    ComfortAuditPlugin.Log.LogInfo("cost weights overridden from " + OverrideFileName);
                }
                catch (Exception ex)
                {
                    ComfortAuditPlugin.Log.LogWarning(
                        "could not read " + OverrideFileName + ", using defaults: " + ex.Message);
                }
            }
        }

        public static float Weight(string itemToken)
        {
            if (_weights == null)
                Load();

            if (string.IsNullOrEmpty(itemToken))
                return DefaultWeight;

            float w;
            if (_weights.TryGetValue(itemToken, out w))
                return w;

            Unknown.Add(itemToken);
            return DefaultWeight;
        }

        /// <summary>
        /// Whether a token has an explicit entry. Distinct from "weight == 1.0": a material can
        /// be deliberately weighted 1.0, and reporting that as unconfigured is misleading.
        /// </summary>
        public static bool HasWeight(string itemToken)
        {
            if (_weights == null)
                Load();

            return !string.IsNullOrEmpty(itemToken) && _weights.ContainsKey(itemToken);
        }

        public static IEnumerable<string> UnknownTokens => Unknown;

        public static int Count => _weights == null ? 0 : _weights.Count;

        /// <summary>
        /// Minimal flat string-to-number JSON reader, so a file this simple needs no JSON
        /// dependency. It must skip string values properly: an earlier version scanned to the
        /// next comma, which meant a comment value containing a comma swallowed the key that
        /// followed it (this silently ate "$item_wood").
        /// </summary>
        private static void Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return;

            int i = 0;
            while (i < json.Length)
            {
                int keyStart = json.IndexOf('"', i);
                if (keyStart < 0) return;

                int keyEnd = EndOfString(json, keyStart);
                if (keyEnd < 0) return;

                string key = json.Substring(keyStart + 1, keyEnd - keyStart - 1);

                int colon = json.IndexOf(':', keyEnd);
                if (colon < 0) return;

                int v = colon + 1;
                while (v < json.Length && char.IsWhiteSpace(json[v]))
                    v++;

                if (v >= json.Length)
                    return;

                if (json[v] == '"')
                {
                    // String value (a comment, say). Skip the whole quoted run, commas included.
                    int close = EndOfString(json, v);
                    if (close < 0) return;
                    i = close + 1;
                    continue;
                }

                int valEnd = v;
                while (valEnd < json.Length && json[valEnd] != ',' && json[valEnd] != '}')
                    valEnd++;

                float value;
                if (float.TryParse(json.Substring(v, valEnd - v).Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out value))
                {
                    _weights[key] = value;
                }

                i = valEnd + 1;
            }
        }

        /// <summary>Index of the closing quote of the string starting at <paramref name="open"/>.</summary>
        private static int EndOfString(string json, int open)
        {
            for (int i = open + 1; i < json.Length; i++)
            {
                if (json[i] == '\\')
                {
                    i++;
                    continue;
                }
                if (json[i] == '"')
                    return i;
            }
            return -1;
        }

        private static string ReadEmbedded(string name)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (stream == null)
                    return null;
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd();
            }
        }
    }
}
