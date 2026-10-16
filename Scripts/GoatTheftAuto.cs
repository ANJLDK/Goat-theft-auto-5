// Goat Theft Auto 5 - ScriptHookVDotNet v3 script (single player only).
// Turns the player into a Goat Simulator goat with that goat's abilities.
// The goat stat tables are at the bottom of this file. See Docs/README.md for install steps.

using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using Control = GTA.Control;

namespace GoatTheftAuto
{
    public class GoatTheftAutoScript : Script
    {
        // ---- constants -------------------------------------------------------------
        const float UdkGravityUU = 520f;      // Unreal default gravity (uu/s^2)
        const float GtaGravity = 9.81f;
        const int SkelHead = 0x796E;
        const int SkelSpine = 0xE0FD;

        // ---- settings --------------------------------------------------------------
        readonly ScriptSettings settings;
        Keys keyToggle = Keys.F10, keyNextGoat = Keys.OemCloseBrackets, keyPrevGoat = Keys.OemOpenBrackets;
        Keys keyNextAcc = Keys.OemPeriod, keyPrevAcc = Keys.Oemcomma;
        Keys keyHeadbutt = Keys.H, keyKick = Keys.J, keyLick = Keys.L, keySpecial = Keys.K;
        Keys keyBaa = Keys.B, keyAccessory = Keys.G;
        float damageScale = 0.25f;     // original damage points -> GTA health
        float forceScale = 1.0f;       // knock-back multiplier
        bool godMode = true;
        string fallbackModel = "a_c_deer";

        // ---- state -----------------------------------------------------------------
        readonly List<GoatDef> goats;
        readonly List<AccessoryDef> accessories;
        int goatIndex = 0;
        int accIndex = 0;
        bool active = false;
        Model originalModel;
        readonly HashSet<Keys> held = new HashSet<Keys>();

        // movement / physics
        bool wasInAir; float lastVz; float prevHSpeed;
        bool jumpQueued; int jumpQueuedFrames;
        int flapReadyAt; int hoverReadyAt; int ragdollUntil;
        bool gliding;

        // lick / carry
        Entity carried;
        Ped controlledPed; Model controlledModel;

        // specials
        bool fieldOn;        // devil pull field (goat special)
        bool thrustOn;       // sperm goat thrust
        float water;         // camel
        bool spraying; int sprayFx; bool sprayAssetLoaded;
        Entity cartVehicle;

        // accessory state
        bool accToggle;      // jetpack / tornado / repulsive / demonic / glider on-off
        bool grappling; Vector3 grapplePoint; float ropeLength;
        float streakCharge; int boostReadyAt;
        Prop accessoryProp;
        SoundPlayer baaSound;

        // soundtrack (plays mp3 through Windows MCI, no extra libraries)
        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        static extern int mciSendString(string command, StringBuilder ret, int retLen, IntPtr hwnd);
        string musicFolder = "";
        string musicPlaylist = "";          // comma separated file names; empty = every song in the folder
        bool musicEnabled = true;
        int musicVolume = 600;              // 0-1000
        Keys keyMusicToggle = Keys.M, keyMusicNext = Keys.N;
        readonly List<string> tracks = new List<string>();
        int trackIndex = -1;
        bool musicPlaying; int musicCheckAt;

        // radio station ("Chilled Goat") - works with or without goat mode
        bool radioEnabled = true;
        string radioName = "Chilled Goat";
        string radioFile = "1x02_Goat_Chill";
        bool radioAlways = false;           // false: only plays while you are in a vehicle
        bool radioTunedIn = false;          // the dial: tuned to Chilled Goat or not
        int radioVolume = 600;
        Keys keyRadio = Keys.F9;
        string radioPath = "";
        bool radioOpen, radioPaused; int radioCheckAt;
        bool radioDefault = true;          // every time you get into a vehicle, tune to Chilled Goat
        bool radioWasInVehicle; int radioGraceUntil;

        public GoatTheftAutoScript()
        {
            goats = GoatTable.Goats();
            accessories = GoatTable.Accessories();
            string iniPath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "GoatTheftAuto.ini");
            settings = ScriptSettings.Load(iniPath);
            LoadSettings();

            string baa = Path.Combine(Path.GetDirectoryName(iniPath), "GoatTheftAuto", "baa.wav");
            if (File.Exists(baa)) { try { baaSound = new SoundPlayer(baa); } catch { baaSound = null; } }

            Tick += OnTick;
            KeyDown += OnKeyDown;
            KeyUp += OnKeyUp;
            Interval = 0;
            Aborted += OnAborted;
        }

        // ---- config ----------------------------------------------------------------
        void LoadSettings()
        {
            keyToggle = settings.GetValue("Keys", "ToggleGoatMode", keyToggle);
            keyNextGoat = settings.GetValue("Keys", "NextGoat", keyNextGoat);
            keyPrevGoat = settings.GetValue("Keys", "PreviousGoat", keyPrevGoat);
            keyNextAcc = settings.GetValue("Keys", "NextAccessory", keyNextAcc);
            keyPrevAcc = settings.GetValue("Keys", "PreviousAccessory", keyPrevAcc);
            keyHeadbutt = settings.GetValue("Keys", "Headbutt", keyHeadbutt);
            keyKick = settings.GetValue("Keys", "Kick", keyKick);
            keyLick = settings.GetValue("Keys", "Lick", keyLick);
            keySpecial = settings.GetValue("Keys", "Special", keySpecial);
            keyBaa = settings.GetValue("Keys", "Baa", keyBaa);
            keyAccessory = settings.GetValue("Keys", "AccessoryAction", keyAccessory);
            keyMusicToggle = settings.GetValue("Keys", "MusicToggle", keyMusicToggle);
            keyMusicNext = settings.GetValue("Keys", "MusicNext", keyMusicNext);
            musicFolder = settings.GetValue("Music", "Folder", musicFolder);
            keyRadio = settings.GetValue("Keys", "RadioToggle", keyRadio);
            radioEnabled = settings.GetValue("Radio", "Enabled", radioEnabled);
            radioName = settings.GetValue("Radio", "StationName", radioName);
            radioFile = settings.GetValue("Radio", "Track", radioFile);
            radioVolume = settings.GetValue("Radio", "Volume", radioVolume);
            radioAlways = string.Equals(settings.GetValue("Radio", "Mode", "Vehicle"), "Always", StringComparison.OrdinalIgnoreCase);
            radioTunedIn = settings.GetValue("Radio", "StartTunedIn", radioTunedIn);
            radioDefault = settings.GetValue("Radio", "DefaultStation", radioDefault);
            ResolveRadioFile();
            musicEnabled = settings.GetValue("Music", "Enabled", musicEnabled);
            musicPlaylist = settings.GetValue("Music", "Playlist", musicPlaylist);
            musicVolume = settings.GetValue("Music", "Volume", musicVolume);
            LoadTracks();

            damageScale = settings.GetValue("Options", "DamageScale", damageScale);
            forceScale = settings.GetValue("Options", "ForceScale", forceScale);
            godMode = settings.GetValue("Options", "GodMode", godMode);
            fallbackModel = settings.GetValue("Options", "FallbackModel", fallbackModel);

            // Per-goat overrides: [Goat.<id>] Walk=.. Sprint=.. Model=.. (any field of GoatDef)
            foreach (GoatDef g in goats)
            {
                string section = "Goat." + g.Id;
                foreach (FieldInfo f in typeof(GoatDef).GetFields())
                {
                    if (f.FieldType == typeof(float)) f.SetValue(g, settings.GetValue(section, f.Name, (float)f.GetValue(g)));
                    else if (f.FieldType == typeof(bool)) f.SetValue(g, settings.GetValue(section, f.Name, (bool)f.GetValue(g)));
                    else if (f.FieldType == typeof(string)) f.SetValue(g, settings.GetValue(section, f.Name, (string)f.GetValue(g)));
                }
            }
        }

        GoatDef Goat { get { return goats[goatIndex]; } }
        AccessoryDef Acc { get { return accessories[accIndex]; } }

        // ---- input -----------------------------------------------------------------
        void OnKeyDown(object sender, KeyEventArgs e)
        {
            held.Add(e.KeyCode);
            if (e.KeyCode == keyRadio) { ToggleRadio(); return; }
            if (e.KeyCode == keyToggle) { if (active) LeaveGoatMode(); else EnterGoatMode(); return; }
            if (!active) return;

            if (e.KeyCode == keyNextGoat) SelectGoat(goatIndex + 1);
            else if (e.KeyCode == keyPrevGoat) SelectGoat(goatIndex - 1);
            else if (e.KeyCode == keyNextAcc) SelectAccessory(accIndex + 1);
            else if (e.KeyCode == keyPrevAcc) SelectAccessory(accIndex - 1);
            else if (e.KeyCode == keyHeadbutt) Headbutt();
            else if (e.KeyCode == keyKick) Kick();
            else if (e.KeyCode == keyLick) Lick();
            else if (e.KeyCode == keySpecial) SpecialPressed();
            else if (e.KeyCode == keyBaa) Baa();
            else if (e.KeyCode == keyAccessory) AccessoryPressed();
            else if (e.KeyCode == keyMusicToggle) ToggleMusic();
            else if (e.KeyCode == keyMusicNext) NextTrack();
        }

        void OnKeyUp(object sender, KeyEventArgs e)
        {
            held.Remove(e.KeyCode);
            if (!active) return;
            if (e.KeyCode == keySpecial) SpecialReleased();
            if (e.KeyCode == keyAccessory) AccessoryReleased();
        }

        bool Held(Keys k) { return held.Contains(k); }

        // ---- enter / leave ---------------------------------------------------------
        void EnterGoatMode()
        {
            originalModel = Game.Player.Character.Model;
            active = true;
            ApplyGoatModel();
            Notify("Goat mode: " + Goat.Name + "   [ ] switch goat   . , accessory   M music");
            if (musicEnabled) StartMusic();
        }

        void LeaveGoatMode()
        {
            ClearAll();
            StopMusic();
            active = false;
            Game.Player.IsInvincible = false;
            try
            {
                Model m = originalModel;
                if (m.IsValid && m.Request(3000)) { Game.Player.ChangeModel(m); m.MarkAsNoLongerNeeded(); }
                Function.Call(Hash.SET_PED_DEFAULT_COMPONENT_VARIATION, Game.Player.Character.Handle);
            }
            catch { }
            Notify("Goat mode off");
        }

        void OnAborted(object sender, EventArgs e)
        {
            try { StopRadioDevice(); ClearAll(); StopMusic(); if (active) Game.Player.IsInvincible = false; } catch { }
        }

        void SelectGoat(int i)
        {
            ClearAll();
            goatIndex = ((i % goats.Count) + goats.Count) % goats.Count;
            ApplyGoatModel();
            Notify(Goat.Name + (Goat.Note.Length > 0 ? " - " + Goat.Note : ""));
        }

        void SelectAccessory(int i)
        {
            ClearAccessory();
            accIndex = ((i % accessories.Count) + accessories.Count) % accessories.Count;
            AttachAccessoryProp();
            Notify("Accessory: " + Acc.Name + (Acc.Note.Length > 0 ? " - " + Acc.Note : ""));
        }

        void ApplyGoatModel()
        {
            GoatDef g = Goat;
            Model m = new Model(g.Model);
            if (!(m.IsInCdImage && m.IsValid)) m = new Model(fallbackModel);
            if (m.IsValid && m.Request(5000))
            {
                Game.Player.ChangeModel(m);
                m.MarkAsNoLongerNeeded();
            }
            Ped p = Game.Player.Character;
            Game.Player.IsInvincible = godMode;
            Function.Call(Hash.SET_ENTITY_MAX_SPEED, p.Handle, 80f);
            Function.Call(Hash.SET_PED_CAN_RAGDOLL, p.Handle, true);
            water = 0; thrustOn = false; fieldOn = false;

            if (g.Special == Special.Vehicle) SpawnCart(p);
            if (g.Special == Special.Thrust) RagdollFor(p, 600000);
            AttachAccessoryProp();
        }

        void ClearAll()
        {
            DropCarried(false);
            StopSpray();
            StopGrapple();
            ClearAccessory();
            if (cartVehicle != null && cartVehicle.Exists()) { cartVehicle.MarkAsNoLongerNeeded(); }
            cartVehicle = null;
            if (controlledPed != null && controlledPed.Exists()) { controlledPed.IsVisible = true; controlledPed.IsCollisionEnabled = true; }
            controlledPed = null;
            fieldOn = false; thrustOn = false; accToggle = false; gliding = false;
        }

        // ---- main tick -------------------------------------------------------------
        void OnTick(object sender, EventArgs e)
        {
            TickRadio();
            if (!active) return;
            TickMusic();
            Ped p = Game.Player.Character;
            if (p == null || !p.Exists() || p.IsDead) return;
            float dt = Math.Max(Game.LastFrameTime, 0.001f);
            GoatDef g = Goat;

            if (g.Special == Special.Vehicle) { TickCart(p); return; }

            TickMovement(p, g, dt);
            TickRagdollRules(p, g, dt);
            TickSpecial(p, g, dt);
            TickAccessory(p, g, dt);
            TickCarried(p);
        }

        // ---- movement --------------------------------------------------------------
        static Vector3 CamForward()
        {
            float h = GameplayCamera.Rotation.Z * (float)Math.PI / 180f;
            return new Vector3(-(float)Math.Sin(h), (float)Math.Cos(h), 0f);
        }

        static Vector3 CamForward3D()
        {
            Vector3 r = GameplayCamera.Rotation * ((float)Math.PI / 180f);
            float cp = (float)Math.Cos(r.X);
            return new Vector3(-(float)Math.Sin(r.Z) * cp, (float)Math.Cos(r.Z) * cp, (float)Math.Sin(r.X));
        }

        bool OnGround(Ped p) { return !p.IsInAir && !p.IsRagdoll && !p.IsSwimming; }

        float SpeedMult()
        {
            float m = 1f;
            if (Acc.Kind == AccessoryKind.RollerSkates) m *= 1.4f;
            return m;
        }

        void TickMovement(Ped p, GoatDef g, float dt)
        {
            if (p.IsInVehicle() || p.IsRagdoll || p.IsDead) return;

            bool ground = OnGround(p);

            // Horizontal speed: replaces GTA's walk/run speed with the goat's
            float ud = Game.GetControlValueNormalized(Control.MoveUpDown);
            float lr = Game.GetControlValueNormalized(Control.MoveLeftRight);
            Vector3 f = CamForward();
            Vector3 r = new Vector3(f.Y, -f.X, 0f);
            Vector3 dir = f * (-ud) + r * lr;
            float mag = Math.Min(1f, dir.Length());
            bool sprint = Game.IsControlPressed(Control.Sprint);
            float target = (sprint ? g.Sprint : g.Walk) * SpeedMult();

            if (ground && mag > 0.1f && !carriedBlocksMove())
            {
                dir.Normalize();
                Vector3 v = p.Velocity;
                p.Velocity = new Vector3(dir.X * target * mag, dir.Y * target * mag, v.Z);
                p.Heading = (float)(Math.Atan2(-dir.X, dir.Y) * 180.0 / Math.PI);
            }
            else if (!ground && !p.IsRagdoll && !gliding && mag > 0.1f)
            {
                // some air control
                dir.Normalize();
                Vector3 v = p.Velocity;
                float airTarget = Math.Max(3f, target * 0.5f);
                Vector3 h = new Vector3(v.X, v.Y, 0f);
                Vector3 want = dir * airTarget;
                h = h + (want - h) * Math.Min(1f, 1.5f * dt);
                p.Velocity = new Vector3(h.X, h.Y, v.Z);
            }

            // Jump height from JumpZ and gravity scale
            if (ground && Game.IsControlJustPressed(Control.Jump)) { jumpQueued = true; jumpQueuedFrames = 0; }
            if (jumpQueued)
            {
                jumpQueuedFrames++;
                if (jumpQueuedFrames >= 2)
                {
                    float apex = (g.JumpZ * g.JumpZ) / (2f * UdkGravityUU * Math.Max(0.05f, g.Gravity)) / 100f; // metres
                    float vz = (float)Math.Sqrt(2f * GtaGravity * Math.Max(0.05f, g.Gravity) * apex);
                    Vector3 v = p.Velocity;
                    p.Velocity = new Vector3(v.X, v.Y, vz);
                    jumpQueued = false;
                }
            }

            // Gravity scale (angel / feather float, flamingoat wings)
            if (!ground && !p.IsRagdoll)
            {
                float k = g.Gravity;
                Vector3 v = p.Velocity;
                float extra = GtaGravity * (1f - k) * dt;
                p.Velocity = new Vector3(v.X, v.Y, v.Z + extra);
            }

            // Flamingoat flight
            if (g.CanFly && !ground && !p.IsRagdoll) TickFlight(p, g, dt);
            else gliding = false;

            // Dolphwheel wall walk
            if (g.Special == Special.WallWalk) TickWallWalk(p, g, dt, mag > 0.1f);
        }

        bool carriedBlocksMove() { return false; }

        void TickFlight(Ped p, GoatDef g, float dt)
        {
            int now = Game.GameTime;
            if (Game.IsControlJustPressed(Control.Jump) && now >= flapReadyAt)
            {
                Vector3 v = p.Velocity;
                Vector3 f = CamForward();
                float boost = g.FlapZ * (carried != null ? 0.8f : 1f);
                p.Velocity = new Vector3(v.X + f.X * 3f, v.Y + f.Y * 3f, Math.Max(v.Z, boost));
                flapReadyAt = now + (int)(g.FlapCooldown * 1000f);
            }

            gliding = Game.IsControlPressed(Control.Jump);
            if (gliding)
            {
                Vector3 want = CamForward3D() * g.AirSpeed;
                Vector3 v = p.Velocity;
                // wings only hold you up if you keep flying forward: pitch limits the climb
                float vz = Math.Min(want.Z, 3f);
                vz = Math.Max(vz, -1.5f);
                Vector3 target = new Vector3(want.X, want.Y, vz);
                p.Velocity = v + (target - v) * Math.Min(1f, 1.8f * dt);
                p.Heading = (float)(Math.Atan2(-want.X, want.Y) * 180.0 / Math.PI);
            }
        }

        void TickWallWalk(Ped p, GoatDef g, float dt, bool pushing)
        {
            if (!pushing || p.IsRagdoll) return;
            Vector3 fwd = p.ForwardVector;
            Vector3 origin = p.Position + new Vector3(0f, 0f, 0.2f);
            Entity hit; Vector3 end;
            if (Raycast(origin, fwd, 1.1f, p, out hit, out end) || RaycastWorld(origin, fwd, 1.1f, p, out end))
            {
                Vector3 v = p.Velocity;
                p.Velocity = new Vector3(v.X * 0.2f, v.Y * 0.2f, 3.0f);
            }
        }

        // ---- ragdoll rules (uses the original thresholds) --------------------------
        void TickRagdollRules(Ped p, GoatDef g, float dt)
        {
            if (Goat.Special == Special.Thrust) { if (!p.IsRagdoll && Game.GameTime > ragdollUntil) RagdollFor(p, 600000); return; }

            Vector3 v = p.Velocity;
            float h = new Vector3(v.X, v.Y, 0f).Length();
            bool air = p.IsInAir;

            if (!p.IsRagdoll)
            {
                bool hit = Function.Call<bool>(Hash.HAS_ENTITY_COLLIDED_WITH_ANYTHING, p.Handle);
                if (hit && prevHSpeed > g.RagdollCollisionSpeed && h < prevHSpeed * 0.4f)
                    RagdollFor(p, 2500);

                if (wasInAir && !air && lastVz < -g.RagdollLandSpeed)
                    RagdollFor(p, 2500);
            }
            prevHSpeed = h; lastVz = v.Z; wasInAir = air;
        }

        void RagdollFor(Ped p, int ms)
        {
            Function.Call(Hash.SET_PED_TO_RAGDOLL, p.Handle, ms, ms, 0, false, false, false);
            ragdollUntil = Game.GameTime + ms;
        }

        // ---- attacks ---------------------------------------------------------------
        void Headbutt()
        {
            Ped p = Game.Player.Character;
            GoatDef g = Goat;
            if (p.IsRagdoll || p.IsInVehicle()) return;
            Vector3 fwd = p.ForwardVector;
            p.Velocity = p.Velocity + fwd * 4f;
            HitCone(p, fwd, g.HornRange, g.HornDamage);
        }

        void Kick()
        {
            Ped p = Game.Player.Character;
            GoatDef g = Goat;
            if (p.IsRagdoll || p.IsInVehicle()) return;
            Vector3 back = -p.ForwardVector;
            HitCone(p, back, g.KickRange, g.KickDamage);
        }

        void HitCone(Ped p, Vector3 dir, float range, float damage)
        {
            Vector3 centre = p.Position + dir * (0.5f + range);
            float radius = range + 0.8f;
            Vector3 push = dir; push.Z += 0.35f; push.Normalize();
            float impulse = damage * 0.035f * forceScale;

            foreach (Ped o in World.GetNearbyPeds(centre, radius))
            {
                if (o == null || o.Handle == p.Handle || o.IsDead) continue;
                if (carried != null && o.Handle == carried.Handle) continue;
                RagdollFor(o, 3000);
                o.Velocity = o.Velocity + push * impulse;
                o.ApplyDamage((int)(damage * damageScale));
            }
            foreach (Vehicle v in World.GetNearbyVehicles(centre, radius + 1.5f))
            {
                if (v == null || v.Handle == p.Handle) continue;
                if (carried != null && v.Handle == carried.Handle) continue;
                v.Velocity = v.Velocity + push * (impulse * 0.35f);
                Function.Call(Hash.SET_VEHICLE_DAMAGE, v.Handle, 0f, 1.5f, 0f, damage * 0.1f, 300f, true);
            }
            foreach (Prop o in World.GetNearbyProps(centre, radius))
            {
                if (o == null) continue;
                if (carried != null && o.Handle == carried.Handle) continue;
                o.Velocity = o.Velocity + push * (impulse * 0.8f);
            }
        }

        // ---- lick / carry ----------------------------------------------------------
        void Lick()
        {
            Ped p = Game.Player.Character;
            if (p.IsRagdoll || p.IsInVehicle()) return;

            if (carried != null) { DropCarried(true); return; }

            GoatDef g = Goat;
            Vector3 origin = p.Position + new Vector3(0f, 0f, 0.4f);
            Vector3 dir = p.ForwardVector;

            // camel: lick water to fill the hump
            if (g.Special == Special.Water && (p.IsInWater || p.IsSwimming || IsWaterAhead(p, g.BiteRange)))
            {
                water = Math.Min(60f, water + 20f);
                Notify("Water: " + (int)water);
                return;
            }

            Entity e; Vector3 end;
            if (Raycast(origin, dir, g.BiteRange + 0.6f, p, out e, out end) && e != null && e.Exists())
            {
                GrabEntity(p, e);
            }
        }

        bool IsWaterAhead(Ped p, float range)
        {
            Vector3 a = p.Position + p.ForwardVector * (range + 0.3f);
            float z;
            OutputArgument oz = new OutputArgument();
            bool found = Function.Call<bool>(Hash.GET_WATER_HEIGHT, a.X, a.Y, a.Z + 1f, oz);
            if (!found) return false;
            z = oz.GetResult<float>();
            return Math.Abs(z - p.Position.Z) < 3f;
        }

        void GrabEntity(Ped p, Entity e)
        {
            Vector3 offset;
            if (e is Vehicle) offset = new Vector3(0f, 3.4f, 0.3f);
            else if (e is Ped) offset = new Vector3(0f, 1.1f, 0.2f);
            else offset = new Vector3(0f, 1.3f, 0.4f);

            Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, e.Handle, p.Handle, 0,
                offset.X, offset.Y, offset.Z, 0f, 0f, 0f, false, false, false, false, 2, true);
            carried = e;
            Notify("Got it. Press Lick again to drop.");
        }

        void DropCarried(bool throwIt)
        {
            if (carried == null) return;
            Entity c = carried; carried = null;
            if (!c.Exists()) return;
            Function.Call(Hash.DETACH_ENTITY, c.Handle, true, true);
            if (throwIt)
            {
                Ped p = Game.Player.Character;
                c.Velocity = p.Velocity + p.ForwardVector * 3f;
            }
        }

        void TickCarried(Ped p)
        {
            if (carried != null && !carried.Exists()) carried = null;
        }

        // ---- special ability -------------------------------------------------------
        void SpecialPressed()
        {
            Ped p = Game.Player.Character;
            GoatDef g = Goat;
            switch (g.Special)
            {
                case Special.AngelHover:
                    if (p.IsInAir && Game.GameTime >= hoverReadyAt)
                    {
                        Vector3 v = p.Velocity;
                        if (v.Z < 0f) p.Velocity = new Vector3(v.X, v.Y, 0f);
                        hoverReadyAt = Game.GameTime + 230;
                    }
                    break;
                case Special.Attract:
                    fieldOn = !fieldOn; Notify(fieldOn ? "Demonic pull ON" : "Demonic pull OFF");
                    break;
                case Special.Thrust:
                    thrustOn = !thrustOn; Notify(thrustOn ? "Thrust ON" : "Thrust OFF");
                    break;
                case Special.Flight:
                    ToggleMindControl(p);
                    break;
                case Special.Water:
                    StartSpray(p);
                    break;
            }
        }

        void SpecialReleased()
        {
            if (Goat.Special == Special.Water) StopSpray();
        }

        void TickSpecial(Ped p, GoatDef g, float dt)
        {
            switch (g.Special)
            {
                case Special.AngelHover:
                    // "hold in the air" also works when held
                    if (Held(keySpecial) && p.IsInAir && Game.GameTime >= hoverReadyAt)
                    {
                        Vector3 v = p.Velocity;
                        if (v.Z < 0f) p.Velocity = new Vector3(v.X, v.Y, 0f);
                        hoverReadyAt = Game.GameTime + 230;
                    }
                    break;
                case Special.Attract:
                    if (fieldOn) ApplyField(p, 5.14f, 25f, FieldMode.Pull, dt);
                    break;
                case Special.Thrust:
                    if (thrustOn) ApplyThrust(p, 25f, 45f, dt);
                    break;
                case Special.Water:
                    TickWater(p, dt);
                    break;
            }
        }

        // ---- fields (devil / tornado / repulsive) ----------------------------------
        enum FieldMode { Pull, Push, Swirl }

        void ApplyField(Ped p, float radius, float strength, FieldMode mode, float dt)
        {
            Vector3 c = p.Position;
            foreach (Ped o in World.GetNearbyPeds(c, radius)) { if (o.Handle != p.Handle && !o.IsDead) { if (!o.IsRagdoll) RagdollFor(o, 1500); FieldPush(o, c, radius, strength, mode, dt); } }
            foreach (Vehicle v in World.GetNearbyVehicles(c, radius)) { if (carried != null && v.Handle == carried.Handle) continue; FieldPush(v, c, radius, strength * 0.6f, mode, dt); }
            foreach (Prop o in World.GetNearbyProps(c, radius)) { if (carried != null && o.Handle == carried.Handle) continue; FieldPush(o, c, radius, strength, mode, dt); }
        }

        void FieldPush(Entity e, Vector3 centre, float radius, float strength, FieldMode mode, float dt)
        {
            Vector3 rel = e.Position - centre;
            float d = rel.Length();
            if (d < 0.05f) return;
            Vector3 u = rel / d;
            float fall = Math.Max(0.1f, 1f - d / radius);
            Vector3 a;
            switch (mode)
            {
                case FieldMode.Push: a = u * strength * fall; break;
                case FieldMode.Swirl:
                    Vector3 tangent = Vector3.Cross(Vector3.WorldUp, u);
                    a = (-u * (strength * 0.5f) + tangent * strength + Vector3.WorldUp * (strength * 0.3f)) * fall;
                    break;
                default: a = -u * strength * fall; break;
            }
            e.Velocity = e.Velocity + a * dt * forceScale;
        }

        void ApplyThrust(Ped p, float accel, float maxSpeed, float dt)
        {
            Vector3 dir = CamForward3D();
            Vector3 v = p.Velocity + dir * accel * dt;
            float s = v.Length();
            if (s > maxSpeed) v = v * (maxSpeed / s);
            p.Velocity = v;
            if (!p.IsRagdoll) RagdollFor(p, 600000);
        }

        // ---- camel water jet -------------------------------------------------------
        void StartSpray(Ped p)
        {
            if (water <= 0f) { Notify("Hump is empty - lick some water (Lick key near water)."); return; }
            spraying = true;
        }

        void StopSpray()
        {
            spraying = false;
            if (sprayFx != 0)
            {
                try { Function.Call((Hash)0x8F75998877616996, sprayFx, false); } catch { }
                sprayFx = 0;
            }
        }

        void TickWater(Ped p, float dt)
        {
            if (!spraying) return;
            if (water <= 0f) { StopSpray(); return; }
            water = Math.Max(0f, water - 10f * dt);

            if (sprayFx == 0) StartSprayFx(p);

            Vector3 dir = p.ForwardVector;
            Vector3 centre = p.Position + dir * 4f + new Vector3(0f, 0f, 0.6f);
            Function.Call(Hash.STOP_FIRE_IN_RANGE, centre.X, centre.Y, centre.Z, 4f);
            foreach (Ped o in World.GetNearbyPeds(centre, 3.5f))
            {
                if (o.Handle == p.Handle || o.IsDead) continue;
                if (!o.IsRagdoll) RagdollFor(o, 1200);
                o.Velocity = o.Velocity + dir * 14f * dt * forceScale;
            }
            foreach (Prop o in World.GetNearbyProps(centre, 3.5f)) o.Velocity = o.Velocity + dir * 10f * dt * forceScale;
        }

        void StartSprayFx(Ped p)
        {
            try
            {
                if (!sprayAssetLoaded)
                {
                    Function.Call((Hash)0xB80D8756B4668AB6, "core");           // REQUEST_NAMED_PTFX_ASSET
                    int t = 0;
                    while (!Function.Call<bool>((Hash)0x8702416E512EC454, "core") && t++ < 100) Script.Wait(10);
                    sprayAssetLoaded = true;
                }
                Function.Call((Hash)0x6C38AF3693A69A91, "core");               // USE_PARTICLE_FX_ASSET
                sprayFx = Function.Call<int>((Hash)0x1AE42C1660FD6517,         // START_PARTICLE_FX_LOOPED_ON_ENTITY
                    "water_cannon_jet", p.Handle, 0f, 0.6f, 0.5f, 0f, 0f, 0f, 1.0f, false, false, false);
            }
            catch { sprayFx = 0; }
        }

        // ---- flamingoat mind control -----------------------------------------------
        void ToggleMindControl(Ped p)
        {
            if (controlledPed == null)
            {
                Ped target = carried as Ped;
                if (target == null) { Notify("Lick a person first, then press Special."); return; }
                Entity c = carried; DropCarried(false);
                controlledPed = target;
                controlledModel = target.Model;
                Vector3 pos = p.Position;
                target.IsVisible = false;
                target.IsCollisionEnabled = false;
                target.IsPositionFrozen = true;
                Model goatModel = new Model(Goat.Model);
                if (!(goatModel.IsInCdImage && goatModel.IsValid)) goatModel = new Model(fallbackModel);
                if (controlledModel.Request(3000)) { Game.Player.ChangeModel(controlledModel); }
                Game.Player.IsInvincible = godMode;
                Notify("Controlling " + "person. Special again to release.");
            }
            else
            {
                ReleaseMindControl();
            }
        }

        void ReleaseMindControl()
        {
            Ped current = Game.Player.Character;
            Vector3 pos = current.Position;
            Ped t = controlledPed; controlledPed = null;
            ApplyGoatModel();
            Ped p = Game.Player.Character;
            p.Position = pos + new Vector3(0f, 0f, 0.5f);
            if (t != null && t.Exists())
            {
                t.IsPositionFrozen = false;
                t.IsCollisionEnabled = true;
                t.IsVisible = true;
                t.Position = pos + new Vector3(1.5f, 0f, 0.2f);
            }
        }

        // ---- shopping goat ---------------------------------------------------------
        void SpawnCart(Ped p)
        {
            Model vm = new Model(Goat.VehicleModel);
            if (!(vm.IsInCdImage && vm.IsValid) || !vm.Request(3000)) return;
            Vehicle v = World.CreateVehicle(vm, p.Position + p.ForwardVector * 3f, p.Heading);
            if (v == null) return;
            v.PlaceOnGround();
            p.SetIntoVehicle(v, VehicleSeat.Driver);
            cartVehicle = v;
            vm.MarkAsNoLongerNeeded();
        }

        void TickCart(Ped p)
        {
            if (cartVehicle == null || !cartVehicle.Exists())
            {
                if (!p.IsInVehicle()) SpawnCart(p);
            }
        }

        // ---- radio station: "Chilled Goat" ------------------------------------------
        // GTA's radio wheel cannot be extended from a script, so this behaves like a station that
        // you tune with one key. In "Vehicle" mode it only plays while you are in a vehicle and
        // pauses when you get out (like a real station you leave). In "Always" mode it plays everywhere.
        void ResolveRadioFile()
        {
            radioPath = "";
            try
            {
                if (musicFolder.Length == 0 || !Directory.Exists(musicFolder)) return;
                foreach (string f in Directory.GetFiles(musicFolder))
                    if (Path.GetFileName(f).StartsWith(radioFile, StringComparison.OrdinalIgnoreCase)) { radioPath = f; return; }
            }
            catch { }
        }

        void ToggleRadio()
        {
            if (!radioEnabled) return;
            if (radioPath.Length == 0) { Notify("Radio: song not found. Check [Music] Folder and [Radio] Track in the ini."); return; }
            radioTunedIn = !radioTunedIn;
            Notify(radioTunedIn ? "Radio: " + radioName + (radioAlways ? " (always on)" : "") : "Radio: " + radioName + " off");
            if (!radioTunedIn) StopRadioDevice();
        }

        void StopRadioDevice()
        {
            mciSendString("stop gsradio", null, 0, IntPtr.Zero);
            mciSendString("close gsradio", null, 0, IntPtr.Zero);
            radioOpen = false; radioPaused = false;
        }

        void TickRadio()
        {
            if (!radioEnabled || radioPath.Length == 0) return;
            if (Game.GameTime < radioCheckAt) return;
            radioCheckAt = Game.GameTime + 500;

            Ped p = Game.Player.Character;
            bool inVehicle = p != null && p.Exists() && p.IsInVehicle() && p.CurrentVehicle != null;

            // Default station: getting into a vehicle tunes the dial to Chilled Goat again
            if (inVehicle && !radioWasInVehicle && radioDefault)
            {
                radioTunedIn = true;
                radioGraceUntil = Game.GameTime + 2000;        // let the car's own radio settle before we look at it
                Notify(radioName);
            }
            radioWasInVehicle = inVehicle;

            // If you turn the real radio wheel to another station, you tuned away from Chilled Goat
            if (radioTunedIn && inVehicle && Game.GameTime > radioGraceUntil)
            {
                string station = Function.Call<string>(Hash.GET_PLAYER_RADIO_STATION_NAME);
                if (!string.IsNullOrEmpty(station) && station != "OFF")
                {
                    radioTunedIn = false;
                    Notify(station.Replace("RADIO_", "").Replace("_", " "));
                }
            }

            if (!radioTunedIn) { if (radioOpen) StopRadioDevice(); return; }
            bool shouldPlay = radioAlways || (p != null && p.Exists() && p.IsInVehicle());

            if (!radioOpen)
            {
                mciSendString("open \"" + radioPath + "\" type mpegvideo alias gsradio", null, 0, IntPtr.Zero);
                mciSendString("setaudio gsradio volume to " + Math.Max(0, Math.Min(1000, radioVolume)), null, 0, IntPtr.Zero);
                radioOpen = true;
                radioPaused = true;                        // opened but not started yet
                if (shouldPlay) { mciSendString("play gsradio", null, 0, IntPtr.Zero); radioPaused = false; }
                else mciSendString("seek gsradio to start", null, 0, IntPtr.Zero);
            }

            if (shouldPlay && radioPaused)
            {
                mciSendString("play gsradio", null, 0, IntPtr.Zero);       // resume where the "broadcast" is
                radioPaused = false;
            }
            else if (!shouldPlay && !radioPaused)
            {
                mciSendString("pause gsradio", null, 0, IntPtr.Zero);
                radioPaused = true;
            }

            if (!radioPaused)
            {
                // keep the car's own radio quiet and loop the song forever
                if (p != null && p.IsInVehicle() && p.CurrentVehicle != null)
                    Function.Call(Hash.SET_VEH_RADIO_STATION, p.CurrentVehicle.Handle, "OFF");
                StringBuilder sb = new StringBuilder(64);
                mciSendString("status gsradio mode", sb, 64, IntPtr.Zero);
                if (sb.ToString().Trim().ToLowerInvariant() == "stopped")
                {
                    mciSendString("seek gsradio to start", null, 0, IntPtr.Zero);
                    mciSendString("play gsradio", null, 0, IntPtr.Zero);
                }
            }
        }

        // ---- soundtrack ------------------------------------------------------------
        void LoadTracks()
        {
            tracks.Clear();
            try
            {
                if (musicFolder.Length > 0 && Directory.Exists(musicFolder))
                {
                    foreach (string f in Directory.GetFiles(musicFolder))
                    {
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext == ".mp3" || ext == ".wav" || ext == ".wma" || ext == ".m4a") tracks.Add(f);
                    }
                    tracks.Sort(StringComparer.OrdinalIgnoreCase);
                    if (musicPlaylist.Trim().Length > 0)
                    {
                        // keep only the listed songs, in the listed order (name with or without extension)
                        List<string> picked = new List<string>();
                        foreach (string want in musicPlaylist.Split(','))
                        {
                            string w = want.Trim();
                            if (w.Length == 0) continue;
                            foreach (string t in tracks)
                            {
                                string n = Path.GetFileName(t);
                                if (n.StartsWith(w, StringComparison.OrdinalIgnoreCase) && !picked.Contains(t)) { picked.Add(t); break; }
                            }
                        }
                        tracks.Clear(); tracks.AddRange(picked);
                    }
                }
            }
            catch { }
        }

        void StartMusic()
        {
            if (tracks.Count == 0) return;
            if (trackIndex < 0) trackIndex = 0;
            PlayTrack(trackIndex);
        }

        void PlayTrack(int i)
        {
            StopMusicDevice();
            if (tracks.Count == 0) return;
            trackIndex = ((i % tracks.Count) + tracks.Count) % tracks.Count;
            string file = tracks[trackIndex];
            mciSendString("open \"" + file + "\" type mpegvideo alias gsmusic", null, 0, IntPtr.Zero);
            mciSendString("setaudio gsmusic volume to " + Math.Max(0, Math.Min(1000, musicVolume)), null, 0, IntPtr.Zero);
            mciSendString("play gsmusic", null, 0, IntPtr.Zero);
            musicPlaying = true;
            Notify("Now playing: " + Path.GetFileNameWithoutExtension(file));
        }

        void StopMusicDevice()
        {
            mciSendString("stop gsmusic", null, 0, IntPtr.Zero);
            mciSendString("close gsmusic", null, 0, IntPtr.Zero);
        }

        void StopMusic() { StopMusicDevice(); musicPlaying = false; }

        void ToggleMusic()
        {
            if (tracks.Count == 0) { Notify("No music found. Set [Music] Folder in the ini."); return; }
            musicEnabled = !musicEnabled;
            if (musicEnabled) StartMusic(); else StopMusic();
            Notify(musicEnabled ? "Music on" : "Music off");
        }

        void NextTrack()
        {
            if (tracks.Count == 0) return;
            musicEnabled = true;
            PlayTrack(trackIndex + 1);
        }

        void TickMusic()
        {
            if (!musicPlaying || Game.GameTime < musicCheckAt) return;
            musicCheckAt = Game.GameTime + 1000;
            StringBuilder sb = new StringBuilder(64);
            mciSendString("status gsmusic mode", sb, 64, IntPtr.Zero);
            if (sb.ToString().Trim().ToLowerInvariant() == "stopped") PlayTrack(trackIndex + 1);   // next song
        }

        // ---- baa -------------------------------------------------------------------
        void Baa()
        {
            if (baaSound != null) { try { baaSound.Play(); } catch { } }
            else Notify("Baa! (drop a baa.wav into scripts/GoatTheftAuto/ for sound)");
            Ped p = Game.Player.Character;
            foreach (Ped o in World.GetNearbyPeds(p.Position, 6f))
            {
                if (o.Handle == p.Handle || o.IsDead) continue;
                Function.Call(Hash.TASK_SMART_FLEE_PED, o.Handle, p.Handle, 40f, 4000, false, false);
            }
        }

        // ---- accessories -----------------------------------------------------------
        void AttachAccessoryProp()
        {
            ClearAccessoryProp();
            AccessoryDef a = Acc;
            if (string.IsNullOrEmpty(a.PropModel)) return;
            Model m = new Model(a.PropModel);
            if (!(m.IsInCdImage && m.IsValid) || !m.Request(3000)) return;
            Ped p = Game.Player.Character;
            Prop pr = World.CreateProp(m, p.Position, false, false);
            if (pr == null) return;
            int bone = Function.Call<int>(Hash.GET_PED_BONE_INDEX, p.Handle, a.Bone == "spine" ? SkelSpine : SkelHead);
            Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, pr.Handle, p.Handle, bone, 0f, 0f, 0f, 0f, 0f, 0f, false, false, false, false, 2, true);
            accessoryProp = pr;
            m.MarkAsNoLongerNeeded();
        }

        void ClearAccessoryProp()
        {
            if (accessoryProp != null && accessoryProp.Exists()) accessoryProp.Delete();
            accessoryProp = null;
        }

        void ClearAccessory()
        {
            ClearAccessoryProp();
            StopGrapple();
            accToggle = false; streakCharge = 0f; gliding = false;
        }

        void AccessoryPressed()
        {
            Ped p = Game.Player.Character;
            switch (Acc.Kind)
            {
                case AccessoryKind.JetPack:
                case AccessoryKind.Tornado:
                case AccessoryKind.Repulsive:
                case AccessoryKind.Demonic:
                case AccessoryKind.HangGlider:
                    accToggle = !accToggle;
                    Notify(Acc.Name + (accToggle ? " ON" : " OFF"));
                    if (Acc.Kind == AccessoryKind.JetPack && accToggle) RagdollFor(p, 600000);
                    break;
                case AccessoryKind.GrapplingHook:
                    if (grappling) StopGrapple(); else ShootGrapple(p);
                    break;
                case AccessoryKind.RocketBoots:
                    if (Game.GameTime >= boostReadyAt)
                    {
                        Vector3 f = CamForward();
                        p.Velocity = new Vector3(f.X * 8f, f.Y * 8f, 13f);
                        boostReadyAt = Game.GameTime + 1000;
                    }
                    break;
                case AccessoryKind.BlueStreak:
                    streakCharge = 0.01f;
                    break;
            }
        }

        void AccessoryReleased()
        {
            Ped p = Game.Player.Character;
            if (Acc.Kind == AccessoryKind.BlueStreak && streakCharge > 0f)
            {
                float speed = 12f + 28f * Math.Min(1f, streakCharge / 2f);
                Vector3 f = p.ForwardVector;
                p.Velocity = new Vector3(f.X * speed, f.Y * speed, p.Velocity.Z);
                streakCharge = 0f;
            }
        }

        void TickAccessory(Ped p, GoatDef g, float dt)
        {
            switch (Acc.Kind)
            {
                case AccessoryKind.JetPack:
                    if (accToggle) ApplyThrust(p, 18f, 35f, dt);
                    break;
                case AccessoryKind.Tornado:
                    if (accToggle) ApplyField(p, 7.68f, 22f, FieldMode.Swirl, dt);
                    break;
                case AccessoryKind.Repulsive:
                    if (accToggle) ApplyField(p, 5.12f, 28f, FieldMode.Push, dt);
                    break;
                case AccessoryKind.Demonic:
                    if (accToggle) ApplyField(p, 5.14f, 25f, FieldMode.Pull, dt);
                    break;
                case AccessoryKind.HangGlider:
                    if (accToggle && p.IsInAir && !p.IsRagdoll)
                    {
                        Vector3 f = p.ForwardVector;
                        Vector3 v = p.Velocity;
                        Vector3 target = new Vector3(f.X * 14f, f.Y * 14f, -2.5f);
                        p.Velocity = v + (target - v) * Math.Min(1f, 2f * dt);
                    }
                    break;
                case AccessoryKind.BlueStreak:
                    if (streakCharge > 0f && Held(keyAccessory)) streakCharge += dt;
                    break;
                case AccessoryKind.GrapplingHook:
                    TickGrapple(p, dt);
                    break;
            }
        }

        void ShootGrapple(Ped p)
        {
            Vector3 origin = GameplayCamera.Position;
            Vector3 dir = CamForward3D();
            Entity e; Vector3 end;
            if (RaycastWorld(origin, dir, 100f, p, out end) || Raycast(origin, dir, 100f, p, out e, out end))
            {
                grapplePoint = end;
                grappling = true;
                ropeLength = Math.Max(3f, (end - p.Position).Length() * 0.85f);   // rope starts a bit shorter than the shot: you get yanked up, then swing
                if (!p.IsRagdoll) RagdollFor(p, 800);
            }
            else Notify("Nothing to hook.");
        }

        void StopGrapple() { grappling = false; }

        // Rope swing: a rigid rope (pendulum constraint) instead of a straight pull.
        // The goat falls under gravity, the rope removes any velocity that would stretch it,
        // WASD adds air control, hold Jump to reel in, hold Sprint to let rope out.
        void TickGrapple(Ped p, float dt)
        {
            if (!grappling) return;
            if (!p.IsRagdoll) RagdollFor(p, 400);               // stay limp while swinging
            Vector3 to = grapplePoint - p.Position;
            float d = to.Length();
            if (d < 0.5f) { StopGrapple(); return; }
            Vector3 dir = to / d;                                // from goat toward the anchor

            if (Game.IsControlPressed(Control.Jump)) ropeLength = Math.Max(2.5f, ropeLength - 9f * dt);
            if (Game.IsControlPressed(Control.Sprint)) ropeLength = Math.Min(80f, ropeLength + 9f * dt);

            Vector3 v = p.Velocity;
            // air control from the camera-relative stick
            Vector3 fwd = CamForward3D(); fwd.Z = 0f;
            float fl = fwd.Length();
            if (fl > 0.01f)
            {
                fwd = fwd / fl;
                Vector3 right = new Vector3(fwd.Y, -fwd.X, 0f);
                float f = -Game.GetControlValueNormalized(Control.MoveUpDown);
                float r = Game.GetControlValueNormalized(Control.MoveLeftRight);
                v = v + (fwd * f + right * r) * (14f * dt);
            }

            if (d > ropeLength)
            {
                // taut rope: cancel the outward part of the velocity and spring the goat back in
                float outward = -(v.X * dir.X + v.Y * dir.Y + v.Z * dir.Z);   // >0 when moving away from the anchor
                if (outward > 0f) v = v + dir * outward;
                v = v + dir * ((d - ropeLength) * 6f);
            }
            float s = v.Length();
            if (s > 45f) v = v * (45f / s);
            p.Velocity = v;
            Vector3 hand = p.Position + new Vector3(0f, 0f, 0.6f);
            Function.Call(Hash.DRAW_LINE, hand.X, hand.Y, hand.Z, grapplePoint.X, grapplePoint.Y, grapplePoint.Z, 200, 180, 120, 255);
        }

        // ---- raycasts (native, version independent) --------------------------------
        bool Raycast(Vector3 origin, Vector3 dir, float range, Entity ignore, out Entity hitEntity, out Vector3 end)
        {
            hitEntity = null; end = origin + dir * range;
            Vector3 to = origin + dir * range;
            int handle = Function.Call<int>(Hash.START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE,
                origin.X, origin.Y, origin.Z, to.X, to.Y, to.Z, 2 | 4 | 8 | 16, ignore != null ? ignore.Handle : 0, 7);
            OutputArgument hit = new OutputArgument(), endc = new OutputArgument(), norm = new OutputArgument(), ent = new OutputArgument();
            Function.Call<int>(Hash.GET_SHAPE_TEST_RESULT, handle, hit, endc, norm, ent);
            if (!hit.GetResult<bool>()) return false;
            end = endc.GetResult<Vector3>();
            int h = ent.GetResult<int>();
            if (h != 0) hitEntity = Entity.FromHandle(h);
            return hitEntity != null;
        }

        bool RaycastWorld(Vector3 origin, Vector3 dir, float range, Entity ignore, out Vector3 end)
        {
            end = origin + dir * range;
            Vector3 to = origin + dir * range;
            int handle = Function.Call<int>(Hash.START_EXPENSIVE_SYNCHRONOUS_SHAPE_TEST_LOS_PROBE,
                origin.X, origin.Y, origin.Z, to.X, to.Y, to.Z, 1 | 16, ignore != null ? ignore.Handle : 0, 7);
            OutputArgument hit = new OutputArgument(), endc = new OutputArgument(), norm = new OutputArgument(), ent = new OutputArgument();
            Function.Call<int>(Hash.GET_SHAPE_TEST_RESULT, handle, hit, endc, norm, ent);
            if (!hit.GetResult<bool>()) return false;
            end = endc.GetResult<Vector3>();
            return true;
        }

        // ---- util ------------------------------------------------------------------
        static void Notify(string text)
        {
            GTA.UI.Screen.ShowSubtitle("~y~Goat~s~: " + text, 3500);
        }
    }

    // ===================== data tables =====================

    public enum Special
    {
        None,
        AngelHover,     // Angel/Gloria: press in air to stop falling (0.23 s cooldown)
        Attract,        // Devil: toggle pull field (radius 5.14 m)
        Thrust,         // Sperm goat: toggle rocket thrust while ragdolled
        Flight,         // Flamingoat: wings, glide, flap
        Water,          // Camel: drink, then spray water jet
        WallWalk,       // Dolphwheel: wall-walk
        Vehicle         // Shopping goat: rides a cart
    }

    public enum AccessoryKind
    {
        None,
        JetPack,        // thrust toggle (ragdoll + rocket)
        GrapplingHook,  // rope pull
        Tornado,        // swirl field
        Repulsive,      // push field
        Demonic,        // pull field
        BlueStreak,     // charge-and-dash (approx)
        RocketBoots,    // boost jump (approx)
        HangGlider,     // glide (approx)
        RollerSkates,   // fast, slippery (approx)
        HatCrown, HatWizard, HatSombrero, HatHornedWood, HatGeneral, HatGlasses, HatSwagGlasses, HatPlain
    }

    public class GoatDef
    {
        public string Id;
        public string Name;
        public string Model;            // addon ped spawn name
        public string VehicleModel = "caddy";

        // Movement (metres per second)
        public float Walk = 5.0f;
        public float Sprint = 12.0f;
        public float JumpZ = 650f;      // uu/s, same field as the original
        public float Gravity = 1.0f;    // CustomGravityScaling
        public float Mass = 45f;

        // Abilities (metres / damage points as in the original)
        public float HornRange = 0.6f;
        public float HornDamage = 400f;
        public float KickRange = 0.7f;
        public float KickDamage = 800f;
        public float BiteRange = 1.5f;  // tongue / lick reach

        // Ragdoll thresholds (m/s)
        public float RagdollCollisionSpeed = 8.5f;
        public float RagdollLandSpeed = 17.8f;

        // Flying (Flamingoat)
        public bool CanFly = false;
        public float FlapZ = 7.0f;
        public float FlapCooldown = 0.5f;
        public float AirSpeed = 15.0f;

        public Special Special = Special.None;
        public string Note = "";
    }

    public class AccessoryDef
    {
        public AccessoryKind Kind;
        public string Name;
        public string PropModel;        // optional addon prop (hats, jetpack ...)
        public string Bone = "head";
        public string Note = "";
    }

    public static class GoatTable
    {
        const float DefaultRagColl = 8.5f;

        static GoatDef G(string id, string name, string model)
        {
            return new GoatDef { Id = id, Name = name, Model = model };
        }

        static void SetRag(GoatDef g, float landUU)
        {
            g.RagdollCollisionSpeed = g.Walk + (g.Sprint - g.Walk) * 0.5f;
            g.RagdollLandSpeed = landUU / 100f;
        }

        public static List<GoatDef> Goats()
        {
            var list = new List<GoatDef>();

            // Base goat (GGGoat defaults: JumpZ 650, walk 500, sprint 1200, mass 45)
            var goat = G("goat", "Goat", "gs_goat");
            goat.Note = "Plain goat: headbutt, kick, lick.";
            list.Add(goat);

            // Gloria = Angel goat. GGAngelGoatComponents: gravity -0.4, JumpZ +50.
            var angel = G("angel", "Angel Goat", "gs_gloria");
            angel.Gravity = 1.0f - 0.4f;
            angel.JumpZ = 650f + 50f;
            angel.Special = Special.AngelHover;
            angel.Note = "Floaty. Press Special in the air to stop falling.";
            list.Add(angel);

            // Devil goat. GGDevilGoatComponents: speed x1.5, radial force -1000 radius 514.
            var devil = G("devil", "Devil Goat", "gs_devil");
            devil.Walk *= 1.5f; devil.Sprint *= 1.5f;
            devil.Special = Special.Attract;
            devil.Note = "1.5x speed. Special toggles a pull field around you.";
            list.Add(devil);

            // Feather goat (GGMutatorComponentFeatherGoat): walk 500 sprint 1600 grav 0.7 JumpZ 900
            var feather = G("feather", "Feather Goat", "gs_feather");
            feather.Walk = 5f; feather.Sprint = 16f; feather.Gravity = 0.7f; feather.JumpZ = 900f;
            feather.HornRange = 0.6f; feather.HornDamage = 400f; feather.KickRange = 0.7f; feather.KickDamage = 800f;
            SetRag(feather, 2300f);
            list.Add(feather);

            var fa = G("feather_angel", "Feather Goat (Angel)", "gs_feather");
            CopyStats(feather, fa);
            fa.Gravity = feather.Gravity - 0.4f; fa.JumpZ += 50f; fa.Special = Special.AngelHover;
            list.Add(fa);

            var fd = G("feather_devil", "Feather Goat (Devil)", "gs_feather");
            CopyStats(feather, fd);
            fd.Walk *= 1.5f; fd.Sprint *= 1.5f; fd.Special = Special.Attract;
            list.Add(fd);

            // Tall goat: walk 200 sprint 1400, ragdoll land 1780, bite range 170
            var tall = G("tall", "Tall Goat", "gs_tall");
            tall.Walk = 2f; tall.Sprint = 14f; tall.BiteRange = 1.7f;
            SetRag(tall, 1780f);
            list.Add(tall);

            var ta = G("tall_angel", "Tall Goat (Angel)", "gs_tall");
            CopyStats(tall, ta); ta.Gravity = 0.6f; ta.JumpZ += 50f; ta.Special = Special.AngelHover;
            list.Add(ta);

            var td = G("tall_devil", "Tall Goat (Devil)", "gs_tall");
            CopyStats(tall, td); td.Walk *= 1.5f; td.Sprint *= 1.5f; td.Special = Special.Attract;
            list.Add(td);

            // Classy goat: walk 100 strafe 130 sprint 2000, horn/kick 300 damage range 80
            var classy = G("classy", "Classy Goat", "gs_classy");
            classy.Walk = 1f; classy.Sprint = 20f;
            classy.HornRange = 0.8f; classy.HornDamage = 300f; classy.KickRange = 0.8f; classy.KickDamage = 300f;
            SetRag(classy, 1780f);
            list.Add(classy);

            // G2 goat: horn/kick 300 / range 80
            var g2 = G("g2", "G2 Goat", "gs_g2");
            g2.HornRange = 0.8f; g2.HornDamage = 300f; g2.KickRange = 0.8f; g2.KickDamage = 300f;
            list.Add(g2);

            // Space goat (HeadBobber): bite range 170
            var space = G("space", "Space Goat", "gs_space");
            space.HornRange = 0.6f; space.HornDamage = 400f; space.BiteRange = 1.7f;
            SetRag(space, 1780f);
            list.Add(space);

            // Goast: walk 300 strafe 200 sprint 800, bite 100, horn/kick 300
            var goast = G("goast", "Goast", "gs_goast");
            goast.Walk = 3f; goast.Sprint = 8f; goast.BiteRange = 1.0f;
            goast.HornRange = 0.8f; goast.HornDamage = 300f; goast.KickRange = 0.8f; goast.KickDamage = 300f;
            SetRag(goast, 1780f);
            list.Add(goast);

            // Sperm goat: ragdolled all the time, thruster 3000
            var sperm = G("sperm", "Sperm Goat", "gs_sperm");
            sperm.Special = Special.Thrust;
            sperm.Note = "Always ragdolled. Special toggles rocket thrust toward the camera.";
            list.Add(sperm);

            // Shopping goat: spawns a cart and rides it
            var shop = G("shopping", "Shopping Goat", "gs_goat");
            shop.Special = Special.Vehicle;
            shop.VehicleModel = "caddy";
            shop.Note = "Rides a cart (stand-in vehicle 'caddy' unless you add a cart model).";
            list.Add(shop);

            // Flamingoat: walk 170 sprint 800 JumpZ 900 flapZ 700 air speed 1500 flap cd 0.5
            var fl = G("flamingoat", "Flamingoat", "gs_flamingoat");
            fl.Walk = 1.7f; fl.Sprint = 8f; fl.JumpZ = 900f;
            fl.CanFly = true; fl.FlapZ = 7f; fl.FlapCooldown = 0.5f; fl.AirSpeed = 15f;
            fl.HornRange = 1.1f; fl.HornDamage = 400f; fl.KickRange = 0.7f; fl.KickDamage = 800f;
            fl.Special = Special.Flight;
            fl.Note = "Fly: Jump in the air to flap, hold Jump to glide. Lick a car or person, then fly away. Lick a person + Special = take over.";
            list.Add(fl);

            // Camel goat: walk 325, JumpZ 1400, horn 400 kick 800, water jet
            var camel = G("camel", "Camel Goat", "gs_camel");
            camel.Walk = 3.25f; camel.JumpZ = 1400f;
            camel.Special = Special.Water;
            camel.Note = "Lick water to fill the hump, hold Special to spray.";
            list.Add(camel);

            // Dolphwheel: wall walk + roll, walk 500 sprint 1200
            var dw = G("dolphwheel", "Dolphwheel Goat", "gs_dolphwheel");
            dw.Special = Special.WallWalk;
            dw.Note = "Walks up walls when you push into them.";
            list.Add(dw);

            // Handsome goat: JumpZ 700 mass 60 walk 500 sprint 1250 horn range 110
            var hs = G("handsome", "Handsome Goat", "gs_handsome");
            hs.Mass = 60f; hs.JumpZ = 700f; hs.Sprint = 12.5f; hs.HornRange = 1.1f; hs.HornDamage = 400f;
            list.Add(hs);

            // Panzer goat: no ability script was found, so base stats.
            var pz = G("panzer", "Panzer Goat", "gs_panzer");
            pz.Note = "No readable ability code in the install; base stats.";
            list.Add(pz);

            return list;
        }

        static void CopyStats(GoatDef from, GoatDef to)
        {
            to.Walk = from.Walk; to.Sprint = from.Sprint; to.JumpZ = from.JumpZ; to.Gravity = from.Gravity; to.Mass = from.Mass;
            to.HornRange = from.HornRange; to.HornDamage = from.HornDamage;
            to.KickRange = from.KickRange; to.KickDamage = from.KickDamage;
            to.BiteRange = from.BiteRange;
            to.RagdollCollisionSpeed = from.RagdollCollisionSpeed; to.RagdollLandSpeed = from.RagdollLandSpeed;
        }

        public static List<AccessoryDef> Accessories()
        {
            return new List<AccessoryDef>
            {
                new AccessoryDef { Kind = AccessoryKind.None, Name = "None" },
                new AccessoryDef { Kind = AccessoryKind.JetPack, Name = "Jet Pack", PropModel = "gs_jetpack", Bone = "spine", Note = "Toggle: ragdoll + rocket thrust toward the camera." },
                new AccessoryDef { Kind = AccessoryKind.GrapplingHook, Name = "Grappling Hook", Note = "Press to shoot at the crosshair, press again to release." },
                new AccessoryDef { Kind = AccessoryKind.Tornado, Name = "Tornado Mask", PropModel = "gs_hornedwood", Note = "Toggle: swirling pull field (radius 7.7 m)." },
                new AccessoryDef { Kind = AccessoryKind.Repulsive, Name = "Repulsive Goat", Note = "Toggle: push field (radius 5.1 m)." },
                new AccessoryDef { Kind = AccessoryKind.Demonic, Name = "Demonic Horns", PropModel = "gs_hornedwood", Note = "Toggle: pull field (radius 5.1 m)." },
                new AccessoryDef { Kind = AccessoryKind.BlueStreak, Name = "Blue Streak", Note = "approx: hold to charge, release to dash." },
                new AccessoryDef { Kind = AccessoryKind.RocketBoots, Name = "Rocket Boots", Note = "approx: boost jump." },
                new AccessoryDef { Kind = AccessoryKind.HangGlider, Name = "Hang Glider", Note = "approx: toggle glide while airborne." },
                new AccessoryDef { Kind = AccessoryKind.RollerSkates, Name = "Roller Skates", Note = "approx: faster, more slippery." },
                new AccessoryDef { Kind = AccessoryKind.HatCrown, Name = "Crown", PropModel = "gs_crown", Note = "cosmetic" },
                new AccessoryDef { Kind = AccessoryKind.HatWizard, Name = "Wizard Hat", PropModel = "gs_wizrdhat", Note = "cosmetic" },
                new AccessoryDef { Kind = AccessoryKind.HatSombrero, Name = "Sombrero", PropModel = "gs_sombrero", Note = "cosmetic" },
                new AccessoryDef { Kind = AccessoryKind.HatHornedWood, Name = "Horned Helm", PropModel = "gs_hornedwood", Note = "cosmetic" },
                new AccessoryDef { Kind = AccessoryKind.HatGeneral, Name = "General Hat", PropModel = "gs_generalhat", Note = "cosmetic" },
                new AccessoryDef { Kind = AccessoryKind.HatGlasses, Name = "Glasses", PropModel = "gs_glasses", Note = "cosmetic" },
                new AccessoryDef { Kind = AccessoryKind.HatSwagGlasses, Name = "Swag Glasses", PropModel = "gs_swagglasses", Note = "cosmetic" },
                new AccessoryDef { Kind = AccessoryKind.HatPlain, Name = "Hat", PropModel = "gs_hat", Note = "cosmetic" },
            };
        }
    }
}
