# Changelog

## 0.4.4 — honest advice

Three fixes from a second code review, all about the panel telling you something untrue.

- Fixed: an unlit piece beaten by a lit one — an unlit hearth next to a burning campfire — was
  listed as "safe to remove" while the suggestions said to light it. Unlit pieces now show what
  lighting them would add, and the "light it" gain is what lighting really adds rather than the
  piece's designed value.
- Fixed: "safe to remove" was printed for every ignored piece. The game deduplicates by comparing
  neighbours in a sorted list, so a piece can matter only because it keeps two identical names
  apart. Each ignored piece is now checked by re-running the calculation without it, and one that
  matters reads "keep — removing it costs N".
- Fixed: suggestion gains were estimated from per-group maxima and a set of counted names, which
  goes wrong whenever equal names are involved (mostly modded pieces). Each candidate's gain is now
  the game's own calculation run with that piece added.
- Fixed: with no suggestions, the panel said "you have the best of every comfort group you've
  unlocked" even when a better piece was known but filtered out for want of a station or materials.
  It now says better pieces are known but cannot be built here right now.

## 0.4.3 — review fixes

Eight fixes from a full code review, all in the mod itself.

- Fixed: the "another mod is changing the comfort calculation" warning flashed for one scan
  every time comfort changed. The mismatch test ran before the change was recorded, so the first
  scan after placing a piece compared fresh comfort against the game's 2 s cache with the previous
  change time. The history is also reset on login and logout now.
- Fixed: unsheltered, the panel listed furniture under "Contributing" with lit-up values and the
  suggestions advertised gains — "light it +2" above "build a roof" — that change comfort by
  exactly zero without a roof. The section now reads "Would contribute with a roof" with dimmed
  values, and the roof is the only suggestion until it exists.
- Fixed: the panel could grow past the bottom of the screen, taking the suggestions with it. The
  Ignored list — the only one with no natural bound — collapses after `MaxIgnoredShown` rows
  (default 8) into "… and N more", the panel is capped to the screen height and clipped, and it
  nudges itself up when its bottom edge would otherwise leave the canvas.
- Fixed: suggestions and the unlocked ceiling included pieces no build tool offers, and seasonal
  pieces out of season — a maypole in September passed the known-recipe test and was recommended.
  The catalogue now records build-table membership and `m_enabled`, mirroring
  `PieceTable.UpdateAvailable`. Excluded pieces are named in the log once per world, with the
  nearest table entry, so an over-eager filter is visible rather than silent.
- Fixed: right after placing a chair, the placement preview reported the same gain again for up to
  half a second, because it subtracted the cached snapshot from a fresh walk. It now walks the same
  fresh buffer with and without the ghost and reports the difference.
- The placement preview memoises on the held piece, both positions and the scan revision. A
  stationary ghost now costs a few comparisons per frame instead of a registry scan and two walks.
- The panel text is rebuilt only when a scan, the preview or a config setting changes, instead of
  every frame followed by a string comparison that saved the TMP update but none of the formatting.
- The Rested icon label no longer drives a full scan every 2 s while the panel is closed. It reads
  the panel's scan when one is fresh, and otherwise the game's own cached comfort plus the cached
  ceiling — so a closed panel really does cost nothing.
- The out-of-range distance in the preview now updates as the ghost moves.
- The ceiling no longer caches a "not ready" answer for 10 s during the first seconds of a world.
- Docs: the README's claim that "the two armour stands" stack was derived from two legacy
  prefabs the hammer cannot place. The buildable armour stand sits in the Display group.

## 0.4.2 — readability

- Material lists no longer annotate every entry with "(have 0)". Red already means you do not have
  the material; the count now appears only when you have some but not enough, which is the case
  where the number tells you something.
- Blank lines before the Ignored and Missing Groups sections, which previously ran flush against
  the block above them.
- The last line no longer sits hard against the panel frame: TextMeshPro under-reports preferred
  height for rich text mixing tag sizes, so the box is given a little slack.
- Screenshots added to the README and the Thunderstore page.

## 0.4.1 — logout fix

- Fixed: the panel stayed open after logging out, reappearing at the main menu as an empty frame
  that F7 could not close.

  The 0.3.0 fix replaced a `Player.OnDestroy` patch with polling `Player.m_localPlayer`, comparing
  it against the previous reference. That comparison never fired, because `UnityEngine.Object`
  overloads `==` so a *destroyed* object compares equal to `null` — on logout the field is `null`
  and the cached reference is destroyed, which Unity reads as "unchanged". Presence is now tracked
  as a plain bool and identity with `ReferenceEquals`, neither of which Unity reinterprets.

  As a second line of defence, the panel is forced closed when the GUI is rebuilt with no local
  player, so it cannot return at the menu even if detection is somehow missed.

## 0.4.0 — placement preview

- While building, the panel shows what the held piece would add if placed where the ghost is:
  the gain, the resulting comfort, and — when the answer is nothing — which piece already beats it.
- Pieces aimed beyond the 10 m radius report the distance rather than a misleading +0, since
  comfort is always evaluated at the resting spot rather than at the ghost.
- Unsheltered, the preview says so outright instead of showing +0 with no explanation.

- Fixed: the reported cover percentage disagreed with the shelter verdict next to it. The panel
  recomputed cover from the player's feet, while `Player.UpdateCover` casts from chest height and
  `InShelter()` tests that cached result — so the panel could read "NOT sheltered, cover 71%" while
  the game considered you sheltered. It now reads the game's own values, which is consistent by
  construction and drops 17 raycasts per scan.

- Ungrouped suggestions are labelled "stacks — adds on top of what you have" rather than being
  lumped in as a new group. They do not compete for a group slot, so calling them a new group
  described the wrong mechanic.
- `comfortaudit now` prints the live breakdown — every piece in range, its status, what beat it,
  and the current suggestions — to both the console and the log. The per-world diagnostic report
  now includes the same thing; previously it covered the inputs (catalogue, cost tokens, ceiling)
  but not the mod's actual output.
- The per-world report waits for the game's comfort cache to initialise before firing.
  `Player.GetComfortLevel()` reads 0 until `UpdateBaseValue` first runs, and
  `CalculateComfortLevel` can never return 0 — so reporting that as a mismatch was a false alarm.

Internal, but load-bearing:

- The game's sort-and-adjacency walk now exists in exactly one place (`ComfortWalk`), shared by
  the live scan and the preview. A second copy written for the preview would have been free to
  drift from the behaviour the whole mod is built on.
- Stacking pieces are deduplicated on the raw `m_name` token everywhere — recommendations, the
  ceiling and the preview. They were compared by localized display name, which is wrong in two
  ways: distinct tokens can share a translation, and an untranslated token is rendered through a
  fallback. No user-visible symptom found, but the comparison did not match the game's.

## 0.3.0 — in progress

- Comfort ceiling: the panel now shows the highest comfort reachable at a sheltered spot, both
  with your current recipes and with everything unlocked, each with the Rested duration it buys.
  Says "at your maximum" once you are there.
- Ungrouped pieces are summed by distinct display name when computing the ceiling, matching the
  game's own duplicate-name rule rather than counting prefabs.

- Fixed: the panel stayed open after logging out. It was closed from a postfix on
  `Player.OnDestroy`, but that method nulls `Player.m_localPlayer` in its own body, so the
  postfix's identity check always failed. Logout and world changes are now detected by watching
  `m_localPlayer` directly, which is robust to teardown ordering. The mod now applies no Harmony
  patches at all until the tooltip work lands.

- The Rested icon's label now reads `10:00  3/11` — time remaining, current comfort, and the
  ceiling you can reach. Visible during play with no interaction.
- A hover tooltip on the status icons was built and then removed before release: Valheim frees the
  cursor only with the inventory, map or menu open, and MyLittleUI hides the status icons in
  exactly that state, so it could never actually be read. The F7 panel remains the detail surface.
- ComfortGroup.None is now labelled "Stacking" rather than "Ungrouped" — it is a real vanilla
  group whose defining property is that its pieces do not compete, not a fallback bucket.
- Panel and tooltip now share a single cached scan instead of each running their own.

- Current comfort and your ceiling now appear on the Rested icon's own label, visible during
  play. The hover tooltip needs a free cursor (inventory, map or menu open), which makes it a poor
  home for the headline number.
- Norwegian (Bokmål) translation, all 70 strings.
- Thunderstore icon.

## 0.2.0 — recommendations

Works out the cheapest next comfort upgrade and says why.

- Runtime catalogue of every comfort-bearing piece prefab, so pieces from other mods are included
  automatically without a hardcoded table.
- Suggestions ranked by comfort gain, then weighted material cost, then a chain of deterministic
  tie-breaks so the list never flickers between equal options.
- Free gains rank above anything that costs materials: unlit fires and lanterns first, then a roof
  when unsheltered — which is usually the single largest gain available, since furniture counts for
  nothing without one.
- Grouped pieces are scored on the *difference* over the incumbent; ungrouped pieces stack, so they
  score their full value unless that name already counts.
- Material cost per suggestion, with a per-item have/need check against your inventory.
- Availability filter: known recipe only, known + station in range (default), or fully buildable now.
- Material cost weights live in an editable table; unknown materials score 1.0, which degrades the
  ranking to raw item count rather than guessing.
- When there is nothing to suggest, the panel says why — whether you already hold the best of
  every unlocked group, how many pieces are still recipe-locked, and how many are blocked on a
  crafting station or materials — rather than a bare "nothing available".
- `comfortaudit` console command: `pieces`, `costs`, `groups` — dumps the live catalogue, and flags
  which material tokens have no weight configured. The same report is written to the BepInEx log
  once per world, so it can be checked without the console.
- Cost weights cover all 87 material tokens actually used by comfort pieces on Valheim 1.0.12,
  verified against a live install rather than guessed.
- Pieces whose name token has no translation (vanilla ships at least one: ArmorStand_Male) fall
  back to a readable form of the prefab name instead of rendering "[piece_armorstand_male]".

## 0.1.0 — breakdown

First release. Read-only, client-side.

- Live comfort breakdown for everything within the 10 m radius, with each piece attributed as
  contributing, beaten by its group, or dropped as a duplicate name.
- Unlit fires and lanterns are called out separately — they sit in the registry at comfort 0
  until lit.
- Shelter reported as the real numbers behind it (cover % vs the 80% threshold, roof yes/no),
  plus what comfort *would* be here with a roof.
- All seven Resting conditions reported independently, not just the outcome.
- Rested duration computed from the live `SE_Rested` values, falling back to the ObjectDB prefab.
- Cross-checks its own result against the game's and says so if another mod disagrees.

Configuration manager integration:

- Settings are ordered sensibly, with prefab names and verbose logging behind the Advanced toggle.
- Changes to panel width and position apply live instead of waiting for a restart.
- A "Reset panel position" button, and an automatic clamp that recovers the panel if a saved
  position lands off-screen after a resolution change.
- Dragging the panel persists its position once the drag settles.
- Fixed: the panel opened mostly off-screen. Jotunn's `CreateWoodpanel` sets anchors but not the
  pivot, leaving it centred, so a top-left anchored offset placed the panel's *centre* near the
  corner. The pivot is now pinned to the top-left and the panel centres itself on first open.
- Fixed: off-screen clamping measured `Screen.width/height` rather than the scaled canvas, so it
  computed the wrong bounds on any non-1:1 UI scale.
- Readability: text now sits on a translucent dark plate rather than directly on the wood
  texture, and dimmed text is a muted warm grey instead of reduced alpha (transparency let the
  wood grain show through the glyphs). Warning and success colours brightened to suit.

Not yet: upgrade recommendations (0.2), Rested tooltip line (0.3), placement preview (0.4).
