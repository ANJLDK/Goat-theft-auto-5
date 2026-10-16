<h1 align="center">🐐 GOAT THEFT AUTO V 🚗</h1>

<p align="center"><b>Los Santos has a goat problem. The goat is you.</b></p>

<p align="center">
  <img alt="single player only" src="https://img.shields.io/badge/mode-single%20player%20ONLY-red">
  <img alt="tested" src="https://img.shields.io/badge/tested%20in%20game-ask%20again%20later-yellow">
  <img alt="bleats" src="https://img.shields.io/badge/baa-yes-brightgreen">
  <img alt="dignity" src="https://img.shields.io/badge/dignity-not%20included-lightgrey">
</p>

---

## What is this?

A [ScriptHookVDotNet](https://github.com/scripthookvdotnet/scripthookvdotnet) mod that replaces
Michael, Franklin, or Trevor with a goat from *Goat Simulator*, complete with that goat's
original stats and special power, and then hands you the keys to a city full of
pedestrians who did nothing to deserve this.

Trevor was already like this. Now it's official.

## Features

- **16 goats.** Plain Goat, Angel, Devil, Feather, Tall, Classy, G2, Space, Goast, Sperm,
  Shopping, Flamingoat, Camel, Dolphwheel, Handsome, and Panzer. Yes, there is a Sperm Goat.
  No, we will not be taking questions.
- **Real numbers.** Walk speed, jump height, gravity, headbutt damage, and the speed at which
  you ragdoll into a mailbox are all lifted from the game's own scripts. Science.
- **The Flamingoat.** Flap, glide, lick a car, fly away with the car. The sentence
  "lick a car and fly away with it" is a *feature request that is now closed*.
- **Lick anything.** Cars, cops, props, the concept of personal space. Press the lick key
  again to throw it.
- **Accessories.** Jet pack, grappling hook, tornado, repulsive field, demonic horns, hats.
  A wizard hat. On a goat. In 2026.
- **Mind control.** Lick a pedestrian as the Flamingoat, press the special key, and you are now
  that pedestrian. Legally distinct from the Flamingoat. Morally, no comment.
- **Ragdoll physics that are simply too honest.** Hit something too fast and you fall over,
  exactly like the original. Land from too high and you fall over, exactly like your
  sleep schedule.

## Controls

| Key | What happens |
|---|---|
| `F10` | Become goat / stop being goat |
| `[` `]` | Previous / next goat |
| `,` `.` | Previous / next accessory |
| `H` | Headbutt. The oldest form of diplomacy. |
| `J` | Kick. For when the headbutt was too diplomatic. |
| `L` | Lick. Press again to throw whatever you licked. |
| `K` | Goat special (fly, hover, spray water, summon vortex of regret...) |
| `G` | Accessory action |
| `B` | Baa. Nearby pedestrians flee. Correct response. |
| `Space` | Jump. Height depends on how much of a goat you are. |

## Install

1. Install **Script Hook V** and **ScriptHookVDotNet v3**.
2. Drop `GoatTheftAuto.cs` and `GoatTheftAuto.ini` into your GTA V `scripts` folder.
3. Start **story mode**. Press `F10`.
4. Become the goat.

No goat models? You will be a **deer** until you add some. This is fine. Deer are goats with
better PR. Every ability still works.

## Custom goat models

The code expects add-on peds named like `gs_goat`, `gs_flamingoat`, `gs_camel` and so on
(change them in the `.ini`). Getting a goat into GTA V means rigging it to the GTA ped
skeleton with Blender and Sollumz, and then installing the pack with OpenIV. It is about as
fun as it sounds. See [`Blender/import_goats.py`](Blender/import_goats.py) for the batch
import and scaling helper.

> **Important:** this repo does **not** contain Goat Simulator's models, textures or animations.
> Those belong to Coffee Stain Studios. Extract them from your own copy of the game, for
> your own use, and please do not upload them. We would like to keep our legal department
> (a goat) fed and happy.

## Configuration

Everything is in `GoatTheftAuto.ini`. Remap keys, change damage and knock-back scale, or
override any goat's stats:

```ini
[Goat.camel]
JumpZ=99999     ; the camel now leaves the atmosphere
```

(We do not recommend `JumpZ=99999`. We also cannot stop you.)

## Honest status

| Thing | State |
|---|---|
| Code compiles | Against a stand-in for the ScriptHookVDotNet API, yes |
| Tested in actual GTA V | **Not yet.** Please file bugs, not lawsuits. |
| Goat physics identical to Goat Simulator | No. GTA has its own physics engine and a different idea of fun. |
| Some accessories are approximations | Yes (blue streak, rocket boots, hang glider, roller skates) |
| Goats animate like goats | They animate like whatever GTA's ped animations make them do |

## FAQ

**Is this safe for GTA Online?**
**NO.** Single player only. Taking a goat into Online is how you meet a ban, and Online
already has enough people who behave like goats.

**Why a goat?**
Why not?

**Why the Sperm Goat?**
Because the original game shipped it and we are a faithful port.

**Can I lick the police?**
You can lick the police.

**Will the police mind?**
Please see the previous answer.

**My goat fell over.**
That's the physics working. See "Ragdoll physics that are simply too honest."

## Contributing

Pull requests welcome, especially for: Flamingoat flight tuning, better headbutt animations,
a cart model for the Shopping Goat, and a `baa.wav` that doesn't make your neighbours
call someone.

## Credits and legal

- *Goat Simulator* is by Coffee Stain Studios. This is a fan project and is not affiliated
  with, endorsed by, or approved by them, Rockstar Games, or any actual goat.
- Built with Script Hook V and ScriptHookVDotNet.
- No pedestrians were harmed. Several were licked.

<p align="center"><i>Baa.</i></p>


---

## Extra chaos (added later)

- **PAYDAY-style driving** (`Scripts/GoatDriver.cs`): the goat is warped into the driver seat, clamped to the steering wheel,
  and its upper body flops out of the window with spring physics.
- **Goat Sim score layer** (`Scripts/GoatStyle.cs`): trick scoring and combos, mutators menu (F11), WASTED/BUSTED bonus
  screen, 15 achievements (F8) and a photo mode with hats and filters (F7).
- **Chilled Goat radio**: a one-song radio station that becomes your default when you get in a car.
- **Rope-swing grappling hook** with real pendulum physics.

Full controls, install steps and the goat table are in [`Docs/README.md`](Docs/README.md).

## Assets are NOT in this repo

The goat models, textures and music belong to Coffee Stain Studios. Extract them from your own copy of the game
(see `Blender/import_goats.py` and the docs) and put them in `exports/`, which is git-ignored.
