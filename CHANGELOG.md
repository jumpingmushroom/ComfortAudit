# Changelog

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
