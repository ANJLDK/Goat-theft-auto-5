# Goat Theft Auto 5

A ScriptHookVDotNet mod that turns the player into a Goat Simulator goat, with the stats and
special abilities taken from the game's own UnrealScript. **Single player only.**
Never load it in GTA Online.

> Status: written without being able to run GTA V. It compiles against a stand-in for the
> ScriptHookVDotNet API, but has not been tested in game. Expect some tuning. Send back
> `ScriptHookVDotNet.log` if anything fails.

## What is in the zip

| Folder | What |
|---|---|
| `Scripts/` | the core mod: `GoatTheftAuto.cs` (all code and the goat stat tables) and `GoatTheftAuto.ini` |
| `Optional/` | add-ons, each a drop-in: `GoatStyle` (score, mutators, achievements, photo mode), `GoatDriver` (PAYDAY-style driving), `ForeverChillGoat` (radio plays everywhere) |
| `Blender/import_goats.py` | batch-imports the exported goats, scales to metres, applies textures |
| `exports/` | the goat models, animations and textures pulled from your install |
| `Docs/` | this file |

## Install (script only, works right away)

1. Install **Script Hook V** and **ScriptHookVDotNet v3** (needs .NET Framework 4.8).
2. Copy `GoatTheftAuto.cs` and `GoatTheftAuto.ini` from `Scripts/` into GTA V's `scripts` folder.
   (SHVDN compiles the `.cs` file itself.) The goat stat tables are at the bottom of that file.
3. Start the game in story mode, press **F10**.

Without custom models the mod uses `FallbackModel` (a deer) so you can test every ability.

### Optional add-ons

Copy the files from the add-on's folder in `Optional/` into the same `scripts` folder. Delete them to remove the add-on.

* **GoatStyle**: trick score, HUD, mutators, bonus screen, achievements, photo mode, slow motion on F.
* **GoatDriver**: PAYDAY-style driving (R to get in and out, Shift boost, left mouse jump). Without it you get into cars the normal GTA way.
* **ForeverChillGoat**: just the file `ForeverChillGoat.ini`; the Chilled Goat radio then plays everywhere, not only in vehicles.

## Controls (the original Goat Simulator PC layout, change them in the .ini)

| Key | Action |
|---|---|
| W A S D / Space / Shift | move / jump / sprint |
| Left mouse | headbutt |
| Right mouse (hold, in the air) | air trick: spin |
| E | lick: grabs what is in front of you (cars, people, props); press again to drop/throw |
| Q | toggle ragdoll |
| F (hold) | slow motion |
| R | goat special (see below). Next to a vehicle, R gets you in and out instead |
| 1 | baa (nearby pedestrians run; put a `baa.wav` in `scripts/GoatTheftAuto/` for sound) |
| Tab / `[` | next / previous goat |
| `.` / `,` | next / previous accessory |
| G | accessory action. On a PAYDAY mask: toggle the mask |
| J / H | extra kick key / extra headbutt key (not in the original) |
| F10 | goat mode on/off |
| F9 | Chilled Goat radio on/off |
| M / N | goat-mode soundtrack on/off / next song (off by default, see Music) |
| F11 / F8 / F7 | mutators / achievements / photo mode (GoatStyle) |

In a vehicle (GoatDriver, Goat Simulator: PAYDAY): R get in / out, W accelerate, S brake / reverse, Space handbrake,
Shift boost, left mouse jump.

While you are a goat, GTA's own use of left/right mouse, E, Q, R, Tab and F is switched off. Set `GoatSimControls=false`
in `GoatTheftAuto.ini` to keep GTA's controls.

Jump height uses the goat's own JumpZ and gravity.

## Driving like Goat Simulator: PAYDAY (GoatDriver)

An optional add-on (`Optional/GoatDriver`: `GoatDriver.cs` + `GoatDriver.ini`, copy both into `scripts`). While your ped
model is a goat, pressing **R** within 4 m of a car warps you straight into the driver
seat (the current driver is thrown out), skips the door animation, stops the human driving clips,
rolls the driver window down, and attaches the goat to the car's `steeringwheel` bone, pitched
forward. A spring simulation fed by the car's real acceleration swings the upper body, so the goat
leans out of the window as you speed up, flings sideways in turns, and gets kicked on crashes.
**R** again warps you out and rolls the window back up.

PAYDAY extras: hold **Shift** to boost, left mouse button jumps the car.

Setting it up: the wheel bone's axes differ per car, so tune once with `TuneMode=true`. NumPad
4/6/8/2/9/3 move, Shift+those rotate, NumPad5 resets, NumPad0 writes the values to
`GoatDriver.log` to paste into `[Offset]`. If the goat leans the wrong way, flip `RollSign`.
Real ragdoll is not possible while attached, so `CrashRagdoll=true` briefly detaches the goat
on big hits. Cars only: no bikes, boats, helicopters, planes or trains.

Untested in game. The least certain parts are marked `RISK` in the code (attaching a seated ped,
and suppressing the native driving pose). If it misbehaves, send `ScriptHookVDotNet.log`.

## GoatStyle: score, mutators, achievements, photo mode

An optional add-on (`Optional/GoatStyle`: `GoatStyle.cs` + `GoatStyle.ini`). Everything is saved to `GoatStyle.save` next to the script.

* **Trick score ("Wreckage").** While you are a goat: air time, backflips / frontflips / barrel rolls / 360 spins, big drops,
  landing on car roofs (more if the car is moving), headbutts and kicks on pedestrians (cops pay extra, K.O.s pay extra),
  smashing windows (a ped hit through a window pays most), wrecking cars, crashing your own car. Tricks feed a combo:
  a multiplier from x1 to x10 that banks when you stop for 4 seconds. Floating popups show each trick.
* **HUD.** Score at the top, combo multiplier, pending points and a timer bar.
* **Mutators (F11).** Up/Down, Enter. Low gravity, slow motion, Touch of Chaos (everyone near you ragdolls), Bouncy World
  (things rebound when they land), Mega Jump, Drunk Goat, Double Points. Giant head is not possible from a script.
* **Bonus screen.** When you die or get arrested, a tally of the life: wreckage, best combo, longest air time, peds, cars,
  windows, flips, plus "the stupidest thing you did" and a rank.
* **Achievements (F8).** 15 of them, with progress bars and an unlock banner.
* **Photo mode (F7).** Free camera (WASD, mouse, Q/E, Shift, Z/X zoom), Left/Right filters, Up/Down hats on the head, Space
  for the shutter flash (take the actual screenshot with Steam or Windows), H hides the hints.

Untested in game. Windows broken, cars wrecked and peds hit are detected by watching health and window state near the goat, so
expect some false positives and misses. Send `ScriptHookVDotNet.log` and `GoatStyle.log` if anything fails.

## Radio: "Chilled Goat"

A one-song radio station that loops *Goat Chill* from your own copy of the soundtrack
(`[Music] Folder=` in the ini). It works whether or not you are in goat mode.

* **Default station:** every time you get into a vehicle the dial goes to Chilled Goat.
* **Only in vehicles (default):** it plays while you drive and pauses when you get out, like a station you leave.
* **Always on (optional add-on "Forever Chill Goat"):** drop `Optional/ForeverChillGoat/ForeverChillGoat.ini` into `scripts` (or set `Mode=Always` in `[Radio]`) and it plays everywhere, on foot too.
* **Leave it:** turn the real radio wheel to another station, or press **F9** to switch Chilled Goat off (F9 again turns it back on).

GTA's radio wheel itself cannot be extended by a script, so Chilled Goat is not an extra
slot on the wheel; it is a station the script runs and shows as a subtitle. A real wheel slot
means building an audio pack and radio metadata in OpenIV/CodeWalker, which I cannot do or test from here.

## Music

While you are a goat the mod plays the songs in the folder set under `[Music] Folder=` in the ini
(mp3, wav, wma), one after another. **M** turns it on/off, **N** skips. It uses Windows' built-in
audio, so nothing else needs installing. The soundtrack is not included: point the ini at your
own copy of the Goat Simulator soundtrack.

## Goats and what they do

Numbers come from the original scripts (Unreal units converted at 1 uu = 1 cm).

| Goat | Original behavior carried over |
|---|---|
| Goat | walk 5 m/s, sprint 12, jump 650, mass 45, headbutt/kick/lick |
| Angel (Gloria) | gravity x0.6, jump +50. **R in the air** stops your fall (0.23 s cooldown) |
| Devil | speed x1.5. **R** toggles a pull field (radius 5.1 m) |
| Feather (+angel/devil) | sprint 16, gravity 0.7, jump 900 |
| Tall (+angel/devil) | walk 2, sprint 14, lick range 1.7 m |
| Classy | walk 1, sprint 20, horn/kick damage 300 |
| G2 | horn/kick 300 |
| Space (HeadBobber) | lick range 1.7 m |
| Goast | walk 3, sprint 8, lick range 1.0 m |
| Sperm | stays ragdolled. **R** toggles rocket thrust toward the camera |
| Shopping | spawns a vehicle and rides it (uses `caddy` unless you add a cart model) |
| **Flamingoat** | walk 1.7, sprint 8, jump 900. **Jump in the air** flaps (7 m/s up, 0.5 s cooldown), **hold Jump** to glide in the camera direction. **Lick a car, then fly off with it.** Lick a person and press **R** to take them over; **R** again to release |
| Camel | jump 1400. Lick water to fill the hump, hold **R** to spray the water jet (puts out fires, shoves people) |
| Dolphwheel | pushing into a wall walks you up it |
| Handsome | jump 700, mass 60, horn range 1.1 m |
| Panzer | no ability script was found in your install; base stats |

Goat Simulator's ragdoll rules are kept: you ragdoll if you hit something above the goat's
collision speed, or land faster than its landing speed.

## Accessories

Jet Pack, Grappling Hook, Tornado, Repulsive, Demonic Horns work like the originals (toggle or
fire with **G**). The grappling hook is a real rope swing: you pendulum under the anchor, WASD steers in the air, hold Jump to reel in, hold Sprint to let rope out, **G** to let go. Blue Streak, Rocket Boots, Hang Glider and Roller Skates are approximations
because their code was not among the scripts I read. Hats (crown, wizard, sombrero, glasses...)
are cosmetic props and show up once you add the matching prop models (`gs_crown`, `gs_wizrdhat`, ...).

## Making the models work (the manual part)

The exported models are Unreal skeletons. GTA V peds need the GTA ped skeleton, so this part
is hand work in Blender:

1. In Blender install the PSK/PSA importer add-on and the Sollumz add-on.
2. Run `Blender/import_goats.py` (command at the top of the file). You get a `.blend` and `.fbx`
   per goat, in metres, textured.
3. Rig each goat to a GTA ped skeleton with Sollumz (or weight it to a stock quadruped ped).
4. Export with Sollumz to `.ydd` / `.yft`, build an add-on ped pack, and give the ped the spawn
   names used here: `gs_goat`, `gs_gloria`, `gs_devil`, `gs_feather`, `gs_tall`, `gs_classy`, `gs_g2`,
   `gs_space`, `gs_goast`, `gs_sperm`, `gs_flamingoat`, `gs_camel`, `gs_dolphwheel`, `gs_handsome`,
   `gs_panzer`. Different names are fine: set `Model=` in the ini.
5. The game's own `.psa` animations only help as reference. GTA plays GTA animation
   dictionaries, so movement uses stock ped clips unless you author `.ycd` files.

Not covered: sounds, and the Goat Simulator particle effects (the mod uses GTA's own).

## Legal

The goat models, textures and animations belong to Coffee Stain Studios. Using them privately
for your own offline game is your call. Do not upload or sell the extracted assets or any mod
that contains them. The scripts in `Scripts/` are yours to share.
