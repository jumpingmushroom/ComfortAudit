using System.Collections.Generic;
using UnityEngine;

namespace ComfortAudit.Model
{
    /// <summary>Why a nearby comfort piece did or did not contribute.</summary>
    public enum PieceStatus
    {
        /// <summary>First of its run in the game's sorted list; its comfort was added.</summary>
        Counted,

        /// <summary>A higher-value piece of the same comfort group won. Safe to remove.</summary>
        ShadowedByGroup,

        /// <summary>
        /// Dropped by the game's <c>m_name == prev.m_name</c> clause. Note this test is applied
        /// regardless of group, so it can fire across a group boundary.
        /// </summary>
        ShadowedByName
    }

    public sealed class PieceEntry
    {
        public string PrefabName;
        public string DisplayName;

        /// <summary>Raw m_name token. The game deduplicates on this, not on the localized name.</summary>
        public string NameToken;

        public Piece.ComfortGroup Group;

        /// <summary>False when a mod supplied a ComfortGroup value outside the vanilla enum.</summary>
        public bool GroupKnown;

        /// <summary>What the piece actually contributed — <c>Piece.GetComfort()</c>.</summary>
        public int Comfort;

        /// <summary>The designed value — <c>Piece.m_comfort</c>. Differs from Comfort when Inactive.</summary>
        public int RawComfort;

        /// <summary>
        /// <c>m_comfortObject</c> exists but is switched off, so GetComfort() returned 0.
        /// An unlit fire or lantern: switching it on is a free gain.
        /// </summary>
        public bool Inactive;

        public PieceStatus Status;

        /// <summary>Display name of the piece that shadowed this one, when shadowed.</summary>
        public string ShadowedBy;

        public float Distance;
        public Sprite Icon;

        public bool Contributing => Status == PieceStatus.Counted && Comfort > 0;
    }

    /// <summary>One of the seven conditions Player.UpdateEnvStatusEffects requires for Resting.</summary>
    public sealed class GateCondition
    {
        public string Token;
        public bool Met;

        public GateCondition(string token, bool met)
        {
            Token = token;
            Met = met;
        }
    }

    public sealed class RestGateResult
    {
        public List<GateCondition> Conditions = new List<GateCondition>();
        public bool CanRest;
        public bool NearFire;
        public bool Sheltered;
        public bool Sitting;

        public bool AllMet
        {
            get
            {
                for (int i = 0; i < Conditions.Count; i++)
                    if (!Conditions[i].Met) return false;
                return true;
            }
        }
    }

    public enum RecommendationKind
    {
        /// <summary>An already-placed piece that is switched off. Costs nothing.</summary>
        LightIt,

        /// <summary>Build a roof. Unsheltered, furniture contributes nothing at all.</summary>
        Shelter,

        /// <summary>A comfort group with nothing in range — usually the cheapest win.</summary>
        NewGroup,

        /// <summary>
        /// An ungrouped piece. It adds its full value alongside whatever is already there rather
        /// than competing for a group slot, so it is neither an upgrade nor a new group.
        /// </summary>
        Stacking,

        /// <summary>Beats the piece currently winning its group.</summary>
        Upgrade
    }

    public sealed class MaterialLine
    {
        public string DisplayName;
        public int Amount;
        public int Have;
        public bool Enough => Have >= Amount;
    }

    public sealed class Recommendation
    {
        public RecommendationKind Kind;
        public string DisplayName;
        public string PrefabName;
        public Piece.ComfortGroup Group;
        public bool GroupKnown;

        public int Gain;
        public float CostScore;

        public List<MaterialLine> Materials = new List<MaterialLine>();
        public bool HaveAllMaterials;
        public bool StationInRange;

        /// <summary>Display name of the piece this would replace, when it is an Upgrade.</summary>
        public string Replaces;

        /// <summary>True for recommendations that need no materials at all.</summary>
        public bool Free => Kind == RecommendationKind.LightIt;
    }

    /// <summary>
    /// Why candidates did not make the list. An empty recommendation list is a legitimate state
    /// (a fully-optimised base), but it is indistinguishable from a filter bug without this.
    /// </summary>
    public sealed class RecommendationStats
    {
        public int Catalogue;

        /// <summary>Not offered by any build tool right now: no piece table, or out of season.</summary>
        public int NotInMenu;
        public int NoGain;
        public int RecipeUnknown;
        public int StationOutOfRange;
        public int MaterialsShort;
        public int Candidates;

        public override string ToString()
        {
            return string.Format(
                "catalogue={0} notInMenu={1} noGain={2} recipeUnknown={3} stationOutOfRange={4} materialsShort={5} -> candidates={6}",
                Catalogue, NotInMenu, NoGain, RecipeUnknown, StationOutOfRange, MaterialsShort, Candidates);
        }
    }

    public sealed class PlacementPreviewResult
    {
        /// <summary>True when a comfort-bearing build ghost is currently shown.</summary>
        public bool Active;

        public string DisplayName;
        public Piece.ComfortGroup Group;
        public bool GroupKnown;

        /// <summary>The piece's designed comfort value.</summary>
        public int PieceComfort;

        public int Delta;
        public int NewTotal;
        public float Distance;
        public bool InRange;

        /// <summary>What already beats it, when the delta is zero.</summary>
        public string BlockedBy;
    }

    public sealed class ComfortSnapshot
    {
        public bool Valid;
        public float Time;

        /// <summary>Comfort as this mod computed it, replicating SE_Rested.CalculateComfortLevel.</summary>
        public int ComfortLevel;

        /// <summary>Player.GetComfortLevel() — the game's own cache, refreshed only every 2 s.</summary>
        public int VanillaComfortLevel;

        /// <summary>
        /// True when our replica disagrees with the game beyond the 2 s cache lag can explain.
        /// Surfaced rather than hidden: it means another mod is patching the calculation.
        /// </summary>
        public bool Mismatch;

        public bool InShelter;
        public float CoverPercentage;
        public bool UnderRoof;

        /// <summary>What comfort would be at this spot if the shelter test passed.</summary>
        public int PotentialIfSheltered;

        public float RestedSeconds;
        public float BaseTTL;
        public float TTLPerLevel;
        public bool TtlFromLiveEffect;

        public RestGateResult Gate;
        public List<PieceEntry> Pieces = new List<PieceEntry>();
        public List<Piece.ComfortGroup> MissingGroups = new List<Piece.ComfortGroup>();
        public List<Recommendation> Recommendations = new List<Recommendation>();

        /// <summary>False while the prefab catalogue is still being built.</summary>
        public bool RecommendationsReady;

        public RecommendationStats RecStats;

        /// <summary>Highest comfort reachable at a sheltered spot, unlocked-only and overall.</summary>
        public int CeilingUnlocked;
        public int CeilingAll;
        public bool CeilingValid;

        public int PieceCount => Pieces.Count;
    }
}
