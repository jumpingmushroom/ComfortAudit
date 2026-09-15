# Comfort Audit

Valheim tells you your Comfort level and nothing else. This mod shows the arithmetic.

Press **F7** (configurable) to open a panel listing every comfort piece within the 10 m radius
and what it is actually doing:

- **Contributing** — the piece won its comfort group, with the value it added.
- **Ignored** — beaten by a higher piece of the same group, or dropped by the game's
  duplicate-name rule. These are safe to remove; you lose nothing.
- **Unlit** — fires and lanterns count for zero while switched off, even though they are placed.
  Lighting one is a free gain.
- **Missing groups** — the groups you have nothing for at all.

It also explains why you are not Rested. The game requires seven separate conditions and shows
none of them; Comfort Audit lists each one, so "near a fire, under a roof, still not resting"
resolves to *wet*, *spotted*, or *cold* instead of a mystery.

## Screenshots

**The panel.** What is in range, what each piece contributes, and the cheapest way up.

![Comfort Audit panel](https://raw.githubusercontent.com/jumpingmushroom/ComfortAudit/v0.4.3/docs/images/panel.png)

**Shelter is a gate, not a bonus.** Step outside and comfort collapses to 1 — the furniture stops
counting entirely, not partially. Building a roof here is worth +4.

![Unsheltered](https://raw.githubusercontent.com/jumpingmushroom/ComfortAudit/v0.4.3/docs/images/unsheltered.png)

**Pieces that are doing nothing.** The stool loses its group to the chair, so removing it costs you
nothing at all.

![Ignored pieces](https://raw.githubusercontent.com/jumpingmushroom/ComfortAudit/v0.4.3/docs/images/ignored.png)

**Placement preview.** While building, what the held piece would add if placed where the ghost is.

![Placement preview](https://raw.githubusercontent.com/jumpingmushroom/ComfortAudit/v0.4.3/docs/images/preview.png)

## Two things worth knowing

**Shelter is not a bonus, it is a gate.** Unsheltered, your comfort is 1 and *no furniture counts
at all* — not reduced, zero. The panel shows what comfort would be at your position with a roof.

**Ungrouped pieces stack.** Most furniture is deduplicated to the best piece per group, but pieces
with no comfort group are only deduplicated by name, so several different ones all count. In
vanilla 1.0.12 the buildable ones are the maypole and the yule tree, at +1 each, and only while in
season. Two legacy gendered armour-stand prefabs also carry +1 but cannot be placed; the hammer's
armour stand counts in the Display group instead.

Rested duration is `8:00 + 1:00 per comfort level above 1`, and the panel reads those numbers from
the game rather than assuming them.

## Compatibility

Client-side only. It reads local state and draws UI — no RPCs, no ZDO writes, no network patches —
so it is safe on vanilla servers and does not affect joining. Pieces added by other mods are
picked up automatically; unrecognised comfort groups display as "Other".

Requires [Jotunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/).
