using ComfortAudit.Model;

namespace ComfortAudit.Core
{
    /// <summary>
    /// Rested duration is m_baseTTL + (comfort - 1) * m_TTLPerComfortLevel.
    ///
    /// Both fields are Unity-serialized on the SE_Rested asset, so the C# initialisers
    /// (300f / 60f) are not authoritative — the shipped prefab can override them. We always read
    /// the real values: from the live effect when Rested is active, otherwise from the ObjectDB
    /// prefab, and only fall back to the initialisers if neither is reachable.
    /// </summary>
    public static class RestedMath
    {
        private const float FallbackBaseTTL = 300f;
        private const float FallbackPerLevel = 60f;

        public static void Fill(ComfortSnapshot snap)
        {
            float baseTtl = FallbackBaseTTL;
            float perLevel = FallbackPerLevel;
            bool fromLive = false;

            SE_Rested rested = Resolve(out fromLive);
            if (rested != null)
            {
                baseTtl = rested.m_baseTTL;
                perLevel = rested.m_TTLPerComfortLevel;
            }

            snap.BaseTTL = baseTtl;
            snap.TTLPerLevel = perLevel;
            snap.TtlFromLiveEffect = fromLive;
            snap.RestedSeconds = baseTtl + (snap.ComfortLevel - 1) * perLevel;
        }

        private static SE_Rested Resolve(out bool fromLive)
        {
            fromLive = false;

            Player player = Player.m_localPlayer;
            if (player != null)
            {
                SEMan seman = player.GetSEMan();
                if (seman != null)
                {
                    var live = seman.GetStatusEffect(SEMan.s_statusEffectRested) as SE_Rested;
                    if (live != null)
                    {
                        fromLive = true;
                        return live;
                    }
                }
            }

            // Not currently Rested — read the prefab so the panel can still show a real number.
            if (ObjectDB.instance != null)
                return ObjectDB.instance.GetStatusEffect(SEMan.s_statusEffectRested) as SE_Rested;

            return null;
        }

        public static string Format(float seconds)
        {
            if (seconds < 0f) seconds = 0f;
            int total = (int)seconds;
            return string.Format("{0}:{1:00}", total / 60, total % 60);
        }
    }
}
