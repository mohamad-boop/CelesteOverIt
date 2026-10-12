using System;
using System.Collections.Generic;
using UnityEngine;

namespace CelesteOverIt
{
    /// <summary>
    /// Celeste-style virtual input: 0.08s press buffers for Jump/Dash, "take newer" axis overlap,
    /// and 8-way aiming. Polled every render frame, consumed once per 60 Hz simulation step.
    /// </summary>
    public class CelesteInput
    {
        public const float BufferTime = 0.08f;

        public KeyCode[] JumpKeys, DashKeys, GrabKeys, LeftKeys, RightKeys, UpKeys, DownKeys;

        public int MoveX, MoveY;          // Celeste convention: MoveY = -1 is up
        public bool JumpCheck, GrabCheck;
        float jumpBuffer, dashBuffer;
        bool jumpLatch, dashLatch;
        int lastX, lastY;
        bool axesAvailable = true;

        /// <summary>When set, replaces the physical devices (used by the dev self-test).</summary>
        public Func<int, ScriptedFrame> Script;
        int scriptStep;

        public struct ScriptedFrame
        {
            public int X, Y;
            public bool Jump, Dash, Grab;
        }
        ScriptedFrame prevScripted;

        public bool JumpPressed { get { return jumpBuffer > 0; } }
        public bool DashPressed { get { return dashBuffer > 0; } }
        public void ConsumeJump() { jumpBuffer = 0; }
        public void ConsumeDash() { dashBuffer = 0; }

        public static KeyCode[] ParseKeys(string list)
        {
            var keys = new List<KeyCode>();
            foreach (var raw in list.Split(','))
            {
                var s = raw.Trim();
                if (s.Length == 0) continue;
                try { keys.Add((KeyCode)Enum.Parse(typeof(KeyCode), s, true)); }
                catch (Exception) { Debug.LogWarning("[CelesteOverIt] Unknown key: " + s); }
            }
            return keys.ToArray();
        }

        static bool Any(KeyCode[] keys, bool down)
        {
            for (int i = 0; i < keys.Length; i++)
                if (down ? Input.GetKeyDown(keys[i]) : Input.GetKey(keys[i])) return true;
            return false;
        }

        /// <summary>Call once per render frame so short taps between sim steps are never lost.</summary>
        public void PollFrame()
        {
            if (Script != null) return;
            if (Any(JumpKeys, true)) jumpLatch = true;
            if (Any(DashKeys, true)) dashLatch = true;
        }

        /// <summary>Call once per simulation step, before the player update.</summary>
        public void Step(float dt)
        {
            if (Script != null)
            {
                var f = Script(scriptStep++);
                if (f.Jump && !prevScripted.Jump) jumpLatch = true;
                if (f.Dash && !prevScripted.Dash) dashLatch = true;
                prevScripted = f;
                MoveX = f.X; MoveY = f.Y; JumpCheck = f.Jump; GrabCheck = f.Grab;
            }
            else
            {
                JumpCheck = Any(JumpKeys, false);
                GrabCheck = Any(GrabKeys, false);
                MoveX = Axis(Any(LeftKeys, false), Any(RightKeys, false), ref lastX, "Horizontal", false);
                MoveY = Axis(Any(UpKeys, false), Any(DownKeys, false), ref lastY, "Vertical", true);
            }

            jumpBuffer = jumpLatch ? BufferTime : Math.Max(0, jumpBuffer - dt);
            dashBuffer = dashLatch ? BufferTime : Math.Max(0, dashBuffer - dt);
            jumpLatch = dashLatch = false;
        }

        int Axis(bool neg, bool posi, ref int last, string unityAxis, bool invert)
        {
            int v;
            if (neg && posi) v = -last;             // Celeste's "take newer" overlap behaviour
            else if (neg) { v = -1; last = -1; }
            else if (posi) { v = 1; last = 1; }
            else v = 0;
            if (v == 0 && axesAvailable)
            {
                try
                {
                    float a = Input.GetAxisRaw(unityAxis);
                    if (invert) a = -a;
                    if (a > 0.3f) v = 1; else if (a < -0.3f) v = -1;
                }
                catch (Exception) { axesAvailable = false; }
            }
            return v;
        }

        /// <summary>8-direction aim vector in Celeste space (y down); falls back to facing.</summary>
        public Vector2 GetAim(int facing)
        {
            if (MoveX == 0 && MoveY == 0) return new Vector2(facing, 0);
            return new Vector2(MoveX, MoveY).normalized;
        }
    }
}
