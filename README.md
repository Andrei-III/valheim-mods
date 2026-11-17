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
<summary><b>QuickFill</b>: Shift + E fills a smelter, kiln, blast furnace, ballista, shield generator, fire or other processing station in one go.</summary>

<br>

In vanilla Valheim, every press of **E** on a smelter puts in one piece of ore or fuel. QuickFill keeps that, and adds **Shift + E**: it puts in as much as the station can take, or as much as you have. It works on every station of this type: smelter, charcoal kiln, blast furnace, spinning wheel, windmill, eitr refinery and so on. Use it on the ore slot or the fuel slot, whichever you're looking at. It also fills fires and lights (campfire, hearth, bonfire, braziers, sconces, the hot tub and so on) with their fuel, fills the Ashlands shield generator with bones, and loads a ballista with as many bolts as it has room for (up to BallistaGuard's raised cap if you have that mod). The hover text shows a "Shift + E Fill" hint.

You get one message, for example "Added Copper ore x7, Tin ore x3", and one "item added" sound, instead of one of each per item.

Each item goes in through the game's normal add-ore or add-fuel code, exactly as if you had pressed E that many times. Other mods that hook into adding items still work, such as InventoryReforged taking ore and fuel from nearby chests.

The modifier is the game's "alternative placement" key (Left Shift by default), the same one other Shift + E actions use.

#### Installation

Put `QuickFill.dll` in `BepInEx/plugins/QuickFill/`.

**Multiplayer:** only you need it for filling. It sends the same messages to the station as pressing E does. The "item added" sound is played by whichever machine controls the station, which can be you, another player or the server. That machine plays the sound once per fill if it has the mod, and once per item if it doesn't. Install it on the server and for your friends too if you want one sound for every fill.

#### Configuration

`BepInEx/config/andrei.valheim.quickfill.cfg`: `Enabled`, `ShowHint` (the "Shift + E Fill" line in the hover text), `DebugLogging`.

</details>

<details>
<summary><b>DamagePreview</b>: see how much damage your arrow or bolt will deal, drawn on the target's health bar before you shoot.</summary>

<br>

While you draw a bow at a creature, two yellow segments grow on its health bar, starting from the bar's left edge:
- **Solid yellow:** the damage you'll deal even on the lowest roll.
- **Faded yellow:** the extra damage you might deal, up to the highest roll.
- When even the lowest roll would kill, the solid segment turns **white**.

Damage in Valheim is partly random. The roll depends on your skill, so the faded segment shrinks as your Bows or Crossbows skill goes up. A bow's damage also scales with how far it's drawn, so the segments grow as the draw charges. Crossbows show the preview whenever they're loaded.

The prediction takes into account:
- your weapon and the ammo you have equipped
- your skill level and, for bows, how far the bow is drawn
- meads and set bonuses
- the target's resistances and weaknesses, including weak spots under the crosshair
- sneak-attack bonuses on creatures that haven't noticed you
- double damage against staggered creatures
- multiplayer damage scaling and world damage modifiers

Fire, poison and spirit damage hurt the target over time and aren't drawn.

**With ArrowArc:** when **ArrowArc** (below) is installed and drawing an arc for your weapon, the preview is for the creature the arc lands on, so arrow drop is taken into account. Otherwise it's for the creature under the crosshair. Turn this off with `UseArrowArc = false`.

Works on hostile and neutral creatures (deer, hares and so on) and on bosses, using the boss bar at the top of the screen. Tamed creatures and other players never get a preview. While you aim at a creature, its health bar also shows at range (vanilla only shows it within 10 m).

#### Installation

Put `DamagePreview.dll` in `BepInEx/plugins/DamagePreview/`.

**Multiplayer:** only you need it. The mod reads data the game already syncs and only draws on your own screen.

#### Configuration

`BepInEx/config/andrei.valheim.damagepreview.cfg`: `Enabled`, `IncludeCrossbows`, `MaxRange` (meters, default 60), `UseArrowArc` (see below), `GuaranteedColor`, `PossibleColor`, `KillColor`, `DebugLogging` (logs the predicted damage range to `BepInEx/LogOutput.log`).

</details>

<details>
<summary><b>ArrowArc</b>: draws the path your arrow, bolt or grappling hook will fly, and where it lands, before you shoot.</summary>

<br>

While you draw a bow, a thin line shows the shot's predicted flight, with a ring where it lands. Crossbows and the grappling hook show it whenever they're loaded. The line follows the game's own launch and flight rules: the weapon and ammo speed, how far the bow is drawn, gravity and air drag.

The colour tells you what the shot will do:
- **White:** it lands on terrain, a building or water.
- **Red:** it hits a creature.
- **Blue:** it hits nothing. The shot runs out of flight time in mid-air (the grappling hook only flies about 40 m) or goes past `MaxDistance`.

The ring's size shows the shot's random spread at that distance, so it shrinks as a bow charges.

Each weapon type can be turned on or off. For example, you can keep the arc for the grappling hook only. When the arc is off for a weapon, **DamagePreview** goes back to the creature under the crosshair for it.

#### Installation

Put `ArrowArc.dll` in `BepInEx/plugins/ArrowArc/`.

**Multiplayer:** only you need it. The mod predicts the shot locally and only draws on your own screen.

#### Configuration

`BepInEx/config/andrei.valheim.arrowarc.cfg`: `Enabled`, `ShowForBows`, `ShowForCrossbows`, `ShowForGrapplingHook`, `OnlyAtFullDraw` (hide the arc until the bow is fully drawn), `MaxDistance` (meters, default 200), `ShowImpactMarker`, `MarkerShowsSpread`, `LineWidth`, `LineColor`, `CreatureHitColor`, `NoHitColor`, `DebugLogging` (logs the launch speed, gravity and drag to `BepInEx/LogOutput.log`).

</details>

<details>
<summary><b>QuietAoe</b>: stops a vanilla warning that can flood the log 50 times a second and cause stutter.</summary>

<br>

Some objects carry an area-damage component that isn't meant to react to touch, such as a pufferfish on an item stand. When something rests against one, for example the feathers from a chicken farm that uses a pufferfish to cull chickens, vanilla writes `AOE got OnTriggerStay but trigger damage is disabled in ...` to the log on every physics tick. That's 50 lines a second for each object touching it. Writing that many Unity warnings causes frame hitches. It can also make creatures your game controls look laggy to other players.

In that situation the game does nothing except write the warning. QuietAoe skips the warning and changes nothing else. A pufferfish on a stand still damages creatures exactly as before.

#### Installation

Put `QuietAoe.dll` in `BepInEx/plugins/QuietAoe/`.

**Multiplayer:** each player who wants the fix needs it. The warning is written by whichever game runs physics for that area, usually the player closest to it. The server doesn't need it.

#### Configuration

`BepInEx/config/andrei.valheim.quietaoe.cfg`: `Enabled`, `DebugLogging` (logs each object that triggers the warning once, with its prefab, position and what touched it, plus a count every 30 seconds).

</details>

<details>
<summary><b>BallistaGuard</b>: a ballista with no trophy shoots hostile creatures instead of everything, you included, and sees all around it.</summary>

<br>

In vanilla, a ballista with no trophy loaded shoots the closest living thing it can see: players, tames, deer, chickens and Dvergr included. Loading trophies is the only way to filter it, and that limits it to a few creature types. With BallistaGuard, a ballista with no trophy only shoots hostile creatures: monsters from every biome, sea monsters and bosses. It still ignores sleeping creatures and anything it can't see. Each of these groups is left alone by default, and each has a setting to target it:

- players
- tamed creatures and staff summons
- passive animals that aren't tamed
- Dvergr that aren't angry at you

Creatures listed in `AlwaysTarget` are always shot unless tamed. The default list is boars, necks and greylings, which break windmills and other buildings at night. A ballista with trophies loaded still targets only those creatures, like vanilla.

The mod also:
- **Targets in a full circle.** The vanilla ballista covers only 50° to each side of where it faces. With the mod it turns to shoot in any direction. The range stays at vanilla 30 m, and the ring on the ground shows the full circle. With no target, it still sweeps back and forth across its front like vanilla.
- **Turns faster.** The default is twice as fast as vanilla's 45° per second, so a half turn takes about 2 s.
- **Fires faster.** The default is one bolt every 1.6 s instead of every 2 s, which lets its staggers be followed up. Ammo runs out faster accordingly.
- **Holds more ammo.** The default is 100 bolts, a full stack, instead of 40. The limit is checked by whoever loads the ballista, so a player without the mod still stops at 40.

Burnt ballistas found in the world aren't affected.

#### Installation

Put `BallistaGuard.dll` in `BepInEx/plugins/BallistaGuard/`.

**Multiplayer:** every player should have it, with the same config. The ballista is controlled by one player's game, usually whoever reached the area first. That game picks the targets and fires. If that player doesn't have the mod, the ballista behaves like vanilla and can shoot players. The server doesn't need it.

#### Configuration

The settings are in `BepInEx/config/andrei.valheim.ballistaguard.cfg`.

| Setting | Default | Description |
|---|---|---|
| `Enabled` | `true` | Use the hostile-only targeting for ballistas with no trophy. |
| `FullCircle` | `true` | See and aim all around, not just 50° to each side. Applies with or without trophies. |
| `TurnRateMultiplier` | `2` (0.5 to 5) | Multiplier for turning speed. 1 is vanilla. |
| `FireRateMultiplier` | `1.25` (0.5 to 3) | Multiplier for fire rate. 1 is vanilla (one bolt every 2 s). |
| `MaxAmmo` | `100` (0 to 1000) | Bolts a ballista holds. 0 is vanilla (40). |
| `HoverLabel` | `hostiles` | Shown as the target in the ballista's hover text when no trophy is loaded. Vanilla shows "everything". |
| `TargetPlayers` | `false` | Also shoot players. |
| `TargetTamed` | `false` | Also shoot tamed creatures and staff summons. |
| `TargetPassiveAnimals` | `false` | Also shoot passive animals that aren't tamed, such as deer and chickens. |
| `TargetDverger` | `false` | Also shoot Dvergr that aren't angry at you. Angry Dvergr are always shot. |
| `AlwaysTarget` | `Boar,Neck,Greyling` | Comma-separated creature prefab names that are always shot unless tamed. |

</details>

<details>
<summary><b>SmoothNet</b>: stops creatures from teleporting around in big multiplayer fights.</summary>

<br>

In Valheim, creatures are run by one player's game, not by the server. The other players see them through updates sent player → server → player. Vanilla sends at most 10 KB of updates per player in each round (20 rounds a second), over a connection limited to 150 KB/s. In a big fight, for example in the Ashlands with many mobs, projectiles and lava effects, that isn't enough. Some creatures go a while without an update. When a creature you don't control ends up 5 m or more from where your game expected it, the game moves it instantly to its real position, so it looks like it teleports.

SmoothNet changes three things:
- **More updates per round:** the per-round limit goes from 10 KB to 64 KB, so more creatures get updated each round.
- **Faster connection:** the Steam send rate goes from 150 KB/s to 2 MB/s, so the larger rounds actually go out.
- **Gliding instead of jumping:** a creature that's off by less than 20 m glides quickly to its real position instead of jumping there. Larger jumps, such as respawns, still happen instantly.

#### Installation

Put `SmoothNet.dll` in `BepInEx/plugins/SmoothNet/`. Tested with Valheim 1.0.21 and Jötunn 2.30.2.

**Multiplayer:** install it on the dedicated server **and** every player. The server sends updates to players and each player sends updates for the creatures their game runs, so each side only speeds up what it sends itself. The gliding only needs the player's side. Players without the mod can still join; they just don't get the improvement. The server needs a restart to pick it up.

Only Steam connections are sped up. A server started with `-crossplay` uses a different connection type, and only the larger per-round limit and the gliding apply.

#### Configuration

`BepInEx/config/andrei.valheim.smoothnet.cfg`:

| Setting | Default | Description |
|---|---|---|
| `UpdateBudgetKB` | `64` | Updates allowed per player in each round, in KB (vanilla 10, max 128). |
| `SendRateKB` | `2048` | Steam send rate per connection in KB/s (vanilla 150). Applied when the game starts. If you play over the internet, keep it below your upload speed. |
| `SnapDistance` | `20` | How far off, in meters, a creature you don't control can be before it jumps instead of gliding (vanilla 5). |
| `DebugLogging` | `false` | Every 10 seconds, logs the updates sent and received per second and how much data is waiting to go to each player. |

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
