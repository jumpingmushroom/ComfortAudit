# Comfort Audit 0.5 — Icons and Chests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show piece icons inline in the F7 panel, and count materials held in nearby chests in each suggestion's have/need check.

**Architecture:** Game-independent logic (material classification, chest tallying, sprite tags) lives in `src/ComfortAudit/Core/Pure/` with no Unity or game references, so a new net8 xUnit project can compile and test it directly. Unity-facing code (`ContainerStock`, `IconAtlas`) wraps it and is verified on the rig. The panel stays a single TextMeshPro rich-text block; icons are `<sprite name=…>` tags resolved against runtime `TMP_SpriteAsset`s built over the game's own icon textures.

**Tech Stack:** C# (net472, LangVersion latest), BepInEx 5, Jotunn 2.30.0, HarmonyLib, Unity TextMeshPro, xUnit on net8.0 for pure-logic tests.

**Spec:** `docs/superpowers/specs/2026-09-28-comfortaudit-0.5-0.7-design.md` (section "0.5 — Icons and chests")

## Global Constraints

- Target Valheim 1.0.12; client-side only — no RPCs, no ZDO writes, no network patches. 0.5 adds **no** Harmony patches.
- Nothing runs while the panel is closed (chest scan and icon use are driven from the panel's scan).
- Files under `src/ComfortAudit/Core/Pure/` must not reference `UnityEngine`, TMPro, or any game type. Namespace `ComfortAudit.Core.Pure`.
- Every user-visible string goes through `Strings.Get` with a token present in **both** `L10n/en.json` and `L10n/nb.json`.
- Match surrounding style: XML `<summary>` comments that explain *why*, `PluginConfig` entries with `ConfigurationManagerAttributes` order, no per-tick logging.
- No Claude/AI attribution in commit messages. Commit author is the repo's configured git user.
- A Thunderstore version can never be replaced: nothing is published until the rig check (Task 8) passes and the user says go.
- Shell environment for every `dotnet` command in this plan:
  `export DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 LANG=C.UTF-8`
- Build: `dotnet build src/ComfortAudit/ComfortAudit.csproj -c Release --nologo -v minimal` → must report `0 Error(s)`.
- Tests: `dotnet test tests/ComfortAudit.Tests/ComfortAudit.Tests.csproj --nologo -v minimal`.

---

## File Structure

| File | Status | Responsibility |
|---|---|---|
| `tests/ComfortAudit.Tests/ComfortAudit.Tests.csproj` | new | net8 xUnit project compiling `Core/Pure/*.cs` by link |
| `tests/ComfortAudit.Tests/MaterialMathTests.cs` | new | tests for material classification/annotation |
| `tests/ComfortAudit.Tests/StockTallyTests.cs` | new | tests for chest tallying |
| `tests/ComfortAudit.Tests/SpriteTagsTests.cs` | new | tests for sprite tag generation |
| `src/ComfortAudit/Core/Pure/MaterialMath.cs` | new | `MaterialState`, `MaterialAnnotation`, classification |
| `src/ComfortAudit/Core/Pure/StockTally.cs` | new | token → count aggregation across containers |
| `src/ComfortAudit/Core/Pure/SpriteTags.cs` | new | glyph naming and `<sprite>` tag strings |
| `src/ComfortAudit/Core/ContainerStock.cs` | new | finds accessible containers near the player, fills a `StockTally` |
| `src/ComfortAudit/UI/IconAtlas.cs` | new | builds `TMP_SpriteAsset`s over piece icon textures |
| `src/ComfortAudit/Model/ComfortModel.cs` | modify | `MaterialLine.InChests`, `Recommendation.HaveAllWithChests`, `PlacementPreviewResult.PrefabName` |
| `src/ComfortAudit/Core/Recommender.cs` | modify | fill `InChests`, new tie-break |
| `src/ComfortAudit/Core/PlacementPreview.cs` | modify | set `PrefabName` |
| `src/ComfortAudit/Core/PieceCatalog.cs` | modify | `Invalidate` also invalidates `IconAtlas` |
| `src/ComfortAudit/Core/ConsoleCommands.cs` | modify | `comfortaudit chests` |
| `src/ComfortAudit/UI/ComfortPanel.cs` | modify | material rendering; icon tags; `SetSpriteAsset` |
| `src/ComfortAudit/Plugin.cs` | modify | drive `IconAtlas` + push sprite asset to panel; version |
| `src/ComfortAudit/PluginConfig.cs` | modify | `ChestRadius`, `ShowIcons` |
| `src/ComfortAudit/L10n/en.json`, `nb.json` | modify | `comfortaudit_in_chests` |
| `src/ComfortAudit/ComfortAudit.csproj` | modify | TextCore reference; publicize TMP; version |
| `ComfortAudit.sln` | modify | add test project |
| `README.md`, `CHANGELOG.md`, `thunderstore/manifest.json`, `thunderstore/README.md` | modify | release 0.5.0 |

---

### Task 1: Test project and material classification

**Files:**
- Create: `tests/ComfortAudit.Tests/ComfortAudit.Tests.csproj`
- Create: `tests/ComfortAudit.Tests/MaterialMathTests.cs`
- Create: `src/ComfortAudit/Core/Pure/MaterialMath.cs`
- Modify: `ComfortAudit.sln`

**Interfaces:**
- Produces: `ComfortAudit.Core.Pure.MaterialState { Enough, EnoughWithChests, Short }`,
  `MaterialAnnotation { None, Have, HaveAndChests }`,
  `static MaterialState MaterialMath.Classify(int amount, int have, int inChests)`,
  `static MaterialAnnotation MaterialMath.Annotate(int amount, int have, int inChests)`.

- [ ] **Step 1: Create the test project**

`tests/ComfortAudit.Tests/ComfortAudit.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <!-- Tests the game-independent logic in src/ComfortAudit/Core/Pure by compiling those files
       directly. Nothing here references Unity or the game, so it runs on plain .NET. The root
       Directory.Build.props sets net472 for the mod; this project overrides it. -->
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>

  <ItemGroup>
    <Compile Include="../../src/ComfortAudit/Core/Pure/*.cs" LinkBase="Pure" />
  </ItemGroup>

</Project>
```

Then add it to the solution:

```bash
dotnet sln ComfortAudit.sln add tests/ComfortAudit.Tests/ComfortAudit.Tests.csproj
```

- [ ] **Step 2: Write the failing tests**

`tests/ComfortAudit.Tests/MaterialMathTests.cs`:

```csharp
using ComfortAudit.Core.Pure;
using Xunit;

namespace ComfortAudit.Tests
{
    public class MaterialMathTests
    {
        [Theory]
        [InlineData(4, 4, 0, MaterialState.Enough)]
        [InlineData(4, 9, 0, MaterialState.Enough)]
        [InlineData(4, 4, 7, MaterialState.Enough)]            // inventory alone suffices
        [InlineData(4, 1, 6, MaterialState.EnoughWithChests)]
        [InlineData(4, 0, 4, MaterialState.EnoughWithChests)]
        [InlineData(4, 1, 2, MaterialState.Short)]
        [InlineData(4, 0, 0, MaterialState.Short)]
        [InlineData(4, 1, -3, MaterialState.Short)]           // negative chest count is treated as none
        public void Classify(int amount, int have, int inChests, MaterialState expected)
        {
            Assert.Equal(expected, MaterialMath.Classify(amount, have, inChests));
        }

        [Theory]
        [InlineData(4, 4, 0, MaterialAnnotation.None)]         // enough: no clutter
        [InlineData(4, 4, 6, MaterialAnnotation.None)]         // enough in inventory: chests irrelevant
        [InlineData(4, 0, 0, MaterialAnnotation.None)]         // red already says "you have none"
        [InlineData(4, 2, 0, MaterialAnnotation.Have)]         // some but not enough
        [InlineData(4, 1, 6, MaterialAnnotation.HaveAndChests)]
        [InlineData(4, 0, 2, MaterialAnnotation.HaveAndChests)] // short even with chests, but chests help
        public void Annotate(int amount, int have, int inChests, MaterialAnnotation expected)
        {
            Assert.Equal(expected, MaterialMath.Annotate(amount, have, inChests));
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/ComfortAudit.Tests/ComfortAudit.Tests.csproj --nologo -v minimal`
Expected: build FAILS with `CS0246: The type or namespace name 'MaterialState' could not be found`.

- [ ] **Step 4: Implement**

`src/ComfortAudit/Core/Pure/MaterialMath.cs`:

```csharp
using System;

namespace ComfortAudit.Core.Pure
{
    /// <summary>Whether a suggestion's material can be covered, and from where.</summary>
    public enum MaterialState
    {
        /// <summary>Enough in the inventory — what building actually needs.</summary>
        Enough,

        /// <summary>
        /// Not enough carried, but enough once nearby chests are counted. Vanilla cannot build
        /// from a chest, so this is "go and fetch it", not "ready".
        /// </summary>
        EnoughWithChests,

        /// <summary>Not enough even with the chests.</summary>
        Short
    }

    /// <summary>Which count, if any, is worth printing after a material.</summary>
    public enum MaterialAnnotation
    {
        None,

        /// <summary>"(have N)" — some carried, not enough, none in chests.</summary>
        Have,

        /// <summary>"(N + M in chests)" — chests hold some of it.</summary>
        HaveAndChests
    }

    /// <summary>
    /// Material have/need rules, kept free of Unity so they can be unit-tested. The panel only
    /// decides colours and wording from these answers.
    /// </summary>
    public static class MaterialMath
    {
        public static MaterialState Classify(int amount, int have, int inChests)
        {
            if (have >= amount)
                return MaterialState.Enough;

            if (have + Math.Max(0, inChests) >= amount)
                return MaterialState.EnoughWithChests;

            return MaterialState.Short;
        }

        /// <summary>
        /// A count is printed only when it tells the player something: never when the inventory
        /// already covers it, and not "(have 0)" — red already says that.
        /// </summary>
        public static MaterialAnnotation Annotate(int amount, int have, int inChests)
        {
            if (have >= amount)
                return MaterialAnnotation.None;

            if (inChests > 0)
                return MaterialAnnotation.HaveAndChests;

            return have > 0 ? MaterialAnnotation.Have : MaterialAnnotation.None;
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/ComfortAudit.Tests/ComfortAudit.Tests.csproj --nologo -v minimal`
Expected: `Passed!  - Failed: 0, Passed: 14`.

Also run the mod build (the new file is globbed into it): expect `0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
git add tests/ComfortAudit.Tests src/ComfortAudit/Core/Pure/MaterialMath.cs ComfortAudit.sln
git commit -m "Add pure-logic test project and material classification"
```

---

### Task 2: Chest tally

**Files:**
- Create: `src/ComfortAudit/Core/Pure/StockTally.cs`
- Create: `tests/ComfortAudit.Tests/StockTallyTests.cs`

**Interfaces:**
- Produces: `sealed class StockTally` with `void Add(string token, int count)`, `int Count(string token)`,
  `void CountContainer()`, `int Containers { get; }`, `IEnumerable<KeyValuePair<string,int>> Items { get; }` (sorted by token, ordinal), `static readonly StockTally Empty`.

- [ ] **Step 1: Write the failing tests**

`tests/ComfortAudit.Tests/StockTallyTests.cs`:

```csharp
using System.Linq;
using ComfortAudit.Core.Pure;
using Xunit;

namespace ComfortAudit.Tests
{
    public class StockTallyTests
    {
        [Fact]
        public void SumsAcrossStacksAndContainers()
        {
            var t = new StockTally();
            t.CountContainer();
            t.Add("$item_finewood", 10);
            t.Add("$item_finewood", 5);
            t.CountContainer();
            t.Add("$item_finewood", 3);

            Assert.Equal(18, t.Count("$item_finewood"));
            Assert.Equal(2, t.Containers);
        }

        [Fact]
        public void UnknownTokenIsZero()
        {
            Assert.Equal(0, new StockTally().Count("$item_bronze"));
            Assert.Equal(0, new StockTally().Count(null));
        }

        [Fact]
        public void IgnoresEmptyTokensAndNonPositiveCounts()
        {
            var t = new StockTally();
            t.Add(null, 5);
            t.Add("", 5);
            t.Add("$item_stone", 0);
            t.Add("$item_stone", -2);

            Assert.Empty(t.Items);
        }

        [Fact]
        public void TokensAreCaseSensitive()
        {
            // Inventory.CountItems compares m_shared.m_name ordinally; so must we.
            var t = new StockTally();
            t.Add("$item_Wood", 1);
            Assert.Equal(0, t.Count("$item_wood"));
        }

        [Fact]
        public void ItemsAreSortedForStableOutput()
        {
            var t = new StockTally();
            t.Add("$item_wood", 1);
            t.Add("$item_bronze", 1);
            Assert.Equal(new[] { "$item_bronze", "$item_wood" }, t.Items.Select(kv => kv.Key));
        }

        [Fact]
        public void EmptyIsEmpty()
        {
            Assert.Equal(0, StockTally.Empty.Containers);
            Assert.Empty(StockTally.Empty.Items);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/ComfortAudit.Tests/ComfortAudit.Tests.csproj --nologo -v minimal`
Expected: build FAILS, `StockTally` not found.

- [ ] **Step 3: Implement**

`src/ComfortAudit/Core/Pure/StockTally.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace ComfortAudit.Core.Pure
{
    /// <summary>
    /// Item counts summed over a set of containers, keyed by the item's shared-name token
    /// (e.g. "$item_finewood") — the same key Inventory.CountItems uses, compared ordinally.
    /// </summary>
    public sealed class StockTally
    {
        public static readonly StockTally Empty = new StockTally();

        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);

        public int Containers { get; private set; }

        public void CountContainer()
        {
            Containers++;
        }

        public void Add(string token, int count)
        {
            if (string.IsNullOrEmpty(token) || count <= 0)
                return;

            int existing;
            _counts.TryGetValue(token, out existing);
            _counts[token] = existing + count;
        }

        public int Count(string token)
        {
            int n;
            return token != null && _counts.TryGetValue(token, out n) ? n : 0;
        }

        /// <summary>Sorted, so console output and logs do not reorder between identical scans.</summary>
        public IEnumerable<KeyValuePair<string, int>> Items
        {
            get { return _counts.OrderBy(kv => kv.Key, StringComparer.Ordinal); }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/ComfortAudit.Tests/ComfortAudit.Tests.csproj --nologo -v minimal`
Expected: `Passed!  - Failed: 0, Passed: 20`.

- [ ] **Step 5: Commit**

```bash
git add src/ComfortAudit/Core/Pure/StockTally.cs tests/ComfortAudit.Tests/StockTallyTests.cs
git commit -m "Add StockTally for summing chest contents"
```

---

### Task 3: ContainerStock, `ChestRadius` and `comfortaudit chests`

**Files:**
- Create: `src/ComfortAudit/Core/ContainerStock.cs`
- Modify: `src/ComfortAudit/PluginConfig.cs` (new field + bind, next to `ShowMaterials`)
- Modify: `src/ComfortAudit/Core/ConsoleCommands.cs` (new sub-command)
- Modify: `src/ComfortAudit/Plugin.cs` (`LocalPlayerGone` / `LocalPlayerArrived` reset)

**Interfaces:**
- Consumes: `StockTally` (Task 2).
- Produces: `static StockTally ContainerStock.Get(Player player)` (cached ≤ 2 s; `StockTally.Empty` when disabled or no player), `static void ContainerStock.Reset()`, `static List<string> ContainerStock.LastReport` (one line per container, counted or skipped with reason), `PluginConfig.ChestRadius` (`ConfigEntry<float>`).

Game facts this relies on (decompiled 1.0.12): `Container.GetInventory()`; `Container.m_checkGuardStone`; `Container.m_privacy` (`PrivacySetting.Public/Private/Group`); private chests belong to `m_piece.GetCreator()`; `PrivateArea.CheckAccess(Vector3 point, float radius = 0f, bool flash = true, bool wardCheck = false)`; tombstones carry a `Container` plus a `TombStone` component; carts carry `Vagon.m_container`. Containers reload their inventory from the ZDO every second on every client, so a remote chest's contents are readable. There is no container registry, hence the physics query.

- [ ] **Step 1: Add the config entry**

In `PluginConfig.cs`, add the field beside the others:

```csharp
        public static ConfigEntry<float> ChestRadius;
```

and bind it directly after `ShowMaterials`:

```csharp
            ChestRadius = cfg.Bind("Recommendations", "ChestRadius", 20f,
                new ConfigDescription(
                    "Count materials in chests and carts within this many metres when checking " +
                    "what you have. Chests you could not open (warded, or someone else's private " +
                    "chest) are skipped. Vanilla cannot build straight from a chest, so these " +
                    "are shown apart from what you carry. 0 turns this off.",
                    new AcceptableValueRange<float>(0f, 50f),
                    Attr(24)));
```

and with the other handlers at the end of `Bind`:

```csharp
            ChestRadius.SettingChanged += (s, e) => { Core.ContainerStock.Reset(); Raise(ContentChanged); };
```

- [ ] **Step 2: Implement ContainerStock**

`src/ComfortAudit/Core/ContainerStock.cs`:

```csharp
using System.Collections.Generic;
using ComfortAudit.Core.Pure;
using UnityEngine;

namespace ComfortAudit.Core
{
    /// <summary>
    /// Materials in chests and carts near the player, for the have/need check.
    ///
    /// Found with a physics query because the game keeps no registry of containers. Refreshed at
    /// most every two seconds and only when asked — the recommender asks during a scan, and
    /// scans run only while the panel is open — so a closed panel costs nothing.
    ///
    /// Reads contents only. Containers reload their inventory from the ZDO once a second on
    /// every client that has them loaded, so this sees what the owner sees without any RPC.
    /// </summary>
    public static class ContainerStock
    {
        private const float RefreshSeconds = 2f;

        private static StockTally _tally = StockTally.Empty;
        private static float _stamp = float.NegativeInfinity;
        private static readonly Collider[] Hits = new Collider[256];
        private static readonly HashSet<Container> Seen = new HashSet<Container>();
        private static int _mask;

        /// <summary>One line per container found on the last refresh, for the console.</summary>
        public static readonly List<string> LastReport = new List<string>();

        public static void Reset()
        {
            _tally = StockTally.Empty;
            _stamp = float.NegativeInfinity;
            LastReport.Clear();
        }

        public static StockTally Get(Player player)
        {
            float radius = PluginConfig.ChestRadius.Value;
            if (player == null || radius <= 0f)
                return StockTally.Empty;

            if (Time.time - _stamp < RefreshSeconds)
                return _tally;

            _stamp = Time.time;
            _tally = Collect(player, radius);
            return _tally;
        }

        private static StockTally Collect(Player player, float radius)
        {
            if (_mask == 0)
                _mask = LayerMask.GetMask("piece", "piece_nonsolid", "vehicle");

            var tally = new StockTally();
            LastReport.Clear();
            Seen.Clear();

            Vector3 origin = player.transform.position;
            long playerId = player.GetPlayerID();

            int n = Physics.OverlapSphereNonAlloc(origin, radius, Hits, _mask, QueryTriggerInteraction.Collide);
            if (n == Hits.Length)
                ComfortAuditPlugin.Log.LogWarning("chest scan hit its collider cap; some chests may be missed");

            for (int i = 0; i < n; i++)
            {
                Collider c = Hits[i];
                if (c == null)
                    continue;

                Container container = c.GetComponentInParent<Container>();
                if (container == null || !Seen.Add(container))
                    continue;

                string name = container.gameObject.name;
                string skip = SkipReason(container, playerId);
                if (skip != null)
                {
                    LastReport.Add("  skip  " + name + "  (" + skip + ")");
                    continue;
                }

                Inventory inv = container.GetInventory();
                tally.CountContainer();

                int stacks = 0;
                List<ItemDrop.ItemData> items = inv.GetAllItems();
                for (int k = 0; k < items.Count; k++)
                {
                    ItemDrop.ItemData item = items[k];
                    if (item == null || item.m_shared == null)
                        continue;
                    tally.Add(item.m_shared.m_name, item.m_stack);
                    stacks++;
                }

                LastReport.Add(string.Format("  count {0}  {1:0.0} m, {2} stack(s)",
                    name, Vector3.Distance(origin, container.transform.position), stacks));
            }

            return tally;
        }

        /// <summary>Null when the player could open this container; otherwise why not.</summary>
        private static string SkipReason(Container container, long playerId)
        {
            if (container.GetInventory() == null)
                return "no inventory";

            // A grave holds the dead player's gear, not base stock.
            if (container.GetComponent<TombStone>() != null)
                return "tombstone";

            if (container.m_checkGuardStone &&
                !PrivateArea.CheckAccess(container.transform.position, 0f, false))
                return "warded";

            if (container.m_privacy == Container.PrivacySetting.Private &&
                (container.m_piece == null || container.m_piece.GetCreator() != playerId))
                return "private";

            // Group privacy is unused in vanilla and CheckAccess returns false for it.
            if (container.m_privacy == Container.PrivacySetting.Group)
                return "group";

            return null;
        }
    }
}
```

Note: `m_piece` and `PrivacySetting` are reachable because `assembly_valheim` is publicized at build time. If the compiler reports `Container.PrivacySetting` does not exist, check the enum's declaring type with
`grep -rn "enum PrivacySetting" <decomp>/` and qualify it accordingly.

- [ ] **Step 3: Reset on world change**

In `Plugin.cs`, add `ContainerStock.Reset();` as the last line of both `LocalPlayerGone()` and `LocalPlayerArrived()`.

- [ ] **Step 4: Console command**

In `ConsoleCommands.cs`, add a case and a help line:

```csharp
                        case "chests": DumpChests(args.Context); break;
```

```csharp
                            args.Context.AddString("comfortaudit chests  - containers counted for materials, and which were skipped");
```

Update the command description string to `"ComfortAudit diagnostics: pieces | costs | groups | now | chests"`.

Add the method:

```csharp
        private static void DumpChests(Terminal ctx)
        {
            Player player = Player.m_localPlayer;
            if (player == null)
            {
                ctx.AddString("ComfortAudit: no local player.");
                return;
            }

            ContainerStock.Reset();
            Pure.StockTally tally = ContainerStock.Get(player);

            var lines = new List<string>();
            lines.Add(string.Format("Chests within {0:0} m: {1} counted", PluginConfig.ChestRadius.Value, tally.Containers));
            lines.AddRange(ContainerStock.LastReport);
            foreach (KeyValuePair<string, int> kv in tally.Items)
                lines.Add(string.Format("  {0,-28} {1}", kv.Key, kv.Value));

            for (int i = 0; i < lines.Count; i++)
            {
                ctx.AddString(lines[i]);
                ComfortAuditPlugin.Log.LogInfo(lines[i]);
            }
        }
```

- [ ] **Step 5: Build**

Run: `dotnet build src/ComfortAudit/ComfortAudit.csproj -c Release --nologo -v minimal`
Expected: `0 Error(s)`. Run the tests too: still 20 passing.

- [ ] **Step 6: Commit**

```bash
git add src/ComfortAudit/Core/ContainerStock.cs src/ComfortAudit/PluginConfig.cs src/ComfortAudit/Core/ConsoleCommands.cs src/ComfortAudit/Plugin.cs
git commit -m "Count materials in nearby chests and carts"
```

---

### Task 4: Chest counts in suggestions and the panel

**Files:**
- Modify: `src/ComfortAudit/Model/ComfortModel.cs` (`MaterialLine`, `Recommendation`)
- Modify: `src/ComfortAudit/Core/Recommender.cs` (`FillMaterials`, `AddFreeGains`, `AddShelter`, `Compare`)
- Modify: `src/ComfortAudit/UI/ComfortPanel.cs` (`AppendRecommendation` material loop)
- Modify: `src/ComfortAudit/L10n/en.json`, `src/ComfortAudit/L10n/nb.json`

**Interfaces:**
- Consumes: `MaterialMath` (Task 1), `ContainerStock.Get` (Task 3).
- Produces: `MaterialLine.InChests` (int), `MaterialLine.State` (`MaterialState`), `Recommendation.HaveAllWithChests` (bool).

- [ ] **Step 1: Model**

In `ComfortModel.cs`, add `using ComfortAudit.Core.Pure;` at the top and replace `MaterialLine` with:

```csharp
    public sealed class MaterialLine
    {
        public string DisplayName;
        public int Amount;
        public int Have;

        /// <summary>Held in accessible chests and carts nearby, not counting the inventory.</summary>
        public int InChests;

        public bool Enough => Have >= Amount;
        public MaterialState State => MaterialMath.Classify(Amount, Have, InChests);
    }
```

In `Recommendation`, directly after `HaveAllMaterials`:

```csharp
        /// <summary>
        /// Every material is covered once nearby chests are counted. A tie-break only: building
        /// needs the materials carried, which HaveAllMaterials already ranks on.
        /// </summary>
        public bool HaveAllWithChests;
```

- [ ] **Step 2: Recommender**

In `FillMaterials`, set both flags and fill `InChests`:

```csharp
        private static void FillMaterials(Player player, Piece piece, Recommendation rec)
        {
            rec.HaveAllMaterials = true;
            rec.HaveAllWithChests = true;

            if (piece.m_resources == null)
                return;

            Inventory inv = player.GetInventory();
            Pure.StockTally chests = ContainerStock.Get(player);

            for (int i = 0; i < piece.m_resources.Length; i++)
            {
                Piece.Requirement req = piece.m_resources[i];
                if (req == null || req.m_resItem == null || req.m_amount <= 0)
                    continue;

                string token = PieceCatalog.ItemToken(req);
                int have = inv != null && token != null ? inv.CountItems(token) : 0;
                int inChests = chests.Count(token);

                rec.Materials.Add(new MaterialLine
                {
                    DisplayName = token != null ? Localization.instance.Localize(token) : "?",
                    Amount = req.m_amount,
                    Have = have,
                    InChests = inChests
                });

                if (have < req.m_amount)
                    rec.HaveAllMaterials = false;
                if (have + inChests < req.m_amount)
                    rec.HaveAllWithChests = false;
            }
        }
```

In `AddFreeGains` and `AddShelter`, beside each `HaveAllMaterials = true,` add `HaveAllWithChests = true,`.

In `Compare`, directly after the `HaveAllMaterials` block:

```csharp
            if (a.HaveAllWithChests != b.HaveAllWithChests)
                return a.HaveAllWithChests ? -1 : 1;
```

- [ ] **Step 3: Strings**

`en.json` — add before the closing brace (keep the comma on the previous last line):

```json
  "comfortaudit_in_chests": "({0} + {1} in chests)"
```

`nb.json`:

```json
  "comfortaudit_in_chests": "({0} + {1} i kister)"
```

- [ ] **Step 4: Panel rendering**

In `ComfortPanel.cs`, add `using ComfortAudit.Core.Pure;` and add an amber helper next to `Good`/`Bad`:

```csharp
        private static string Amber(string s) { return "<color=#F2C14E>" + s + "</color>"; }
```

Replace the material loop in `AppendRecommendation` (from `sb.Append("      ");` before the `for` to the closing `sb.Append('\n');`) with:

```csharp
            sb.Append("      ");
            for (int i = 0; i < r.Materials.Count; i++)
            {
                MaterialLine m = r.Materials[i];
                if (i > 0) sb.Append(Dim).Append(", ").Append(Reset);

                string line = m.Amount + " " + m.DisplayName;

                // Counts appear only when they say something: "(have 2)" when some is carried,
                // "(1 + 6 in chests)" when chests hold some. Annotating every line was noise.
                switch (MaterialMath.Annotate(m.Amount, m.Have, m.InChests))
                {
                    case MaterialAnnotation.Have:
                        line += " " + Strings.Get("$comfortaudit_have", m.Have);
                        break;
                    case MaterialAnnotation.HaveAndChests:
                        line += " " + Strings.Get("$comfortaudit_in_chests", m.Have, m.InChests);
                        break;
                }

                switch (m.State)
                {
                    case MaterialState.Enough:
                        sb.Append(Dim).Append(line).Append(Reset);
                        break;
                    case MaterialState.EnoughWithChests:
                        sb.Append(Amber(line));
                        break;
                    default:
                        sb.Append(Bad(line));
                        break;
                }
            }
            sb.Append('\n');
```

- [ ] **Step 5: Build and test**

Run the build (`0 Error(s)`) and the tests (20 passing).

- [ ] **Step 6: Commit**

```bash
git add src/ComfortAudit/Model/ComfortModel.cs src/ComfortAudit/Core/Recommender.cs src/ComfortAudit/UI/ComfortPanel.cs src/ComfortAudit/L10n/en.json src/ComfortAudit/L10n/nb.json
git commit -m "Show chest stock in suggestion materials"
```

---

### Task 5: Build references for runtime sprite assets, and sprite tags

**Files:**
- Modify: `src/ComfortAudit/ComfortAudit.csproj`
- Add (gitignored): `lib/UnityEngine.TextCoreFontEngineModule.dll`
- Create: `src/ComfortAudit/Core/Pure/SpriteTags.cs`
- Create: `tests/ComfortAudit.Tests/SpriteTagsTests.cs`
- Modify: `README.md` (Building section's list of `lib/` assemblies)

**Interfaces:**
- Produces: `static string SpriteTags.GlyphName(string prefabName)` (null/empty → null), `static string SpriteTags.Tag(string glyphName)` (null/empty → `""`; otherwise `<sprite name="…">` followed by one space).

Why: `TMP_SpriteGlyph`, `GlyphMetrics`, `GlyphRect` and `FaceInfo` live in `UnityEngine.TextCore` (`UnityEngine.TextCoreFontEngineModule.dll`), and `TMP_Asset.version`'s setter is `internal` — an asset whose version is empty is "upgraded" (its tables cleared) the first time lookups are built. Publicizing `Unity.TextMeshPro` makes the setter reachable, exactly as `assembly_valheim` is handled; the shipped DLL still binds to the real members.

- [ ] **Step 1: Fetch the TextCore module from the rig**

```bash
scp equ@192.168.1.160:/games/SteamLibrary/steamapps/common/Valheim/valheim_Data/Managed/UnityEngine.TextCoreFontEngineModule.dll lib/
```

Expected: the file exists in `lib/` (gitignored — do not commit it).

- [ ] **Step 2: csproj**

Change the TMP reference and add TextCore beside the other Unity modules:

```xml
    <Reference Include="Unity.TextMeshPro"                  HintPath="$(ValheimManaged)/Unity.TextMeshPro.dll" Publicize="true" Private="false" />
    <Reference Include="UnityEngine.TextCoreFontEngineModule" HintPath="$(ValheimManaged)/UnityEngine.TextCoreFontEngineModule.dll" Private="false" />
```

In `README.md`'s Building section, add `UnityEngine.TextCoreFontEngineModule` to the list of needed assemblies (it is covered by "the `UnityEngine.*` modules", but name it since it is new), and change "`assembly_valheim` is publicized at build time" to "`assembly_valheim` and `Unity.TextMeshPro` are publicized at build time".

Build: `0 Error(s)`.

- [ ] **Step 3: Write the failing tests**

`tests/ComfortAudit.Tests/SpriteTagsTests.cs`:

```csharp
using ComfortAudit.Core.Pure;
using Xunit;

namespace ComfortAudit.Tests
{
    public class SpriteTagsTests
    {
        [Fact]
        public void TagWrapsNameAndAddsSpacing()
        {
            Assert.Equal("<sprite name=\"piece_chair\"> ", SpriteTags.Tag("piece_chair"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void NoNameNoTag(string name)
        {
            Assert.Equal("", SpriteTags.Tag(name));
        }

        [Theory]
        [InlineData("piece_chair", "piece_chair")]
        [InlineData("Some Mod \"Chair\" <v2>", "Some Mod _Chair_ _v2_")] // would break the tag
        [InlineData(null, null)]
        [InlineData("", null)]
        public void GlyphNameStripsTagBreakingCharacters(string prefab, string expected)
        {
            Assert.Equal(expected, SpriteTags.GlyphName(prefab));
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Expected: build FAILS, `SpriteTags` not found.

- [ ] **Step 5: Implement**

`src/ComfortAudit/Core/Pure/SpriteTags.cs`:

```csharp
namespace ComfortAudit.Core.Pure
{
    /// <summary>
    /// Names and tags for piece icons rendered inline by TextMeshPro. Glyphs are named after the
    /// prefab, which is unique per piece; the tag is looked up by that name across the primary
    /// sprite asset and its fallbacks.
    /// </summary>
    public static class SpriteTags
    {
        /// <summary>
        /// The prefab name with any character that would end the tag's quoted attribute or the
        /// tag itself replaced. Vanilla prefab names never contain these; modded ones might.
        /// </summary>
        public static string GlyphName(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName))
                return null;

            return prefabName.Replace('"', '_').Replace('<', '_').Replace('>', '_');
        }

        /// <summary>The inline tag, with a trailing space so the icon never touches the text.</summary>
        public static string Tag(string glyphName)
        {
            return string.IsNullOrEmpty(glyphName) ? "" : "<sprite name=\"" + glyphName + "\"> ";
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Expected: `Passed!  - Failed: 0, Passed: 27`. Build: `0 Error(s)`.

- [ ] **Step 7: Commit**

```bash
git add src/ComfortAudit/ComfortAudit.csproj src/ComfortAudit/Core/Pure/SpriteTags.cs tests/ComfortAudit.Tests/SpriteTagsTests.cs README.md
git commit -m "Reference TextCore and publicize TMP for runtime sprite assets"
```

---

### Task 6: IconAtlas and icons in the panel

**Files:**
- Create: `src/ComfortAudit/UI/IconAtlas.cs`
- Modify: `src/ComfortAudit/PluginConfig.cs` (`ShowIcons`)
- Modify: `src/ComfortAudit/Core/PieceCatalog.cs` (`Invalidate`)
- Modify: `src/ComfortAudit/Model/ComfortModel.cs` (`PlacementPreviewResult.PrefabName`)
- Modify: `src/ComfortAudit/Core/PlacementPreview.cs` (set it)
- Modify: `src/ComfortAudit/UI/ComfortPanel.cs` (`SetSpriteAsset`, tags in rows)
- Modify: `src/ComfortAudit/Plugin.cs` (drive the atlas)

**Interfaces:**
- Consumes: `SpriteTags` (Task 5), `PieceCatalog.Entries()`, `PieceCatalog.Ready`.
- Produces: `static void IconAtlas.EnsureBuilt()`, `static void IconAtlas.Invalidate()`, `static TMP_SpriteAsset IconAtlas.Primary` (null until built / when none), `static string IconAtlas.Tag(string prefabName)` (`""` when icons are off, unbuilt, or the prefab has no glyph), `PluginConfig.ShowIcons`, `ComfortPanel.SetSpriteAsset(TMP_SpriteAsset)`, `PlacementPreviewResult.PrefabName`.

Approach: one `TMP_SpriteAsset` per distinct icon texture, glyph rects taken from `sprite.textureRect` — no pixels are copied. Metrics are normalised so every icon renders one line tall regardless of its pixel size: the asset's `FaceInfo.pointSize` is 100 and each glyph is 100×100 units with bearing-Y 80 (sits on the baseline like a capital letter) and advance 105. `Sprite.textureRect` throws for tightly-packed atlas sprites; those pieces simply get no icon.

- [ ] **Step 1: Config**

`PluginConfig.cs` — field:

```csharp
        public static ConfigEntry<bool> ShowIcons;
```

bind after `ShowDistances`:

```csharp
            ShowIcons = cfg.Bind("Panel", "ShowIcons", true,
                new ConfigDescription(
                    "Show each piece's build-menu icon beside its name in the panel.",
                    null,
                    Attr(52)));
```

handler:

```csharp
            ShowIcons.SettingChanged += (s, e) => Raise(ContentChanged);
```

- [ ] **Step 2: IconAtlas**

`src/ComfortAudit/UI/IconAtlas.cs`:

```csharp
using System.Collections.Generic;
using ComfortAudit.Core;
using ComfortAudit.Core.Pure;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;

namespace ComfortAudit.UI
{
    /// <summary>
    /// Piece icons as TextMeshPro sprites, so the panel can stay one rich-text block and still
    /// show a piece's icon inline.
    ///
    /// Icons already live in the game's sprite textures, so each distinct texture gets a sprite
    /// asset whose glyphs point at the icons' existing rects — nothing is copied. The first asset
    /// is the panel's; the rest are its fallbacks, and TMP searches those when resolving a
    /// <c>&lt;sprite name=…&gt;</c> tag, so it does not matter which texture holds an icon.
    /// </summary>
    public static class IconAtlas
    {
        // Glyph geometry in the asset's own units; TMP scales by fontSize / pointSize, so every
        // icon renders one line tall whatever its pixel size.
        private const float PointSize = 100f;
        private const float Ascent = 80f;
        private const float Advance = 105f;

        private static readonly List<TMP_SpriteAsset> Assets = new List<TMP_SpriteAsset>();
        private static readonly HashSet<string> Glyphs = new HashSet<string>();
        private static bool _built;
        private static Shader _shader;

        public static TMP_SpriteAsset Primary => Assets.Count > 0 ? Assets[0] : null;

        public static string Tag(string prefabName)
        {
            if (!_built || !PluginConfig.ShowIcons.Value)
                return "";

            string glyph = SpriteTags.GlyphName(prefabName);
            return glyph != null && Glyphs.Contains(glyph) ? SpriteTags.Tag(glyph) : "";
        }

        public static void Invalidate()
        {
            for (int i = 0; i < Assets.Count; i++)
            {
                if (Assets[i] == null) continue;
                if (Assets[i].material != null) Object.Destroy(Assets[i].material);
                Object.Destroy(Assets[i]);
            }
            Assets.Clear();
            Glyphs.Clear();
            _built = false;
        }

        /// <summary>Builds once the catalogue exists; a no-op afterwards and while icons are off.</summary>
        public static void EnsureBuilt()
        {
            if (_built || !PluginConfig.ShowIcons.Value || !PieceCatalog.Ready)
                return;

            _built = true;   // one attempt per catalogue: a failure must not retry every frame

            if (_shader == null)
                _shader = Shader.Find("TextMeshPro/Sprite");
            if (_shader == null)
            {
                ComfortAuditPlugin.Log.LogWarning("icons: TextMeshPro/Sprite shader not found; panel will show no icons");
                return;
            }

            var byTexture = new Dictionary<Texture2D, List<KeyValuePair<string, Rect>>>();
            int skipped = 0;

            List<PieceCatalog.Entry> entries = PieceCatalog.Entries();
            for (int i = 0; i < entries.Count; i++)
            {
                PieceCatalog.Entry e = entries[i];
                Sprite icon = e.Piece != null ? e.Piece.m_icon : null;
                string glyph = SpriteTags.GlyphName(e.PrefabName);
                if (icon == null || icon.texture == null || glyph == null || Glyphs.Contains(glyph))
                    continue;

                Rect rect;
                try
                {
                    rect = icon.textureRect;   // throws for tightly-packed atlas sprites
                }
                catch (System.Exception)
                {
                    skipped++;
                    continue;
                }

                List<KeyValuePair<string, Rect>> list;
                if (!byTexture.TryGetValue(icon.texture, out list))
                    byTexture[icon.texture] = list = new List<KeyValuePair<string, Rect>>();
                list.Add(new KeyValuePair<string, Rect>(glyph, rect));
                Glyphs.Add(glyph);
            }

            foreach (KeyValuePair<Texture2D, List<KeyValuePair<string, Rect>>> kv in byTexture)
                Assets.Add(Build(kv.Key, kv.Value));

            if (Assets.Count > 1)
                Assets[0].fallbackSpriteAssets = Assets.GetRange(1, Assets.Count - 1);

            ComfortAuditPlugin.Log.LogInfo(string.Format(
                "icons: {0} glyph(s) over {1} texture(s), {2} skipped", Glyphs.Count, Assets.Count, skipped));
        }

        private static TMP_SpriteAsset Build(Texture2D texture, List<KeyValuePair<string, Rect>> icons)
        {
            var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            // hashCode is derived lazily from the name by TMP itself; do not assign it.
            asset.name = "ComfortAudit_" + texture.name;

            // Set before the material: an empty version makes UpdateLookupTables "upgrade" the
            // asset from its legacy list, clearing the tables built below.
            asset.version = "1.1.0";
            asset.spriteSheet = texture;

            var face = new FaceInfo();
            face.pointSize = (int)PointSize;
            face.scale = 1f;
            face.lineHeight = PointSize;
            face.ascentLine = Ascent;
            face.descentLine = Ascent - PointSize;
            asset.faceInfo = face;

            for (int i = 0; i < icons.Count; i++)
            {
                Rect r = icons[i].Value;
                var glyph = new TMP_SpriteGlyph(
                    (uint)i,
                    new GlyphMetrics(PointSize, PointSize, 0f, Ascent, Advance),
                    new GlyphRect((int)r.x, (int)r.y, (int)r.width, (int)r.height),
                    1f, 0);
                asset.spriteGlyphTable.Add(glyph);

                var character = new TMP_SpriteCharacter(0xFFFE, asset, glyph);
                character.name = icons[i].Key;
                asset.spriteCharacterTable.Add(character);
            }

            var material = new Material(_shader);
            material.SetTexture(ShaderUtilities.ID_MainTex, texture);
            asset.material = material;

            asset.UpdateLookupTables();
            return asset;
        }
    }
}
```

If a member used above does not compile against the game's TMP build (e.g. `faceInfo` has no setter or `FaceInfo` fields differ), inspect it with
`ilspycmd -t TMPro.TMP_Asset lib/Unity.TextMeshPro.dll -r lib` (run with `DOTNET_ROOT=$HOME/.dotnet`) and use the publicized backing field (`m_FaceInfo`, `m_Material`) instead. Do not change the approach.

- [ ] **Step 3: Invalidate with the catalogue**

In `PieceCatalog.Invalidate()` add `UI.IconAtlas.Invalidate();` after `Ceiling.Invalidate();`.

- [ ] **Step 4: Preview prefab name**

`ComfortModel.cs`, in `PlacementPreviewResult` after `DisplayName`:

```csharp
        public string PrefabName;
```

`PlacementPreview.cs`, after `result.DisplayName = …;`:

```csharp
            result.PrefabName = PrefabName(ghost.name);
```

- [ ] **Step 5: Panel**

In `ComfortPanel.cs`:

Add the method near `SetPreview`:

```csharp
        /// <summary>
        /// Icon glyphs come from IconAtlas once the catalogue is ready, which is after the panel
        /// has already rendered, so a change of asset forces a rebuild of the text.
        /// </summary>
        public void SetSpriteAsset(TMP_SpriteAsset asset)
        {
            if (_body == null || _body.spriteAsset == asset)
                return;

            _body.spriteAsset = asset;
            _dirty = true;
        }
```

Insert icon tags (each `IconAtlas.Tag(...)` returns `""` when there is no icon, so no other change is needed):

- Contributing rows: replace
  `sb.Append("  ").Append(ComfortGroups.Name(e.Group)).Append(": ")`
  with
  `sb.Append("  ").Append(IconAtlas.Tag(e.PrefabName)).Append(ComfortGroups.Name(e.Group)).Append(": ")`
- Ignored rows: replace
  `sb.Append(Dim).Append("  ").Append(ComfortGroups.Name(e.Group)).Append(": ")`
  with
  `sb.Append(Dim).Append("  ").Append(IconAtlas.Tag(e.PrefabName)).Append(ComfortGroups.Name(e.Group)).Append(": ")`
- Recommendations: in `AppendRecommendation`, replace
  `sb.Append("  ").Append(Orange("+" + r.Gain)).Append("  ").Append(r.DisplayName);`
  with
  `sb.Append("  ").Append(Orange("+" + r.Gain)).Append("  ").Append(IconAtlas.Tag(r.PrefabName)).Append(r.DisplayName);`
- Preview: in `AppendPreview`, change `.Append(Orange(Strings.Get("$comfortaudit_preview"))).Append('\n').Append("  ");` to end with `.Append("  ").Append(IconAtlas.Tag(_preview.PrefabName));`

Sprites ignore `<color>` unless tagged `tint`, so icons inside dimmed rows keep their own colours.

- [ ] **Step 6: Plugin**

In `Plugin.Update`, directly before `_panel.Render(_snapshot);`:

```csharp
            IconAtlas.EnsureBuilt();
            _panel.SetSpriteAsset(PluginConfig.ShowIcons.Value ? IconAtlas.Primary : null);
```

(`ComfortAudit.UI` is already imported in `Plugin.cs`.)

- [ ] **Step 7: Build and test**

Build `0 Error(s)`; tests 27 passing.

- [ ] **Step 8: Commit**

```bash
git add src/ComfortAudit/UI/IconAtlas.cs src/ComfortAudit/PluginConfig.cs src/ComfortAudit/Core/PieceCatalog.cs src/ComfortAudit/Model/ComfortModel.cs src/ComfortAudit/Core/PlacementPreview.cs src/ComfortAudit/UI/ComfortPanel.cs src/ComfortAudit/Plugin.cs
git commit -m "Show piece icons in the panel"
```

---

### Task 7: Rig check (needs the user at the machine)

**Files:** none changed unless a check fails.

- [ ] **Step 1: Deploy over the Thunderstore install in the Default profile**

```bash
P=/home/equ/.config/r2modmanPlus-local/Valheim/profiles/Default/BepInEx/plugins/Jumpingmushroom-ComfortAudit/ComfortAudit
ssh equ@192.168.1.160 "bash -lc 'test -f $P/ComfortAudit.dll.bak-0.4.5 || cp $P/ComfortAudit.dll $P/ComfortAudit.dll.bak-0.4.5'"
scp src/ComfortAudit/bin/Release/net472/ComfortAudit.dll equ@192.168.1.160:$P/
```

- [ ] **Step 2: Ask the user to run these checks, and read the BepInEx log afterwards**

Log: `ssh equ@192.168.1.160 "bash -lc 'tail -n 200 ~/.config/r2modmanPlus-local/Valheim/profiles/Default/BepInEx/LogOutput.log'"` — look for the `icons: N glyph(s) over M texture(s), K skipped` line and any exceptions.

Icons:
1. F7 in a furnished room: icons before Contributing rows, Ignored rows, suggestions, and the placement-preview line while holding a chair.
2. Icons are one line tall, sit on the baseline, and the panel's height/scroll still fit the text.
3. Roof suggestion and Missing groups have no icon, and nothing is misaligned.
4. `ShowIcons` off in the config manager removes them immediately; on brings them back.

Chests:
5. `comfortaudit chests` lists nearby chests and a cart as `count`, a chest in someone else's ward (or a private chest placed by another player) as `skip`, and a chest just beyond 20 m not at all.
6. A suggestion needing wood you carry partly, with the rest in a chest: amber `N Wood (h + c in chests)`.
7. A material short even with chests: red with the `(h + c in chests)` annotation.
8. `ChestRadius` 0: no chest annotations anywhere.

- [ ] **Step 3: If icons fail to render (blank, magenta, or missing) — fallback**

Decide from the log and a screenshot. If the shader was not found or icons render wrong, replace the per-texture approach in `IconAtlas.EnsureBuilt` with a copy into one atlas: for each icon, `Graphics.Blit` its texture into a temporary `RenderTexture` sized to the source texture, `ReadPixels` the icon's `textureRect` into a 64×64 cell of a new readable `Texture2D` (`TextureFormat.RGBA32`), track each cell's rect, `Apply()`, and call `Build(atlas, cells)` once. If `TextMeshPro/Sprite` itself is missing, take the shader from `TMP_Settings.defaultSpriteAsset?.material?.shader` instead. Re-run the checks.

- [ ] **Step 4: Fix-and-recheck any failure, committing each fix separately.**

---

### Task 8: Release 0.5.0 (publish only on the user's go)

**Files:**
- Modify: `src/ComfortAudit/Plugin.cs` (`PluginVersion = "0.5.0"`)
- Modify: `src/ComfortAudit/ComfortAudit.csproj` (`<Version>0.5.0</Version>` — it still says 0.4.4)
- Modify: `thunderstore/manifest.json` (`version_number`)
- Modify: `CHANGELOG.md`, `README.md`, `thunderstore/README.md`

- [ ] **Step 1: Versions** — set all three to `0.5.0`.

- [ ] **Step 2: CHANGELOG** — add at the top:

```markdown
## 0.5.0 — icons and chests

- New: piece icons beside every piece in the panel — contributing, ignored, suggestions and the
  placement preview. `Panel/ShowIcons` turns them off.
- New: suggestion materials count what is in nearby chests and carts. Enough once the chests are
  counted reads amber, `4 Fine wood (1 + 6 in chests)`, since vanilla cannot build from a chest.
  Chests you could not open — warded, or someone else's private chest — and graves are skipped.
  `Recommendations/ChestRadius` (default 20 m, 0 = off).
- New: `comfortaudit chests` lists which containers were counted and why others were skipped.
- Suggestions that are fully covered by carried-plus-chest materials now rank above ones that are
  not, when otherwise tied.
```

- [ ] **Step 3: README** — under "What it does", extend the Next upgrade bullet with "…with a have/need check against your inventory and nearby chests"; add `ShowIcons` and `ChestRadius` rows to the Configuration table; add `comfortaudit chests` to the Console block. Mirror the same in `thunderstore/README.md` (absolute image URLs pinned to the release tag, per the release notes in memory).

- [ ] **Step 4: Package**

```bash
./build/package.sh
```

Expected: validation `ok`, `dist/ComfortAudit-0.5.0.zip` created.

- [ ] **Step 5: Commit**

```bash
git add -A src thunderstore CHANGELOG.md README.md
git commit -m "Release 0.5.0: icons and chests"
```

- [ ] **Step 6: Stop and ask the user before publishing.** On their go:

```bash
TS_TEAM=Jumpingmushroom TCLI_AUTH_TOKEN=<token> ./build/publish.sh
gh release create v0.5.0 dist/ComfortAudit-0.5.0.zip --repo jumpingmushroom/ComfortAudit
git push
```
