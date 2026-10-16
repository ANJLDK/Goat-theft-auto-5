// GoatDriver.cs - "Goat Simulator: PAYDAY" style driving for GTA V (single player).
// ScriptHookVDotNet v3. Drop GoatDriver.cs and GoatDriver.ini into the "scripts" folder.
//
// WHAT IT DOES
//   * Press the vehicle-entry key (F / Enter) near a vehicle: the goat is warped straight into the
//     driver seat. No door animation.
//   * The native human driving pose is suppressed. The goat is rigidly attached to the vehicle's
//     "steeringwheel" bone with a position/rotation offset, pitched forward so the front legs rest
//     on the wheel.
//   * The upper body is driven by a small spring simulation fed by the car's real acceleration, so
//     it sways, leans out, and gets thrown around in turns, braking and crashes.
//   * The driver window rolls down on entry so the lean-out reads as "head out of the window".
//
// HOW THE "FLOP" WORKS (and why it is not a native ragdoll)
//   A ragdolled ped cannot stay attached to a vehicle, and a ped that is attached cannot ragdoll.
//   So the flop is done by re-applying the attachment every frame with spring-driven rotation
//   offsets (pitch / roll / yaw) and a small outward shift. It is cheap and stable. If you want
//   a real ragdoll on big crashes, set CrashRagdoll=true in the ini (the goat is thrown out of
//   the attachment for a moment and put back in the seat afterwards).
//
// TUNING THE ATTACHMENT OFFSETS  (read this first)
//   ATTACH_ENTITY_TO_ENTITY places the ped relative to a vehicle bone, in that BONE'S local axes,
//   not the car's axes. The steeringwheel bone is tilted and its axes differ between vehicles, so
//   no single default fits every car. Use the live tuning keys (TuneMode=true in the ini):
//
//       NumPad 4 / 6   move along bone X      (usually sideways)
//       NumPad 8 / 2   move along bone Y      (usually along the steering column)
//       NumPad 9 / 3   move along bone Z      (usually up / down)
//       Shift + NumPad 4 / 6   roll  (rotate about Y)
//       Shift + NumPad 8 / 2   pitch (rotate about X)
//       Shift + NumPad 9 / 3   yaw   (rotate about Z)
//       NumPad 5               reset to the ini values
//       NumPad 0               print the current values (subtitle + GoatDriver.log) - copy them
//                              into the [Offset] section of GoatDriver.ini
//
//   Tips:
//     - Position offsets are metres. Start by moving the goat until its chest is just behind the
//       wheel, then pitch it until the front legs reach the rim. Changes apply instantly.
//     - BasePitch is the "resting" forward lean. It is added on top of the spring motion.
//     - The sign of the spring motion (which way the goat leans in a turn) depends on the bone's
//       axes. If the goat leans INTO the car instead of out of the window, flip RollSign (and
//       PitchSign if braking throws it backwards).
//     - WindowIndex 0 is the front-left window (the driver side in left-hand-drive cars). For a
//       right-hand-drive vehicle set WindowIndex=1, negate LeanShiftX and flip RollSign.
//
// NOTES / LIMITS
//   * Not tested in game when this was written. The least certain parts are marked "RISK".
//   * Only active while the player's model is in the GoatModels list (so humans drive normally).

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using Control = GTA.Control;

namespace GoatDriver
{
    public class GoatDriverScript : Script
    {
        // ------------------------------------------------------------------ constants
        const int InputEnter = 23;       // INPUT_ENTER (F / Y)
        const int InputVehExit = 75;     // INPUT_VEH_EXIT
        const int WarpOutFlag = 16;      // TASK_LEAVE_VEHICLE flag: leave instantly
        const int PcfWillFlyThroughWindscreen = 32;
        const int PcfPreventAutoShuffleToDriversSeat = 184;

        // ------------------------------------------------------------------ settings
        bool enabled = true;
        bool tuneMode = true;
        float entryRange = 4.0f;               // how close to a vehicle you must be (metres)
        bool stealOccupied = true;             // throw the current driver out
        bool openWindow = true;
        int windowIndex = 0;                   // 0 = front left (driver side in left-hand-drive)
        bool rollWindowUpOnExit = true;
        bool crashRagdoll = false;
        float crashSpeedDrop = 9.0f;           // m/s lost in one frame that counts as a crash
        string wheelBone = "steeringwheel";
        string fallbackBone = "seat_dside_f";  // used when the car has no steeringwheel bone
        string[] goatModels = { "gs_goat", "gs_gloria", "gs_devil", "gs_feather", "gs_tall", "gs_classy",
            "gs_g2", "gs_space", "gs_goast", "gs_sperm", "gs_flamingoat", "gs_camel", "gs_dolphwheel",
            "gs_handsome", "gs_panzer", "a_c_deer" };
        string[] drivingAnims = { "move_driving|", "veh@std@ds@base|idle", "veh@driveby@driver|" };

        // Attachment offset in the steeringwheel bone's local space (see header comment)
        Vector3 posOffset = new Vector3(0.0f, -0.35f, -0.45f);
        Vector3 rotOffset = new Vector3(0.0f, 0.0f, 0.0f);      // x = pitch, y = roll, z = yaw (degrees)
        float basePitch = 25.0f;                                // resting forward lean (degrees)
        float leanShiftX = -0.18f;                              // extra sideways shift at full lean-out (metres)

        // Spring simulation
        float stiffness = 38.0f;     // pulls the pose back toward its rest value
        float damping = 5.5f;        // 0 = bounces forever, high = stiff
        float latGain = 3.2f;        // degrees of roll per m/s^2 of sideways acceleration
        float longGain = 2.4f;       // degrees of pitch per m/s^2 of forward/back acceleration
        float windowLeanDeg = 32.0f; // constant lean toward the open window, scaled by speed
        float leanFullSpeed = 22.0f; // speed (m/s) at which the full window lean is reached
        float wobble = 6.0f;         // random sway amplitude (degrees) at speed
        float rollSign = -1.0f;      // flip if the goat leans the wrong way sideways
        float pitchSign = 1.0f;      // flip if braking throws it the wrong way
        float crashKick = 70.0f;     // spring velocity added on a crash (degrees/s)

        Keys keyTuneReset = Keys.NumPad5, keyTunePrint = Keys.NumPad0;

        // ------------------------------------------------------------------ state
        bool driving;
        Vehicle vehicle;
        int boneIndex = -1;
        Vector3 prevVelocity;
        bool havePrev;
        Spring pitch, roll, yaw;
        readonly Random rng = new Random();
        float time;
        int ragdollReattachAt;
        Vector3 tunePos, tuneRot;
        float tunePitch;
        readonly HashSet<Keys> held = new HashSet<Keys>();
        readonly HashSet<int> goatHashes = new HashSet<int>();
        readonly string logPath;

        struct Spring { public float Angle, Velocity; }

        // ------------------------------------------------------------------ setup
        public GoatDriverScript()
        {
            string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            logPath = Path.Combine(dir, "GoatDriver.log");
            LoadSettings(Path.Combine(dir, "GoatDriver.ini"));
            foreach (string m in goatModels)
            {
                string name = m.Trim();
                if (name.Length > 0) goatHashes.Add(new Model(name).Hash);
            }
            ResetTuning();

            Tick += OnTick;
            KeyDown += (s, e) => { held.Add(e.KeyCode); OnTuneKey(e); };
            KeyUp += (s, e) => held.Remove(e.KeyCode);
            Aborted += (s, e) => { try { StopDriving(false); } catch { } };
            Interval = 0;
        }

        void LoadSettings(string path)
        {
            ScriptSettings s = ScriptSettings.Load(path);
            enabled = s.GetValue("General", "Enabled", enabled);
            tuneMode = s.GetValue("General", "TuneMode", tuneMode);
            entryRange = s.GetValue("General", "EntryRange", entryRange);
            stealOccupied = s.GetValue("General", "StealOccupiedSeat", stealOccupied);
            openWindow = s.GetValue("General", "RollDownWindow", openWindow);
            windowIndex = s.GetValue("General", "WindowIndex", windowIndex);
            rollWindowUpOnExit = s.GetValue("General", "RollUpOnExit", rollWindowUpOnExit);
            crashRagdoll = s.GetValue("General", "CrashRagdoll", crashRagdoll);
            crashSpeedDrop = s.GetValue("General", "CrashSpeedDrop", crashSpeedDrop);
            wheelBone = s.GetValue("General", "WheelBone", wheelBone);
            fallbackBone = s.GetValue("General", "FallbackBone", fallbackBone);

            string models = s.GetValue("General", "GoatModels", string.Join(",", goatModels));
            goatModels = models.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            string anims = s.GetValue("General", "BlockedAnims", string.Join(";", drivingAnims));
            drivingAnims = anims.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            posOffset = new Vector3(
                s.GetValue("Offset", "PosX", posOffset.X),
                s.GetValue("Offset", "PosY", posOffset.Y),
                s.GetValue("Offset", "PosZ", posOffset.Z));
            rotOffset = new Vector3(
                s.GetValue("Offset", "RotX", rotOffset.X),
                s.GetValue("Offset", "RotY", rotOffset.Y),
                s.GetValue("Offset", "RotZ", rotOffset.Z));
            basePitch = s.GetValue("Offset", "BasePitch", basePitch);
            leanShiftX = s.GetValue("Offset", "LeanShiftX", leanShiftX);

            stiffness = s.GetValue("Physics", "Stiffness", stiffness);
            damping = s.GetValue("Physics", "Damping", damping);
            latGain = s.GetValue("Physics", "LateralGain", latGain);
            longGain = s.GetValue("Physics", "LongitudinalGain", longGain);
            windowLeanDeg = s.GetValue("Physics", "WindowLeanDegrees", windowLeanDeg);
            leanFullSpeed = s.GetValue("Physics", "LeanFullSpeed", leanFullSpeed);
            wobble = s.GetValue("Physics", "Wobble", wobble);
            rollSign = s.GetValue("Physics", "RollSign", rollSign);
            pitchSign = s.GetValue("Physics", "PitchSign", pitchSign);
            crashKick = s.GetValue("Physics", "CrashKick", crashKick);
        }

        void ResetTuning() { tunePos = posOffset; tuneRot = rotOffset; tunePitch = basePitch; }

        // ------------------------------------------------------------------ main loop
        void OnTick(object sender, EventArgs e)
        {
            if (!enabled) return;
            Ped ped = Game.Player.Character;
            if (ped == null || !ped.Exists()) return;
            float dt = Math.Min(Math.Max(Game.LastFrameTime, 0.001f), 0.05f);
            time += dt;

            if (!IsGoat(ped)) { if (driving) StopDriving(false); return; }

            if (!driving) TickOnFoot(ped);
            else TickDriving(ped, dt);
        }

        bool IsGoat(Ped ped) { return goatHashes.Contains(ped.Model.Hash); }

        // ------------------------------------------------------------------ entering
        void TickOnFoot(Ped ped)
        {
            if (ped.IsInVehicle()) return;                       // e.g. another script seated us normally

            Vehicle v = World.GetClosestVehicle(ped.Position, entryRange);
            if (v == null || !v.Exists() || !CanDrive(v)) return;

            // Take over the entry key so GTA never starts its human door animation.
            Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, InputEnter, true);
            if (Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, InputEnter))
                StartDriving(ped, v);
        }

        bool CanDrive(Vehicle v)
        {
            int cls = Function.Call<int>(Hash.GET_VEHICLE_CLASS, v.Handle);
            // bikes (8), cycles (13), boats (14), helis (15), planes (16), trains (21): not supported
            if (cls == 8 || cls == 13 || cls == 14 || cls == 15 || cls == 16 || cls == 21) return false;
            int lockState = Function.Call<int>(Hash.GET_VEHICLE_DOOR_LOCK_STATUS, v.Handle);
            if (lockState > 1) return false;                     // locked
            if (!v.IsSeatFree(VehicleSeat.Driver))
            {
                Ped d = v.GetPedOnSeat(VehicleSeat.Driver);
                if (!stealOccupied || d == null || d.Handle == Game.Player.Character.Handle) return false;
            }
            return true;
        }

        void StartDriving(Ped ped, Vehicle v)
        {
            // 1) throw out whoever is in the seat
            if (!v.IsSeatFree(VehicleSeat.Driver))
            {
                Ped d = v.GetPedOnSeat(VehicleSeat.Driver);
                if (d != null && d.Exists()) Function.Call(Hash.TASK_LEAVE_VEHICLE, d.Handle, v.Handle, WarpOutFlag);
                Wait(60);
            }

            // 2) warp into the driver seat - no door animation
            ped.SetIntoVehicle(v, VehicleSeat.Driver);
            vehicle = v;

            // 3) keep GTA from fighting us
            Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, PcfWillFlyThroughWindscreen, false);
            Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, PcfPreventAutoShuffleToDriversSeat, true);
            Function.Call(Hash.CLEAR_PED_TASKS, ped.Handle);     // drop the native driving task state (non-immediate: keeps us seated)

            // 4) window
            if (openWindow) Function.Call(Hash.ROLL_DOWN_WINDOW, v.Handle, windowIndex);

            // 5) find the bone we will attach to
            boneIndex = Function.Call<int>(Hash.GET_ENTITY_BONE_INDEX_BY_NAME, v.Handle, wheelBone);
            if (boneIndex < 0) boneIndex = Function.Call<int>(Hash.GET_ENTITY_BONE_INDEX_BY_NAME, v.Handle, fallbackBone);
            if (boneIndex < 0) boneIndex = 0;                    // chassis, offsets are then relative to the vehicle origin

            pitch = roll = yaw = default(Spring);
            havePrev = false;
            driving = true;
            ApplyAttachment(ped, 0f);
            Notify("Goat at the wheel. " + (tuneMode ? "Tune mode: NumPad keys." : ""));
        }

        // ------------------------------------------------------------------ driving
        void TickDriving(Ped ped, float dt)
        {
            if (vehicle == null || !vehicle.Exists() || ped.IsDead || vehicle.IsDead)
            {
                StopDriving(false);
                return;
            }

            // exit key: also taken over so GTA does not play the human exit animation
            Function.Call(Hash.DISABLE_CONTROL_ACTION, 0, InputVehExit, true);
            if (Function.Call<bool>(Hash.IS_DISABLED_CONTROL_JUST_PRESSED, 0, InputVehExit))
            {
                StopDriving(true);
                return;
            }

            // briefly ragdolled after a crash? put the goat back in the seat when it is over
            if (ragdollReattachAt != 0)
            {
                if (Game.GameTime < ragdollReattachAt) return;
                ragdollReattachAt = 0;
                ped.SetIntoVehicle(vehicle, VehicleSeat.Driver);
                pitch = roll = yaw = default(Spring);
            }

            if (!ped.IsInVehicle()) ped.SetIntoVehicle(vehicle, VehicleSeat.Driver);   // RISK: GTA can drop us out of the seat

            SuppressHumanDrivingAnims(ped);
            SimulateSprings(ped, dt);
            ApplyAttachment(ped, dt);
        }

        // Native human driving pose.
        // RISK: the pose comes from the in-vehicle task, which picks clip sets per vehicle layout,
        // so there is no single dictionary to stop. Clearing the secondary task every frame and
        // stopping the known clips is best effort; the wheel-bone attachment is what actually
        // replaces the pose.
        void SuppressHumanDrivingAnims(Ped ped)
        {
            Function.Call(Hash.CLEAR_PED_SECONDARY_TASK, ped.Handle);
            foreach (string pair in drivingAnims)
            {
                string[] p = pair.Split('|');
                if (p.Length < 1 || p[0].Length == 0) continue;
                string clip = p.Length > 1 ? p[1] : "";
                if (clip.Length > 0) Function.Call(Hash.STOP_ANIM_TASK, ped.Handle, p[0], clip, -4.0f);
                if (Function.Call<bool>(Hash.HAS_ANIM_DICT_LOADED, p[0])) Function.Call(Hash.REMOVE_ANIM_DICT, p[0]);
            }
        }

        // ------------------------------------------------------------------ the physics
        void SimulateSprings(Ped ped, float dt)
        {
            Vector3 vel = vehicle.Velocity;
            Vector3 accel = havePrev ? (vel - prevVelocity) / dt : new Vector3(0f, 0f, 0f);
            float speedDrop = havePrev ? (prevVelocity.Length() - vel.Length()) : 0f;
            prevVelocity = vel;
            havePrev = true;

            // acceleration in the car's own axes
            float aLong = Dot(accel, vehicle.ForwardVector);
            float aLat = Dot(accel, vehicle.RightVector);
            float speed = vel.Length();
            float speedFactor = Clamp(speed / leanFullSpeed, 0f, 1f);

            // rest pose: leaning toward the window more the faster we go, plus a little sway
            float restRoll = rollSign * windowLeanDeg * speedFactor;
            float restPitch = 0f;
            float restYaw = 0f;
            float sway = wobble * speedFactor;
            restRoll += sway * (float)Math.Sin(time * 3.1f);
            restPitch += sway * 0.6f * (float)Math.Sin(time * 2.3f + 1.7f);
            restYaw += sway * 0.8f * (float)Math.Sin(time * 1.7f + 0.4f);

            // forces from the car: sideways (centrifugal) throws the body outward, braking pitches it forward
            float rollForce = rollSign * latGain * aLat * stiffness;
            float pitchForce = pitchSign * -longGain * aLong * stiffness;
            float yawForce = 0.3f * latGain * aLat * stiffness;

            Step(ref roll, restRoll, rollForce, dt);
            Step(ref pitch, restPitch, pitchForce, dt);
            Step(ref yaw, restYaw, yawForce, dt);

            // crash: a big sudden speed loss kicks the springs
            if (speedDrop > crashSpeedDrop)
            {
                float k = Clamp(speedDrop / crashSpeedDrop, 1f, 3f);
                roll.Velocity += rollSign * crashKick * k * (float)(rng.NextDouble() * 2 - 1 + 0.6);
                pitch.Velocity += pitchSign * crashKick * k;
                yaw.Velocity += crashKick * k * (float)(rng.NextDouble() * 2 - 1);
                if (crashRagdoll) CrashRagdoll(ped);
            }

            roll.Angle = Clamp(roll.Angle, -85f, 85f);
            pitch.Angle = Clamp(pitch.Angle, -60f, 75f);
            yaw.Angle = Clamp(yaw.Angle, -70f, 70f);
        }

        // damped spring: angle'' = stiffness * (rest - angle) - damping * angle' + force
        void Step(ref Spring s, float rest, float force, float dt)
        {
            float acc = stiffness * (rest - s.Angle) - damping * s.Velocity + force;
            s.Velocity += acc * dt;
            s.Angle += s.Velocity * dt;
        }

        void CrashRagdoll(Ped ped)
        {
            Function.Call(Hash.DETACH_ENTITY, ped.Handle, true, true);
            Function.Call(Hash.SET_PED_CAN_RAGDOLL, ped.Handle, true);
            Function.Call(Hash.SET_PED_TO_RAGDOLL, ped.Handle, 1800, 1800, 0, false, false, false);
            ragdollReattachAt = Game.GameTime + 2000;
        }

        // ------------------------------------------------------------------ attachment
        // ATTACH_ENTITY_TO_ENTITY(entity, target, boneIndex, x, y, z, xRot, yRot, zRot,
        //                         p9, useSoftPinning, collision, isPed, vertexIndex, fixedRot)
        // RISK: attaching a ped that is also seated. If your game drops the goat out of the seat or
        // you lose steering control, set TuneMode=true and test; the usual fixes are isPed=false and
        // collision=false (below), or attaching to the chassis (WheelBone=chassis_dummy).
        void ApplyAttachment(Ped ped, float dt)
        {
            float leanT = Clamp(Math.Abs(roll.Angle) / 45f, 0f, 1f);
            Vector3 pos = tunePos + new Vector3(leanShiftX * leanT, 0f, 0f);
            Vector3 rot = new Vector3(
                tuneRot.X + tunePitch + pitch.Angle,
                tuneRot.Y + roll.Angle,
                tuneRot.Z + yaw.Angle);

            Function.Call(Hash.ATTACH_ENTITY_TO_ENTITY, ped.Handle, vehicle.Handle, boneIndex,
                pos.X, pos.Y, pos.Z, rot.X, rot.Y, rot.Z,
                false, false, false, true, 2, true);
        }

        // ------------------------------------------------------------------ leaving
        void StopDriving(bool warpOut)
        {
            Ped ped = Game.Player.Character;
            if (ped != null && ped.Exists())
            {
                Function.Call(Hash.DETACH_ENTITY, ped.Handle, true, true);
                Function.Call(Hash.SET_PED_CONFIG_FLAG, ped.Handle, PcfPreventAutoShuffleToDriversSeat, false);
                if (warpOut && vehicle != null && vehicle.Exists())
                    Function.Call(Hash.TASK_LEAVE_VEHICLE, ped.Handle, vehicle.Handle, WarpOutFlag);
            }
            if (vehicle != null && vehicle.Exists() && openWindow && rollWindowUpOnExit)
                Function.Call(Hash.ROLL_UP_WINDOW, vehicle.Handle, windowIndex);

            driving = false;
            vehicle = null;
            ragdollReattachAt = 0;
        }

        // ------------------------------------------------------------------ live tuning
        void OnTuneKey(KeyEventArgs e)
        {
            if (!tuneMode || !driving) return;
            bool shift = held.Contains(Keys.ShiftKey) || held.Contains(Keys.LShiftKey) || held.Contains(Keys.RShiftKey) || e.Shift;
            const float stepPos = 0.02f, stepRot = 2.0f;
            bool changed = true;

            switch (e.KeyCode)
            {
                case Keys.NumPad4: if (shift) tuneRot.Y -= stepRot; else tunePos.X -= stepPos; break;
                case Keys.NumPad6: if (shift) tuneRot.Y += stepRot; else tunePos.X += stepPos; break;
                case Keys.NumPad8: if (shift) tuneRot.X += stepRot; else tunePos.Y += stepPos; break;
                case Keys.NumPad2: if (shift) tuneRot.X -= stepRot; else tunePos.Y -= stepPos; break;
                case Keys.NumPad9: if (shift) tuneRot.Z += stepRot; else tunePos.Z += stepPos; break;
                case Keys.NumPad3: if (shift) tuneRot.Z -= stepRot; else tunePos.Z -= stepPos; break;
                default: changed = false; break;
            }

            if (e.KeyCode == keyTuneReset) { ResetTuning(); changed = true; }
            if (e.KeyCode == keyTunePrint) { PrintTuning(); return; }
            if (changed) Notify(string.Format("pos {0:0.00} {1:0.00} {2:0.00}  rot {3:0} {4:0} {5:0}",
                tunePos.X, tunePos.Y, tunePos.Z, tuneRot.X, tuneRot.Y, tuneRot.Z));
        }

        void PrintTuning()
        {
            string text = string.Format(
                "[Offset]\nPosX={0:0.###}\nPosY={1:0.###}\nPosZ={2:0.###}\nRotX={3:0.#}\nRotY={4:0.#}\nRotZ={5:0.#}\nBasePitch={6:0.#}\n",
                tunePos.X, tunePos.Y, tunePos.Z, tuneRot.X, tuneRot.Y, tuneRot.Z, tunePitch);
            try { File.AppendAllText(logPath, DateTime.Now + "\n" + text + "\n"); } catch { }
            Notify("Offsets written to GoatDriver.log");
        }

        // ------------------------------------------------------------------ helpers
        static float Dot(Vector3 a, Vector3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
        static void Notify(string text) { GTA.UI.Screen.ShowSubtitle("~y~GoatDriver~s~: " + text, 2500); }
    }
}
