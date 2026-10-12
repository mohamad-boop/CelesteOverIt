using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using BepInEx.Logging;

namespace CelesteOverIt
{
    /// <summary>
    /// Plays Celeste's real FMOD sound events. Celeste's FMOD is 32-bit and this game is 64-bit, so the
    /// sounds are played by CelesteAudioHost.exe (a 32-bit helper next to this plugin) that we talk to
    /// over its stdin. If the game dies, the pipe closes and the helper quits by itself.
    /// </summary>
    public class CelesteAudio
    {
        public const string Jump = "event:/char/madeline/jump";
        public const string JumpSuper = "event:/char/madeline/jump_super";
        public const string JumpSuperSlide = "event:/char/madeline/jump_superslide";
        public const string JumpSuperWall = "event:/char/madeline/jump_superwall";
        public const string JumpWallLeft = "event:/char/madeline/jump_wall_left";
        public const string JumpWallRight = "event:/char/madeline/jump_wall_right";
        public const string JumpClimbLeft = "event:/char/madeline/jump_climb_left";
        public const string JumpClimbRight = "event:/char/madeline/jump_climb_right";
        public const string DashRedLeft = "event:/char/madeline/dash_red_left";
        public const string DashRedRight = "event:/char/madeline/dash_red_right";
        public const string DashPinkLeft = "event:/char/madeline/dash_pink_left";
        public const string DashPinkRight = "event:/char/madeline/dash_pink_right";
        public const string Grab = "event:/char/madeline/grab";
        public const string GrabLetGo = "event:/char/madeline/grab_letgo";
        public const string ClimbLedge = "event:/char/madeline/climb_ledge";
        public const string Handhold = "event:/char/madeline/handhold";
        public const string Landing = "event:/char/madeline/landing";
        public const string Footstep = "event:/char/madeline/footstep";
        public const string WallSlide = "event:/char/madeline/wallslide";
        public const string Duck = "event:/char/madeline/duck";
        public const string Stand = "event:/char/madeline/stand";

        static readonly string[] Preload =
        {
            Jump, JumpSuper, JumpSuperSlide, JumpSuperWall, JumpWallLeft, JumpWallRight, JumpClimbLeft, JumpClimbRight,
            DashRedLeft, DashRedRight, DashPinkLeft, DashPinkRight, Grab, GrabLetGo, ClimbLedge, Handhold,
            Landing, Footstep, WallSlide, Duck, Stand,
            "event:/game/general/diamond_touch", "event:/game/general/diamond_return",
            "event:/game/general/strawberry_touch", "event:/game/general/strawberry_get"
        };

        Process proc;
        StreamWriter stdin;
        readonly ManualLogSource log;
        public int Surface = 3;

        CelesteAudio(ManualLogSource log) { this.log = log; }

        /// <summary>
        /// Pick CelesteAudioHost.exe (32-bit) or CelesteAudioHost64.exe to match the bitness of the
        /// user's Celeste FMOD (the XNA build ships 32-bit FMOD, FNA builds ship 64-bit).
        /// </summary>
        public static string PickHost(string pluginDir, string celesteDir)
        {
            foreach (var sub in new[] { "", "lib64", "x64", "lib", "x86" })
            {
                var dll = Path.Combine(Path.Combine(celesteDir, sub), "fmodstudio.dll");
                if (!File.Exists(dll)) continue;
                try
                {
                    using (var r = new BinaryReader(File.OpenRead(dll)))
                    {
                        r.BaseStream.Seek(0x3C, SeekOrigin.Begin);
                        int pe = r.ReadInt32();
                        r.BaseStream.Seek(pe + 4, SeekOrigin.Begin);
                        bool is64 = r.ReadUInt16() == 0x8664;
                        return Path.Combine(pluginDir, is64 ? "CelesteAudioHost64.exe" : "CelesteAudioHost.exe");
                    }
                }
                catch (Exception) { }
            }
            return Path.Combine(pluginDir, "CelesteAudioHost.exe");
        }

        public static CelesteAudio Start(string hostExe, string celesteDir, float volume, ManualLogSource log)
        {
            if (!File.Exists(hostExe)) { log.LogWarning("Sound disabled: " + hostExe + " not found"); return null; }
            var a = new CelesteAudio(log);
            try
            {
                var psi = new ProcessStartInfo(hostExe,
                    "\"" + celesteDir.TrimEnd('\\') + "\" " + volume.ToString(CultureInfo.InvariantCulture));
                psi.UseShellExecute = false;
                psi.RedirectStandardInput = true;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = Path.GetDirectoryName(hostExe);
                a.proc = Process.Start(psi);
                a.stdin = a.proc.StandardInput;
                a.stdin.AutoFlush = true;
                foreach (var e in Preload) a.Send("w " + e);
                log.LogInfo("Celeste audio host started (pid " + a.proc.Id + ")");
                return a;
            }
            catch (Exception e)
            {
                log.LogWarning("Sound disabled: " + e.Message);
                return null;
            }
        }

        public bool Alive { get { return stdin != null && proc != null && !proc.HasExited; } }

        void Send(string line)
        {
            if (stdin == null) return;
            try { stdin.WriteLine(line); }
            catch (Exception e)
            {
                log.LogWarning("Audio host went away: " + e.Message);
                stdin = null;
            }
        }

        public void Play(string ev) { Send("p " + ev); }
        public void PlaySurface(string ev) { Send("p " + ev + " surface_index " + Surface); }
        public void PlayParams(string ev, string paramsAndValues) { Send("p " + ev + " " + paramsAndValues); }
        public void Loop(string id, string ev) { Send("l " + id + " " + ev + " surface_index " + Surface); }
        public void StopLoop(string id) { Send("s " + id); }
        public void Volume(float v) { Send("v " + v.ToString(CultureInfo.InvariantCulture)); }

        public void Shutdown()
        {
            Send("q");
            try { if (stdin != null) stdin.Close(); } catch (Exception) { }
            stdin = null;
        }
    }
}
