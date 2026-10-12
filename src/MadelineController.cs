using System;
using System.Collections.Generic;
using UnityEngine;

namespace CelesteOverIt
{
    /// <summary>Getting Over It's 2D colliders, seen through Celeste's pixel grid.</summary>
    public class GoiSolids : ISolids
    {
        public Vector2 Origin;
        public float Ps;
        ContactFilter2D filter;
        readonly Collider2D[] buf = new Collider2D[4];
        const float Shrink = 0.01f;

        public readonly int Mask;

        public GoiSolids(int layerMask)
        {
            Mask = layerMask;
            filter = new ContactFilter2D();
            filter.useTriggers = false;
            filter.SetLayerMask(layerMask);
        }

        public bool Rect(int left, int top, int width, int height)
        {
            var c = new Vector2(Origin.x + (left + width * 0.5f) * Ps, Origin.y - (top + height * 0.5f) * Ps);
            var size = new Vector2(width * Ps - Shrink, height * Ps - Shrink);
            return Physics2D.OverlapBox(c, size, 0f, filter, buf) > 0;
        }

        public bool Point(int x, int y)
        {
            var p = new Vector2(Origin.x + (x + 0.5f) * Ps, Origin.y - (y + 0.5f) * Ps);
            return Physics2D.OverlapPoint(p, filter, buf) > 0;
        }

        public Ledges Ledges;
        readonly RaycastHit2D[] rayHits = new RaycastHit2D[2];

        public float GroundAngle(int x, int y, out int downhill)
        {
            // Probe straight down under each of her 8 foot pixels. A ray always lands on a facet's surface
            // (a box resting on a polygon corner would report the box's own flat bottom instead), so the
            // closest hit is the rock actually holding her up, with its true slope. Ties within half a pixel
            // go to the gentler surface, so standing on a flat lip next to a drop is fine.
            downhill = 0;
            float bestDist = float.MaxValue, bestAngle = 0f;
            for (int c = 0; c < 8; c++) colDist[c] = float.MaxValue;
            for (int i = -4; i < 4; i++)
            {
                var origin = new Vector2(Origin.x + (x + i + 0.5f) * Ps, Origin.y - (y - 1f) * Ps);
                if (Physics2D.Raycast(origin, Vector2.down, filter, rayHits, 20f * Ps) == 0) continue;
                var h = rayHits[0];
                if (h.distance <= 0f) continue;            // started inside rock: no usable surface
                colDist[i + 4] = h.distance / Ps;
                float a = Vector2.Angle(h.normal, Vector2.up);
                bool closer = h.distance < bestDist - 0.5f * Ps;
                bool tieGentler = Mathf.Abs(h.distance - bestDist) <= 0.5f * Ps && a < bestAngle;
                if (closer || tieGentler)
                {
                    bestDist = Math.Min(bestDist, h.distance);
                    bestAngle = a;
                    downhill = h.normal.x > 0.001f ? 1 : h.normal.x < -0.001f ? -1 : 0;
                }
            }
            if (bestDist == float.MaxValue) return 0f;

            // Perched on a corner: only a sliver of her feet is on rock and the ground falls away steeply
            // beside it. Celeste lets you teeter on 1px; here that slides you off toward the drop.
            if (EdgeSupport > 0 && !JumpThru(x - 4, y, 8, 1))
            {
                int supported = 0, first = -1, last = -1;
                for (int c = 0; c < 8; c++)
                    if (colDist[c] <= 1.6f) { supported++; if (first < 0) first = c; last = c; }
                if (supported == 0)
                {
                    // held up only by a corner touching the very edge of her hitbox: the side nearest rock
                    int near = 0;
                    for (int c = 1; c < 8; c++) if (colDist[c] < colDist[near]) near = c;
                    first = last = near;
                }
                if (supported <= EdgeSupport)
                {
                    if (first >= 4 && colDist[0] >= 6f) { downhill = -1; return 90f; }   // rock under her right foot only
                    if (last <= 3 && colDist[7] >= 6f) { downhill = 1; return 90f; }     // rock under her left foot only
                }
            }
            return bestAngle;
        }

        public string DebugColumns()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var d in colDist) sb.Append(d == float.MaxValue ? "-" : d.ToString("0.0")).Append(' ');
            return sb.ToString();
        }

        public int EdgeSupport = 2;                  // px of foot on rock at or below which a corner perch slips
        readonly float[] colDist = new float[8];

        public bool JumpThru(int left, int top, int width, int height)
        {
            if (Ledges == null) return false;
            float xMin = Origin.x + left * Ps + Shrink, xMax = Origin.x + (left + width) * Ps - Shrink;
            float yMax = Origin.y - top * Ps - Shrink, yMin = Origin.y - (top + height) * Ps + Shrink;
            return Ledges.Overlaps(xMin, yMin, xMax, yMax);
        }
    }

    /// <summary>
    /// Owns Madeline in the Getting Over It scene: steps the Celeste simulation at 60 Hz, renders the
    /// sprite/hair/outline/trails like PlayerHair + PlayerSprite, and drags GOI's (now kinematic) Player
    /// body along so the camera, saves, progress meter and narrator keep working.
    /// </summary>
    public class MadelineController : MonoBehaviour
    {
        public const float StepDt = 1f / 60f;
        static readonly Color NormalHair = Hex(0xAC3232), UsedHair = Hex(0x44B7FF), TwoDashHair = Hex(0xFF6DEF);

        public CelesteAssets Assets;
        public CelesteInput Input;
        public Madeline M;
        public GoiSolids Solids;
        public Rigidbody2D PlayerRb;
        public Vector2 PlayerOffset = new Vector2(0, 1.0f);
        public float Ps;
        public float Z;
        public bool Trails = true;
        public Action<MadelineController> OnStep;   // dev hook
        public int StepCount;
        public CelesteAudio Audio;
        public Ledges LedgeField;
        public float Zoom = 1f, ExtraZoom = 1f;
        public bool Paused;                          // level editor freezes Madeline

        // ---- Reset / unstuck ------------------------------------------------------------------------
        public static readonly Vector2 MountainStart = new Vector2(-44.3f, -3.4f);   // GOI's new-game spot (feet)
        public KeyCode[] RespawnKeys = new KeyCode[0];
        readonly List<Vector2> safeSpots = new List<Vector2>();   // newest last
        float safeTimer, stuckTimer, respawnBackTimer;
        int respawnBack;                                           // how many checkpoints back the next press goes

        public Material SpriteMaterial { get { return spriteMat; } }
        bool wallSlideLoop;
        float tilt, tiltTarget;                      // lean into slanted walls while climbing (degrees)

        // camera zoom bookkeeping (re-applied every frame; adopts any change GOI makes itself)
        Camera zoomCam;
        float baseOrtho, lastOrtho;
        readonly List<Camera> perspCams = new List<Camera>();
        readonly List<float> baseFov = new List<float>(), lastFov = new List<float>();

        float accumulator;
        Vector2 prevFeet, curFeet;
        Vector2 lastPlayerTarget;

        // rendering
        Transform root;
        SpriteRenderer body;
        readonly SpriteRenderer[] bodyOutline = new SpriteRenderer[4];
        readonly SpriteRenderer[] hair = new SpriteRenderer[4];
        readonly SpriteRenderer[] hairBorder = new SpriteRenderer[16];
        readonly Vector2[] hairNodes = new Vector2[4];
        Material spriteMat, silhouetteMat;
        static readonly Vector2[] OutlineDirs = { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1) };

        // animation
        AnimData anim;
        public string AnimId;
        int frameIdx;
        float animTimer;
        bool animating;
        Color hairColor = NormalHair;
        float hairFlashTimer;
        int lastDashes;
        float flashClock;
        int lastTrailRequests;

        class Trail { public GameObject Go; public SpriteRenderer[] Rs; public Color C; public float T; }
        readonly List<Trail> trails = new List<Trail>();

        public void Init(Vector2 feetWorld)
        {
            spriteMat = new Material(Shader.Find("Sprites/Default"));
            var sil = Shader.Find("GUI/Text Shader");
            silhouetteMat = sil != null ? new Material(sil) : spriteMat;

            root = new GameObject("Madeline").transform;
            root.SetParent(transform, false);
            for (int i = 0; i < 4; i++) bodyOutline[i] = MakeRenderer("BodyOutline" + i, 0);
            for (int i = 0; i < 16; i++) hairBorder[i] = MakeRenderer("HairBorder" + i, 0);
            for (int i = 0; i < 4; i++) hair[i] = MakeRenderer("Hair" + i, 4 - i);
            body = MakeRenderer("Body", 10);

            M.Sfx = OnSfx;
            FindGravityControl();
            if (LedgeField != null)
            {
                LedgeField.OnCreated = CreateLedgeVisual;
                LedgeField.Ensure(feetWorld, 2, 1000);
            }
            SyncTo(feetWorld + PlayerOffset);
            lastDashes = M.Dashes;
            Play("idle", false);
        }

        // ---- Sound (Celeste's own FMOD events via the audio host) --------------------------------------
        void OnSfx(string name, int dir)
        {
            if (Audio == null) return;
            switch (name)
            {
                case "jump": Audio.Play(CelesteAudio.Jump); break;
                case "super": Audio.Play(CelesteAudio.JumpSuper); break;
                case "superslide": Audio.Play(CelesteAudio.JumpSuperSlide); break;
                case "superwall": Audio.Play(CelesteAudio.JumpSuperWall); break;
                // dir is the jump direction, so the wall is on the other side
                case "walljump": Audio.Play(dir < 0 ? CelesteAudio.JumpWallRight : CelesteAudio.JumpWallLeft); break;
                case "climbjump": Audio.Play(dir > 0 ? CelesteAudio.JumpClimbRight : CelesteAudio.JumpClimbLeft); break;
                case "dash": Audio.Play(dir > 0 ? CelesteAudio.DashRedRight : CelesteAudio.DashRedLeft); break;
                case "dashpink": Audio.Play(dir > 0 ? CelesteAudio.DashPinkRight : CelesteAudio.DashPinkLeft); break;
                case "grab": Audio.PlaySurface(CelesteAudio.Grab); break;
                case "letgo": Audio.Play(CelesteAudio.GrabLetGo); break;
                case "ledge": Audio.Play(CelesteAudio.ClimbLedge); break;
                case "land": Audio.PlaySurface(CelesteAudio.Landing); break;
                case "footstep": Audio.PlaySurface(CelesteAudio.Footstep); break;
                case "duck": Audio.Play(CelesteAudio.Duck); break;
                case "stand": Audio.Play(CelesteAudio.Stand); break;
            }
        }

        /// <summary>Footsteps and handholds come from animation frames, like PlayerSprite's frame events.</summary>
        void OnAnimFrame()
        {
            if (Audio == null) return;
            if ((AnimId == "runSlow" || AnimId == "runFast" || AnimId == "walk" || AnimId == "runStumble") &&
                (frameIdx == 0 || frameIdx == 6) && M.OnGround)
                Audio.PlaySurface(CelesteAudio.Footstep);
            else if (AnimId == "climbup" && (frameIdx == 0 || frameIdx == 3))
                Audio.PlaySurface(CelesteAudio.Handhold);
        }

        void UpdateLoops(bool paused)
        {
            if (Audio == null) return;
            var m = M;
            bool slide = !paused && ((m.State == Madeline.StNormal && m.WallSlideDir != 0 && !m.OnGround) ||
                                     (m.State == Madeline.StClimb && m.Speed.y > 20));
            if (slide == wallSlideLoop) return;
            wallSlideLoop = slide;
            if (slide) Audio.Loop("wallslide", CelesteAudio.WallSlide);
            else Audio.StopLoop("wallslide");
        }

        void OnDestroy()
        {
            if (Audio != null && wallSlideLoop) Audio.StopLoop("wallslide");
        }

        // ---- Generated ledges: Celeste's wooden jump-through art ---------------------------------------
        void CreateLedgeVisual(Ledges.Ledge l)
        {
            const string tex = "objects/jumpthru/wood";
            var go = new GameObject("CelesteLedge");
            go.transform.position = new Vector3(l.XMin, l.Top, Z);
            // Celeste's JumpthruPlatform: column 0 = left end, 2 = right end; row 0 = end bolted to a wall, row 1 = free end.
            bool wallLeft = l.Open > 0;
            int tiles = Mathf.Max(2, Mathf.RoundToInt((l.XMax - l.XMin) / Ps / 8f));
            for (int i = 0; i < tiles; i++)
            {
                int col = i == 0 ? 0 : (i == tiles - 1 ? 2 : 1);
                int row = i == 0 ? (wallLeft ? 0 : 1) : (i == tiles - 1 ? (wallLeft ? 1 : 0) : 0);
                var t = new GameObject("t");
                t.transform.SetParent(go.transform, false);
                t.transform.localPosition = new Vector3(i * 8 * Ps, 0, 0);
                var r = t.AddComponent<SpriteRenderer>();
                r.sprite = Assets.Sub(tex, col * 8, row * 8, 8, 8);
                r.sharedMaterial = spriteMat;
                r.sortingOrder = 29500;
            }
        }

        // ---- Camera zoom -----------------------------------------------------------------------------
        void LateUpdate()
        {
            float zoom = Zoom * ExtraZoom;
            if (Mathf.Approximately(zoom, 1f) && Mathf.Approximately(lastOrtho, -1f)) return;
            var cam = Camera.main;
            if (cam == null) return;
            if (cam != zoomCam)
            {
                zoomCam = cam;
                lastOrtho = -1;
                perspCams.Clear(); baseFov.Clear(); lastFov.Clear();
                foreach (var c in cam.GetComponentsInChildren<Camera>(true))
                {
                    if (c == cam || c.orthographic) continue;   // keep UI cameras as they are
                    perspCams.Add(c); baseFov.Add(c.fieldOfView); lastFov.Add(-1);
                }
            }
            if (cam.orthographic)
            {
                if (Mathf.Abs(cam.orthographicSize - lastOrtho) > 1e-4f) baseOrtho = cam.orthographicSize;
                cam.orthographicSize = lastOrtho = baseOrtho * zoom;
            }
            for (int i = 0; i < perspCams.Count; i++)
            {
                var c = perspCams[i];
                if (c == null) continue;
                if (Mathf.Abs(c.fieldOfView - lastFov[i]) > 1e-3f) baseFov[i] = c.fieldOfView;
                float half = Mathf.Atan(Mathf.Tan(baseFov[i] * 0.5f * Mathf.Deg2Rad) * zoom);
                c.fieldOfView = lastFov[i] = 2f * half * Mathf.Rad2Deg;
            }
        }

        SpriteRenderer MakeRenderer(string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var r = go.AddComponent<SpriteRenderer>();
            r.sharedMaterial = spriteMat;
            r.sortingOrder = 30000 + order;
            return r;
        }

        public Vector2 PxToWorld(float x, float y) { return Solids.Origin + new Vector2(x * Ps, -y * Ps); }
        Vector2 FeetWorld() { return PxToWorld(M.X, M.Y); }

        /// <summary>Re-anchor Madeline when something else (save load, teleporter) moved GOI's player.</summary>
        public void SyncTo(Vector2 playerPos)
        {
            Solids.Origin = playerPos - PlayerOffset;
            M.Teleport(0, 0);
            // Don't start inside a rock: nudge upward until free.
            for (int i = 0; i < 96 && M.Colliding; i++) M.Y--;
            curFeet = prevFeet = FeetWorld();
            lastPlayerTarget = curFeet + PlayerOffset;
            for (int i = 0; i < 4; i++) hairNodes[i] = new Vector2(M.X, M.Y - 9 + i * 2);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            Input.PollFrame();
            UpdateLoops(dt <= 0 || Paused);
            if (!Paused && dt > 0)
                foreach (var k in RespawnKeys)
                    if (UnityEngine.Input.GetKeyDown(k))
                    {
                        Respawn(UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift));
                        break;
                    }
            if (Paused) { Render(1f); return; }
            if (dt <= 0) return;                            // paused
            if (LedgeField != null) LedgeField.Ensure(curFeet, 2, 2);
            accumulator = Math.Min(accumulator + dt, StepDt * 6);
            while (accumulator >= StepDt)
            {
                accumulator -= StepDt;
                prevFeet = curFeet;
                Input.Step(StepDt);
                ApplyGoiGravity();
                M.Update(StepDt, Input);
                StepCount++;
                TrackSafety(StepDt);
                UpdateSprite();
                AnimUpdate(StepDt);
                UpdateHair(StepDt);
                UpdateTilt();
                curFeet = FeetWorld();
                if (OnStep != null) OnStep(this);
            }
            Render(accumulator / StepDt);
            UpdateTrails(dt);
        }

        // ---- GOI's ending: the space column above the antenna --------------------------------------------
        // GravityControl is a big trigger box: entering it turns gravity off (asteroids pull you), leaving it
        // through the top starts the credits and gravity points gently up. A kinematic body doesn't fire
        // trigger messages against a static trigger, so we detect enter/exit ourselves and call GOI's handlers.
        MonoBehaviour gravityControl;
        BoxCollider2D gravityBox;
        System.Reflection.MethodInfo gcEnter, gcExit;
        System.Reflection.FieldInfo gcWells, gcCreditsUp;
        Collider2D playerCollider;
        bool inSpace;
        int playerColliderCount = 1;
        public float PlayerMass = 52f;
        public Action<string> Log;

        void FindGravityControl()
        {
            var t = Type.GetType("GravityControl, Assembly-CSharp");
            if (t == null) return;
            gravityControl = FindObjectOfType(t) as MonoBehaviour;
            if (gravityControl == null) return;
            const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            gravityBox = gravityControl.GetComponent<BoxCollider2D>();
            gcEnter = t.GetMethod("OnTriggerEnter2D", all);
            gcExit = t.GetMethod("OnTriggerExit2D", all);
            gcWells = t.GetField("gravityWells", all);
            gcCreditsUp = t.GetField("creditsUp", all);
            playerCollider = PlayerRb != null ? PlayerRb.GetComponent<Collider2D>() : null;
            playerColliderCount = 0;
            if (PlayerRb != null)
                foreach (var col in PlayerRb.GetComponentsInChildren<Collider2D>())
                    if (col.enabled && col.attachedRigidbody == PlayerRb) playerColliderCount++;
            playerColliderCount = Math.Max(1, playerColliderCount);
            if (Log != null && gravityBox != null)
                Log("Space zone (GOI ending) at " + gravityBox.bounds.min + " - " + gravityBox.bounds.max);
        }

        void UpdateSpace()
        {
            if (gravityControl == null || gravityBox == null || PlayerRb == null) return;
            bool inside = gravityBox.OverlapPoint(PlayerRb.position);
            if (inside != inSpace)
            {
                inSpace = inside;
                var m = inside ? gcEnter : gcExit;
                if (m != null && playerCollider != null)
                {
                    try { m.Invoke(gravityControl, new object[] { playerCollider }); }
                    catch (Exception e) { if (Log != null) Log("GravityControl call failed: " + e.Message); }
                }
                if (Log != null) Log(inside ? "Entered space (zero-g)" : "Left space at y " + PlayerRb.position.y.ToString("0.0") + ", gravity now " + Physics2D.gravity);
            }
        }

        /// <summary>Copy GOI's current gravity (and the asteroid pull while in space) onto Madeline.</summary>
        public float SpaceGravity = 0.08f;   // fraction of normal gravity inside GOI's space zone (GOI itself uses 0)

        void ApplyGoiGravity()
        {
            M.GravityScale = -Physics2D.gravity.y / 30f;
            // GOI turns gravity fully off in space; Madeline gets a little back so she has to hop asteroid to asteroid.
            bool creditsUp = gcCreditsUp != null && gravityControl != null && (bool)gcCreditsUp.GetValue(gravityControl);
            if (inSpace && !creditsUp && Physics2D.gravity == Vector2.zero) M.GravityScale = SpaceGravity;
            var accel = Vector2.zero;
            if (inSpace && gcWells != null && !(gcCreditsUp != null && (bool)gcCreditsUp.GetValue(gravityControl)))
            {
                var wells = gcWells.GetValue(gravityControl) as Transform[];
                if (wells != null)
                    foreach (var w in wells)
                    {
                        if (w == null) continue;
                        var d = (Vector2)w.position - PlayerRb.position;
                        float sq = Mathf.Max(d.sqrMagnitude, 1f);
                        // GOI: OnTriggerStay2D adds 2500/d^2 once per player collider (Diogenes' body has several)
                        var a = d.normalized * (2500f * playerColliderCount / sq / PlayerMass);
                        accel += new Vector2(a.x, -a.y) / Ps;                  // world u/s^2 -> Celeste px/s^2 (y down)
                    }
            }
            M.ExternalAccel = accel;
        }

        void FixedUpdate()
        {
            if (PlayerRb == null) return;
            UpdateSpace();
            // Detect GOI teleporting its player (save load / reset) and follow it.
            if ((PlayerRb.position - lastPlayerTarget).sqrMagnitude > 1.5f * 1.5f) SyncTo(PlayerRb.position);
            lastPlayerTarget = curFeet + PlayerOffset;
            PlayerRb.velocity = Vector2.zero;
            PlayerRb.angularVelocity = 0;
            // Set directly (not MovePosition): a queued MovePosition would be applied by the Physics2D.Simulate
            // that GOI runs while loading a save, snapping the restored player back to where we were.
            PlayerRb.rotation = 0;
            PlayerRb.position = lastPlayerTarget;
        }

        /// <summary>While climbing, measure how the rock face leans between her feet and head and tilt to match.</summary>
        void UpdateTilt()
        {
            var m = M;
            tiltTarget = 0;
            if (m.State != Madeline.StClimb) return;
            int top = m.Y - m.HitboxHeight + 1, bottom = m.Y - 1;
            int gTop = m.WallGap(top, 8), gBottom = m.WallGap(bottom, 8);
            if (gTop < 0 || gBottom < 0) return;
            float lean = Mathf.Atan2(gTop - gBottom, bottom - top) * Mathf.Rad2Deg;
            tiltTarget = Mathf.Clamp(-m.Facing * lean, -35f, 35f);
        }

        /// <summary>Editor teleport: move Madeline (and GOI's player body) to a world point.</summary>
        /// <summary>
        /// Remember where she last stood safely (a checkpoint every second of solid footing), and get her
        /// out by herself if she ends up inside rock or falls out of the world.
        /// </summary>
        // ---- Own position save (belt and braces next to GOI's save) -----------------------------------
        public string PositionPath;
        float positionSaveTimer;

        void SavePosition(float dt)
        {
            if (PositionPath == null) return;
            positionSaveTimer -= dt;
            if (positionSaveTimer > 0) return;
            positionSaveTimer = 1f;
            var m = M;
            if (m.Colliding || !m.OnGround) return;          // only remember spots she can stand on
            var f = FeetWorld();
            try
            {
                System.IO.File.WriteAllText(PositionPath, f.x.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " +
                                                         f.y.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception) { }
        }

        public static bool TryLoadPosition(string path, out Vector2 feet)
        {
            feet = Vector2.zero;
            try
            {
                if (!System.IO.File.Exists(path)) return false;
                var t = System.IO.File.ReadAllText(path).Split(' ');
                feet = new Vector2(float.Parse(t[0], System.Globalization.CultureInfo.InvariantCulture),
                                   float.Parse(t[1], System.Globalization.CultureInfo.InvariantCulture));
                return true;
            }
            catch (Exception) { return false; }
        }

        void TrackSafety(float dt)
        {
            var m = M;
            var feet = FeetWorld();
            SavePosition(dt);
            if (respawnBackTimer > 0) respawnBackTimer -= dt; else respawnBack = 0;

            if (m.Colliding)
            {
                stuckTimer += dt;
                if (stuckTimer > 0.25f)
                {
                    stuckTimer = 0;
                    if (!PushOut()) Respawn(false);
                }
                return;
            }
            stuckTimer = 0;

            // Fell below the lake bed / out of the mountain
            if (feet.y < -30f || Mathf.Abs(feet.x) > 400f) { Respawn(false); return; }

            if (m.OnGround && m.State == Madeline.StNormal)
            {
                safeTimer += dt;
                if (safeTimer >= 1f)
                {
                    safeTimer = 0;
                    if (safeSpots.Count == 0 || (safeSpots[safeSpots.Count - 1] - feet).sqrMagnitude > 0.25f)
                    {
                        safeSpots.Add(feet);
                        if (safeSpots.Count > 20) safeSpots.RemoveAt(0);
                    }
                }
            }
            else safeTimer = 0;
        }

        /// <summary>Nudge out of rock: nearest free spot within 16px, preferring up.</summary>
        bool PushOut()
        {
            var m = M;
            int x0 = m.X, y0 = m.Y;
            for (int r = 1; r <= 16; r++)
            {
                int[][] dirs = { new[] { 0, -r }, new[] { -r, 0 }, new[] { r, 0 }, new[] { -r, -r }, new[] { r, -r }, new[] { 0, r } };
                foreach (var d in dirs)
                {
                    m.X = x0 + d[0]; m.Y = y0 + d[1];
                    if (!m.Colliding) { m.Speed = Vector2.zero; return true; }
                }
            }
            m.X = x0; m.Y = y0;
            return false;
        }

        /// <summary>
        /// Respawn. toStart: back to the bottom of the mountain. Otherwise the most recent safe checkpoint;
        /// pressing again within 3 s steps one checkpoint further back each time.
        /// </summary>
        public void Respawn(bool toStart)
        {
            Vector2 target = MountainStart;
            if (!toStart)
            {
                // drop checkpoints that are no longer free (e.g. recorded while inside a hollow rock)
                int idx = safeSpots.Count - 1 - respawnBack;
                while (idx >= 0)
                {
                    target = safeSpots[idx];
                    if (FreeAt(target)) break;
                    idx--;
                }
                if (idx < 0) target = MountainStart;
                respawnBack = Mathf.Max(0, safeSpots.Count - idx);
                respawnBackTimer = 3f;
            }
            else safeSpots.Clear();
            TeleportTo(target + new Vector2(0, Ps * 2));
            M.Dashes = M.MaxDashes;
            M.Stamina = Madeline.ClimbMaxStamina;
            M.SpriteScale = new Vector2(0.6f, 1.4f);
            if (Audio != null) Audio.Play("event:/char/madeline/revive");
        }

        bool FreeAt(Vector2 feetWorld)
        {
            var size = new Vector2(8 * Ps - 0.02f, 11 * Ps - 0.02f);
            var c = feetWorld + new Vector2(0, 5.5f * Ps + Ps * 2);
            return Physics2D.OverlapBox(c, size, 0f, Solids.Mask) == null;
        }

        public void TeleportTo(Vector2 feetWorld)
        {
            SyncTo(feetWorld + PlayerOffset);
            if (PlayerRb != null) PlayerRb.position = lastPlayerTarget;
        }

        // ---- Animation (PlayerSprite / Player.UpdateSprite) ------------------------------------------
        void Play(string id, bool restart)
        {
            if (!restart && id == AnimId) return;
            AnimData a;
            if (!Assets.Anims.TryGetValue(id, out a) || a.Frames.Length == 0) return;
            anim = a;
            AnimId = id;
            frameIdx = 0;
            animTimer = 0;
            animating = true;
        }

        void AnimUpdate(float dt)
        {
            if (!animating || anim.Delay <= 0) return;
            animTimer += dt;
            if (animTimer < anim.Delay) return;
            animTimer -= anim.Delay;
            frameIdx++;
            if (frameIdx >= anim.Frames.Length)
            {
                if (anim.Goto == null) { frameIdx = anim.Frames.Length - 1; animating = false; return; }
                if (anim.Goto == AnimId) frameIdx = 0;
                else Play(anim.Goto, true);
            }
            OnAnimFrame();
        }

        bool Running { get { return AnimId == "runSlow" || AnimId == "runFast" || AnimId == "runWind" || AnimId == "runStumble"; } }

        void UpdateSprite()
        {
            var m = M;
            if (m.State == Madeline.StDash) { Play(m.Ducking ? "duck" : "dash", false); return; }
            if (m.Ducking && m.State == Madeline.StNormal) { Play("duck", false); return; }
            if (m.State == Madeline.StClimb)
            {
                if (m.LastClimbMove < 0) Play("climbup", false);
                else if (m.LastClimbMove > 0) Play("wallslide", false);
                else if (!m.CollideCheck(m.X + m.Facing, m.Y + 6)) Play("dangling", false);
                else if (Input.MoveX == -m.Facing) { if (AnimId != "climbLookBack") Play("climbLookBackStart", false); }
                else Play("wallslide", false);
                return;
            }
            if (m.OnGround)
            {
                m.FastJump = false;
                if (m.MoveX != 0 && m.CollideCheck(m.X + m.MoveX, m.Y)) Play("push", false);
                else if (Math.Abs(m.Speed.x) <= Madeline.RunAccel / 40f && m.MoveX == 0)
                {
                    if (!m.SolidPoint(m.X + m.Facing, m.Y + 2)) Play("edge", false);
                    else if (!m.SolidPoint(m.X - m.Facing, m.Y + 2)) Play("edgeBack", false);
                    else if (Input.MoveY == -1) Play("lookUp", false);
                    else if (AnimId == null || !AnimId.Contains("idle")) Play("idle", false);
                }
                else if (Math.Sign(m.Speed.x) == -m.MoveX && m.MoveX != 0)
                {
                    if (Math.Abs(m.Speed.x) > Madeline.MaxRun) Play("skid", false);
                    else if (AnimId != "skid") Play("flip", false);
                }
                else if (!Running)
                {
                    Play(Math.Abs(m.Speed.x) < Madeline.MaxRun * .5f ? "runSlow" : "runFast", false);
                }
            }
            else if (m.WallSlideDir != 0) Play("wallslide", false);
            else if (m.Speed.y < 0)
            {
                if (m.FastJump || Math.Abs(m.Speed.x) > Madeline.MaxRun) { m.FastJump = true; Play("jumpFast", false); }
                else Play("jumpSlow", false);
            }
            else
            {
                if (m.FastJump || Math.Abs(m.Speed.x) > Madeline.MaxRun) { m.FastJump = true; Play("fallFast", false); }
                else Play("fallSlow", false);
            }
        }

        AnimFrame CurFrame { get { return anim.Frames[Math.Min(frameIdx, anim.Frames.Length - 1)]; } }

        // ---- Hair (PlayerHair.AfterUpdate + Player.UpdateHair) ------------------------------------------
        void UpdateHair(float dt)
        {
            var m = M;
            if (m.Dashes == 0 && m.Dashes < m.MaxDashes) hairColor = Color.Lerp(hairColor, UsedHair, 6f * dt);
            else
            {
                if (lastDashes != m.Dashes) { hairColor = Color.white; hairFlashTimer = .12f; }
                else if (hairFlashTimer > 0) { hairColor = Color.white; hairFlashTimer -= dt; }
                else hairColor = m.Dashes >= 2 ? TwoDashHair : NormalHair;
            }
            lastDashes = m.Dashes;

            var f = CurFrame;
            var offset = new Vector2(f.HairOffset.x * m.Facing, f.HairOffset.y);
            hairNodes[0] = new Vector2(m.X, m.Y) + new Vector2(0, -9 * m.SpriteScale.y) + offset;
            var target = hairNodes[0] + new Vector2(-m.Facing * 0.5f, 2f);
            var prev = hairNodes[0];
            for (int i = 1; i < hairNodes.Length; i++)
            {
                float approach = (1 - (float)i / hairNodes.Length * 0.5f) * 64f;
                hairNodes[i] = Vector2.MoveTowards(hairNodes[i], target, approach * dt);
                var d = hairNodes[i] - prev;
                if (d.magnitude > 3) hairNodes[i] = prev + d.normalized * 3;
                target = hairNodes[i] + new Vector2(-m.Facing * 0.5f, 2f);
                prev = hairNodes[i];
            }
        }

        // ---- Rendering -------------------------------------------------------------------------------
        void Render(float alpha)
        {
            var m = M;
            var feet = Vector2.Lerp(prevFeet, curFeet, alpha);
            root.position = new Vector3(feet.x, feet.y, Z);
            tilt = Mathf.Lerp(tilt, tiltTarget, 1 - Mathf.Exp(-14f * Time.deltaTime));
            root.rotation = Quaternion.Euler(0, 0, tilt);
            flashClock += Time.deltaTime;

            var f = CurFrame;
            var bodyScale = new Vector3(m.Facing * m.SpriteScale.x, m.SpriteScale.y, 1);
            bool tiredFlash = m.IsTired && ((int)(flashClock / 0.1f) % 2 == 0);
            body.sprite = f.Sprite;
            body.transform.localScale = bodyScale;
            body.color = tiredFlash ? Color.red : Color.white;
            for (int i = 0; i < 4; i++)
            {
                var r = bodyOutline[i];
                r.sprite = f.Sprite;
                r.color = Color.black;
                r.transform.localScale = bodyScale;
                r.transform.localPosition = new Vector3(OutlineDirs[i].x * Ps, OutlineDirs[i].y * Ps, 0);
            }

            bool showHair = f.HasHair;
            for (int i = 0; i < 4; i++)
            {
                var spr = i == 0 ? Assets.BangsSprites[Math.Min(f.BangsFrame, Assets.BangsSprites.Length - 1)] : Assets.HairSprite;
                float s = 0.25f + (1 - (float)i / hairNodes.Length) * 0.75f;
                var scale = new Vector3((i == 0 ? m.Facing : s) * Math.Abs(m.SpriteScale.x), s * m.SpriteScale.y, 1);
                var local = new Vector3((hairNodes[i].x - m.X) * Ps, -(hairNodes[i].y - m.Y) * Ps, 0);
                hair[i].enabled = showHair;
                hair[i].sprite = spr;
                hair[i].color = hairColor;
                hair[i].transform.localScale = scale;
                hair[i].transform.localPosition = local;
                for (int k = 0; k < 4; k++)
                {
                    var b = hairBorder[i * 4 + k];
                    b.enabled = showHair;
                    b.sprite = spr;
                    b.color = Color.black;
                    b.transform.localScale = scale;
                    b.transform.localPosition = local + new Vector3(OutlineDirs[k].x * Ps, OutlineDirs[k].y * Ps, 0);
                }
            }

            if (Trails && m.DashTrailRequests != lastTrailRequests)
            {
                lastTrailRequests = m.DashTrailRequests;
                SpawnTrail();
            }
        }

        void SpawnTrail()
        {
            var t = new Trail();
            t.Go = new GameObject("MadelineTrail");
            t.Go.transform.position = root.position;
            t.C = M.Dashes == 0 ? UsedHair : hairColor;
            var list = new List<SpriteRenderer>();
            Action<SpriteRenderer> copy = src =>
            {
                if (!src.enabled) return;
                var go = new GameObject("t");
                go.transform.SetParent(t.Go.transform, false);
                go.transform.localPosition = src.transform.localPosition;
                go.transform.localScale = src.transform.localScale;
                var r = go.AddComponent<SpriteRenderer>();
                r.sprite = src.sprite;
                r.sharedMaterial = silhouetteMat;
                r.sortingOrder = 29000;
                list.Add(r);
            };
            for (int i = 3; i >= 0; i--) copy(hair[i]);
            copy(body);
            t.Rs = list.ToArray();
            trails.Add(t);
        }

        void UpdateTrails(float dt)
        {
            for (int i = trails.Count - 1; i >= 0; i--)
            {
                var t = trails[i];
                t.T += dt;
                float a = Mathf.Clamp01(1 - t.T / 0.5f) * 0.75f;
                if (a <= 0) { Destroy(t.Go); trails.RemoveAt(i); continue; }
                var c = new Color(t.C.r, t.C.g, t.C.b, a);
                foreach (var r in t.Rs) r.color = c;
            }
        }

        static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1f);
        }
    }
}
