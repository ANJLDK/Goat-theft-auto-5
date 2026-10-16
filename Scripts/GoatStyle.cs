// GoatStyle.cs - the "Goat Simulator" layer for GTA V (single player): trick score + combos,
// Goat-Sim-style HUD and floating popups, mutators menu, WASTED/BUSTED bonus screen,
// achievements and a photo mode with hats and filters.
// ScriptHookVDotNet v3. Drop GoatStyle.cs and GoatStyle.ini into the "scripts" folder.
//
// KEYS (change them in GoatStyle.ini)
//   F11  mutators menu      (Up/Down to move, Enter to toggle, F11 or Backspace to close)
//   F8   achievements list
//   F7   photo mode         (WASD move, mouse look, Q/E down/up, Shift fast, Z/X zoom,
//                            Left/Right filter, Up/Down hat, Space shutter, H hide hints, F7/Backspace exit)
//
// HOW SCORING WORKS
//   Every trick adds points to a pending combo. Each new trick inside the combo window raises the
//   multiplier (x1 .. x10). When the window runs out the pending points x multiplier are banked
//   into your "Wreckage" score. Dying or being arrested banks the combo and shows the bonus screen.
//
// NOTES
//   * Not tested in game when written. Things that depend on GTA internals are marked RISK.
//   * Tricks only count while your ped model is a goat (OnlyAsGoat=true). Menu, photo mode and
//     achievements work for any ped.
//   * Mutators and the main mod both push the ped around; when both fight (e.g. low gravity plus
//     the angel goat's own gravity) the effects stack. That is on purpose.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using TextElement = GTA.UI.TextElement;
using UIFont = GTA.UI.Font;
using UIAlign = GTA.UI.Alignment;

namespace GoatStyle
{
    public class GoatStyleScript : Script
    {
        // ================================================================== settings
        bool enabled = true;
        bool onlyAsGoat = true;
        bool showHud = true;
        float comboSeconds = 4.0f;
        int comboMax = 10;
        float bonusSeconds = 9.0f;
        string[] goatModels = { "gs_goat", "gs_gloria", "gs_devil", "gs_feather", "gs_tall", "gs_classy",
            "gs_g2", "gs_space", "gs_goast", "gs_sperm", "gs_flamingoat", "gs_camel", "gs_dolphwheel",
            "gs_handsome", "gs_panzer", "a_c_deer" };

        Keys keyMenu = Keys.F11, keyAch = Keys.F8, keyPhoto = Keys.F7;
        Keys keyHead = Keys.H, keyKick = Keys.J, keyLick = Keys.L;

        // photo mode
        bool photoFreeze = true;
        float photoSpeed = 6.0f, photoSens = 4.0f;
        string[] hats = { "prop_roadcone02a", "prop_tool_hardhat", "prop_ld_hat_01", "prop_beachball_02" };
        string[] filters = { "NONE", "CAMERA_BW", "NG_filmic02", "NG_filmic13", "Bank_HLWD", "REDMIST_blend", "scanline_cam_cheap", "drug_flying_base" };
        // Hat attachment, relative to the head bone (SKEL_Head). Tune it like the GoatDriver offsets.
        Vector3 hatPos = new Vector3(0.0f, 0.05f, 0.12f);
        Vector3 hatRot = new Vector3(0f, 0f, 0f);

        // ================================================================== state
        readonly Stopwatch sw = Stopwatch.StartNew();
        readonly HashSet<int> goatHashes = new HashSet<int>();
        readonly HashSet<Keys> held = new HashSet<Keys>();
        readonly Random rng = new Random();
        readonly string savePath, logPath;
        bool active;
        long rt;                                  // real time ms (not affected by slow motion)

        // keys seen from the key event thread (real-time stamps)
        long tHead, tKick, tLick, lastActionAt;

        // ---- tricks / combo
        int comboCount, comboPending, comboMult = 1, comboUntil;
        long score;                               // banked this session
        bool airing; long airCandidate; int airStart; float startZ, maxZ;
        Vector3 lastRot; float accPitch, accRoll, accYaw; int donePitch, doneRoll, doneYaw;
        bool wasInVehicle;
        long nextScan;
        readonly Dictionary<int, int> pedHealth = new Dictionary<int, int>();
        readonly Dictionary<int, float> carBody = new Dictionary<int, float>();
        readonly Dictionary<int, int> carWindows = new Dictionary<int, int>();
        readonly Dictionary<int, long> pedHitAt = new Dictionary<int, long>();
        readonly HashSet<int> wrecked = new HashSet<int>();
        float ownBody = -1f; long nextCrashAt;
        readonly List<Ped> nearPeds = new List<Ped>();
        readonly List<Vehicle> nearVehicles = new List<Vehicle>();
        readonly Dictionary<int, float> lastVz = new Dictionary<int, float>();

        // ---- popups / toasts
        class Popup { public string Text; public Vector3 Pos; public long Born; public Color Col; public float Dx; }
        readonly List<Popup> popups = new List<Popup>();
        string toastTitle, toastText; long toastUntil;

        // ---- life (bonus screen) / stats / achievements
        class Life
        {
            public long Points;
            public int BestMult = 1;
            public long AirMs;
            public readonly Dictionary<string, long> C = new Dictionary<string, long>();
            public string StupidName = ""; public int StupidW = -1, StupidPts;
            public long Get(string k) { long v; return C.TryGetValue(k, out v) ? v : 0; }
        }
        Life life = new Life();
        readonly Dictionary<string, long> stats = new Dictionary<string, long>();
        readonly HashSet<string> unlocked = new HashSet<string>();
        bool saveDirty; long nextSave, nextAchCheck;

        class Ach { public string Id, Name, Desc, Stat; public long Target; }
        readonly List<Ach> achs = new List<Ach>();
        bool achOpen;

        // ---- bonus screen
        bool bonusShowing, bonusArmed = true; long bonusStart; string bonusTitle = ""; Life bonusLife;

        // ---- mutators
        static readonly string[] MutNames = { "Low Gravity", "Slow Motion", "Touch of Chaos (everything ragdolls)", "Bouncy World", "Mega Jump", "Drunk Goat", "Double Points" };
        const int MLow = 0, MSlow = 1, MRag = 2, MBounce = 3, MJump = 4, MDrunk = 5, MDouble = 6;
        readonly bool[] mut = new bool[7];
        bool menuOpen; int menuIndex;
        float appliedScale = 1f; string appliedTimecycle = "";

        // ---- photo mode
        bool photoOn, photoHints = true;
        int cam = -1; Vector3 camPos, camRot; float camFov = 50f;
        int filterIndex, hatIndex = -1; Prop hatProp; long lastFrameRt, flashUntil;

        // ================================================================== setup
        public GoatStyleScript()
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            savePath = Path.Combine(dir, "GoatStyle.save");
            logPath = Path.Combine(dir, "GoatStyle.log");
            LoadSettings(Path.Combine(dir, "GoatStyle.ini"));
            foreach (string m in goatModels) { string n = m.Trim(); if (n.Length > 0) goatHashes.Add(new Model(n).Hash); }
            DefineAchievements();
            LoadSave();

            Tick += OnTick;
            KeyDown += OnKeyDown;
            KeyUp += delegate (object s, KeyEventArgs e) { held.Remove(e.KeyCode); };
            Aborted += delegate (object s, EventArgs e) { Shutdown(); };
            Interval = 0;
        }

        void LoadSettings(string path)
        {
            ScriptSettings s = ScriptSettings.Load(path);
            enabled = s.GetValue("General", "Enabled", enabled);
            onlyAsGoat = s.GetValue("General", "OnlyAsGoat", onlyAsGoat);
            showHud = s.GetValue("General", "ShowHud", showHud);
            comboSeconds = s.GetValue("General", "ComboSeconds", comboSeconds);
            comboMax = s.GetValue("General", "ComboMax", comboMax);
            bonusSeconds = s.GetValue("General", "BonusScreenSeconds", bonusSeconds);
            goatModels = s.GetValue("General", "GoatModels", string.Join(",", goatModels)).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            keyMenu = s.GetValue("Keys", "Mutators", keyMenu);
            keyAch = s.GetValue("Keys", "Achievements", keyAch);
            keyPhoto = s.GetValue("Keys", "PhotoMode", keyPhoto);
            keyHead = s.GetValue("Keys", "Headbutt", keyHead);
            keyKick = s.GetValue("Keys", "Kick", keyKick);
            keyLick = s.GetValue("Keys", "Lick", keyLick);

            photoFreeze = s.GetValue("Photo", "FreezeTime", photoFreeze);
            photoSpeed = s.GetValue("Photo", "CameraSpeed", photoSpeed);
            photoSens = s.GetValue("Photo", "MouseSensitivity", photoSens);
            hats = s.GetValue("Photo", "Hats", string.Join(",", hats)).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            filters = s.GetValue("Photo", "Filters", string.Join(",", filters)).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            hatPos = new Vector3(s.GetValue("Photo", "HatX", hatPos.X), s.GetValue("Photo", "HatY", hatPos.Y), s.GetValue("Photo", "HatZ", hatPos.Z));
            hatRot = new Vector3(s.GetValue("Photo", "HatRotX", hatRot.X), s.GetValue("Photo", "HatRotY", hatRot.Y), s.GetValue("Photo", "HatRotZ", hatRot.Z));
        }

        void DefineAchievements()
        {
            A("headbutt100", "Headache", "Headbutt 100 pedestrians", "headbutts", 100);
            A("kick50", "Soccer Goat", "Kick 50 pedestrians", "kicks", 50);
            A("peds500", "Menace", "Hit 500 pedestrians", "peds", 500);
            A("flips25", "Goat Gymnast", "Land 25 flips", "flips", 25);
            A("air10", "Hang Time", "Stay airborne for 10 seconds in one go", "airMax", 10000);
            A("carair5", "Joyride", "Get 5 seconds of air in a vehicle", "carAirMax", 5000);
            A("roof25", "Roof Rat", "Land on 25 car roofs", "roofs", 25);
            A("windows20", "Window Shopper", "Smash 20 windows", "windows", 20);
            A("wrecks50", "Wreck-It Goat", "Wreck 50 vehicles", "wrecks", 50);
            A("cops25", "Police Brutality", "Hit 25 cops", "cops", 25);
            A("score100k", "Six Figure Goat", "Bank 100,000 wreckage points", "score", 100000);
            A("combo10", "Combo Breaker", "Reach a x10 combo", "bestMult", 10);
            A("mutant", "Mutant", "Switch on every mutator at least once", "mutCount", 7);
            A("photo10", "Paparazzi", "Take 10 photos", "photos", 10);
            A("wasted5", "Bonus Round Regular", "See the bonus screen 5 times", "bonus", 5);
        }
        void A(string id, string name, string desc, string stat, long target)
        {
            achs.Add(new Ach { Id = id, Name = name, Desc = desc, Stat = stat, Target = target });
        }

        // ================================================================== persistence
        void LoadSave()
        {
            try
            {
                if (!File.Exists(savePath)) return;
                foreach (string line in File.ReadAllLines(savePath))
                {
                    int i = line.IndexOf('='); if (i <= 0) continue;
                    string k = line.Substring(0, i), v = line.Substring(i + 1);
                    long n; if (!long.TryParse(v, out n)) continue;
                    if (k.StartsWith("ach.")) { if (n != 0) unlocked.Add(k.Substring(4)); }
                    else stats[k] = n;
                }
                score = Stat("score");
            }
            catch (Exception ex) { Log("load: " + ex.Message); }
        }
        void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, long> kv in stats) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
                foreach (string id in unlocked) sb.Append("ach.").Append(id).Append("=1\n");
                File.WriteAllText(savePath, sb.ToString());
                saveDirty = false;
            }
            catch (Exception ex) { Log("save: " + ex.Message); }
        }
        void Log(string t) { try { File.AppendAllText(logPath, DateTime.Now + " " + t + "\n"); } catch { } }

        long Stat(string k) { long v; return stats.TryGetValue(k, out v) ? v : 0; }
        void Count(string k, long n)
        {
            stats[k] = Stat(k) + n; saveDirty = true;
            life.C[k] = life.Get(k) + n;
        }
        void StatMax(string k, long v) { if (v > Stat(k)) { stats[k] = v; saveDirty = true; } }

        // ================================================================== input
        void OnKeyDown(object sender, KeyEventArgs e)
        {
            held.Add(e.KeyCode);
            long now = sw.ElapsedMilliseconds;
            if (e.KeyCode == keyHead) { tHead = now; lastActionAt = now; }
            if (e.KeyCode == keyKick) { tKick = now; lastActionAt = now; }
            if (e.KeyCode == keyLick) { tLick = now; lastActionAt = now; }
            if (!enabled) return;

            if (e.KeyCode == keyAch && !photoOn) { achOpen = !achOpen; return; }
            if (e.KeyCode == keyMenu && !photoOn) { menuOpen = !menuOpen; return; }
            if (e.KeyCode == keyPhoto) { if (photoOn) ExitPhoto(); else EnterPhoto(); return; }

            if (menuOpen)
            {
                if (e.KeyCode == Keys.Back) menuOpen = false;
                else if (e.KeyCode == Keys.Up) menuIndex = (menuIndex + mut.Length - 1) % mut.Length;
                else if (e.KeyCode == Keys.Down) menuIndex = (menuIndex + 1) % mut.Length;
                else if (e.KeyCode == Keys.Enter) ToggleMutator(menuIndex);
            }
            else if (photoOn)
            {
                if (e.KeyCode == Keys.Back) ExitPhoto();
                else if (e.KeyCode == Keys.Right) filterIndex = (filterIndex + 1) % filters.Length;
                else if (e.KeyCode == Keys.Left) filterIndex = (filterIndex + filters.Length - 1) % filters.Length;
                else if (e.KeyCode == Keys.Up) CycleHat(1);
                else if (e.KeyCode == Keys.Down) CycleHat(-1);
                else if (e.KeyCode == Keys.H) photoHints = !photoHints;
                else if (e.KeyCode == Keys.Space) { flashUntil = now + 180; Count("photos", 1); Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Camera_Shoot", "Phone_SoundSet_Michael"); }
            }
        }

        // ================================================================== main loop
        void OnTick(object sender, EventArgs e)
        {
            if (!enabled) return;
            rt = sw.ElapsedMilliseconds;
            Ped ped = Game.Player.Character;
            if (ped == null || !ped.Exists()) return;
            active = !onlyAsGoat || goatHashes.Contains(ped.Model.Hash);

            if (photoOn) { TickPhoto(); return; }

            if (menuOpen) Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);

            TickBonusDetect(ped);
            if (active && !bonusShowing) TickTricks(ped);
            TickMutators(ped);
            TickCombo();
            ApplyWorldState();

            DrawAll(ped);

            if (rt > nextAchCheck) { nextAchCheck = rt + 1000; CheckAchievements(); }
            if (saveDirty && rt > nextSave) { nextSave = rt + 20000; Save(); }
        }

        // ================================================================== tricks
        void TickTricks(Ped ped)
        {
            int now = Game.GameTime;
            bool inVeh = ped.IsInVehicle();
            Vehicle veh = inVeh ? ped.CurrentVehicle : null;
            if (inVeh && (veh == null || !veh.Exists())) inVeh = false;
            Entity body = inVeh ? (Entity)veh : (Entity)ped;
            if (inVeh != wasInVehicle) { airing = false; airCandidate = 0; wasInVehicle = inVeh; }

            bool air = inVeh ? veh.IsInAir : (ped.IsInAir && !ped.IsInWater);
            Vector3 pos = body.Position;
            Vector3 rot = body.Rotation;

            if (air)
            {
                if (airCandidate == 0) airCandidate = rt;
                if (!airing && rt - airCandidate >= 250)
                {
                    airing = true; airStart = now - 250; startZ = pos.Z; maxZ = pos.Z;
                    accPitch = accRoll = accYaw = 0f; donePitch = doneRoll = doneYaw = 0; lastRot = rot;
                }
                if (airing)
                {
                    if (pos.Z > maxZ) maxZ = pos.Z;
                    accPitch += Wrap(rot.X - lastRot.X);
                    accRoll += Wrap(rot.Y - lastRot.Y);
                    accYaw += Wrap(rot.Z - lastRot.Z);
                    lastRot = rot;
                    while (Math.Abs(accPitch) >= 360f * (donePitch + 1))
                    {
                        donePitch++; Count("flips", 1);
                        AddTrick(accPitch > 0 ? "Backflip" : "Frontflip", 200 + 100 * donePitch, 3);
                    }
                    while (Math.Abs(accRoll) >= 360f * (doneRoll + 1))
                    {
                        doneRoll++; Count("flips", 1);
                        AddTrick("Barrel Roll", 200 + 100 * doneRoll, 3);
                    }
                    while (Math.Abs(accYaw) >= 360f * (doneYaw + 1))
                    {
                        doneYaw++;
                        AddTrick("360 Spin", 150 + 75 * doneYaw, 2);
                    }
                }
            }
            else
            {
                airCandidate = 0;
                if (airing)
                {
                    airing = false;
                    int airMs = now - airStart;
                    life.AirMs = Math.Max(life.AirMs, airMs);
                    StatMax(inVeh ? "carAirMax" : "airMax", airMs);
                    if (airMs >= 900)
                        AddTrick(string.Format("{0}Air time {1:0.0}s", inVeh ? "Car " : "", airMs / 1000f), airMs / 10, 2);
                    float fall = maxZ - pos.Z;
                    if (!inVeh && fall >= 12f) AddTrick("Big drop", (int)(fall * 15f), 4);
                    if (!inVeh) RoofCheck(ped);
                }
            }

            // own vehicle crash
            if (inVeh)
            {
                float bh = Function.Call<float>(Hash.GET_VEHICLE_BODY_HEALTH, veh.Handle);
                if (ownBody >= 0f && ownBody - bh >= 50f && rt > nextCrashAt) { nextCrashAt = rt + 1500; AddTrick("Crash", 100, 5); }
                ownBody = bh;
            }
            else ownBody = -1f;

            if (rt >= nextScan) { nextScan = rt + 120; ScanWorld(ped, inVeh, veh); }
        }

        static float Wrap(float d)
        {
            while (d > 180f) d -= 360f;
            while (d < -180f) d += 360f;
            return d;
        }

        void RoofCheck(Ped ped)
        {
            Vector3 p = ped.Position;
            int hit;
            if (!RayDown(p, 2.5f, 2, ped.Handle, out hit) || hit == 0) return;
            if (!Function.Call<bool>(Hash.IS_ENTITY_A_VEHICLE, hit)) return;
            Vehicle v = (Vehicle)Entity.FromHandle(hit);
            if (v == null || !v.Exists()) return;
            Count("roofs", 1);
            if (v.Speed > 6f) AddTrick("Car surfing", 600, 6);
            else AddTrick("Landed on a car roof", 400, 5);
        }

        bool RayDown(Vector3 from, float len, int flags, int ignore, out int entity)
        {
            Vector3 to = from + new Vector3(0f, 0f, -len);
            int h = Function.Call<int>(Hash.START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE, from.X, from.Y, from.Z, to.X, to.Y, to.Z, flags, ignore, 7);
            OutputArgument hit = new OutputArgument(), end = new OutputArgument(), nrm = new OutputArgument(), ent = new OutputArgument();
            Function.Call<int>(Hash.GET_SHAPE_TEST_RESULT, h, hit, end, nrm, ent);
            entity = ent.GetResult<int>();
            return hit.GetResult<bool>();
        }

        // Looks at what is around the goat every ~120 ms: hits on peds, damage to cars, broken windows.
        void ScanWorld(Ped ped, bool inVeh, Vehicle ownVeh)
        {
            Vector3 pos = ped.Position;
            long now = rt;
            bool recentHead = now - tHead < 700, recentKick = now - tKick < 700;
            bool credit = now - lastActionAt < 2500;
            bool hitInVehicle = false;

            nearPeds.Clear(); nearVehicles.Clear();
            HashSet<int> seenP = new HashSet<int>(), seenV = new HashSet<int>();

            Ped[] peds = World.GetNearbyPeds(pos, 14f);
            if (peds != null)
                foreach (Ped p in peds)
                {
                    if (p == null || !p.Exists() || p.Handle == ped.Handle) continue;
                    int h = p.Handle; seenP.Add(h);
                    nearPeds.Add(p);
                    float dist = (p.Position - pos).Length();

                    int hp = p.Health, last;
                    int drop = pedHealth.TryGetValue(h, out last) ? last - hp : 0;
                    pedHealth[h] = hp;
                    bool byUs = Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY, p.Handle, ped.Handle, true);
                    if (byUs) Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, p.Handle);

                    // Touch of Chaos mutator
                    if (mut[MRag] && active && dist < 2.4f && !p.IsRagdoll && !p.IsInVehicle())
                    {
                        Function.Call(Hash.SET_PED_TO_RAGDOLL, p.Handle, 3000, 3000, 0, false, false, false);
                        Vector3 away = p.Position - pos; away.Z = 0f;
                        float l = away.Length(); if (l < 0.01f) l = 1f;
                        p.Velocity = away / l * 6f + new Vector3(0f, 0f, 3f);
                        AddTrick("Touch of chaos", 25, 1);
                    }

                    bool hit = (drop > 0 && dist < 4.5f) || byUs;
                    long lastHit; if (pedHitAt.TryGetValue(h, out lastHit) && now - lastHit < 300) hit = false;
                    if (!hit) continue;
                    pedHitAt[h] = now;

                    int type = Function.Call<int>(Hash.GET_PED_TYPE, p.Handle);
                    bool cop = type == 6 || type == 27;
                    string name; int pts = 100, stupid = 2;
                    if (recentHead) { name = cop ? "Headbutted a cop" : "Headbutt"; Count("headbutts", 1); stupid = cop ? 9 : 4; }
                    else if (recentKick) { name = cop ? "Kicked a cop" : "Kick"; Count("kicks", 1); stupid = cop ? 9 : 4; }
                    else if (inVeh) { name = "Roadkill"; stupid = 5; pts = 150; }
                    else if (airing) { name = "Air smash"; pts = 150; stupid = 3; }
                    else { name = "Smack"; pts = 50; }
                    if (cop) { Count("cops", 1); pts += 100; }
                    if (p.IsDead) { name += " (K.O.)"; pts += 100; }
                    if (p.IsInVehicle()) hitInVehicle = true;
                    Count("peds", 1);
                    AddTrick(name, pts, stupid);
                    if (p.Velocity.Length() > 12f) AddTrick("Launched!", 150, 4);
                }

            Vehicle[] vehs = World.GetNearbyVehicles(pos, 14f);
            if (vehs != null)
                foreach (Vehicle v in vehs)
                {
                    if (v == null || !v.Exists()) continue;
                    if (ownVeh != null && v.Handle == ownVeh.Handle) continue;
                    int h = v.Handle; seenV.Add(h);
                    nearVehicles.Add(v);
                    float dist = (v.Position - pos).Length();
                    bool byUs = Function.Call<bool>(Hash.HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY, v.Handle, ped.Handle, true);
                    if (byUs) Function.Call(Hash.CLEAR_ENTITY_LAST_DAMAGE_ENTITY, v.Handle);
                    bool cr = credit || byUs;

                    float bh = Function.Call<float>(Hash.GET_VEHICLE_BODY_HEALTH, v.Handle), lastBh;
                    if (carBody.TryGetValue(h, out lastBh) && lastBh - bh >= 40f && dist < 8f && cr)
                        AddTrick("Wreckage", (int)Math.Min(400f, (lastBh - bh) * 0.6f), 5);
                    carBody[h] = bh;
                    if ((v.IsDead || bh <= 0f) && dist < 15f && cr && !wrecked.Contains(h))
                    {
                        wrecked.Add(h); Count("wrecks", 1);
                        AddTrick("Wrecked a car", 500, 6);
                    }

                    if (dist < 7f)
                    {
                        int mask = 0;
                        for (int i = 0; i < 4; i++)
                            if (Function.Call<bool>(Hash.IS_VEHICLE_WINDOW_INTACT, v.Handle, i)) mask |= 1 << i;
                        int lastMask;
                        if (carWindows.TryGetValue(h, out lastMask) && cr)
                        {
                            int lost = lastMask & ~mask;
                            for (int i = 0; i < 4; i++)
                                if ((lost & (1 << i)) != 0)
                                {
                                    Count("windows", 1);
                                    if (hitInVehicle) AddTrick("Through the window!", 400, 8);
                                    else AddTrick("Window smash", 150, 4);
                                }
                        }
                        carWindows[h] = mask;
                    }
                }

            Prune(pedHealth, seenP); Prune(pedHitAt, seenP);
            Prune(carBody, seenV); Prune(carWindows, seenV);
        }

        static void Prune<T>(Dictionary<int, T> d, HashSet<int> keep)
        {
            List<int> dead = null;
            foreach (int k in d.Keys) if (!keep.Contains(k)) { if (dead == null) dead = new List<int>(); dead.Add(k); }
            if (dead != null) foreach (int k in dead) d.Remove(k);
        }

        // ---- combo -------------------------------------------------------------
        void AddTrick(string name, int pts, int stupid)
        {
            if (!active || pts <= 0) return;
            if (mut[MDouble]) pts *= 2;
            comboCount++;
            comboMult = Math.Min(comboMax, 1 + comboCount / 3);
            comboUntil = Game.GameTime + (int)(comboSeconds * 1000f);
            comboPending += pts;
            if (comboMult > life.BestMult) life.BestMult = comboMult;
            StatMax("bestMult", comboMult);
            if (stupid > life.StupidW || (stupid == life.StupidW && pts >= life.StupidPts))
            { life.StupidW = stupid; life.StupidName = name; life.StupidPts = pts; }
            AddPopup(name + " +" + pts, stupid >= 6 ? Color.FromArgb(255, 120, 80) : Color.FromArgb(255, 220, 70));
            saveDirty = true;
        }

        void TickCombo()
        {
            if (comboCount > 0 && Game.GameTime > comboUntil) BankCombo();
        }

        void BankCombo()
        {
            if (comboCount == 0) return;
            int add = comboPending * comboMult;
            score += add; life.Points += add; Count("score", add);
            if (comboMult > 1 || add >= 500) AddPopup("Combo banked +" + add, Color.FromArgb(120, 255, 120));
            comboCount = 0; comboPending = 0; comboMult = 1;
        }

        // ================================================================== bonus screen
        void TickBonusDetect(Ped ped)
        {
            string reason = null;
            if (ped.IsDead) reason = "WASTED";
            else if (Function.Call<bool>(Hash.IS_PLAYER_BEING_ARRESTED, Game.Player.Handle, true)) reason = "BUSTED";

            if (reason == null) bonusArmed = true;
            else if (bonusArmed && !bonusShowing)
            {
                bonusArmed = false;
                BankCombo();
                Count("bonus", 1);
                bonusTitle = reason; bonusLife = life; life = new Life();
                bonusShowing = true; bonusStart = rt;
                airing = false; airCandidate = 0;
            }
            if (bonusShowing && rt - bonusStart > (long)(bonusSeconds * 1000f)) bonusShowing = false;
        }

        static string Rank(long pts)
        {
            if (pts < 500) return "Baby Goat";
            if (pts < 5000) return "Troublemaker";
            if (pts < 25000) return "Menace";
            if (pts < 100000) return "Agent of Chaos";
            return "Goat of Legend";
        }

        // ================================================================== mutators
        void ToggleMutator(int i)
        {
            mut[i] = !mut[i];
            if (mut[i]) { stats["mutmask"] = Stat("mutmask") | (1L << i); stats["mutCount"] = BitCount(Stat("mutmask")); saveDirty = true; }
            Notify(MutNames[i] + (mut[i] ? ": ON" : ": OFF"));
        }
        static int BitCount(long v) { int c = 0; while (v != 0) { c += (int)(v & 1); v >>= 1; } return c; }

        void TickMutators(Ped ped)
        {
            float dt = Math.Min(Math.Max(Game.LastFrameTime, 0.001f), 0.05f);
            bool inVeh = ped.IsInVehicle();
            Vehicle veh = inVeh ? ped.CurrentVehicle : null;

            if (mut[MJump]) Function.Call(Hash.SET_SUPER_JUMP_THIS_FRAME, Game.Player.Handle);

            if (mut[MLow])
            {
                // cancels 70% of gravity on everything near the goat
                float up = 9.81f * 0.7f * dt;
                Lift(inVeh && veh != null ? (Entity)veh : (Entity)ped, up);
                foreach (Vehicle v in nearVehicles) if (v != null && v.Exists()) Lift(v, up);
                foreach (Ped p in nearPeds) if (p != null && p.Exists() && (p.IsRagdoll || p.IsInAir)) Lift(p, up);
            }

            if (mut[MBounce])
            {
                Bounce(inVeh && veh != null ? (Entity)veh : (Entity)ped, true);
                foreach (Vehicle v in nearVehicles) if (v != null && v.Exists()) Bounce(v, false);
                foreach (Ped p in nearPeds) if (p != null && p.Exists()) Bounce(p, false);
            }
        }

        static void Lift(Entity e, float dv)
        {
            Vector3 v = e.Velocity;
            e.Velocity = new Vector3(v.X, v.Y, v.Z + dv);
        }

        // An impact is a big sudden change in vertical speed after falling; turn it into a rebound.
        void Bounce(Entity e, bool player)
        {
            int h = e.Handle;
            Vector3 v = e.Velocity;
            float last;
            if (lastVz.TryGetValue(h, out last) && last < -5f && v.Z - last > 4f && v.Z < 3f)
            {
                float bz = Math.Min(-last * 0.85f, 25f);
                e.Velocity = new Vector3(v.X, v.Y, bz);
                v = e.Velocity;
                if (player) AddTrick("Boing", 20, 1);
            }
            lastVz[h] = v.Z;
            if (lastVz.Count > 200) lastVz.Clear();
        }

        // time scale + screen effect shared by mutators and photo mode
        void ApplyWorldState()
        {
            float want = 1f;
            if (photoOn && photoFreeze) want = 0f; else if (mut[MSlow]) want = 0.3f;
            if (Math.Abs(want - appliedScale) > 0.001f) { Function.Call(Hash.SET_TIME_SCALE, want); appliedScale = want; }

            string tc = "";
            if (photoOn && filterIndex > 0 && filterIndex < filters.Length) tc = filters[filterIndex];
            else if (mut[MDrunk]) tc = "drug_drive_blend01";
            if (tc != appliedTimecycle)
            {
                if (tc.Length > 0) Function.Call(Hash.SET_TIMECYCLE_MODIFIER, tc); else Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER);
                appliedTimecycle = tc;
            }
            bool drunk = mut[MDrunk] && !photoOn;
            if (drunk && !drunkShake) { Function.Call(Hash.SHAKE_GAMEPLAY_CAM, "DRUNK_SHAKE", 1.0f); drunkShake = true; }
            else if (!drunk && drunkShake) { Function.Call(Hash.STOP_GAMEPLAY_CAM_SHAKING, true); drunkShake = false; }
        }
        bool drunkShake;

        // ================================================================== achievements
        void CheckAchievements()
        {
            foreach (Ach a in achs)
            {
                if (unlocked.Contains(a.Id)) continue;
                if (Stat(a.Stat) >= a.Target)
                {
                    unlocked.Add(a.Id); saveDirty = true;
                    toastTitle = "ACHIEVEMENT UNLOCKED"; toastText = a.Name + " - " + a.Desc;
                    toastUntil = rt + 5000;
                    Function.Call(Hash.PLAY_SOUND_FRONTEND, -1, "Mission_Pass_Notify", "DLC_HEISTS_GENERAL_FRONTEND_SOUNDS");
                }
            }
        }

        // ================================================================== photo mode
        void EnterPhoto()
        {
            Ped ped = Game.Player.Character;
            if (ped == null || !ped.Exists() || ped.IsDead || bonusShowing) return;
            menuOpen = false; achOpen = false;
            camPos = GameplayCamera.Position; camRot = GameplayCamera.Rotation; camFov = 50f;
            cam = Function.Call<int>(Hash.CREATE_CAM, "DEFAULT_SCRIPTED_CAMERA", true);
            Function.Call(Hash.SET_CAM_COORD, cam, camPos.X, camPos.Y, camPos.Z);
            Function.Call(Hash.SET_CAM_ROT, cam, camRot.X, camRot.Y, camRot.Z, 2);
            Function.Call(Hash.SET_CAM_FOV, cam, camFov);
            Function.Call(Hash.SET_CAM_ACTIVE, cam, true);
            Function.Call(Hash.RENDER_SCRIPT_CAMS, true, false, 0, true, false);
            photoOn = true; photoHints = true; lastFrameRt = rt;
            ApplyHat();
        }

        void ExitPhoto()
        {
            photoOn = false;
            RemoveHat();
            if (cam != -1)
            {
                Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false);
                Function.Call(Hash.SET_CAM_ACTIVE, cam, false);
                Function.Call(Hash.DESTROY_CAM, cam, false);
                cam = -1;
            }
            ApplyWorldState();
        }

        void TickPhoto()
        {
            long now = sw.ElapsedMilliseconds; rt = now;
            float dt = Math.Min((now - lastFrameRt) / 1000f, 0.1f); lastFrameRt = now;
            Function.Call(Hash.DISABLE_ALL_CONTROL_ACTIONS, 0);
            Function.Call(Hash.HIDE_HUD_AND_RADAR_THIS_FRAME);

            // mouse look (disabled-control reads still work while everything is disabled)
            float lx = Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, 1);
            float ly = Function.Call<float>(Hash.GET_DISABLED_CONTROL_NORMAL, 0, 2);
            camRot.Z -= lx * photoSens * 20f * dt * 6f;
            camRot.X -= ly * photoSens * 20f * dt * 6f;
            if (camRot.X > 89f) camRot.X = 89f; if (camRot.X < -89f) camRot.X = -89f;

            double yaw = camRot.Z * Math.PI / 180.0, pit = camRot.X * Math.PI / 180.0;
            Vector3 fwd = new Vector3((float)(-Math.Sin(yaw) * Math.Cos(pit)), (float)(Math.Cos(yaw) * Math.Cos(pit)), (float)Math.Sin(pit));
            Vector3 right = new Vector3((float)Math.Cos(yaw), (float)Math.Sin(yaw), 0f);
            float sp = photoSpeed * ((held.Contains(Keys.ShiftKey) || held.Contains(Keys.LShiftKey)) ? 4f : 1f) * dt;
            Vector3 mv = new Vector3(0f, 0f, 0f);
            if (held.Contains(Keys.W)) mv = mv + fwd;
            if (held.Contains(Keys.S)) mv = mv - fwd;
            if (held.Contains(Keys.D)) mv = mv + right;
            if (held.Contains(Keys.A)) mv = mv - right;
            if (held.Contains(Keys.E)) mv = mv + new Vector3(0f, 0f, 1f);
            if (held.Contains(Keys.Q)) mv = mv - new Vector3(0f, 0f, 1f);
            camPos = camPos + mv * sp;
            if (held.Contains(Keys.Z)) camFov = Math.Max(10f, camFov - 30f * dt);
            if (held.Contains(Keys.X)) camFov = Math.Min(100f, camFov + 30f * dt);

            Function.Call(Hash.SET_CAM_COORD, cam, camPos.X, camPos.Y, camPos.Z);
            Function.Call(Hash.SET_CAM_ROT, cam, camRot.X, camRot.Y, camRot.Z, 2);
            Function.Call(Hash.SET_CAM_FOV, cam, camFov);
            ApplyWorldState();

            if (photoHints)
            {
                float cx = GTA.UI.Screen.ScaledWidth / 2f;
                Text("PHOTO MODE", cx, 20, 0.6f, Color.White, UIFont.Pricedown, UIAlign.Center);
                string hat = hatIndex >= 0 && hatIndex < hats.Length ? hats[hatIndex] : "none";
                Text("Filter: " + (filterIndex < filters.Length ? filters[filterIndex] : "NONE") + "   Hat: " + hat, cx, 660, 0.4f, Color.White, UIFont.ChaletLondon, UIAlign.Center);
                Text("WASD/QE move  mouse look  Shift fast  Z/X zoom  Left/Right filter  Up/Down hat  Space shutter  H hide hints  F7 exit", cx, 686, 0.3f, Color.FromArgb(200, 255, 255, 255), UIFont.ChaletLondon, UIAlign.Center);
            }
            if (rt < flashUntil)
            {
                int a = (int)(255 * Math.Max(0.0, (flashUntil - rt) / 180.0));
                Function.Call(Hash.DRAW_RECT, 0.5f, 0.5f, 1f, 1f, 255, 255, 255, a);
            }
        }

        void CycleHat(int dir)
        {
            hatIndex += dir;
            if (hatIndex >= hats.Length) hatIndex = -1;
            if (hatIndex < -1) hatIndex = hats.Length - 1;
            ApplyHat();
        }

        void ApplyHat()
        {
            RemoveHat();
            if (hatIndex < 0 || hatIndex >= hats.Length) return;
            Ped ped = Game.Player.Character;
            Model m = new Model(hats[hatIndex].Trim());
            if (!m.IsInCdImage || !m.IsValid || !m.Request(1000)) { Notify("Hat model not found: " + hats[hatIndex]); return; }
            hatProp = World.CreateProp(m, ped.Position, false, false);
            if (hatProp == null) return;
            int bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, ped.Handle, 0x796E);   // SKEL_Head
            Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, hatProp.Handle, ped.Handle, bone,
                hatPos.X, hatPos.Y, hatPos.Z, hatRot.X, hatRot.Y, hatRot.Z, false, false, false, false, 2, true);
            m.MarkAsNoLongerNeeded();
        }

        void RemoveHat()
        {
            if (hatProp != null && hatProp.Exists()) hatProp.Delete();
            hatProp = null;
        }

        // ================================================================== drawing
        void DrawAll(Ped ped)
        {
            float sw2 = GTA.UI.Screen.ScaledWidth, cx = sw2 / 2f;

            if (showHud && active && !bonusShowing)
            {
                Text("WRECKAGE", cx, 10, 0.32f, Color.FromArgb(220, 255, 255, 255), UIFont.ChaletComprimeCologne, UIAlign.Center);
                Text(score.ToString("N0"), cx, 26, 0.75f, Color.White, UIFont.Pricedown, UIAlign.Center);
                if (comboCount > 0)
                {
                    float frac = Math.Max(0f, Math.Min(1f, (comboUntil - Game.GameTime) / (comboSeconds * 1000f)));
                    Text("x" + comboMult, cx, 64, 1.1f, Color.FromArgb(255, 255, 210, 40), UIFont.Pricedown, UIAlign.Center);
                    Text("+" + (comboPending * comboMult).ToString("N0"), cx, 112, 0.5f, Color.White, UIFont.ChaletComprimeCologne, UIAlign.Center);
                    RectPx(cx - 70, 140, 140, 6, Color.FromArgb(120, 0, 0, 0));
                    RectPx(cx - 70, 140, 140 * frac, 6, Color.FromArgb(255, 255, 210, 40));
                }
                string on = ActiveMutators();
                if (on.Length > 0) Text(on, cx, 150, 0.28f, Color.FromArgb(200, 200, 160, 255), UIFont.ChaletLondon, UIAlign.Center);
            }

            DrawPopups(ped);

            if (rt < toastUntil)
            {
                RectPx(cx - 260, 560, 520, 62, Color.FromArgb(200, 20, 20, 20));
                RectPx(cx - 260, 560, 6, 62, Color.FromArgb(255, 255, 210, 40));
                Text(toastTitle, cx, 566, 0.4f, Color.FromArgb(255, 255, 210, 40), UIFont.ChaletComprimeCologne, UIAlign.Center);
                Text(toastText, cx, 590, 0.34f, Color.White, UIFont.ChaletLondon, UIAlign.Center);
            }

            if (menuOpen) DrawMenu(cx);
            if (achOpen) DrawAchievements(cx);
            if (bonusShowing) DrawBonus(cx);
        }

        string ActiveMutators()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < mut.Length; i++) if (mut[i]) { if (sb.Length > 0) sb.Append("  |  "); sb.Append(MutNames[i].Split('(')[0].Trim()); }
            return sb.ToString();
        }

        void AddPopup(string text, Color c)
        {
            Ped ped = Game.Player.Character;
            if (ped == null || !ped.Exists()) return;
            if (popups.Count > 14) popups.RemoveAt(0);
            popups.Add(new Popup { Text = text, Pos = ped.Position + new Vector3(0f, 0f, 1.4f), Born = rt, Col = c, Dx = (float)(rng.NextDouble() * 80 - 40) });
        }

        void DrawPopups(Ped ped)
        {
            for (int i = popups.Count - 1; i >= 0; i--)
            {
                Popup p = popups[i];
                long age = rt - p.Born;
                if (age > 1800) { popups.RemoveAt(i); continue; }
                Vector3 wp = p.Pos + new Vector3(0f, 0f, age * 0.0012f);
                OutputArgument ox = new OutputArgument(), oy = new OutputArgument();
                if (!Function.Call<bool>(Hash.GET_SCREEN_COORD_FROM_WORLD_COORD, wp.X, wp.Y, wp.Z, ox, oy)) continue;
                float x = ox.GetResult<float>() * GTA.UI.Screen.ScaledWidth + p.Dx;
                float y = oy.GetResult<float>() * 720f - (i % 3) * 18f;
                int a = (int)(255 * (1.0 - age / 1800.0));
                Text(p.Text, x, y, 0.5f, Color.FromArgb(a, p.Col.R, p.Col.G, p.Col.B), UIFont.ChaletComprimeCologne, UIAlign.Center);
            }
        }

        void DrawMenu(float cx)
        {
            float w = 460, h = 70 + mut.Length * 30;
            RectPx(cx - w / 2, 170, w, h, Color.FromArgb(215, 15, 15, 20));
            Text("MUTATORS", cx, 178, 0.6f, Color.FromArgb(255, 255, 210, 40), UIFont.Pricedown, UIAlign.Center);
            for (int i = 0; i < mut.Length; i++)
            {
                float y = 222 + i * 30;
                if (i == menuIndex) RectPx(cx - w / 2 + 8, y - 2, w - 16, 28, Color.FromArgb(90, 255, 210, 40));
                Text((mut[i] ? "[ON]  " : "[off] ") + MutNames[i], cx - w / 2 + 20, y, 0.4f, mut[i] ? Color.FromArgb(255, 140, 255, 140) : Color.White, UIFont.ChaletLondon, UIAlign.Left);
            }
            Text("Up/Down select - Enter toggle - F11 close", cx, 170 + h - 22, 0.3f, Color.FromArgb(180, 255, 255, 255), UIFont.ChaletLondon, UIAlign.Center);
        }

        void DrawAchievements(float cx)
        {
            float w = 600, h = 74 + achs.Count * 28;
            float top = Math.Max(10f, 360f - h / 2f);
            RectPx(cx - w / 2, top, w, h, Color.FromArgb(220, 15, 15, 20));
            Text("ACHIEVEMENTS  " + unlocked.Count + "/" + achs.Count, cx, top + 8, 0.6f, Color.FromArgb(255, 255, 210, 40), UIFont.Pricedown, UIAlign.Center);
            for (int i = 0; i < achs.Count; i++)
            {
                Ach a = achs[i];
                bool done = unlocked.Contains(a.Id);
                float y = top + 56 + i * 28;
                long cur = Math.Min(Stat(a.Stat), a.Target);
                Color c = done ? Color.FromArgb(255, 140, 255, 140) : Color.White;
                Text((done ? "[X] " : "[ ] ") + a.Name, cx - w / 2 + 16, y, 0.36f, c, UIFont.ChaletLondon, UIAlign.Left);
                Text(a.Desc, cx - 40, y, 0.3f, Color.FromArgb(190, 255, 255, 255), UIFont.ChaletLondon, UIAlign.Left);
                Text(done ? "done" : cur + "/" + a.Target, cx + w / 2 - 14, y, 0.3f, c, UIFont.ChaletLondon, UIAlign.Right);
            }
        }

        void DrawBonus(float cx)
        {
            long t = rt - bonusStart;
            Life l = bonusLife;
            float w = 520, x = cx - w / 2, y = 150;
            RectPx(x, y, w, 360, Color.FromArgb(Math.Min(215, (int)(t / 3)), 10, 10, 14));
            if (t < 300) return;
            Text("BONUS ROUND", cx, y + 8, 0.9f, Color.FromArgb(255, 255, 210, 40), UIFont.Pricedown, UIAlign.Center);
            Text(bonusTitle == "BUSTED" ? "The goat has been apprehended." : "The goat has been wasted.", cx, y + 62, 0.34f, Color.FromArgb(200, 255, 255, 255), UIFont.ChaletLondon, UIAlign.Center);

            string[] label = { "Wreckage earned", "Best combo", "Longest air time", "Pedestrians assaulted", "Cars wrecked", "Windows smashed", "Flips landed" };
            string[] val = {
                l.Points.ToString("N0"), "x" + l.BestMult, (l.AirMs / 1000f).ToString("0.0") + " s",
                l.Get("peds").ToString(), l.Get("wrecks").ToString(), l.Get("windows").ToString(), l.Get("flips").ToString() };
            for (int i = 0; i < label.Length; i++)
            {
                if (t < 600 + i * 350) break;
                float ry = y + 96 + i * 26;
                Text(label[i], x + 24, ry, 0.38f, Color.White, UIFont.ChaletLondon, UIAlign.Left);
                Text(val[i], x + w - 24, ry, 0.38f, Color.FromArgb(255, 255, 210, 40), UIFont.ChaletLondon, UIAlign.Right);
            }
            if (t > 600 + label.Length * 350)
            {
                string stupid = l.StupidW < 0 ? "Nothing. Not even a headbutt. Disappointing goat."
                    : l.StupidName + " (+" + l.StupidPts + ")";
                Text("Stupidest thing you did:", cx, y + 96 + label.Length * 26 + 6, 0.34f, Color.FromArgb(200, 255, 255, 255), UIFont.ChaletLondon, UIAlign.Center);
                Text(stupid, cx, y + 96 + label.Length * 26 + 26, 0.5f, Color.FromArgb(255, 255, 120, 90), UIFont.ChaletComprimeCologne, UIAlign.Center);
                Text("Rank: " + Rank(l.Points), cx, y + 96 + label.Length * 26 + 62, 0.5f, Color.White, UIFont.Pricedown, UIAlign.Center);
            }
        }

        // ---- draw helpers (1280x720 scaled coordinates) ------------------------
        static void Text(string s, float x, float y, float scale, Color c, UIFont font, UIAlign al)
        {
            new TextElement(s, new PointF(x, y), scale, c, font, al).Draw();
        }
        static void RectPx(float x, float y, float w, float h, Color c)
        {
            float sw2 = GTA.UI.Screen.ScaledWidth;
            Function.Call(Hash.DRAW_RECT, (x + w / 2f) / sw2, (y + h / 2f) / 720f, w / sw2, h / 720f, (int)c.R, (int)c.G, (int)c.B, (int)c.A);
        }
        static void Notify(string text) { GTA.UI.Screen.ShowSubtitle("~y~GoatStyle~s~: " + text, 2500); }

        // ================================================================== shutdown
        void Shutdown()
        {
            try
            {
                if (photoOn) ExitPhoto();
                if (appliedScale != 1f) Function.Call(Hash.SET_TIME_SCALE, 1f);
                if (appliedTimecycle.Length > 0) Function.Call(Hash.CLEAR_TIMECYCLE_MODIFIER);
                if (drunkShake) Function.Call(Hash.STOP_GAMEPLAY_CAM_SHAKING, true);
                BankCombo();
                Save();
            }
            catch { }
        }
    }
}
