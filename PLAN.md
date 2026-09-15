# Comfort Audit — Technical Plan

**Target build:** Valheim **1.0.12** (`Version.CurrentVersion = new GameVersion(1, 0, 12)`), network version **40**, Deep North content version.
**Analysis source:** `assembly_valheim.dll`, `assembly_utils.dll`, `assembly_guiutils.dll` from Steam app **896660** (Valheim Dedicated Server), depot **896661**, pulled anonymously with DepotDownloader and decompiled with ILSpy 9.1. The dedicated-server build ships the same unified `assembly_valheim.dll` as the client, including `Hud`, `InventoryGui` and `StatusEffect`, so every type referenced below is real and present.

**Verified against the real client (2026-09-15).** The client's `assembly_valheim.dll` from `gamingrig01` (`/games/SteamLibrary/steamapps/common/Valheim`, 2,568,192 bytes) differs from the server build (2,560,000 bytes), so I decompiled it separately and diffed. Every comfort-critical file — `SE_Rested.cs`, `SE_Cozy.cs`, `Piece.cs`, `Player.cs`, `SEMan.cs`, `StatusEffect.cs`, `SE_Stats.cs`, `Hud.cs`, `PieceTable.cs`, `CraftingStation.cs` — is **byte-identical** between the two builds. Only `Terminal.cs` differs (client-only console plumbing); all dev commands cited in §5.5 are present in the client. Both report `GameVersion(1, 0, 12)` and `c_networkVersion = 40u`. Everything below therefore holds against the machine you will actually run this on.

Reproduce the analysis:

```bash
DepotDownloader -app 896660 -os linux -osarch 64 \
  -filelist <(echo 'regex:.*[Mm]anaged/.*\.dll$') -dir ./valheim_server
ilspycmd -o ./src -p -r ./valheim_server/valheim_server_Data/Managed assembly_valheim.dll
```

---

## 1. What the code actually says

### 1.1 Corrections to the brief

Seven of your assumptions need adjusting, and two of them change the product.

| # | Brief said | Code says |
|---|---|---|
| 1 | Rested ≈ 8 min base + 1 min/level | ~~`m_baseTTL = 300f` (5 min)~~ — **you were right, I was wrong.** Verified at runtime 2026-09-15: the shipped prefab overrides the C# initialiser with **`m_baseTTL = 480f` (8 min)**. `m_TTLPerComfortLevel = 60f` confirmed. Formula: `ttl = 480 + (comfort − 1) × 60`. |
| 2 | "possibly with a cap on comfort" | **No cap anywhere.** `PlayerStatType.MaxComfort` is only a lifetime achievement stat. |
| 3 | Shelter is a separate requirement to report alongside comfort | **Shelter gates the entire calculation.** Unsheltered comfort is a hard `1` and *no furniture counts at all*. |
| 4 | Comfort starts from the pieces | Sheltered baseline is **2** — `1` floor, `+1` for being sheltered — before any furniture. |
| 5 | Groups: Fire, Bed, Chair, Table, Banner, Rug, Carpet/Decor | `None, Fire, Bed, Banner, Chair, Table, Carpet, Display, Decor, Garland, Lantern, Leisure`. **No "Rug"**; you missed Display, Garland, Lantern, Leisure. |
| 6 | "Only the highest `m_comfort` per group counts" | True *only for grouped pieces*. **`ComfortGroup.None` pieces stack** — they're deduped by display name, not by group. |
| 7 | Method is `CalculateComfortLevel(Player)` on `SE_Rested` | Correct, and there's a second, far more useful overload taking an arbitrary position. |

**Resolved — and this is the cautionary tale of the whole document.** `m_baseTTL` and `m_TTLPerComfortLevel` are `public float` fields under `[Header("__SE_Rested__")]`, i.e. Unity-serialized and overridable on the shipped `SE_Rested` asset. `300f`/`60f` are the *C# field initialisers*, which Unity discards whenever the asset stores its own values — and it does. Decompilation showed `300f` and I flagged it as the one number I could not confirm; the runtime read says **480f**, matching the 8 minutes you remembered.

Two things follow. First, at comfort 10 the observed duration was 1020 s = `480 + 9 × 60`, so the *formula* was right even though the constant was not. Second, and more generally: **a serialized field's initialiser in decompiled Unity code is a default, not a value.** Anything in this document read from a `public` field on a `MonoBehaviour` or `ScriptableObject` carries the same caveat — including every `m_comfort` and `m_comfortGroup`. That is precisely why `PieceCatalog` reads them at runtime instead of shipping a table, and why the mod reads these two fields off the live effect or the ObjectDB prefab rather than hardcoding either number.

### 1.2 `SE_Rested` — exact signatures

`SE_Rested : SE_Stats`

```csharp
public float m_baseTTL = 300f;
public float m_TTLPerComfortLevel = 60f;
private const float c_ComfortRadius = 10f;
private static readonly List<Piece> s_tempPieces;

public  static int  CalculateComfortLevel(Player player);
public  static int  CalculateComfortLevel(bool inShelter, Vector3 position);
private static int  PieceComfortSort(Piece x, Piece y);
private static List<Piece> GetNearbyComfortPieces(Vector3 point);
private void UpdateTTL();
public override void Setup(Character character);
public override void ResetTime();
public override void UpdateStatusEffect(float dt);
```

`public static int CalculateComfortLevel(bool inShelter, Vector3 position)` is the single most valuable method in the game for this mod: it is public, static, side-effect-free, and takes an arbitrary position and shelter flag. It makes hypotheticals ("what if I were sheltered?", "what if I stood there?") a one-line call, and it makes the v0.4 placement preview nearly free.

The algorithm, verbatim:

```csharp
int num = 1;
if (inShelter)
{
    num++;                                        // shelter itself is +1
    var pieces = GetNearbyComfortPieces(position); // Piece.GetAllComfortPiecesInRadius(position, 10f, ...)
    pieces.Sort(PieceComfortSort);
    for (int i = 0; i < pieces.Count; i++)
    {
        Piece piece = pieces[i];
        if (i > 0)
        {
            Piece prev = pieces[i - 1];
            if ((piece.m_comfortGroup != Piece.ComfortGroup.None
                 && piece.m_comfortGroup == prev.m_comfortGroup)
                || piece.m_name == prev.m_name)
                continue;
        }
        num += piece.GetComfort();
    }
}
return num;
```

And the comparer:

```csharp
// group ascending (enum order) → comfort DESCENDING → m_name descending
if (x.m_comfortGroup != y.m_comfortGroup) return x.m_comfortGroup.CompareTo(y.m_comfortGroup);
float a = x.GetComfort(), b = y.GetComfort();
if (a != b) return b.CompareTo(a);
return y.m_name.CompareTo(x.m_name);
```

**Why this matters.** The dedup is *adjacency-based on a sorted list*, not a per-group max. Four consequences the audit can surface and nobody else does:

1. **Grouped pieces** — sorting by group then comfort-descending puts the best piece first in each group run, and every later member of that run trips the group clause. Net effect is "highest per group", so your mental model holds *here*.
2. **`ComfortGroup.None` pieces stack.** The group clause is explicitly skipped for `None`, leaving only the `m_name` equality test. Two *different* None-group comfort pieces both count. This is the biggest actionable insight in the whole mod.
3. **Duplicate-name suppression crosses group boundaries.** The `piece.m_name == prev.m_name` clause is evaluated regardless of group. If the last piece of one group and the first piece of the next share a display token, the second is silently dropped. Rare, but real, and the audit should attribute it honestly rather than guess.
4. **Inactive pieces don't shadow.** `GetComfort()` (below) returns `0` for a piece whose comfort object is switched off, and the sort uses `GetComfort()`, so an unlit hearth sorts to the bottom of the Fire group and a lit campfire still wins. The game gets this right; the audit should say so rather than implying the hearth is blocking anything.

`UpdateTTL()` only ever *extends*:

```csharp
float want = m_baseTTL + (player.GetComfortLevel() - 1) * m_TTLPerComfortLevel;
float remaining = m_ttl - m_time;
if (want > remaining) { m_ttl = want; m_time = 0f; }
```

So dropping comfort never shortens a Rested buff you already have — worth saying in the UI, because players assume the opposite and panic-rebuild.

### 1.3 `Piece` — comfort data and the registry

```csharp
public enum ComfortGroup
{ None, Fire, Bed, Banner, Chair, Table, Carpet, Display, Decor, Garland, Lantern, Leisure }

[Header("Comfort")]
public int m_comfort;
public ComfortGroup m_comfortGroup;
public GameObject m_comfortObject;

private static readonly HashSet<Piece> s_allComfortPieces;   // Awake() adds if m_comfort > 0; OnDestroy() removes
private static int s_ghostLayer;                             // LayerMask.NameToLayer("ghost")

public static void GetAllComfortPiecesInRadius(Vector3 p, float radius, List<Piece> pieces);
public int GetComfort();      // 0 if m_comfortObject != null && !m_comfortObject.activeInHierarchy
```

```csharp
public static void GetAllComfortPiecesInRadius(Vector3 p, float radius, List<Piece> pieces)
{
    foreach (Piece piece in s_allComfortPieces)
        if (piece.gameObject.layer != s_ghostLayer && Vector3.Distance(p, piece.transform.position) < radius)
            pieces.Add(piece);
}
```

Also on `Piece`, needed for the recommender:

```csharp
public string m_name;                  // localization token, e.g. "$piece_chair"
public string m_description;
public Sprite m_icon;
public PieceCategory m_category;
public CraftingStation m_craftingStation;
public Requirement[] m_resources;

public class Requirement {
    public ItemDrop m_resItem;
    public int m_amount = 1;
    public int m_amountPerLevel = 1;
    public bool m_recover = true;
}
```

**This kills the performance problem in the brief.** There is no physics overlap and no scene scan. `s_allComfortPieces` is a `HashSet` containing *only* comfort-bearing pieces, maintained for free by the game in `Awake`/`OnDestroy`. Iterating it is O(comfort pieces in loaded zones) — tens, maybe low hundreds even in a large base. The 10 m test is a **3-D** `Vector3.Distance` against the piece **pivot**, strictly `<`, so a piece directly above or below you counts and a wide piece is measured from its origin, not its bounds.

We still throttle, but because we do more per piece than the game does (attribution, cost lookup, string building), not because the scan is expensive.

### 1.4 `Player` — comfort cache, shelter, recipes

```csharp
public enum RequirementMode { CanBuild, IsKnown, CanAlmostBuild }

private int m_comfortLevel;
private float m_baseValueUpdateTimer;
private GameObject m_placementGhost;
private readonly HashSet<string> m_knownRecipes;
private readonly Dictionary<string, int> m_knownStations;
private PieceTable m_buildPieces;

private void UpdateBaseValue(float dt);                       // 2 s timer; sets m_comfortLevel
public int  GetComfortLevel();                                // returns the cache; 0 if m_nview == null
public bool InShelter();                                      // m_coverPercentage >= 0.8f && m_underRoof
public bool HaveRequirements(Piece piece, RequirementMode mode);
public bool IsRecipeKnown(string name);
public bool IsKnownMaterial(string name);
public bool IsMaterialKnown(string sharedName);
public PieceTable GetBuildTool();
public List<Piece> GetBuildPieces();                          // selected category only — not what we want
```

`UpdateBaseValue` is where comfort is actually recomputed:

```csharp
m_baseValueUpdateTimer += dt;
if (!(m_baseValueUpdateTimer > 2f)) return;
m_baseValueUpdateTimer = 0f;
m_baseValue = EffectArea.GetBaseValue(transform.position, 20f);
m_comfortLevel = SE_Rested.CalculateComfortLevel(this);
```

**The game already throttles comfort to 2 s.** `GetComfortLevel()` is a cache read, so the HUD number can lag reality by up to two seconds. Our own scan at 0.5 s will legitimately *lead* the vanilla display; the panel should show our fresher number and not treat the difference as a bug.

`HaveRequirements(Piece, RequirementMode)` is exactly the recommender's gate:

```csharp
if (piece.m_craftingStation) {
    if (mode == IsKnown || mode == CanAlmostBuild) {
        if (!m_knownStations.ContainsKey(piece.m_craftingStation.m_name)) return false;
    } else if (!CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, transform.position)
               && !ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench)) return false;
}
if (piece.m_dlc.Length > 0 && !DLCMan.instance.IsDLCInstalled(piece.m_dlc)) return false;
if (mode != IsKnown && ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey())) return true;
foreach (var req in piece.m_resources) {
    if (!req.m_resItem || req.m_amount <= 0) continue;
    switch (mode) {
      case IsKnown:        if (!m_knownMaterial.Contains(req.m_resItem.m_itemData.m_shared.m_name)) return false; break;
      case CanAlmostBuild: if (!m_inventory.HaveItem(req.m_resItem.m_itemData.m_shared.m_name)) return false; break;
      case CanBuild:       if (m_inventory.CountItems(req.m_resItem.m_itemData.m_shared.m_name) < req.m_amount) return false; break;
    }
}
```

Note the subtlety you'll want to know before wiring config: `CanAlmostBuild` checks *known station* (not station in range) **and** requires at least one of every material in inventory. `CanBuild` checks *station in range* and full material counts. Neither is literally "known recipe + station in range" — see §3.2 for how we compose that.

### 1.5 The Rested gate — seven conditions, not two

`Player.UpdateEnvStatusEffects(float dt)`, decompiled with the flags named:

```csharp
bool nearFire   = m_nearFireTimer < 0.25f;                                       // flag
bool burning    = m_seman.HaveStatusEffect(SEMan.s_statusEffectBurning);         // flag2
bool sheltered  = InShelter();                                                   // flag3
bool sensed     = IsSensed();                                                    // flag6  — enemies aware of you
bool wet        = m_seman.HaveStatusEffect(SEMan.s_statusEffectWet);             // flag7
bool sitting    = IsSitting();                                                   // flag8
bool warmCozy   = EffectArea.IsPointInsideArea(pos, EffectArea.Type.WarmCozyArea, 1f); // flag9
bool freezing, cold;                                                             // flag11, flag12

bool canRest = !sensed && (sitting || sheltered) && !cold && !freezing
               && (!wet || warmCozy) && !burning && nearFire;

if (canRest) m_seman.AddStatusEffect(SEMan.s_statusEffectResting, ...);
else         m_seman.RemoveStatusEffect(SEMan.s_statusEffectResting);
```

Two things fall out of this that are worth the whole mod on their own:

- **`(sitting || sheltered)`** — you can be Resting while sitting at a campfire under open sky. You will then get Rested at comfort **1**, i.e. the 300 s minimum, with your entire furnished hall contributing nothing. This is precisely the "perfect comfort setup under open sky" mistake in your brief, and the real mechanic is *worse* than you thought: it doesn't merely fail to apply the roof bonus, it zeroes every piece.
- Wetness is forgiven inside a `WarmCozyArea`, and cold/freezing block resting outright. So "I'm next to a fire and under a roof, why no Rested?" usually resolves to *wet*, *sensed*, or *cold* — none of which the game surfaces.

The audit can report all seven independently. That is a genuinely new capability.

### 1.6 The Resting → Rested chain

Deep North introduced `SE_Cozy`, which is the **Resting** effect:

```csharp
public class SE_Cozy : SE_Stats
{
    public float m_delay = 10f;
    public string m_statusEffect = "";      // → "Rested"
    public override void Setup(Character character);              // messages "$se_resting_start"
    public override void UpdateStatusEffect(float dt);            // after m_delay, adds m_statusEffectHash
    public override string GetIconText();                         // "$se_rested_comfort:" + player.GetComfortLevel()
}
```

Chain: conditions met → **Resting** (`SE_Cozy`) → `m_delay` seconds → **Rested** (`SE_Rested`, ttl from comfort). The comfort number the player sees on the HUD during the wait comes from `SE_Cozy.GetIconText()`, not from `SE_Rested`.

### 1.7 Status-effect access (all public — no reflection needed)

```csharp
// SEMan
public static readonly int s_statusEffectRested   = "Rested".GetStableHashCode();
public static readonly int s_statusEffectResting  = "Resting".GetStableHashCode();
public static readonly int s_statusEffectShelter  = "Shelter".GetStableHashCode();
public static readonly int s_statusEffectCampFire = "CampFire".GetStableHashCode();
public static readonly int s_statusEffectWet, s_statusEffectCold, s_statusEffectFreezing, s_statusEffectBurning;

public bool HaveStatusEffect(int nameHash);
public StatusEffect GetStatusEffect(int nameHash);
public List<StatusEffect> GetStatusEffects();
```

`UpdateEnvStatusEffects` mirrors its internal flags into `Shelter` and `CampFire` status effects, so the mod reads every gate condition through public API:

| Audit needs | Public read path |
|---|---|
| Near a burning fire | `seman.HaveStatusEffect(SEMan.s_statusEffectCampFire)` |
| Sheltered | `player.InShelter()` |
| Resting now | `seman.HaveStatusEffect(SEMan.s_statusEffectResting)` |
| Rested, live ttl | `seman.GetStatusEffect(SEMan.s_statusEffectRested) as SE_Rested` → `m_ttl`, `m_time`, **`m_baseTTL`, `m_TTLPerComfortLevel`** |
| Wet / cold / freezing / burning | `HaveStatusEffect(...)` |
| Sitting | `player.IsSitting()` |
| Sensed | `player.IsSensed()` |
| Comfort (vanilla cache) | `player.GetComfortLevel()` |

That last row in the Rested line is how we solve the `m_baseTTL` uncertainty: read the real serialized values off the live instance.

### 1.8 `Cover` (assembly_utils.dll)

```csharp
public static void GetCoverForPoint(Vector3 startPos, out float coverPercentage, out bool underRoof, float minDistance = 0.5f);
public static bool IsUnderRoof(Vector3 startPos);
// 17 rays, 30 m, mask: Default | static_solid | Default_small | piece | terrain | vehicle
```

Public and static. **But do not call it to report the player's own cover.** `Player.UpdateCover` casts from `GetCenterPoint()` (chest height) once a second and caches into `m_coverPercentage` / `m_underRoof`, and `InShelter()` tests exactly those fields. Recomputing from `transform.position` yields a different number, so the panel would show a percentage that contradicts the shelter verdict beside it. Read the cached fields instead (both private, hence the publicizer). `Cover.GetCoverForPoint` remains the right tool only for evaluating a position the player is *not* standing at.

### 1.9 Localization (assembly_guiutils.dll)

```csharp
public string Localize(string text);
public string Localize(string text, params string[] words);
public bool   LoadCSV(TextAsset file, string language);
public string GetSelectedLanguage();
private void  AddWord(string key, string text);      // PRIVATE
```

Language constants include `Localization.Language.Norwegian`. `AddWord` being private is the only friction; see §4.4.

### 1.10 Vanilla-server safety — confirmed

`ZNet.RPC_PeerInfo` validates exactly two things:

```csharp
ZLog.Log("Network version check, their:" + num + ", mine:" + 40u);
if (num != 40) { /* reject: ErrorVersion */ }
```

…plus the `Version.CurrentVersion` string carried alongside it. **There is no mod hash, no assembly checksum, no plugin enumeration, and no client attestation anywhere in the handshake.** A client-only plugin that registers no RPCs, writes no ZDO fields and sends no `ZPackage` is structurally invisible to a vanilla server. Comfort Audit reads local state and draws UI; it touches none of those. Safe to join vanilla and modded servers alike.

The rules that follow from that, which the implementation must hold to:

- Never call `ZRoutedRpc.Register` / `ZRpc.Register`.
- Never write to `ZDO` or `ZNet.instance.m_serverSyncedPlayerData`.
- Never patch anything in `ZNet`, `ZRoutedRpc`, `ZDOMan`, or `Version`.
- No ServerSync, no config sync, no `BepInEx` network dependencies.

---

## 2. Architecture

### 2.1 Components

```
ComfortAuditPlugin : BaseUnityPlugin     // BepInEx entry, config, Harmony lifecycle
  └─ ComfortAuditBehaviour : MonoBehaviour   // owns the tick, hotkey, panel lifetime

Core/
  RestGate          // evaluates the 7 conditions, returns a typed reason list
  ComfortScanner    // builds the snapshot; replicates sort + adjacency walk for attribution
  PieceCatalog      // lazy catalogue of every comfort-capable prefab in ZNetScene
  Recommender       // gains, costs, ranking
  CostTable         // material weights, editable

Model/              // immutable DTOs, no Unity types beyond Sprite
  ComfortSnapshot, GroupEntry, PieceEntry, PieceStatus, Recommendation, RestBlocker

UI/
  ComfortPanel      // uGUI construction + per-tick text refresh
  UiStyle           // harvests fonts/sprites/colours from live HUD once

Patches/
  HudPatches        // Hud.Awake postfix — parent the panel
  TooltipPatches    // SE_Stats.GetTooltipString, SE_Cozy.GetIconText postfixes (v0.3)

L10n/
  Strings           // token → string, backed by embedded JSON per language
```

### 2.2 Data flow

```
                 ┌─ hotkey (ConfigEntry<KeyboardShortcut>)
                 │
ComfortAuditBehaviour.Update()
   │  if (!panelOpen && !tooltipWanted) return;        ← zero work when closed
   │  if (Time.time - lastScan < scanInterval) return;
   ▼
ComfortScanner.Scan(player)
   │   RestGate.Evaluate(player)                → RestBlocker[]
   │   Cover.GetCoverForPoint(pos, ...)         → coverPct, underRoof
   │   Piece.GetAllComfortPiecesInRadius(...)   → List<Piece>
   │   sort (replica of PieceComfortSort)
   │   adjacency walk → each piece tagged Counted / ShadowedByGroup
   │                      / ShadowedByName / InactiveObject
   │   compare own total vs player.GetComfortLevel()  → MismatchFlag
   ▼
ComfortSnapshot  (immutable, cached)
   │
   ├─→ Recommender.Rank(snapshot, PieceCatalog, player)  → Recommendation[]  (v0.2+)
   │
   ├─→ ComfortPanel.Render(snapshot, recommendations)
   └─→ TooltipPatches read the cached snapshot (never scan inside a patch)
```

One rule enforced throughout: **Harmony patches never compute.** They read the last cached snapshot and append a string. All work happens on our own throttled tick, on our own MonoBehaviour. This keeps us off the game's hot paths and makes the mod trivially safe to disable.

### 2.3 Harmony patches — the complete list

Deliberately minimal. **One** patch, a postfix. No prefix, **no transpiler**. Everything else reads public API on the mod's own throttled tick and draws through Jotunn. (A fourth, on `Hud.Awake`, was in the pre-Jotunn draft to parent the panel; Jotunn's `OnCustomGUIAvailable` event replaces it, so we touch one less vanilla method.)

| # | Target | Kind | Phase | Why |
|---|---|---|---|---|
| 1 | `StatusEffect.GetIconText` | postfix, filtered `__instance is SE_Rested` | v0.3 | Append `comfort/ceiling` to the Rested icon's label. `SE_Rested` does not override `GetIconText`, so the base implementation runs for it. **This is the only always-visible surface** — see the tooltip note below. |
| ~~2~~ | ~~`SE_Stats.GetTooltipString`~~ | ~~postfix~~ | **never shipped** | The brief assumed the Rested status icon had a tooltip to extend. It does not. `GetTooltipString` feeds only item tooltips, guardian powers and AoE damage; `Hud.UpdateStatusEffects` renders an icon, a name and `GetIconText()` and attaches no `UITooltip` at all. A real `UITooltip` was attached instead in v0.3, then removed: `GameCamera.UpdateMouseCapture` frees the cursor only when the inventory, map or menu is open, and `shudnal-MyLittleUI` hides the status icons in exactly that state — so the tooltip was unreachable in practice. The headline number moved to the icon label; the detail lives in the F7 panel. |
| ~~3~~ | ~~`Player.OnDestroy`~~ | ~~postfix~~ | **dropped** | Removed in v0.3. `Player.OnDestroy` nulls `Player.m_localPlayer` inside its own body, so a postfix identity-checking against that field never matched and the panel survived logout. Replaced with polling `m_localPlayer` in our own `Update`, which is robust to teardown ordering and costs one reference comparison per frame. |

Explicitly **not** patched, with reasons:

- **`SE_Rested.CalculateComfortLevel`** — we re-derive it instead. Patching it would put our attribution work on the game's 2 s tick for every player, and would fight any other comfort mod. Re-deriving also lets us *cross-check* the vanilla result and report disagreement rather than silently inherit it.
- **`Piece.Awake` / `Piece.OnDestroy`** — event-driven invalidation sounds attractive but these fire constantly during zone streaming, so we'd be doing more work than the timer costs. Mentioned in the brief as an option; recommendation is to skip it. If profiling ever says otherwise, add it behind a config flag.
- **Anything networked** — see §1.10.

### 2.4 Attribution — how a piece gets its status

The scanner replicates the game's sort and walk rather than guessing at "max per group", because the guess is wrong for `None` and for cross-group name collisions. Each piece comes out tagged:

- `Counted` — contributed `GetComfort()` to the total.
- `ShadowedByGroup` — a higher piece in the same group won. **Safe to remove, no loss.** Shows the winner's name.
- `ShadowedByName` — dropped by the `m_name` clause. Called out separately because it's the non-obvious one; includes the cross-group case.
- `InactiveObject` — `m_comfort > 0` but `GetComfort() == 0` because `m_comfortObject` is switched off. **This is an unlit fire or lantern: lighting it is a free comfort gain.** Never reported as shadowing anything, because it doesn't.

### 2.5 Robustness

| Case | Handling |
|---|---|
| No pieces in range | Snapshot with empty group list; panel shows baseline 2 and the full "missing groups" list. Not an error state. |
| Not near fire / not sheltered | `RestGate` returns the specific unmet conditions; panel shows them as a checklist. Never an empty panel. |
| Unsheltered | Show actual comfort (1) **and** `CalculateComfortLevel(true, pos)` as "potential if sheltered". Roof becomes the top recommendation. |
| Modded piece, unknown group | `ComfortGroup` is an `int`-backed enum; a mod can hold an out-of-range value. Gate every display through `Enum.IsDefined(typeof(Piece.ComfortGroup), g)`, else render as **"Other"** with the raw int in verbose mode. Group the unknowns together rather than inventing names. |
| Modded piece, null `m_resItem` | The game itself skips these (`if (!req.m_resItem || req.m_amount <= 0) continue;`). Mirror that; never dereference. |
| Multiplayer, pieces built by others | The game applies no ownership filter, so neither do we. `Piece.m_creator` exists if we ever want a "built by" line; out of scope. |
| `Player.m_nview == null` (early load) | `GetComfortLevel()` returns 0. Treat as "not ready", skip the tick. |
| Another mod patches the calculation | The self-check in §2.2 flags the mismatch in the panel footer instead of showing a confidently wrong breakdown. |
| `ZNetScene.instance == null` | Catalogue build deferred; recommendations section shows "loading". |

---

## 3. The recommendation algorithm, in plain language

### 3.1 Establish the baseline

From the snapshot, for every group build `counted[group]` = the comfort value actually being counted right now (0 if the group has nothing in range, or only inactive pieces). Track the set of `m_name` tokens currently counted, which the `None` maths needs.

### 3.2 Build the candidate set

Catalogue = every prefab in `ZNetScene.instance.m_prefabs` carrying a `Piece` with `m_comfort > 0`. Built once, cached, so modded pieces are included automatically.

Filter to what the player can realistically place. You chose **known recipe + crafting station in range**, which isn't a single `RequirementMode`, so compose it:

```
IsKnown                                   (recipe + materials discovered)
AND (piece.m_craftingStation == null
     OR CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, playerPos) != null
     OR ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoWorkbench))
```

Config exposes this as `RecommendationFilter = KnownOnly | KnownAndStationInRange | Buildable`, defaulting to the middle. `Buildable` maps to `RequirementMode.CanBuild`.

### 3.3 Score the gain

For each candidate `c`:

- If `c.m_comfortGroup != None`: **`gain = max(0, c.m_comfort − counted[c.m_comfortGroup])`**. It has to beat the incumbent to be worth anything.
- If `c.m_comfortGroup == None`: **`gain = c.m_comfort`, unless a counted piece already shares `c.m_name`, in which case 0.** None-group pieces stack, so they add their full value rather than a delta — which is exactly why they're so often the cheapest route to the next level.

Discard `gain <= 0`.

### 3.4 Score the cost

`cost(c) = Σ over requirements (amount × weight(item))`.

`weight` comes from `CostTable`, which ships as an embedded JSON of vanilla base materials and **defaults to 1.0 for anything unlisted**, so modded materials degrade to raw item count rather than breaking. Default weights are deliberately coarse — Wood/Stone 1, Coal/Flint/Resin 2, hides ~3–4, refined metals ~8, chains and crystal high — and the file is user-editable, because any single number here is an opinion and shouldn't pretend otherwise.

With all weights at 1.0 the ranking degenerates to "fewest items", which is a defensible, fully explainable fallback. Verbose mode prints the weighted arithmetic so the ranking is never a black box.

### 3.5 Rank

1. **Free gains first**, above anything that costs materials:
   - `InactiveObject` pieces → *"Light the hearth: +2, costs nothing"*.
   - Not sheltered → *"Roof this spot: comfort 1 → N"*, using `CalculateComfortLevel(true, pos)` for the real number. Usually the single largest gain in the game for a handful of wood.
2. Then by **gain descending**.
3. Tie-break **weighted cost ascending**.
4. Then prefer a piece whose station is already in range.
5. Then fewer distinct materials.
6. Then prefab name, for deterministic output across ticks — a recommendation that flickers between two equal options reads as a bug.

Top entry is "best next upgrade"; the panel lists the rest.

### 3.6 Missing groups

Groups with `counted == 0` are listed separately, sorted by the cost of their cheapest candidate. They deserve their own section because their gain is the piece's **full** comfort value rather than a delta, which usually makes them the cheapest wins — the point you made in the brief, and the code bears it out.

### 3.7 Materials on hand (nice-to-have, v0.2+)

Per recommendation, a ✓/✗ from `m_inventory.CountItems(...)` against each requirement. Nearby containers would mean scanning `Container` components in range and reading their `Inventory`; cheap enough, but it's genuinely optional — deferred to v0.3 behind a config flag.

---

## 4. UI

### 4.1 Construction — Jotunn `GUIManager`

Built with **Jotunn 2.30.0**'s `GUIManager`, which is already installed in your `Mods` profile. Verified API (decompiled from the installed `Jotunn.dll`):

```csharp
// Jotunn.Managers.GUIManager
public static GameObject CustomGUIFront { get; }        // parent for our panel
public static GameObject CustomGUIBack  { get; }
public static event Action OnCustomGUIAvailable;        // construct here, not in Hud.Awake
public static bool IsHeadless();
public static void BlockInput(bool state);

public GameObject CreateWoodpanel(Transform parent, Vector2 anchorMin, Vector2 anchorMax,
                                  Vector2 position, float width = 0f, float height = 0f,
                                  bool draggable = true);
public GameObject CreateText(string text, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
                             Vector2 position, Font font, int fontSize, Color color, bool outline,
                             Color outlineColor, float width, float height, bool addContentSizeFitter);
public void ApplyWoodpanelStyle(Transform woodpanel);
public void ApplyTextStyle(Text text, Font font, Color color, int fontSize = 16, bool createOutline = true);

public Font AveriaSerif, AveriaSerifBold, Norse, NorseBold { get; }
public TMP_FontAsset TMP_Norse { get; }                 // the "Valheim-Norse" TMP asset
public static Color ValheimOrange { get; }
```

Three consequences worth calling out, because each deletes work from the pre-Jotunn draft:

- **`CreateWoodpanel(..., draggable: true)` gives the drag handle for free** — that was a v0.3 item; it's now v0.1 for nothing.
- **`GUIManager.Instance.TMP_Norse`** is the real `Valheim-Norse` TMP font asset, so we get correct TMP rendering without harvesting a font off a live `TMP_Text`. The whole `UiStyle` harvesting class disappears, and with it the Medium-severity fragility risk.
- **`OnCustomGUIAvailable` replaces the `Hud.Awake` patch.** Patch #1 in §2.3 is deleted; we subscribe to Jotunn's event and parent under `CustomGUIFront`. That takes us from four Harmony patches to three, and off a vanilla method entirely.

Structure:

```
CreateWoodpanel(CustomGUIFront.transform, …, draggable: true)
 └─ Content              VerticalLayoutGroup + ContentSizeFitter (PreferredSize, vertical)
     ├─ Header           TMP_Text (TMP_Norse)   "Comfort 7  ·  Rested 11:00"
     ├─ RestStatus       TMP_Text               checklist of the 7 gate conditions
     ├─ GroupList        Image (piece m_icon) + TMP_Text rows
     ├─ IgnoredList      ditto, dimmed
     ├─ MissingList      ditto
     └─ Recommendation   TMP_Text (v0.2+)
```

Guard construction with `if (GUIManager.IsHeadless()) return;` so the plugin is inert on a headless/dedicated run.

### 4.2 Colours and dimming

`GUIManager.ValheimOrange` for headings, white for body, and the same body colour at ~45% alpha for shadowed rows. No new hues — the palette is the game's.

### 4.3 Toggle and input safety

`ConfigEntry<KeyboardShortcut>` (default **F7**), checked in `Update()` and suppressed while any text field has focus:

```csharp
if (Console.IsVisible()) return;
if (Chat.instance != null && Chat.instance.HasFocus()) return;
if (Menu.IsVisible()) return;
if (TextInput.IsVisible()) return;
```

Without those guards the panel toggles while you type "f7" in chat, which is the classic Valheim-mod papercut. If the panel ever gains focusable widgets, `GUIManager.BlockInput(true)` suppresses game input for as long as they're active.

Position via `ConfigEntry<Vector2>` offset plus an anchor-corner enum. Optional drag handle in v0.3.

### 4.4 Localization — Jotunn `LocalizationManager`

The private `Localization.AddWord` problem disappears. Jotunn exposes a public registry, verified:

```csharp
// Jotunn.Managers.LocalizationManager
public void AddToken(string token, string value, bool forceReplace = false);
public void AddToken(string token, string value, string language, bool forceReplace = false);
public void AddJson(string language, string fileContent);
public void AddPath(string path, bool isJson = false);
public string TryTranslate(string word);
```

So `$comfortaudit_*` tokens register into the game's own localizer properly, and the per-language JSON layout planned for `en.json` / `nb.json` feeds `AddJson("English", …)` / `AddJson("Norwegian", …)` directly — same files, no bespoke `Strings` lookup class. Piece and material names keep coming from `Localization.instance.Localize()` and so stay correct in whatever language the player runs.

This retires the second of the two live risks in the pre-Jotunn draft.

### 4.5 Quiet by default

- Panel closed → `SetActive(false)` and the scanner early-returns. No allocation, no scan, no draw.
- Logging: one line at `LogInfo` on load (name + version), everything else `LogDebug` behind a `Verbosity` config. Nothing per-tick, ever.
- Text is rebuilt only when the snapshot's content hash changes, not every tick, so TMP isn't re-laying-out four times a second for a static base.

### 4.6 Tooltip extension (v0.3)

Patch #1 appends one line to the Rested tooltip, patch #2 to the Resting icon text. Both read the cached snapshot; if it's stale or absent they append nothing rather than triggering a scan from inside a render path.

---

## 5. Project layout, build, and local testing

### 5.1 Layout

```
Hygge/
├─ ComfortAudit.sln
├─ Directory.Build.props              # VALHEIM_INSTALL resolution, shared props
├─ src/ComfortAudit/
│  ├─ ComfortAudit.csproj
│  ├─ Plugin.cs  Config.cs
│  ├─ Core/   Model/   UI/   Patches/   L10n/
│  └─ Resources/{en.json, costs.json}
├─ thunderstore/
│  ├─ manifest.json   README.md   CHANGELOG.md   icon.png   (256×256 PNG, required)
├─ build/
│  ├─ deploy.sh                       # copy DLL → BepInEx/plugins/ComfortAudit/
│  └─ package.sh                      # zip the Thunderstore bundle
├─ lib/                               # game DLLs — GITIGNORED, never committed
├─ PLAN.md
└─ .gitignore
```

### 5.2 Assembly references — recommendation

**Use `BepInEx.AssemblyPublicizer.MSBuild`**, not a hand-publicized DLL checked into the repo.

```xml
<ItemGroup>
  <PackageReference Include="BepInEx.AssemblyPublicizer.MSBuild" Version="0.4.*" PrivateAssets="all" />
  <Reference Include="assembly_valheim"  HintPath="$(ValheimManaged)/assembly_valheim.dll"  Publicize="true" Private="false" />
  <Reference Include="assembly_utils"    HintPath="$(ValheimManaged)/assembly_utils.dll"    Private="false" />
  <Reference Include="assembly_guiutils" HintPath="$(ValheimManaged)/assembly_guiutils.dll" Private="false" />
  <Reference Include="Jotunn"            HintPath="$(JotunnPath)/Jotunn.dll"                 Private="false" />
</ItemGroup>
```

Why this way:

- Publicizing happens **at build time, in the obj/ folder**. The shipped DLL still binds to the real members and runs against a vanilla, unmodified game install.
- No publicized derivative of Iron Gate's assemblies is ever committed or redistributed — which matters both legally and for repo hygiene.
- It removes ~all `AccessTools` reflection. As analysed above we need very few privates anyway (`Player.m_coverPercentage`/`m_underRoof` are avoidable via the public `Cover.GetCoverForPoint`), so this is mostly insurance for v0.4.

Target `net472`, `LangVersion latest`, `<AllowUnsafeBlocks>false`, `<DebugType>portable`. Also reference `UnityEngine.CoreModule`, `UnityEngine.UI`, `UnityEngine.IMGUIModule`, `Unity.TextMeshPro`, and BepInEx **5.4.23.5** (`BepInEx.Core` + `BepInEx.Unity`) with HarmonyX via `0Harmony`. All of `UnityEngine.CoreModule.dll`, `UnityEngine.UI.dll`, `UnityEngine.IMGUIModule.dll` and `Unity.TextMeshPro.dll` are confirmed present in the rig's `Managed/` directory.

`Directory.Build.props` resolves `$(ValheimManaged)` from a `VALHEIM_INSTALL` environment variable, falling back to the usual Steam path, and fails the build with a clear message if neither exists.

### 5.3 `.gitignore`

```gitignore
bin/
obj/
lib/                      # game DLLs — never commit
*.user
.vs/
*.zip
decomp/                   # decompiled game source, if you keep it locally
```

The `lib/` and `decomp/` lines are the load-bearing ones: neither Iron Gate's DLLs nor decompiled output belongs in a public repo.

### 5.4 Thunderstore `manifest.json`

```json
{
  "name": "ComfortAudit",
  "version_number": "0.1.0",
  "website_url": "",
  "description": "Shows exactly which furniture is feeding your Comfort level, what is being ignored, and the cheapest next upgrade.",
  "dependencies": [
    "denikson-BepInExPack_Valheim-5.4.2350",
    "ValheimModding-Jotunn-2.30.0"
  ]
}
```

Name, description ≤250 chars, a 256×256 `icon.png`, and `README.md` are all required by the Thunderstore validator. The pack version is the one already installed on `gamingrig01` (BepInEx **5.4.23.5**).

### 5.5 Local testing on `gamingrig01`

**Environment confirmed and reachable.**

| | |
|---|---|
| Host | `user@rig` — `gamingrig01`, CachyOS, kernel 7.2.4 |
| Game | `/games/SteamLibrary/steamapps/common/Valheim` (Steam library `/games/SteamLibrary`) |
| Assemblies | `<game>/valheim_Data/Managed/` |
| Mod loader | **r2modman**, profile **`Mods`** at `~/.config/r2modmanPlus-local/Valheim/profiles/Mods` |
| BepInEx | **5.4.23.5**, pack `denikson-BepInExPack_Valheim-5.4.2350` |
| Plugins dir | `<profile>/BepInEx/plugins/` — **28 mods already installed** |
| Build | **Native Linux** (`valheim.x86_64` + `UnityPlayer.so`) — *not* Proton, so no `WINEDLLOVERRIDES` |
| Launcher | `<profile>/start_game_bepinex.sh` — Doorstop via `LD_PRELOAD=libdoorstop_x64.so`, `target_assembly=BepInEx/core/BepInEx.Preloader.dll` (the profile's `winhttp.dll` is the unused Windows path) |
| Toolchain | No `dotnet` on the rig — build in the container, copy the DLL over |
| Source sync | `/workspace/gamemods/Valheim/Hygge` ⇄ `~/code/gamemods/Valheim/Hygge` via Syncthing |

Because r2modman owns the profile, the plugin goes to the **profile's** plugins directory, not the game directory — there is no `BepInEx/` under the game root at all. `deploy.sh` targets:

```
user@rig:~/.config/r2modmanPlus-local/Valheim/profiles/Mods/BepInEx/plugins/ComfortAudit/
```

The source tree syncs on its own, so only the built DLL needs `scp`. Launch via r2modman, or `start_game_bepinex.sh` directly; log at `<profile>/BepInEx/LogOutput.log`.

**Coexistence.** 28 plugins are live, including `ValheimModding-Jotunn`, `RandyKnapp-EpicLoot`, `AugusDogus-Buildheim`, `RustyMods-Seasonality` and `shudnal-ConfigurationManager`. None patches the comfort calculation, so the §2.2 self-check should stay quiet — but `Buildheim` and `EpicLoot` can introduce pieces and items, which makes the runtime `PieceCatalog` and the unknown-group fallback load-bearing rather than theoretical from day one. `shudnal-ConfigurationManager` means every `ConfigEntry` gets an in-game editor for free, so config descriptions and value ranges are worth writing properly.

Setup:Setup:

All of this is already in place — nothing to install. Remaining steps:

1. Enable the console in `<profile>/BepInEx/config/BepInEx.cfg` → `[Logging.Console] Enabled = true`.
2. Enable Valheim's own console: add `-console` to the launch arguments (r2modman exposes this per profile).
3. `deploy.sh` copies `bin/Release/net472/ComfortAudit.dll` to the profile plugins path above and, optionally, restarts the game.

**Spawning comfort pieces for testing.** All of these exist in `Terminal.cs` in this build:

| Command | Use |
|---|---|
| `devcommands` | Unlocks the rest (F5 console). |
| `debugmode` | Then **`B`** toggles no-cost building — the whole hammer piece table, free. Fastest way to place chairs/tables/banners/lanterns and watch the panel move. |
| `nocost` | Same effect via command. |
| `spawn <prefab> <n>` | Drop specific prefabs by name. |
| `pos` | Coordinates, for verifying the 10 m radius boundary. |
| `tod <0-1>` / `skiptime` / `env <name>` | Force night, rain, or clear weather to exercise the wet/cold gate conditions. |
| `resetcharacter` | Wipes known recipes — the only clean way to test the `IsKnown` filter. |
| `god`, `ghost`, `fly`, `freefly` | Get out of the way of the test; `ghost` also clears `IsSensed()`. |

Specific scenarios worth scripting into a test checklist:

- **Radius edge** — place a chair, walk away with `pos` open, confirm it drops out just under 10 m, measured 3-D from the pivot.
- **Group shadowing** — chair + stool together, confirm the stool is tagged `ShadowedByGroup` and removing it changes nothing.
- **None-group stacking** — the headline claim in §1.1 #6. Place several distinct `None`-group comfort pieces and confirm they *all* count.
- **Inactive object** — build a hearth, don't light it, confirm `InactiveObject` and a "costs nothing" recommendation; light it and watch the gain land.
- **Shelter gate** — fully furnished room, remove roof tiles until cover < 80%, confirm comfort collapses to **1** and not merely to "no roof bonus".
- **Sitting, no roof** — campfire under open sky, sit, confirm Resting → Rested at comfort 1.
- **TTL truth** — read `m_baseTTL` off the live `SE_Rested` and compare to 300; this settles §1.1 #1 in about ten seconds.
- **Vanilla server** — join an unmodded dedicated server and confirm the handshake succeeds (validates §1.10 empirically).

A mod-registered `comfortaudit dump` console command via `Terminal.ConsoleCommand` (ctor confirmed at Terminal.cs:134) that prints the whole catalogue — prefab, group, comfort, cost — makes most of the above one-line checks, and lets you diff the real table against the wiki.

---

## 6. Phased delivery

Declare the dependency in code as well as the manifest, so BepInEx enforces load order:

```csharp
[BepInDependency(Jotunn.Main.ModGuid)]
```

**v0.1 — Breakdown — DELIVERED 2026-09-15**
Scanner, snapshot, attribution walk, rest-gate evaluation, panel with header + gate checklist + contributing/ignored/missing groups, hotkey, position config, English strings, self-check mismatch flag. Patch #3 only, plus Jotunn's GUI event. Ships the thing that doesn't exist anywhere else.

Built clean (0 warnings, 0 errors) against the rig's own assemblies and deployed to the `Mods` profile. **Not yet run in-game** — that needs a human at the machine.

Two deliberate deviations from the design above, both scoped rather than accidental:

- **The panel is one rich-text TMP block, not per-piece rows with icons.** Same information, a fraction of the layout code, and it dodges the `VerticalLayoutGroup` + `ContentSizeFitter` reflow cost on every rescan. Piece icons (`Piece.m_icon`, already captured in `PieceEntry`) move to v0.2 when rows gain a recommendation affordance anyway.
- **Piece entries are built fresh each scan rather than pooled.** At ~tens of pieces on a 0.5 s tick this is irrelevant; pooling would be premature. Revisit only if profiling on a very large base says otherwise.

**v0.2 — Recommendations**
`PieceCatalog`, `Recommender`, `CostTable`, free-gain detection (unlit fires, roof), missing-group ranking, materials-in-inventory ✓/✗. Filter config. No new patches.

*Config-manager integration landed early, in v0.1:* `ConfigurationManagerAttributes` (supplied by Jotunn, matched by type name — no dependency on any particular manager build) for ordering and Advanced grouping; `SettingChanged` handlers so width/position apply live; a reset button via `CustomDrawer`; and an on-screen clamp. Verified against the installed `shudnal-ConfigurationManager` 1.1.18, which has built-in drawers for both `KeyboardShortcut` and `Vector2`.

**v0.3 — Tooltip and polish**
Patches #1 and #2. Nearby-container material check. Drag-to-move panel. `nb.json`. `comfortaudit dump` console command. Thunderstore release.

**v0.4 — Placement preview (stretch)**
`Player.m_placementGhost` position → `SE_Rested.CalculateComfortLevel(inShelter, ghostPos)` and `Cover.GetCoverForPoint(ghostPos, ...)` for the delta. Genuinely small *because* the public static overload exists and because nothing in v0.1–v0.3 was built around the assumption that comfort is only ever evaluated at the player. Confirms the brief's instruction not to let it drive the architecture — it doesn't need to.

---

## 7. Open questions and risks

**Open — needs you**

1. **Cost weights** — see item 3 below; that is now the only genuinely open design question.

**Resolved since the first draft**

- *Reachable host* — `user@rig` is up and analysed; §5.5 is concrete.
- *Client vs server assemblies* — every comfort-critical file is byte-identical; see the header note.
- *UI framework* — **Jotunn 2.30.0**, confirmed installed. Retires the UI-harvesting and `AddWord` risks, deletes one Harmony patch, and hands us a draggable panel for free.
2. ~~**`m_baseTTL`'s real value.**~~ **Resolved 2026-09-15: 480 s (8 min).** Read at runtime from the ObjectDB prefab; see §1.1. The decompiled initialiser (`300f`) was wrong, as flagged.
3. **Cost weights.** §3.4's defaults are my opinion. Worth one pass from you once you see rankings on a real base.

**Risks**

| Risk | Severity | Mitigation |
|---|---|---|
| Per-piece comfort values live in Unity assets, not the DLL, so no table could be produced at plan time | Low | This is *why* `PieceCatalog` reads at runtime. It's the correct design independent of the constraint, and it picks up modded pieces free. |
| ~~UI style harvesting breaks on a game patch~~ | **Retired** | Resolved by adopting Jotunn's `GUIManager`: fonts, panel style and colours come from a maintained library rather than runtime harvesting. |
| Jotunn lags a Valheim patch | Low | Real but well-understood: Jotunn is the most widely used Valheim modding library and updates quickly. You already depend on it for 1 of your 28 plugins, so our exposure adds nothing you don't already carry. |
| The `None`-group stacking claim | Low-Medium | Read directly off the decompiled walk and I'm confident in it, but it's the load-bearing novel insight — it has an explicit test in §5.5 and should be confirmed before v0.2 leans on it in recommendations. |
| Another mod patches `CalculateComfortLevel` | Low | We re-derive and cross-check; mismatch is surfaced in the panel footer rather than silently shown as truth. |
| Enum drift — Iron Gate adds a comfort group | Low | `Enum.IsDefined` gate → "Other". Deep North already added four groups your brief didn't know about, so this *will* happen again. |
| TMP layout cost with a large base | Low | Content-hash gate on text rebuild; scan is a `HashSet` walk, not physics. |
| Thunderstore icon/description validation | Trivial | Fixed requirements, handled in §5.4. |

**Explicitly not a risk**

- **Server mod checks.** §1.10 — there is no mod detection in the handshake, only network version 40 and the version string. Nothing in this design touches the network layer.
- **Scan performance.** The brief's concern assumed a radius query over all pieces. The game keeps a dedicated `HashSet` of comfort pieces and the public helper walks only that. The throttle is for our own string and attribution work, not for the scan.
