# Comfort Audit 0.5–0.7 — Design

**Date:** 2026-09-28
**Target build:** Valheim 1.0.12, as for 0.4.x.
**Scope:** five features across three releases, each tested on the rig and published before the
next begins.

| Release | Features | New Harmony patches |
|---|---|---|
| 0.5 | Piece icons in the panel; materials in nearby chests | none |
| 0.6 | Build-menu comfort info (chips + description line) | 2 (display only, each disableable) |
| 0.7 | In-world markers; best resting spot | none |

Invariants that hold across all three: client-side only, no RPCs, no ZDO writes, no patches to
networking classes. Everything is off-cost when its surface is not visible.

Game APIs relied on, confirmed in the decompiled 1.0.12 source:

- `Hud.PieceIconData` (`m_go`, `m_icon`, ...), `Hud.m_pieceIcons`, `Hud.UpdatePieceList(Player,
  Vector2Int, Piece.PieceCategory, bool)`, `Hud.SetupPieceInfo(Piece)` — the last runs for both the
  hovered and the selected piece and reassigns `m_pieceDescription.text` on every call.
- `Container.GetInventory()`, `Container.m_checkGuardStone`, `Container.m_privacy`,
  `Container.CheckAccess(long)` (private; reachable through the build-time publicizer),
  `PrivateArea.CheckAccess(Vector3, float, bool)`. Container contents are loaded from the ZDO once a
  second on every client that has the container loaded, so remote chests are readable.
- `EffectArea.IsPointInsideArea(Vector3, EffectArea.Type.Heat, float)` — heat areas are what drive
  `Player.OnNearFire`, i.e. the game's own "near a fire" test.
- `Cover.GetCoverForPoint(Vector3, out float, out bool, ...)` (assembly_utils).
- `SE_Rested.CalculateComfortLevel(bool inShelter, Vector3 position)`.

There is no container registry in the game; containers are found with a physics query.

---

## 0.5 — Icons and chests

### Icons (`UI/IconAtlas.cs`)

Built once when `PieceCatalog` is ready, rebuilt if the catalogue is rebuilt.

1. Group every catalogue entry's `Piece.m_icon` by `sprite.texture`.
2. For each texture, create a `TMP_SpriteAsset` whose glyphs/characters use `sprite.textureRect`
   on that texture. No pixels are copied.
3. Glyph name = prefab name. The first asset is assigned to the panel's `TextMeshProUGUI.spriteAsset`;
   the others are its `fallbackSpriteAssets`, so `<sprite name="piece_chair">` resolves regardless of
   which texture holds the icon.
4. `IconAtlas.Tag(prefabName)` returns the `<sprite …>` tag, or an empty string when no icon is
   mapped. A missing icon is never an error; the row simply has none.

**Risk and fallback.** If TMP cannot address the shared textures (e.g. they are not usable as a
sprite-asset atlas), copy the icons into one readable texture via a `RenderTexture` blit and build a
single sprite asset from that. Decide on the rig; the public surface (`Tag`) is identical.

Icons appear before: Contributing, Ignored and Unlit rows; each recommendation; the placement
preview line. Not on the roof recommendation or Missing groups (no piece). Icons are sized to the
line height (`<sprite … >` inside the existing size tags), so panel height and scroll maths are
unchanged.

Config: `ShowIcons` (bool, default on).

### Chests (`Core/ContainerStock.cs`)

- Refreshed every 2 s while the panel is open; nothing runs while it is closed.
- `Physics.OverlapSphere(player position, ChestRadius, piece layers)` → `GetComponentInParent<Container>()`,
  distinct.
- Skip: the player's own inventory; `m_checkGuardStone && !PrivateArea.CheckAccess(pos, 0f, false)`;
  `!CheckAccess(Game.instance.GetPlayerProfile().GetPlayerID())`. Carts (containers on a `Vagon`)
  are included.
- Output: `Dictionary<string itemToken, int count>` plus, for diagnostics, the lists of counted and
  skipped containers with the reason.

Config: `ChestRadius` (float, default 20 m; 0 disables the feature).

**Recommender.** `MaterialLine` gains `InChests`. `HaveAllMaterials` stays inventory-only (it is what
building needs); a new `HaveAllWithChests` is added and used as a tie-break immediately after
`HaveAllMaterials` in `Recommender.Compare`. The `Buildable` filter is unchanged — it keeps using the
game's inventory-only `HaveRequirements`.

**Rendering** of each material:

| State | Look |
|---|---|
| Enough in inventory | dim `4 Fine wood` |
| Enough with chests | amber `4 Fine wood (1 + 6 in chests)` |
| Not enough in total | red, as today; `(have N)` when N > 0, plus `+ M in chests` when M > 0 |

New string `comfortaudit_in_chests`, in `en.json` and `nb.json`.

**Console:** `comfortaudit chests` — containers counted, containers skipped with reason, totals.

---

## 0.6 — Build menu

### `Core/GainCalculator.cs`

"What would this piece add if placed within range of where I stand?" — the same `ComfortWalk` run
over the pieces in range plus one hypothetical item. Extracted from `PlacementPreview`, which is
changed to call it, so preview, chips and description line share one implementation.

Cache: per prefab name, keyed on `SnapshotService.Revision`, shelter state, and player position
quantised to 1 m. Result: `{ Gain, GainIfSheltered, Kind (Adds/Beaten/Stacking), BeatenBy, Replaces }`.

While the player is in build mode with a comfort piece selected or the piece menu open, the plugin's
scan loop runs at `ScanInterval` even if the panel is closed (today `Plugin.Update` scans only while
the panel is open). The same "is any consumer visible" test later covers the 0.7 world markers.

### Chips — postfix on `Hud.UpdatePieceList`

- One small TMP label per icon slot, created lazily as a child of `PieceIconData.m_go`, bottom-right,
  pooled. If a slot's `m_go` is recreated, its chip is recreated with it.
- Only for pieces with `m_comfort > 0`; other slots have their chip hidden.
- Green `+N` adds comfort here; grey `+0` beaten; blue `+N` stacking.
- Unsheltered: show `GainIfSheltered`, dimmed — the "with a roof" value, consistent with the panel.
- Text/colour assigned only when the cached value changes.

### Description line — postfix on `Hud.SetupPieceInfo(Piece)`

Appends one line to `m_pieceDescription.text` for comfort pieces (idempotent: the original method
reassigns the text every call):

- `Comfort: Chair 2 · +1 here, replaces Stool`
- `Comfort: Chair 2 · beaten by Throne`
- `Comfort: Stacking 1 · +1 here`
- `Comfort: Chair 2 · no gain without a roof`
- With a placement ghost beyond the radius: the existing `comfortaudit_preview_outofrange` wording.

Config: `BuildMenuChips`, `BuildMenuLine` (bool, default on). Following `StatusIconText`, both
patches are always applied via `PatchAll` and return immediately when their setting is off; turning
`BuildMenuChips` off also hides any chips already created.

Compatibility: test with Hygge installed (it also writes to the build HUD); a Hygge user can turn
`BuildMenuLine` off.

---

## 0.7 — World markers and best spot

### World markers (`UI/WorldOverlay.cs`)

- Own screen-space canvas, sorted below the game HUD, pooled labels (icon + text).
- Each frame: project a point just above the piece's top (renderer bounds, measured once per piece)
  with the main camera; hide when behind the camera.
- Labels:
  - green `+2 Chair` — counts
  - grey `Stool · ignored` — beaten
  - amber `Hearth · unlit +3` — unlit
  - grey `Banner · 10.4 m` — out of range, for comfort pieces up to 2 m beyond the radius (these
    need their own nearby query; the snapshot holds only in-range pieces)
- Fade with distance; one greedy vertical-nudge pass to separate overlapping labels. Visible through
  walls by design.
- Ring: `LineRenderer` circle, 10 m radius, at the player's feet, following the player. Labels on
  out-of-range pieces explain the vertical part of the sphere.
- Shown when the panel is open (`WorldMarkersWithPanel`, default on) or toggled by `WorldMarkersKey`
  (default F8). Hidden in build mode.

### Best spot (`Core/BestSpotSearch.cs`)

Incremental search, running only while the panel or markers are visible.

- Grid: 1 m spacing over a 10 m disc centred on the search origin (~300 points).
- Per point, cheapest check first:
  1. **Floor** — raycast down from origin height + 2 m, 4 m long, against ground/piece/static
     layers (not characters).
  2. **Headroom** — capsule check, 1.8 m clear.
  3. **Fire** — `EffectArea.IsPointInsideArea(p, Heat)`.
  4. **Shelter** — `Cover.GetCoverForPoint` at chest height; cover ≥ 0.8 and under roof.
  5. **Comfort** — `SE_Rested.CalculateComfortLevel(true, p)`.
- Budget: `BestSpotPointsPerFrame` (Advanced, default 6) → a full pass in about a second.
- Restart when the player is more than 3 m from the origin or `SnapshotService.Revision` changes.
- Result: highest comfort, nearest to the player on ties; reported only if it beats the player's
  current comfort.
- Panel: `Best spot nearby: 17 (+1), 3 m ahead-left` (8-way direction relative to camera facing), or
  `You're in the best spot`. Unsheltered, this becomes the nearest sheltered, fire-warmed spot and
  its comfort.
- World: a `★ 17` marker at the spot while markers are shown.

Config: `ShowBestSpot` (default on), `BestSpotPointsPerFrame`.

**Console:** `comfortaudit spot` — points tested, rejections per check, top results.

---

## Testing

Each release is built, deployed to the rig's play profile and checked in game before publishing
(Thunderstore versions are permanent).

- 0.5: icons on every row type; icon-less modded piece; chests — warded, private, cart, just outside
  the radius; amber and red material states.
- 0.6: every chip colour and every line wording, sheltered and unsheltered; chip equals the placement
  preview for the same piece; Hygge installed alongside.
- 0.7: a hall where one corner is +1 from a banner; a two-storey house (out-of-range labels upstairs);
  standing outside the door; label overlap in a crowded room; frame time with markers and search on.

Pure logic (`GainCalculator`, direction wording, grid generation, material state classification)
is kept free of Unity scene access where practical so it can be checked with the console commands
and the per-world log report.

## Out of scope

Changing comfort mechanics (range, roof requirement, values); a Comfort build category; clickable
panel rows; gamepad-specific controls; new languages beyond keeping `en`/`nb` complete.
