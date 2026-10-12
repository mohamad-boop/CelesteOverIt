using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CelesteOverIt
{
    /// <summary>
    /// Celeste things added on top of Getting Over It's mountain: green dash-refill crystals,
    /// strawberries and wooden jump-through platforms. Stored in a plain text file and editable in-game (F2).
    /// </summary>
    public class Level : MonoBehaviour
    {
        // Five strawberries of our own, in the sky the hammer could never reach. World units.
        const string DefaultLevel =
@"# Celeste Over It - level additions, in Getting Over It world units. Edit in-game with F2.
#   platform <centerX> <topY> <width in 8px tiles>
#   refill <x> <y>
#   strawberry <id> <x> <y>

# 1  Sky ladder above the starting shelf
platform -36.0 -2.1 3
platform -40.0 -0.3 3
platform -36.5 1.5 2
refill -40.0 4.0
strawberry 1 -44.0 6.5

# 2  Double dash off the top of the big boulder
refill -11.0 11.5
strawberry 2 -7.5 14.5

# 3  Out over the empty bowl, left of the first slope
platform 1.5 16.0 3
platform -2.5 17.8 2
refill -6.5 20.5
platform -10.5 22.0 2
strawberry 3 -14.0 25.5

# 4  Floating off the left of the cliff
platform 11.0 58.0 3
platform 7.0 60.5 2
strawberry 4 4.5 63.5

# 5  Above the big block
platform 30.0 83.4 3
platform 34.5 85.3 2
refill 31.0 88.0
strawberry 5 27.0 91.0
";

        class Item
        {
            public string Kind;      // platform | refill | strawberry
            public int Id, Tiles;
            public float X, Y;
            // runtime
            public GameObject Go;
            public SpriteRenderer Sr, Outline;
            public int LedgeId;
            public int State;        // refill: 0 ready, 1 used.  berry: 0 idle, 1 following, 2 collecting
            public float Timer, AnimT, GroundT;
            public Vector2 Pos;
        }

        public MadelineController C;
        public CelesteAssets A;
        public string LevelPath, ProgressPath;
        public BepInEx.Logging.ManualLogSource Log;

        readonly List<Item> items = new List<Item>();
        readonly HashSet<int> collected = new HashSet<int>();
        Sprite[] berry, refillIdle;
        Sprite refillOutline;
        float clock;
        float counterFlash;

        // editor
        public bool Editing;
        int tool = 1, platTiles = 3;
        float editZoom = 1f;
        Behaviour camControl;
        CursorLockMode prevLock;
        bool prevVisible;
        SpriteRenderer ghost;
        GameObject ghostPlatform;
        int ghostTiles = -1;

        static readonly int[] BerryIdle = { 0, 1, 2, 3, 4, 5, 6, 0, 1, 2, 3, 4, 5, 6, 0, 1, 2, 3, 4, 5, 6, 0, 1, 2, 3, 4, 5, 6, 0, 1, 2, 3, 4, 5, 6, 7 };
        static readonly int[] BerryCollect = { 7, 8, 9, 10, 11 };
        static readonly int[] BerryPoints = { 12, 13, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 14, 15, 16, 17, 18, 19 };

        float Ps { get { return C.Ps; } }
        public int Total { get { int n = 0; foreach (var i in items) if (i.Kind == "strawberry") n++; return n; } }
        public int Collected { get { int n = 0; foreach (var i in items) if (i.Kind == "strawberry" && collected.Contains(i.Id)) n++; return n; } }

        public void Init()
        {
            berry = A.Strip("collectables/strawberry/normal");
            refillIdle = A.Strip("objects/refill/idle");
            refillOutline = A.Single("objects/refill/outline");

            if (!File.Exists(LevelPath)) File.WriteAllText(LevelPath, DefaultLevel);
            if (File.Exists(ProgressPath))
                foreach (var l in File.ReadAllLines(ProgressPath))
                {
                    int id;
                    if (int.TryParse(l.Trim(), out id)) collected.Add(id);
                }
            foreach (var raw in File.ReadAllLines(LevelPath))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var t = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                try
                {
                    var it = new Item { Kind = t[0].ToLowerInvariant() };
                    if (it.Kind == "platform") { it.X = F(t[1]); it.Y = F(t[2]); it.Tiles = t.Length > 3 ? int.Parse(t[3]) : 3; }
                    else if (it.Kind == "refill") { it.X = F(t[1]); it.Y = F(t[2]); }
                    else if (it.Kind == "strawberry") { it.Id = int.Parse(t[1]); it.X = F(t[2]); it.Y = F(t[3]); }
                    else continue;
                    Spawn(it);
                }
                catch (Exception e) { Log.LogWarning("Bad level line '" + line + "': " + e.Message); }
            }
            Log.LogInfo("Level: " + items.Count + " items, strawberries " + Collected + "/" + Total);
        }

        static float F(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }
        static string S(float f) { return f.ToString("0.##", CultureInfo.InvariantCulture); }

        void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Celeste Over It - level additions, in Getting Over It world units. Edit in-game with F2.");
            sb.AppendLine("#   platform <centerX> <topY> <width in 8px tiles>   refill <x> <y>   strawberry <id> <x> <y>");
            foreach (var i in items)
            {
                if (i.Kind == "platform") sb.AppendLine("platform " + S(i.X) + " " + S(i.Y) + " " + i.Tiles);
                else if (i.Kind == "refill") sb.AppendLine("refill " + S(i.X) + " " + S(i.Y));
                else sb.AppendLine("strawberry " + i.Id + " " + S(i.X) + " " + S(i.Y));
            }
            File.WriteAllText(LevelPath, sb.ToString());
        }

        void SaveProgress()
        {
            var sb = new StringBuilder();
            foreach (var id in collected) sb.AppendLine(id.ToString());
            File.WriteAllText(ProgressPath, sb.ToString());
        }

        SpriteRenderer MakeSr(Transform parent, string name, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sharedMaterial = C.SpriteMaterial;
            sr.sortingOrder = order;
            return sr;
        }

        void Spawn(Item it)
        {
            it.Go = new GameObject("Celeste_" + it.Kind);
            it.Pos = new Vector2(it.X, it.Y);
            it.Go.transform.position = new Vector3(it.X, it.Y, C.Z);
            if (it.Kind == "platform")
            {
                float w = it.Tiles * 8 * Ps;
                var l = C.LedgeField.AddPlatform(it.X - w / 2, it.X + w / 2, it.Y);
                it.LedgeId = l.Id;
                BuildPlatformTiles(it.Go.transform, it.Tiles, -w / 2, 1f);
            }
            else if (it.Kind == "refill")
            {
                it.Sr = MakeSr(it.Go.transform, "crystal", 29800);
                it.Outline = MakeSr(it.Go.transform, "outline", 29800);
                it.Outline.sprite = refillOutline;
                it.Outline.enabled = false;
            }
            else
            {
                it.Sr = MakeSr(it.Go.transform, "berry", 29810);
                if (collected.Contains(it.Id))
                {
                    // Celeste shows collected berries as a faded "ghost"
                    it.Sr.color = new Color(0.55f, 0.65f, 1f, 0.45f);
                    it.State = 3;
                }
            }
            items.Add(it);
        }

        void BuildPlatformTiles(Transform parent, int tiles, float left, float alpha)
        {
            for (int i = 0; i < tiles; i++)
            {
                // Celeste JumpthruPlatform: ends use the free-end tiles (row 1); middles alternate rows.
                int col = tiles == 1 ? 1 : (i == 0 ? 0 : (i == tiles - 1 ? 2 : 1));
                int row = (i == 0 || i == tiles - 1) ? 1 : (i % 2);
                var sr = MakeSr(parent, "t", 29500);
                sr.sprite = A.Sub("objects/jumpthru/wood", col * 8, row * 8, 8, 8);
                sr.transform.localPosition = new Vector3(left + i * 8 * Ps, 0, 0);
                sr.color = new Color(1, 1, 1, alpha);
            }
        }

        void Remove(Item it)
        {
            if (it.Kind == "platform") C.LedgeField.RemovePlatform(it.LedgeId);
            Destroy(it.Go);
            items.Remove(it);
        }

        /// <summary>Dev: (kind, id/tiles, position) of every item, for the self-test tour.</summary>
        public List<KeyValuePair<string, Vector2>> DebugItems()
        {
            var list = new List<KeyValuePair<string, Vector2>>();
            foreach (var i in items)
                list.Add(new KeyValuePair<string, Vector2>(i.Kind + (i.Kind == "platform" ? ":" + i.Tiles : i.Kind == "strawberry" ? ":" + i.Id : ""), new Vector2(i.X, i.Y)));
            return list;
        }

        // ---- Runtime ----------------------------------------------------------------------------------
        void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.F2)) SetEditing(!Editing);
            if (Editing) EditorUpdate();
            float dt = Editing ? 0 : Time.deltaTime;
            clock += Time.deltaTime;
            if (counterFlash > 0) counterFlash -= Time.deltaTime;

            var m = C.M;
            var feet = C.PxToWorld(m.X, m.Y);
            float hw = 4 * Ps, hh = m.HitboxHeight * Ps;
            var rect = new Rect(feet.x - hw, feet.y, hw * 2, hh);

            for (int n = items.Count - 1; n >= 0; n--)
            {
                var it = items[n];
                if (it.Kind == "refill") UpdateRefill(it, dt, rect);
                else if (it.Kind == "strawberry") UpdateBerry(it, dt, rect, feet);
            }
        }

        void UpdateRefill(Item it, float dt, Rect player)
        {
            it.AnimT += dt;
            float bob = Mathf.Sin(clock * Mathf.PI * 2 * 0.6f) * 2 * Ps;
            it.Go.transform.position = new Vector3(it.X, it.Y + bob, C.Z);
            if (it.State == 1)
            {
                it.Timer -= dt;
                if (it.Timer <= 0 && !Editing)
                {
                    it.State = 0;
                    it.Sr.enabled = true;
                    it.Outline.enabled = false;
                    if (C.Audio != null) C.Audio.Play("event:/game/general/diamond_return");
                }
                return;
            }
            it.Sr.sprite = refillIdle[(int)(it.AnimT / 0.1f) % refillIdle.Length];
            if (Editing) return;
            float h = 8 * Ps;
            if (player.Overlaps(new Rect(it.X - h, it.Y - h, h * 2, h * 2)) && C.M.UseRefill())
            {
                it.State = 1;
                it.Timer = 2.5f;           // Celeste Refill.RespawnTime
                it.Sr.enabled = false;
                it.Outline.enabled = true;
                if (C.Audio != null) C.Audio.Play("event:/game/general/diamond_touch");
            }
        }

        void UpdateBerry(Item it, float dt, Rect player, Vector2 feet)
        {
            it.AnimT += dt;
            var m = C.M;
            if (it.State == 0 || it.State == 3)
            {
                it.Sr.sprite = berry[BerryIdle[(int)(it.AnimT / 0.1f) % BerryIdle.Length]];
                float bob = Mathf.Sin(clock * Mathf.PI * 2 * 0.5f) * 1.5f * Ps;
                it.Pos = new Vector2(it.X, it.Y + bob);
                if (!Editing && it.State == 0)
                {
                    float h = 7 * Ps;
                    if (player.Overlaps(new Rect(it.X - h, it.Y - h, h * 2, h * 2)))
                    {
                        it.State = 1;
                        it.GroundT = 0;
                        if (C.Audio != null) C.Audio.Play("event:/game/general/strawberry_touch");
                    }
                }
            }
            else if (it.State == 1)
            {
                // Follow Madeline until she's standing safely, then collect (Celeste's Strawberry + Follower).
                it.Sr.sprite = berry[(int)(it.AnimT / 0.1f) % 7];
                var target = feet + new Vector2(-m.Facing * 10 * Ps, 16 * Ps);
                it.Pos = Vector2.Lerp(it.Pos, target, 1 - Mathf.Exp(-6f * dt));
                if (m.OnGround && m.State != Madeline.StDash) it.GroundT += dt; else it.GroundT = 0;
                if (it.GroundT > 0.15f)
                {
                    it.State = 2;
                    it.AnimT = 0;
                    collected.Add(it.Id);
                    SaveProgress();
                    counterFlash = 4f;
                    if (C.Audio != null) C.Audio.PlayParams("event:/game/general/strawberry_get", "colour 0 count 0");
                }
            }
            else if (it.State == 2)
            {
                int f = (int)(it.AnimT / 0.07f);
                if (f < BerryCollect.Length) it.Sr.sprite = berry[BerryCollect[f]];
                else
                {
                    int p = (int)((it.AnimT - BerryCollect.Length * 0.07f) / 0.08f);
                    if (p >= BerryPoints.Length) { it.Sr.enabled = false; it.State = 4; }
                    else
                    {
                        it.Sr.sprite = berry[BerryPoints[p]];
                        it.Pos += new Vector2(0, 8 * Ps * dt);
                    }
                }
            }
            it.Go.transform.position = new Vector3(it.Pos.x, it.Pos.y, C.Z);
        }

        // ---- Editor -----------------------------------------------------------------------------------
        public void SetEditing(bool on)
        {
            Editing = on;
            C.Paused = on;
            if (camControl == null)
            {
                var t = Type.GetType("CameraControl, Assembly-CSharp");
                if (t != null) camControl = FindObjectOfType(t) as Behaviour;
            }
            if (on)
            {
                prevLock = Cursor.lockState;
                prevVisible = Cursor.visible;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                if (camControl != null) camControl.enabled = false;
                editZoom = 1f;
            }
            else
            {
                Cursor.lockState = prevLock;
                Cursor.visible = prevVisible;
                if (camControl != null) camControl.enabled = true;
                C.ExtraZoom = 1f;
                if (ghost != null) ghost.gameObject.SetActive(false);
                if (ghostPlatform != null) ghostPlatform.SetActive(false);
                foreach (var it in items) if (it.Kind == "refill" && it.State == 1) it.Timer = Mathf.Min(it.Timer, 0.01f);
            }
        }

        Vector2 MouseWorld()
        {
            var cam = Camera.main;
            var p = cam.ScreenToWorldPoint(new Vector3(UnityEngine.Input.mousePosition.x, UnityEngine.Input.mousePosition.y, -cam.transform.position.z));
            // snap to Celeste's pixel grid
            return new Vector2(Mathf.Round(p.x / Ps) * Ps, Mathf.Round(p.y / Ps) * Ps);
        }

        void EditorUpdate()
        {
            var inp = C.Input;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha1)) tool = 1;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha2)) tool = 2;
            if (UnityEngine.Input.GetKeyDown(KeyCode.Alpha3)) tool = 3;
            float wheel = UnityEngine.Input.mouseScrollDelta.y;
            if (wheel != 0) platTiles = Mathf.Clamp(platTiles + (wheel > 0 ? 1 : -1), 1, 10);
            if (UnityEngine.Input.GetKey(KeyCode.Q)) editZoom = Mathf.Min(4f, editZoom * (1 + Time.unscaledDeltaTime));
            if (UnityEngine.Input.GetKey(KeyCode.E)) editZoom = Mathf.Max(0.4f, editZoom / (1 + Time.unscaledDeltaTime));
            C.ExtraZoom = editZoom;

            // pan
            var cam = Camera.main;
            if (cam != null)
            {
                float speed = 12f * editZoom * (UnityEngine.Input.GetKey(KeyCode.LeftShift) ? 3f : 1f) * Time.unscaledDeltaTime;
                var d = Vector3.zero;
                if (AnyKey(inp.LeftKeys)) d.x -= speed;
                if (AnyKey(inp.RightKeys)) d.x += speed;
                if (AnyKey(inp.UpKeys)) d.y += speed;
                if (AnyKey(inp.DownKeys)) d.y -= speed;
                cam.transform.position += d;
            }

            var mw = MouseWorld();
            UpdateGhost(mw);
            if (UnityEngine.Input.GetMouseButtonDown(0)) Place(mw);
            if (UnityEngine.Input.GetMouseButtonDown(1)) DeleteNear(mw);
            if (UnityEngine.Input.GetKeyDown(KeyCode.T)) C.TeleportTo(mw);
            if (UnityEngine.Input.GetKeyDown(KeyCode.R) && UnityEngine.Input.GetKey(KeyCode.LeftControl)) ResetProgress();
        }

        public void ResetProgress()
        {
            collected.Clear();
            SaveProgress();
            foreach (var it in items)
                if (it.Kind == "strawberry") { it.State = 0; it.Sr.enabled = true; it.Sr.color = Color.white; it.Pos = new Vector2(it.X, it.Y); }
        }

        static bool AnyKey(KeyCode[] keys)
        {
            foreach (var k in keys) if (UnityEngine.Input.GetKey(k)) return true;
            return false;
        }

        void UpdateGhost(Vector2 mw)
        {
            if (ghost == null)
            {
                var go = new GameObject("EditorGhost");
                ghost = go.AddComponent<SpriteRenderer>();
                ghost.sharedMaterial = C.SpriteMaterial;
                ghost.sortingOrder = 31000;
            }
            if (tool == 3)
            {
                ghost.gameObject.SetActive(false);
                if (ghostPlatform == null || ghostTiles != platTiles)
                {
                    if (ghostPlatform != null) Destroy(ghostPlatform);
                    ghostPlatform = new GameObject("EditorGhostPlatform");
                    BuildPlatformTiles(ghostPlatform.transform, platTiles, -platTiles * 4 * Ps, 0.5f);
                    ghostTiles = platTiles;
                }
                ghostPlatform.SetActive(true);
                ghostPlatform.transform.position = new Vector3(mw.x, mw.y, C.Z);
            }
            else
            {
                if (ghostPlatform != null) ghostPlatform.SetActive(false);
                ghost.gameObject.SetActive(true);
                ghost.sprite = tool == 1 ? berry[0] : refillIdle[0];
                ghost.color = new Color(1, 1, 1, 0.5f);
                ghost.transform.position = new Vector3(mw.x, mw.y, C.Z);
            }
        }

        void Place(Vector2 mw)
        {
            var it = new Item { X = mw.x, Y = mw.y };
            if (tool == 1)
            {
                it.Kind = "strawberry";
                int max = 0;
                foreach (var i in items) if (i.Kind == "strawberry") max = Math.Max(max, i.Id);
                it.Id = max + 1;
            }
            else if (tool == 2) it.Kind = "refill";
            else { it.Kind = "platform"; it.Tiles = platTiles; }
            Spawn(it);
            Save();
        }

        void DeleteNear(Vector2 mw)
        {
            Item best = null;
            float bestD = 1.2f;
            foreach (var it in items)
            {
                float d;
                if (it.Kind == "platform")
                {
                    float hw = it.Tiles * 4 * Ps;
                    float dx = Mathf.Max(0, Mathf.Abs(mw.x - it.X) - hw);
                    d = new Vector2(dx, mw.y - (it.Y - 4 * Ps)).magnitude;
                }
                else d = (mw - new Vector2(it.X, it.Y)).magnitude;
                if (d < bestD) { bestD = d; best = it; }
            }
            if (best == null) return;
            Remove(best);
            Save();
        }

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label);
            style.fontSize = Math.Max(14, Screen.height / 45);
            style.alignment = TextAnchor.UpperRight;

            // Strawberry counter (top right)
            if (Total > 0 && (counterFlash > 0 || Editing || UnityEngine.Input.GetKey(KeyCode.Tab)))
            {
                var spr = berry[0];
                var tr = spr.textureRect;
                var tex = spr.texture;
                float size = style.fontSize * 2;
                var r = new Rect(Screen.width - 20 - size - 90, 16, size, size * tr.height / tr.width);
                GUI.DrawTextureWithTexCoords(r, tex, new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height));
                Shadow(new Rect(Screen.width - 110, 20, 90, 40), Collected + " / " + Total, style);
            }

            if (!Editing) return;
            style.alignment = TextAnchor.UpperLeft;
            string[] names = { "", "Strawberry", "Dash refill crystal", "Platform (" + platTiles + " tiles, mouse wheel)" };
            string text = "LEVEL EDITOR  (F2 to play)\n" +
                "Tool: [1] Strawberry  [2] Refill crystal  [3] Platform   ->  " + names[tool] + "\n" +
                "Left click: place   Right click: delete   T: teleport Madeline here\n" +
                "Arrows/WASD: pan (Shift = fast)   Q/E: zoom   Ctrl+R: reset collected strawberries\n" +
                "Saved to " + Path.GetFileName(LevelPath);
            Shadow(new Rect(20, Screen.height - style.fontSize * 7, Screen.width - 40, style.fontSize * 7), text, style);
        }

        static void Shadow(Rect r, string text, GUIStyle style)
        {
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
            style.normal.textColor = Color.white;
            GUI.Label(r, text, style);
        }
    }
}
