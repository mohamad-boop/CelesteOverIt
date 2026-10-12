using System.IO;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;

namespace CelesteOverIt
{
    /// <summary>
    /// Development only (Dev.SelfTest): drives Madeline with a scripted input sequence, logs her state
    /// and saves screenshots to BepInEx/CelesteOverIt_test so movement can be checked without a human.
    /// </summary>
    public static class SelfTest
    {
        struct Phase
        {
            public int Until, X, Y;
            public bool Jump, Dash, Grab;
            public string Name;
            public Phase(int until, string name, int x, int y, bool jump, bool dash, bool grab)
            { Until = until; Name = name; X = x; Y = y; Jump = jump; Dash = dash; Grab = grab; }
        }

        static readonly Phase[] Phases =
        {
            new Phase(60, "settle", 0, 0, false, false, false),
            new Phase(90, "jump in place", 0, 0, true, false, false),
            new Phase(150, "land", 0, 0, false, false, false),
            new Phase(240, "run right", 1, 0, false, false, false),
            new Phase(250, "grab wall", 1, 0, false, false, true),
            new Phase(300, "climb up", 0, -1, false, false, true),
            new Phase(330, "hold still", 0, 0, false, false, true),
            new Phase(332, "climb jump", 0, 0, true, false, true),
            new Phase(360, "fall", 0, 0, false, false, false),
            new Phase(420, "run+jump right", 1, 0, true, false, false),
            new Phase(440, "run right", 1, 0, false, false, false),
            new Phase(470, "back to wall", -1, 0, false, false, false),
            new Phase(480, "wall jump", -1, 0, true, false, false),
            new Phase(520, "drift", 0, 0, false, false, false),
            new Phase(540, "walk to edge", 1, 0, false, false, false),
            new Phase(560, "fall", 0, 0, false, false, false),
            new Phase(320, "unused", 0, 0, false, false, false),
            new Phase(322, "dash up-right", 1, -1, false, true, false),
            new Phase(380, "drift", 1, -1, false, false, false),
            new Phase(440, "idle", 0, 0, false, false, false),
            new Phase(530, "run left", -1, 0, false, false, false),
            new Phase(560, "jump left", -1, 0, true, false, false),
            new Phase(620, "grab left", -1, 0, false, false, true),
            new Phase(740, "climb up", 0, -1, false, false, true),
            new Phase(800, "idle", 0, 0, false, false, false),
        };

        /// <summary>Check every level item sits in clear air, then visit each strawberry and screenshot it.</summary>
        static void Tour(MadelineController c, ManualLogSource log, string dir)
        {
            var level = c.GetComponent<Level>();
            if (level == null) return;
            var items = level.DebugItems();
            int mask = Physics2D.GetLayerCollisionMask(8) & ~(1 << 8) & ~(1 << 9);
            foreach (var kv in items)
            {
                var p = kv.Value;
                Vector2 center, size;
                if (kv.Key.StartsWith("platform"))
                {
                    int tiles = int.Parse(kv.Key.Split(':')[1]);
                    float w = tiles * 8 * c.Ps;
                    center = p + new Vector2(0, 0.5f); size = new Vector2(w, 1.2f);   // room above it to stand
                }
                else { center = p; size = new Vector2(1.2f, 1.2f); }
                var hit = Physics2D.OverlapBox(center, size, 0f, mask);
                log.LogInfo("[tour] " + kv.Key + " at " + p.x.ToString("0.0") + "," + p.y.ToString("0.0") + " -> " +
                    (hit == null ? "clear" : "BLOCKED by " + hit.name));
            }
            c.StartCoroutine(Visit(c, items, dir, log));
        }

        static System.Collections.IEnumerator Visit(MadelineController c, System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, Vector2>> items, string dir, ManualLogSource log)
        {
            foreach (var kv in items)
            {
                if (!kv.Key.StartsWith("strawberry")) continue;
                c.Paused = true;
                c.TeleportTo(kv.Value + new Vector2(-1.5f, -3f));
                yield return new WaitForSeconds(4f);
                string file = Path.Combine(dir, "berry" + kv.Key.Split(':')[1] + ".png");
                ScreenCapture.CaptureScreenshot(file);
                log.LogInfo("[tour] screenshot " + file);
                yield return new WaitForSeconds(0.5f);
            }
            c.Paused = false;
            // Collect strawberry 1: drop Madeline onto it and let her land below.
            var level = c.GetComponent<Level>();
            foreach (var kv in items)
                if (kv.Key == "strawberry:1") c.TeleportTo(kv.Value + new Vector2(0, -0.3f));
            yield return new WaitForSeconds(4f);
            log.LogInfo("[tour] strawberries after drop: " + level.Collected + "/" + level.Total + " at feet " + c.M.X + "," + c.M.Y + " ground " + c.M.OnGround);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "collected.png"));
            yield return new WaitForSeconds(0.5f);
            level.SetEditing(true);
            yield return new WaitForSeconds(1.5f);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, "editor.png"));
            yield return new WaitForSeconds(0.5f);
            level.SetEditing(false);
            level.ResetProgress();
            log.LogInfo("[tour] editor toggled ok, progress reset");

            // Clip-through recovery: bury her deep inside the big boulder (no direct teleport push-out).
            c.Respawn(true);
            yield return new WaitForSeconds(2f);
            c.Solids.Origin = new Vector2(-16f, 0f);   // re-anchor so (0,0) is inside the rock
            c.M.X = 0; c.M.Y = 0;
            log.LogInfo("[tour] buried: colliding=" + c.M.Colliding);
            yield return new WaitForSeconds(1.5f);
            var feet = c.PxToWorld(c.M.X, c.M.Y);
            log.LogInfo("[tour] after auto-unstuck: colliding=" + c.M.Colliding + " feet " + feet);
            c.Respawn(false);
            yield return new WaitForSeconds(1f);
            log.LogInfo("[tour] F5 -> feet " + c.PxToWorld(c.M.X, c.M.Y) + " colliding=" + c.M.Colliding);
            c.Respawn(true);
            yield return new WaitForSeconds(1f);
            log.LogInfo("[tour] Shift+F5 -> feet " + c.PxToWorld(c.M.X, c.M.Y) + " colliding=" + c.M.Colliding);

            // Slipping: flat start shelf should hold her; a steep face should not.
            var startFeet = c.PxToWorld(c.M.X, c.M.Y);
            yield return new WaitForSeconds(1f);
            log.LogInfo("[slip] flat shelf: slipping=" + c.M.Slipping + " ground=" + c.M.OnGround + " moved " + (c.PxToWorld(c.M.X, c.M.Y) - startFeet).magnitude.ToString("0.00"));
            int mask = c.Solids.Mask;
            int found = 0;
            for (float x = -1f; x < 14f && found < 5; x += 0.13f)
            {
                var hit = Physics2D.Raycast(new Vector2(x, 30f), Vector2.down, 60f, mask);
                if (hit.collider == null) continue;
                float ang = Vector2.Angle(hit.normal, Vector2.up);
                if (ang < 76f || ang > 88f) continue;
                found++;
                x += 1.5f;   // spread the samples out
                c.TeleportTo(hit.point + hit.normal * 0.15f + Vector2.up * 0.25f);
                var p0 = c.PxToWorld(c.M.X, c.M.Y);
                int slipFrames = 0;
                float t0 = Time.time;
                while (Time.time - t0 < 1.5f) { if (c.M.Slipping) slipFrames++; yield return null; }
                var p1 = c.PxToWorld(c.M.X, c.M.Y);
                int dh;
                float ga = c.Solids.GroundAngle(c.M.X, c.M.Y, out dh);
                log.LogInfo("[slip]   probe: angle " + ga.ToString("0.0") + " downhill " + dh + " cols " + c.Solids.DebugColumns() +
                            " state " + c.M.State + " ground " + c.M.OnGround + " slipAngle " + c.M.SlipAngle + " anim " + c.AnimId);
                log.LogInfo("[slip] face " + ang.ToString("0") + "deg at " + hit.point + ": slipping frames " + slipFrames +
                            ", slid " + (p1 - p0).ToString() + ", ground now " + c.M.OnGround);
                ScreenCapture.CaptureScreenshot(Path.Combine(dir, "slip" + found + ".png"));
                yield return new WaitForSeconds(0.3f);
            }
            // Park on strawberry 3's first platform so the save/continue test can check she comes back here.
            c.TeleportTo(new Vector2(1.5f, 16.1f));
            yield return new WaitForSeconds(3f);
            log.LogInfo("[save] parked at " + c.PxToWorld(c.M.X, c.M.Y) + " ground " + c.M.OnGround);
            log.LogInfo("[tour] done");
        }

        public static void Attach(MadelineController c, ManualLogSource log)
        {
            string dir = Path.Combine(Paths.BepInExRootPath, "CelesteOverIt_test");
            Directory.CreateDirectory(dir);
            int minY = int.MaxValue, lastState = -1;
            string lastPhase = null;
            // Adaptive: walk until a wall is found, then grab, climb, hold, climb-jump.
            int seekDir = 1, wallAt = -1, walked = 0, blocked = 0, lastX = int.MinValue;
            c.Input.Script = step =>
            {
                var f = new CelesteInput.ScriptedFrame();
                var m = c.M;
                string phase;
                if (step == 30) { m.X += 230; m.Y -= 40; }       // skip past the start tree, drop in front of the big rock
                if (step < 60) phase = "settle";
                else if (wallAt < 0)
                {
                    phase = "seek wall";
                    f.X = seekDir;
                    if (++walked > 300) { seekDir = -seekDir; walked = 0; }
                    // a real wall: pushing into it and not moving for a while
                    if (m.OnGround && m.X == lastX && m.CollideCheck(m.X + seekDir, m.Y)) blocked++; else blocked = 0;
                    lastX = m.X;
                    if (blocked > 20) wallAt = step;
                }
                else
                {
                    int t = step - wallAt;
                    if (t < 10) { phase = "grab"; f.X = seekDir; f.Grab = true; }
                    else if (t < 150) { phase = "climb up"; f.Y = -1; f.Grab = true; }
                    else if (t < 180) { phase = "hold still"; f.Grab = true; }
                    else if (t < 182) { phase = "climb jump"; f.Jump = true; f.Grab = true; }
                    else if (t < 240) { phase = "release"; }
                    else if (t < 242) { phase = "dash up"; f.Y = -1; f.Dash = true; }
                    else phase = "idle";
                }
                if (phase != lastPhase) { lastPhase = phase; log.LogInfo("[test] phase '" + phase + "' at step " + step + " pos " + m.X + "," + m.Y); }
                return f;
            };            c.OnStep = ctl =>
            {
                var m = ctl.M;
                int s = ctl.StepCount;
                if (m.Y < minY) minY = m.Y;
                if (m.State != lastState)
                {
                    log.LogInfo("[test] step " + s + " state -> " + (m.State == 0 ? "Normal" : m.State == 1 ? "Climb" : "Dash"));
                    lastState = m.State;
                }
                if (s % 5 == 0 || s == 1)
                    log.LogInfo(string.Format("[test] {0,4} pos {1,4},{2,4} spd {3,6:0.0},{4,6:0.0} ground {5} dashes {6} stam {7:0} anim {8} minY {9}",
                        s, m.X, m.Y, m.Speed.x, m.Speed.y, m.OnGround ? 1 : 0, m.Dashes, m.Stamina, ctl.AnimId, minY));
                if (s == 150) { log.LogInfo("[test] jump-in-place apex height: " + (-minY) + " px"); }
                if (s == 55 || (wallAt > 0 && (s - wallAt) % 40 == 20 && s - wallAt < 260))
                    ScreenCapture.CaptureScreenshot(Path.Combine(dir, "climb" + s.ToString("0000") + ".png"));
                if (wallAt > 0 && s - wallAt == 175)
                {
                    var p = ctl.PxToWorld(m.X, m.Y - 16);
                    foreach (var col in Physics2D.OverlapBoxAll(p, new Vector2(1.5f, 2f), 0f))
                        log.LogInfo("[test] above head: " + col.name + " layer " + LayerMask.LayerToName(col.gameObject.layer) +
                            " trigger " + col.isTrigger + " " + col.GetType().Name + " bounds " + col.bounds);
                }
                if (s % 300 == 0 && ctl.LedgeField != null) log.LogInfo("[test] ledges generated so far: " + ctl.LedgeField.Count);
                if (wallAt > 0 && s - wallAt == 420) Tour(ctl, log, dir);
                if (wallAt > 0 && s - wallAt == 400) log.LogInfo("[test] done");
            };
        }
    }
}

