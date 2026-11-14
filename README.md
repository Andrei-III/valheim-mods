# valheim-mods

BepInEx mods for Valheim. Click a mod to expand its details.

<details>
<summary><b>HarpoonReel</b>: hold a key to reel a harpooned creature all the way in to you.</summary>

<br>

In vanilla Valheim, the harpoon keeps a creature from getting farther away than it was when you hit it, but never pulls it any closer. HarpoonReel lets you reel it in: hold **Left Alt** and the line keeps shortening until the creature is next to you. It's handy for hunting hares, boars, deer and anything else that runs away.

The mod doesn't add its own pull. It shortens the vanilla harpoon line, so the game's normal behavior still applies:
- Reeling costs stamina. You let go of the creature when your stamina runs out.
- The line can still snap if the creature pulls away hard enough.
- Bosses can't be harpooned.
- Attacking or blocking releases the creature.

#### Installation

Put `HarpoonReel.dll` in `BepInEx/plugins/HarpoonReel/`. Tested with Valheim 1.0.21 and Jötunn 2.30.2.

**Multiplayer:** every player **and** the dedicated server need the mod. The pull is calculated on whichever machine controls the creature, which can be any player or the server. Jötunn checks versions when you connect and refuses to join if someone's version doesn't match.

#### Usage

1. Hit a creature with a harpoon.
2. Hold **Left Alt** to reel it in. Let go to stop. The line then keeps its current length.

Reeling is ignored while chat, the console, a menu, the map or the inventory is open.

#### Configuration

The settings are in `BepInEx/config/andrei.valheim.harpoonreel.cfg`. The file is created the first time the mod loads.

| Setting | Default | Synced from server | Description |
|---|---|---|---|
| `ReelKey` | `LeftAlt` | No | Key to hold for reeling. |
| `AutoReel` | `false` | Yes | Reel automatically, without holding the key. |
| `ReelSpeed` | `6` (0.5 to 30) | Yes | Maximum speed at which the line shortens, in meters per second. |
| `MinDistance` | `1.5` (0.5 to 10) | Yes | How close the creature gets before reeling stops, in meters. |
| `StaminaMultiplier` | `1` (0 to 5) | Yes | Multiplies the vanilla harpoon stamina drain while you're reeling. `0` makes reeling free. |
| `DebugLogging` | `false` | Yes | Logs each harpooned creature's mass and harpoon values to `BepInEx/LogOutput.log`. |

The server's config overrides the synced settings on every client.

#### Compatibility

- Works alongside FishingEnhanced (harpooning, shooting or spearing fish). The two mods patch different code.
- Patches `SE_Harpooned.SetAttacker` and `SE_Harpooned.UpdateStatusEffect`. Other mods that heavily change harpoon behavior may conflict.

#### How it works

Vanilla `SE_Harpooned` records the distance at the moment of the hit (`m_baseDistance`). It only pulls the creature when the creature gets farther away than that. While you reel, the mod shortens `m_baseDistance` toward `MinDistance`, and the vanilla pull then drags the creature in. The mod never shortens the line to more than `m_smoothDistance` below the creature's current distance. At that point the vanilla pull is already at full strength, and reeling can't snap the line by itself.

Whether you're reeling is stored on your player's network object. That way, the machine that controls the creature knows you're reeling even when it isn't your machine.

</details>

<details>
<summary><b>SummonTracker</b>: lists your staff summons under the minimap with their name, health and distance.</summary>

<br>

Shows a list of your summoned creatures under the minimap: the summons that follow you, like Dead Raiser skeletons. Summons that don't follow you are left out, such as Staff of the Wild roots and Trollstav trolls (which are hostile to everyone). Each row shows:
- the creature's icon
- its name (including names you gave it), with level stars
- a health bar
- how far away it is (highlighted when it's more than 40 m away)

When a summon dies or gets unsummoned, its row disappears. So when you see fewer rows than your summon limit, you know it's time to summon again.

A row turns dim when the summon is outside your loaded area. Its health and position then show the last known values.

If a summon type is set up to die after a fixed time, its row also shows a countdown.

#### Installation

Put `SummonTracker.dll` in `BepInEx/plugins/SummonTracker/`.

**Multiplayer:** only you need it. The mod reads data the game already syncs and doesn't change gameplay, so other players and the server don't need it installed.

#### Configuration

`BepInEx/config/andrei.valheim.summontracker.cfg`: `ShowDistance`, `Scale`, `FarDistance`, `DebugLogging`.

</details>

<details>
<summary><b>RepairAll</b>: one click on the repair button repairs every item the crafting station can fix.</summary>

<br>

In vanilla Valheim, each click on a crafting station's repair button (the hammer) repairs only one item. RepairAll makes a single click repair every worn item in your inventory that the station can repair. It works at any station that can repair: workbench, forge, black forge, galdr table, and so on.

You get one small message in the top-left log ("Repaired 5 items") and one repair sound, instead of a big center-screen message and a sound for every item. If some worn items need a different station or a higher station level, the message tells you how many are left.

The mod uses the vanilla repair code for each item, so the vanilla rules still apply: which station repairs what, station level requirements, and Crafting skill gain.

#### Installation

Put `RepairAll.dll` in `BepInEx/plugins/RepairAll/`.

**Multiplayer:** only you need it. Item durability is stored in your own inventory, so other players and the server don't need the mod installed.

#### Configuration

`BepInEx/config/andrei.valheim.repairall.cfg`: `Enabled` (set to `false` for the vanilla one-item-per-click behavior), `Message` (`TopLeft`, `Center` or `None`; where the repair summary appears), `DebugLogging`.

</details>

<details>
<summary><b>StayLoaded</b>: crossbows and other reload weapons stay loaded when you sheathe them or switch weapons.</summary>

<br>

In vanilla Valheim, a weapon that has to be reloaded (like the arbalest) loses its load as soon as you sheathe it with **R** or switch to another weapon. When you draw it again, you have to wait through the reload once more. StayLoaded remembers which of your weapons are loaded. A loaded weapon stays ready to fire until you actually fire it, however often you sheathe it or switch away and back.

Each weapon is tracked separately, so you can carry two loaded crossbows and swap between them. Firing still empties the weapon, and the next reload works as usual.

The loaded state is kept only for the current session. After you log out, or if you drop the weapon and pick it up again, it needs a fresh reload.

#### Installation

Put `StayLoaded.dll` in `BepInEx/plugins/StayLoaded/`.

**Multiplayer:** only you need it. The loaded state belongs to your own character, so other players and the server don't need the mod installed.

#### Configuration

`BepInEx/config/andrei.valheim.stayloaded.cfg`: `Enabled`, `DebugLogging`.

</details>

<details>
<summary><b>QuickFill</b>: Shift + E fills a smelter, kiln, blast furnace or other processing station in one go.</summary>

<br>

In vanilla Valheim, every press of **E** on a smelter puts in one piece of ore or fuel. QuickFill keeps that, and adds **Shift + E**: it puts in as much as the station can take, or as much as you have. It works on every station of this type: smelter, charcoal kiln, blast furnace, spinning wheel, windmill, eitr refinery and so on. Use it on the ore slot or the fuel slot, whichever you're looking at. The hover text shows a "Shift + E Fill" hint.

You get one message, for example "Added Copper ore x7, Tin ore x3", and one "item added" sound, instead of one of each per item.

Each item goes in through the game's normal add-ore or add-fuel code, exactly as if you had pressed E that many times. Other mods that hook into adding items still work, such as InventoryReforged taking ore and fuel from nearby chests.

The modifier is the game's "alternative placement" key (Left Shift by default), the same one other Shift + E actions use.

#### Installation

Put `QuickFill.dll` in `BepInEx/plugins/QuickFill/`.

**Multiplayer:** only you need it for filling. It sends the same messages to the station as pressing E does. The "item added" sound is played by whichever machine controls the station, which can be you, another player or the server. That machine plays the sound once per fill if it has the mod, and once per item if it doesn't. Install it on the server and for your friends too if you want one sound for every fill.

#### Configuration

`BepInEx/config/andrei.valheim.quickfill.cfg`: `Enabled`, `ShowHint` (the "Shift + E Fill" line in the hover text), `DebugLogging`.

</details>

---

## Requirements

Every mod needs:
- [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
- [Jötunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/)

Each mod's config file is created in `BepInEx/config/` the first time the mod loads.

## Building from source

You need the .NET SDK (6 or newer) and a Valheim install with BepInEx and Jötunn. Build a mod from its folder:

```sh
cd HarpoonReel
dotnet build -c Release
```

The build references the game DLLs from the Steam install at `~/.local/share/Steam/steamapps/common/Valheim`. When it finishes, it copies the mod's DLL into that install's `BepInEx/plugins/<ModName>/`. If Valheim is installed somewhere else:

```sh
dotnet build -c Release -p:ValheimDir=/path/to/Valheim
```
