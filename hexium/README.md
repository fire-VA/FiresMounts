# FiresMounts

**Updated for Valheim 1.0.**

Rideable horses, flying mounts and a leaping, gliding Mini Fader for Valheim, with levels, saddle looks and a
whistle to call your mount from anywhere.

## Horses

- **Wild herds.** Horses spawn in the Plains in herds of 2-4, and rarely as a lone horse in the Meadows.
- **Taming.** Feed a wild horse Carrot, Turnip, Blueberries, Raspberries, Barley or Dandelion and keep it calm,
  like taming a Lox. It takes about 20 real minutes by default.
- **Saddling.** A tamed horse needs a saddle before you can ride: hold a Lox saddle and press E on the horse.
  Alt+E on the saddle takes it off again.
- **Looks.** Every horse rolls its own coat (Brown, Gray, Gray2, Palomino, White), mane and tail and keeps them.

## Riding controls

- **E** on a saddled horse mounts; E again dismounts.
- **W** moves forward, ramping through walk, trot, canter and gallop. **Shift** sweeps up to full gallop.
- **A / D** steer the horse itself, so your camera is free to look around.
- **S** or no input eases back to a stop. **Tap C** toggles a steady walk.
- **Jump** hops obstacles while moving, or rears when standing still. **Attack** is a rear-up strike that never
  harms players or tames.

Small rocks and stumps below belly height are ridden over instead of acting as walls. A dismounted horse stands
where you left it instead of wandering off. Your Ride skill improves handling.

## Flying mounts

- **Mini Moder.** Set a Moder egg (the Moder drops one) in a **Mount nest** and it hatches a tame mini Moder
  after a day. The nest is a build piece: Stone 20, Crystal 5 and Dragon tears 2.
- **Drake.** Mountain drakes sometimes drop a Drake egg (5% by default), which hatches the same way. Wild drakes
  can also be tamed with Freeze glands or Crystal, once Bonemass is defeated.
- **Sky saddle.** Crafted at the Artisan table (Dragon tears 5, Silver 10, Deer hide 10, Linen thread 10) and used
  on a tamed flyer. Unsaddled, E toggles follow / stay.
- A tamed flyer that follows you fights the enemies around you with its own attacks.

**Flight controls (helicopter style).** Shift speeds up and takes off. W noses down to fly forward, S noses up to
slow down. A / D bank and slide, Q / E turn flat. Hold Space to climb; double-tap Space to lock a hover. Hold Ctrl
to descend and land. Double-tap E (or Ctrl) to step off in mid-air: you fall, the mount lands and follows you.
Let go of everything and it settles into a hover.

**Mounted attacks.** Left / middle mouse fire the mount's own main and special attacks at your crosshair
(Alt + mouse wheel picks the special). Right mouse raises a bubble shield on you and the mount that soaks up
about 200 damage, on a 30 second cooldown.

Flying spends the mount's stamina; a Tired flyer sinks and lands. If you die while riding, the mount waits at
your tombstone.

## Mini Fader

The Fader drops a Fader egg. Set it in a Mount nest and after a day it hatches a tame Mini Fader; saddle it with a
Sky saddle.

- On the ground it rides like the horse.
- **Space** jumps; hold it longer for a higher leap, about 14 m up and 20 m ahead at full charge. Hold A or D for
  a side leap.
- Past the top of a leap it glides on the flying keys. Off a cliff, hold Space while falling to glide. Neither
  you nor the Fader takes fall damage while you ride.
- **Wall climbing.** Ride into a cliff, rock face or built wall steeper than about 75 degrees to climb it.
  W / S go up and down, A / D along it, Space jumps off.
- It has the Fader's own bite, flame breath and specials (claws, spin, fissure, wall of fire, meteors), and the
  bubble shield.

## Levels, stamina and looks

- Every mount levels from 1 to 20 and keeps its level. XP comes from riding, flying, kills and feeding.
- Each level adds stamina, stamina regen, speed, health and attack damage. A flyer's height limit rises from
  40 m at level 1 to 100 m at level 20. Level 10 unlocks a boost in the air; level 20 earns a gold name.
- At 0 stamina a mount is Tired: no climbing or boost, flyers land, the Fader can't leap. It recovers on the
  ground.
- **Saddle dyes.** Craft a Saddle dye at the workbench and use it on a saddled mount. Each mount has three looks.
- **Essences.** Use an essence (brewed at the Cauldron) on a tamed mount or an egg in a nest to give it that
  essence's colours, with matching attack glow. An Essence of Clarity returns it to its own colours.

## Mount whistle

Craft a Mount whistle at the workbench (Wood 2, Leather scraps 2) and carry it. Double-tap H and the last mount
you rode appears a few metres behind you, from anywhere in the world. 30 seconds between calls; a mount someone
else is riding can't come.

## Requires

- BepInExPack for Valheim
- FiresUnifiedCore 0.2.389 or newer

## Installation

Install with a mod manager, or copy the plugin folder into `BepInEx/plugins`. Install it on every client and on
the dedicated server.

## Configuration

Settings live in `com.Fire.FiresMounts.cfg` in `BepInEx/config` and can be edited in the Fires config window (F1).
Most apply live.

- `[Mounts.General] Enabled` - master switch.
- `[Mounts.Spawns]` - natural horse spawns: `Enabled`, Plains chance, interval, herd size and cap, Meadows chance
  and cap. Off means horses only come from the admin command.
- `[Mounts.Taming]` - `TameTimeMinutes` (20), `TamingFood`, `MaxStamina` (120).
- `[Mounts.Movement]` - `WalkSpeed` (3), `RunSpeed` (10), `TurnSpeed` (120), `GaitRampRate`, `ShiftSprint`,
  `EnableRiderJump`, `AutoHitchOnDismount`, `BellyClearance`. `UseVanillaSteering` (off) switches back to
  vanilla look-direction steering.
- `[Mounts.Body]` - `HorseScale` (1.4), pin a `Coat`, `Mane` or `Tail` instead of Random, `CoatColor` tint, seat
  position.
- `[Mounts.Flyers]` - flyers on/off, sizes, hatch time, drake egg chance and taming, `FightWhenFollowing`,
  `MountAttacks`, shield length and cooldown, `AttackDamage`.
- `[Mounts.Fader]` - size, leap height and distance, glide, `WallClimb`.
- `[Mounts.Levels]` and `[Mounts.Whistle]` - levelling on/off and XP rate; whistle key, cooldown and distance.
- `[Mounts.Audio]` - hoof sound volume and effects.

## Multiplayer

Gameplay values are set by the server and pushed to every player: spawns, taming time and food, stamina, speeds,
rider jump and auto-hitch. Looks, audio and seat positions stay local to each player.

## Admin commands

Cheat commands (need devcommands / admin):

| command | what it does |
|---|---|
| `firesmounts_spawn [distance]` | spawn a horse in front of you |
| `firesmounts_tame [name]` | instantly tame the nearest horse (does not saddle it) |
| `firesmounts_saddle` | drop a Lox saddle at your feet |
| `firesmounts_spawnflyer [moder\|drake\|fader] [distance]` | spawn a creature mount in front of you |
| `firesmounts_hatch` | finish the nearest Mount nest's incubation now |
| `firesmounts_level <1-20>` / `firesmounts_xp <n>` | set the nearest mount's level, or give it XP |

Other commands: `firesmounts_whistle` (whistle as the double tap does), `firesmounts_mountinfo` (the nearest
mount's level, XP and stamina) and `firesmounts_diag`.

## Notes

- `[Mounts.Flyers] GroundMounts` (on) also lets Eikthyr, Bjorn and the Fuling brute be ridden as ground mounts,
  but for now they can only be tamed with `firesmounts_tame`.
- The in-game Fires help panel ("? Help" on the pause menu) has a FiresMounts section with every control.

## Licence

MIT License, Copyright (c) 2025 fire-VA. See the LICENSE file.

## Issues / questions

https://discord.gg/zQRgHbqms4
