using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CelesteOverIt
{
    [BepInPlugin("celesteoverit.madeline", "Celeste Over It", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        ConfigEntry<bool> modEnabled, infiniteStamina, trails, devAutoStart, devSelfTest, devLogState, devTestEnding;
        float devLogTimer;
        bool continuing;
        ConfigEntry<string> celesteDir, keyRespawn, keyJump, keyDash, keyGrab, keyLeft, keyRight, keyUp, keyDown;
        ConfigEntry<float> pixelSize, zOffset, cameraZoom, sfxVolume, slipAngle, gripTime, spaceGravity;
        ConfigEntry<int> edgeSupport, maxDashes, slopeStep, ledgeStep, climbContour, ledgeSpacing, surfaceIndex;
        ConfigEntry<bool> sound;
        static CelesteAudio audio;

        CelesteAssets assets;
        MadelineController controller;
        float helpTimer;
        bool showDebug;
        string error;

        void Awake()
        {
            modEnabled = Config.Bind("General", "Enabled", true, "Replace Diogenes with Madeline.");
            celesteDir = Config.Bind("General", "CelesteDirectory", "",
                "Folder containing Celeste.exe (sprites are loaded from its Content folder). Empty = auto-detect.");
            pixelSize = Config.Bind("Gameplay", "PixelSize", 0.075f,
                "Size of one Celeste pixel in Getting Over It world units (Madeline is 11px tall). 0.125 = Diogenes-sized, 0.075 = 40% smaller.");
            cameraZoom = Config.Bind("Visual", "CameraZoom", 1.38f, "How far the camera zooms out (1 = vanilla Getting Over It, 1.38 = 38% more view).");
            ledgeStep = Config.Bind("Gameplay", "LedgeStep", 4,
                "Bumps/lips up to this many pixels are walked over when there's level ground behind them.");
            climbContour = Config.Bind("Gameplay", "ClimbContour", 2,
                "While climbing, Madeline follows a rock face that leans up to this many pixels per pixel climbed (0 = pure Celeste).");
            spaceGravity = Config.Bind("Gameplay", "SpaceGravity", 0.05f,
                "Gravity in the space section above the antenna, as a fraction of normal (0.05 = hop asteroid to asteroid; 0 = Getting Over It's own zero-g).");
            gripTime = Config.Bind("Gameplay", "GripTime", 3f,
                "Seconds Madeline can hold onto a wall before she runs out of grip and slides down (0 = Celeste's normal stamina, about 11s).");
            edgeSupport = Config.Bind("Gameplay", "EdgeSupport", 2,
                "Teetering on a rock corner with only this many pixels of foot on it (over a steep drop) slides her off. 0 = Celeste rules (1px is enough to stand).");
            slipAngle = Config.Bind("Gameplay", "SlipAngle", 75f,
                "Rock at least this steep (degrees from horizontal) cannot be stood on: Madeline slides off with no dash/stamina refill and no jumping. 0 = off.");
            ledgeSpacing = Config.Bind("Gameplay", "LedgeSpacing", 48,
                "Adds Celeste wooden ledges to tall steep walls every this-many pixels of height (0 = off).");
            sound = Config.Bind("Sound", "Enabled", true, "Play Madeline's sound effects using Celeste's own FMOD audio.");
            sfxVolume = Config.Bind("Sound", "Volume", 0.8f, "Sound effect volume, 0..1.");
            surfaceIndex = Config.Bind("Sound", "SurfaceIndex", 3,
                "Celeste footstep surface (3 Dirt, 4 Snow, 5 Wood, 6 Stone bridge, 8 Brick, 33 Grass).");
            maxDashes = Config.Bind("Gameplay", "MaxDashes", 1, "Dashes per refill (1 = Madeline, 2 = pink-hair two dashes).");
            infiniteStamina = Config.Bind("Gameplay", "InfiniteStamina", false, "Assist mode: never run out of climbing stamina.");
            slopeStep = Config.Bind("Gameplay", "SlopeStep", 2,
                "Pixels Madeline may step up/down per pixel walked on sloped rocks (Celeste has no slopes; 0 = pure Celeste).");
            trails = Config.Bind("Visual", "DashTrails", true, "Draw dash after-images.");
            zOffset = Config.Bind("Visual", "ZOffset", 0f, "Depth tweak if Madeline draws behind scenery.");
            keyJump = Config.Bind("Controls", "Jump", "C,Space,JoystickButton0", "Comma-separated Unity KeyCodes.");
            keyDash = Config.Bind("Controls", "Dash", "X,LeftShift,K,JoystickButton2,JoystickButton1", "Comma-separated Unity KeyCodes.");
            keyGrab = Config.Bind("Controls", "Grab", "Z,V,LeftControl,J,JoystickButton5,JoystickButton4", "Comma-separated Unity KeyCodes.");
            keyRespawn = Config.Bind("Controls", "Respawn", "F5,JoystickButton6",
                "Respawn at the last safe spot (press again to go further back). Hold Shift: back to the start of the mountain.");
            keyLeft = Config.Bind("Controls", "Left", "LeftArrow,A", "");
            keyRight = Config.Bind("Controls", "Right", "RightArrow,D", "");
            keyUp = Config.Bind("Controls", "Up", "UpArrow,W", "");
            keyDown = Config.Bind("Controls", "Down", "DownArrow,S", "");
            devAutoStart = Config.Bind("Dev", "AutoStart", false, "Skip the title screen (development).");
            devTestEnding = Config.Bind("Dev", "TestEnding", false, "Launch Madeline into the space ending and log it (development).");
            devLogState = Config.Bind("Dev", "LogState", false, "Log Madeline's state (incl. ground slope) twice a second (development).");
            devSelfTest = Config.Bind("Dev", "SelfTest", false, "Run a scripted movement test and log results (development).");

            if (!modEnabled.Value) return;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Application.quitting += () => { if (audio != null) audio.Shutdown(); };
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Logger.LogInfo("Scene loaded: " + scene.name);
            // "New game" deletes GOI's save slots before loading the mountain; "Continue" keeps them.
            if (scene.name != "Loader")
                continuing = PlayerPrefs.GetString("SaveGame0").Length > 0 || PlayerPrefs.GetString("SaveGame1").Length > 0;
            if (scene.name == "Loader" && devAutoStart.Value) StartCoroutine(AutoStart());
            var pcType = Type.GetType("PlayerControl, Assembly-CSharp");
            if (pcType != null && FindObjectOfType(pcType) != null) StartCoroutine(TakeOver(pcType));
        }

        IEnumerator AutoStart()
        {
            yield return new WaitForSeconds(3f);
            var loader = FindObjectOfType(Type.GetType("Loader, Assembly-CSharp"));
            if (loader == null) yield break;
            // The self-test needs the real starting rocks, so it always begins a new game.
            bool canContinue = (bool)loader.GetType().GetField("canContinue", All).GetValue(loader);
            bool cont = canContinue && !devSelfTest.Value && !devTestEnding.Value;
            loader.GetType().GetMethod(cont ? "ContinueGame" : "StartGame", All).Invoke(loader, null);
        }

        IEnumerator TakeOver(Type pcType)
        {
            var pc = (MonoBehaviour)FindObjectOfType(pcType);
            var loadFinished = pcType.GetField("loadFinished", All);
            float waited = 0;
            // Let GOI restore the save / play its intro first.
            while (pc != null && !(bool)loadFinished.GetValue(pc) && waited < 30f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (pc == null) yield break;
            yield return new WaitForFixedUpdate();

            try
            {
                if (assets == null)
                {
                    string dir = CelesteAssets.FindCelesteDir(celesteDir.Value, Paths.GameRootPath);
                    if (dir == null) throw new Exception("Couldn't find Celeste. Set CelesteDirectory in BepInEx/config/celesteoverit.madeline.cfg");
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    assets = CelesteAssets.Load(dir, pixelSize.Value);
                    Logger.LogInfo("Loaded " + assets.Anims.Count + " Madeline animations from " + dir + " in " + sw.ElapsedMilliseconds + " ms");
                }
                Spawn(pc);
            }
            catch (Exception e)
            {
                error = e.Message;
                Logger.LogError(e);
            }
        }

        void Spawn(MonoBehaviour pc)
        {
            var player = pc.gameObject;
            var rb = player.GetComponent<Rigidbody2D>();

            // --- Retire Diogenes: hide him, freeze the hammer rig, silence his scripts. ---
            foreach (var r in player.GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer)) r.enabled = false;
            foreach (var body in player.GetComponentsInChildren<Rigidbody2D>(true))
                if (body != rb) body.simulated = false;
            foreach (var name in new[] { "PlayerSounds", "PoseControl", "HammerCollisions", "PotSounds", "RattlePot" })
            {
                var t = Type.GetType(name + ", Assembly-CSharp");
                if (t == null) continue;
                foreach (var c in player.GetComponentsInChildren(t, true)) ((Behaviour)c).enabled = false;
            }
            var fakeCursor = pc.GetType().GetField("fakeCursor", All).GetValue(pc) as Transform;
            if (fakeCursor != null) foreach (var r in fakeCursor.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
            pc.enabled = false;

            float playerMass = rb.mass;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0;
            rb.rotation = 0;

            // --- Madeline ---
            int pole = LayerMask.NameToLayer("Pole");
            int mask = Physics2D.GetLayerCollisionMask(player.layer) & ~(1 << player.layer);
            if (pole >= 0) mask &= ~(1 << pole);
            var go = new GameObject("CelesteOverIt");
            controller = go.AddComponent<MadelineController>();
            controller.Assets = assets;
            controller.Ps = pixelSize.Value;
            controller.Solids = new GoiSolids(mask);
            controller.Solids.Ps = pixelSize.Value;
            controller.M = new Madeline(controller.Solids);
            controller.M.MaxDashes = Math.Max(1, maxDashes.Value);
            controller.M.Dashes = controller.M.MaxDashes;
            controller.M.InfiniteStamina = infiniteStamina.Value;
            controller.M.SlopeStep = Math.Max(0, slopeStep.Value);
            controller.M.LedgeStep = Math.Max(0, ledgeStep.Value);
            controller.M.ClimbContour = Math.Max(0, climbContour.Value);
            controller.M.SlipAngle = Mathf.Clamp(slipAngle.Value, 0f, 90f);
            controller.M.GripTime = Mathf.Max(0f, gripTime.Value);
            controller.SpaceGravity = Mathf.Clamp(spaceGravity.Value, 0f, 1f);
            controller.Solids.EdgeSupport = Mathf.Clamp(edgeSupport.Value, 0, 7);
            controller.Zoom = Mathf.Clamp(cameraZoom.Value, 0.5f, 3f);
            // Generated wall ledges (optional) and the level's platforms share one jump-through set.
            controller.LedgeField = new Ledges(pixelSize.Value, ledgeSpacing.Value > 0 ? Math.Max(24, ledgeSpacing.Value) : 0, mask);
            controller.Solids.Ledges = controller.LedgeField;
            if (sound.Value)
            {
                if (audio == null || !audio.Alive)
                {
                    string dir = CelesteAssets.FindCelesteDir(celesteDir.Value, Paths.GameRootPath);
                    string host = CelesteAudio.PickHost(System.IO.Path.GetDirectoryName(Info.Location), dir);
                    audio = CelesteAudio.Start(host, dir, Mathf.Clamp01(sfxVolume.Value), Logger);
                }
                if (audio != null) audio.Surface = surfaceIndex.Value;
                controller.Audio = audio;
            }
            controller.Trails = trails.Value;
            controller.Z = player.transform.position.z + zOffset.Value;
            controller.PlayerRb = rb;
            controller.Input = new CelesteInput();
            controller.Input.JumpKeys = CelesteInput.ParseKeys(keyJump.Value);
            controller.Input.DashKeys = CelesteInput.ParseKeys(keyDash.Value);
            controller.Input.GrabKeys = CelesteInput.ParseKeys(keyGrab.Value);
            controller.Input.LeftKeys = CelesteInput.ParseKeys(keyLeft.Value);
            controller.Input.RightKeys = CelesteInput.ParseKeys(keyRight.Value);
            controller.Input.UpKeys = CelesteInput.ParseKeys(keyUp.Value);
            controller.Input.DownKeys = CelesteInput.ParseKeys(keyDown.Value);
            controller.RespawnKeys = CelesteInput.ParseKeys(keyRespawn.Value);
            controller.PlayerMass = playerMass > 0 ? playerMass : 52f;
            controller.Log = s => Logger.LogInfo(s);
            controller.Init(rb.position - controller.PlayerOffset);

            // Continue: put Madeline back exactly where she last stood. New game: forget it.
            controller.PositionPath = System.IO.Path.Combine(Paths.ConfigPath, "CelesteOverIt_position.txt");
            bool fromSave = continuing;
            Vector2 savedFeet;
            if (!fromSave)
            {
                try { System.IO.File.Delete(controller.PositionPath); } catch (Exception) { }
            }
            else if (MadelineController.TryLoadPosition(controller.PositionPath, out savedFeet))
            {
                controller.TeleportTo(savedFeet);
                Logger.LogInfo("Restored Madeline's position " + savedFeet);
            }

            var level = go.AddComponent<Level>();
            level.C = controller;
            level.A = assets;
            level.Log = Logger;
            level.LevelPath = System.IO.Path.Combine(Paths.ConfigPath, "CelesteOverIt_level.txt");
            level.ProgressPath = System.IO.Path.Combine(Paths.ConfigPath, "CelesteOverIt_strawberries.txt");
            level.Init();
            if (devSelfTest.Value)
            {
                MapDump.Write(System.IO.Path.Combine(Paths.BepInExRootPath, "CelesteOverIt_map.txt"), mask);
                SelfTest.Attach(controller, Logger);
            }
            else if (devTestEnding.Value) SelfTest.AttachEnding(controller, Logger);
            helpTimer = 10f;
            Logger.LogInfo("Madeline spawned at " + rb.position + " (collision mask " + mask + ")");
        }

        void Update()
        {
            if (UnityEngine.Input.GetKeyDown(KeyCode.F1)) helpTimer = helpTimer > 0 ? 0 : 1e9f;
            if (UnityEngine.Input.GetKeyDown(KeyCode.F3)) showDebug = !showDebug;
            if (helpTimer > 0) helpTimer -= Time.unscaledDeltaTime;
            if (devLogState.Value && controller != null)
            {
                devLogTimer -= Time.unscaledDeltaTime;
                if (devLogTimer <= 0)
                {
                    devLogTimer = 0.5f;
                    var m = controller.M;
                    Logger.LogInfo(string.Format("[state] pos {0},{1} feet {2} speed {3:0},{4:0} state {5} ground {6} slope {7:0.0} slipping {8} stamina {9:0}",
                        m.X, m.Y, controller.PxToWorld(m.X, m.Y), m.Speed.x, m.Speed.y, m.State, m.OnGround, m.GroundSlope, m.Slipping, m.Stamina));
                }
            }
        }

        void OnGUI()
        {
            var style = new GUIStyle(GUI.skin.label);
            style.fontSize = Math.Max(14, Screen.height / 50);
            if (error != null)
            {
                style.normal.textColor = Color.red;
                GUI.Label(new Rect(20, 20, Screen.width - 40, 200), "Celeste Over It: " + error, style);
                return;
            }
            if (controller == null) return;
            if (helpTimer > 0)
            {
                string text = "CELESTE OVER IT  -  Madeline climbs the mountain\n" +
                    "Move: Arrows/WASD   Jump: " + keyJump.Value.Split(',')[0] + "   Dash: " + keyDash.Value.Split(',')[0] +
                    "   Grab: " + keyGrab.Value.Split(',')[0] + "\nF5: respawn (stuck? press again to go further back, Shift+F5: start of mountain)\nF1: toggle this help   F2: level editor   F3: debug   Tab: strawberries";
                DrawShadowed(new Rect(20, 20, Screen.width - 40, 200), text, style);
            }
            if (showDebug)
            {
                var m = controller.M;
                string st = m.State == Madeline.StDash ? "Dash" : m.State == Madeline.StClimb ? "Climb" : "Normal";
                string text = string.Format("pos {0},{1}  speed {2:0},{3:0}  {4}  ground {5}  slope {8:0}°{9}  dashes {6}  stamina {7:0}",
                    m.X, m.Y, m.Speed.x, m.Speed.y, st, m.OnGround, m.Dashes, m.Stamina, m.GroundSlope, m.Slipping ? " SLIPPING" : "");
                DrawShadowed(new Rect(20, Screen.height - 60, Screen.width - 40, 40), text, style);
            }
        }

        static void DrawShadowed(Rect r, string text, GUIStyle style)
        {
            style.normal.textColor = Color.black;
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), text, style);
            style.normal.textColor = Color.white;
            GUI.Label(r, text, style);
        }
    }
}
