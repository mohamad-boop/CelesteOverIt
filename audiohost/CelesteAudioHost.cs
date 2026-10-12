// CelesteAudioHost: a tiny 32-bit process that plays Celeste's own FMOD Studio events.
// Celeste ships 32-bit FMOD (1.10.14) while Getting Over It is 64-bit, so the mod runs this
// helper next to the game and sends it one command per line on stdin:
//   p <event> [param value]...        play a one-shot
//   l <id> <event> [param value]...   start a loop under <id>
//   s <id>                            stop loop <id> (with fade out)
//   v <volume>                        master volume 0..1
//   w <event>                         preload an event's samples
//   q                                 quit
// The helper exits on its own when the game closes the pipe.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

static class Fmod
{
    const string S = "fmodstudio.dll";
    public const uint Version = 0x00011014;
    [DllImport(S)] public static extern int FMOD_Studio_System_Create(out IntPtr system, uint headerVersion);
    [DllImport(S)] public static extern int FMOD_Studio_System_Initialize(IntPtr system, int maxChannels, uint studioFlags, uint flags, IntPtr extra);
    [DllImport(S)] public static extern int FMOD_Studio_System_LoadBankFile(IntPtr system, byte[] file, uint flags, out IntPtr bank);
    [DllImport(S)] public static extern int FMOD_Studio_System_GetEvent(IntPtr system, byte[] path, out IntPtr desc);
    [DllImport(S)] public static extern int FMOD_Studio_System_GetBus(IntPtr system, byte[] path, out IntPtr bus);
    [DllImport(S)] public static extern int FMOD_Studio_System_Update(IntPtr system);
    [DllImport(S)] public static extern int FMOD_Studio_System_Release(IntPtr system);
    [DllImport(S)] public static extern int FMOD_Studio_Bus_SetVolume(IntPtr bus, float volume);
    [DllImport(S)] public static extern int FMOD_Studio_EventDescription_CreateInstance(IntPtr desc, out IntPtr inst);
    [DllImport(S)] public static extern int FMOD_Studio_EventDescription_LoadSampleData(IntPtr desc);
    [DllImport(S)] public static extern int FMOD_Studio_EventInstance_SetParameterValue(IntPtr inst, byte[] name, float value);
    [DllImport(S)] public static extern int FMOD_Studio_EventInstance_Start(IntPtr inst);
    [DllImport(S)] public static extern int FMOD_Studio_EventInstance_Stop(IntPtr inst, int mode);
    [DllImport(S)] public static extern int FMOD_Studio_EventInstance_Release(IntPtr inst);

    public static byte[] U(string s) { return Encoding.UTF8.GetBytes(s + "\0"); }
}

static class Program
{
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern bool SetDllDirectory(string path);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string path);

    static IntPtr system;
    static readonly Dictionary<string, IntPtr> descs = new Dictionary<string, IntPtr>();
    static readonly Dictionary<string, IntPtr> loops = new Dictionary<string, IntPtr>();
    static StreamWriter log;

    static int Main(string[] args)
    {
        string celeste = args.Length > 0 ? args[0] : ".";
        float volume = args.Length > 1 ? float.Parse(args[1], CultureInfo.InvariantCulture) : 1f;
        try
        {
            log = new StreamWriter(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CelesteAudioHost.log"), false);
            log.AutoFlush = true;
        }
        catch (Exception) { }
        try
        {
            // FMOD lives next to Celeste.exe (XNA build) or in a lib folder (FNA builds).
            string fmodDir = celeste;
            foreach (var sub in new[] { "", "lib64", "x64", "lib", "x86" })
            {
                var d = Path.Combine(celeste, sub);
                if (File.Exists(Path.Combine(d, "fmodstudio.dll")) && IntPtr.Size == (Is64(Path.Combine(d, "fmodstudio.dll")) ? 8 : 4)) { fmodDir = d; break; }
            }
            Log("using FMOD from " + fmodDir + " (" + (IntPtr.Size * 8) + "-bit host)");
            SetDllDirectory(fmodDir);
            LoadLibrary(Path.Combine(fmodDir, "fmod.dll"));
            LoadLibrary(Path.Combine(fmodDir, "fmodstudio.dll"));
            Check(Fmod.FMOD_Studio_System_Create(out system, Fmod.Version), "create");
            Check(Fmod.FMOD_Studio_System_Initialize(system, 256, 0, 0, IntPtr.Zero), "initialize");
            string banks = Path.Combine(celeste, @"Content\FMOD\Desktop");
            foreach (var b in new[] { "Master Bank.bank", "Master Bank.strings.bank", "sfx.bank" })
            {
                IntPtr bank;
                Check(Fmod.FMOD_Studio_System_LoadBankFile(system, Fmod.U(Path.Combine(banks, b)), 0, out bank), b);
            }
            SetVolume(volume);
            Log("ready");
        }
        catch (Exception e)
        {
            Log("startup failed: " + e.Message);
            return 1;
        }

        var queue = new ConcurrentQueue<string>();
        bool eof = false;
        var reader = new Thread(() =>
        {
            try
            {
                string line;
                while ((line = Console.In.ReadLine()) != null) queue.Enqueue(line);
            }
            catch (Exception) { }
            eof = true;
        });
        reader.IsBackground = true;
        reader.Start();

        bool quit = false;
        while (!quit && (!eof || !queue.IsEmpty))
        {
            string cmd;
            while (queue.TryDequeue(out cmd))
            {
                try { if (!Handle(cmd)) quit = true; }
                catch (Exception e) { Log("error in '" + cmd + "': " + e.Message); }
            }
            Fmod.FMOD_Studio_System_Update(system);
            Thread.Sleep(4);
        }
        foreach (var l in loops.Values) Fmod.FMOD_Studio_EventInstance_Stop(l, 1);
        Fmod.FMOD_Studio_System_Update(system);
        Fmod.FMOD_Studio_System_Release(system);
        return 0;
    }

    static bool Handle(string cmd)
    {
        var t = cmd.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (t.Length == 0) return true;
        switch (t[0])
        {
            case "q": return false;
            case "v": SetVolume(F(t[1])); break;
            case "w": Desc(t[1]); break;
            case "p":
            {
                var inst = Create(t[1], t, 2);
                if (inst != IntPtr.Zero) { Fmod.FMOD_Studio_EventInstance_Start(inst); Fmod.FMOD_Studio_EventInstance_Release(inst); }
                break;
            }
            case "l":
            {
                if (loops.ContainsKey(t[1])) break;
                var inst = Create(t[2], t, 3);
                if (inst != IntPtr.Zero) { Fmod.FMOD_Studio_EventInstance_Start(inst); loops[t[1]] = inst; }
                break;
            }
            case "s":
            {
                IntPtr inst;
                if (loops.TryGetValue(t[1], out inst))
                {
                    Fmod.FMOD_Studio_EventInstance_Stop(inst, 0);
                    Fmod.FMOD_Studio_EventInstance_Release(inst);
                    loops.Remove(t[1]);
                }
                break;
            }
        }
        return true;
    }

    static IntPtr Create(string path, string[] t, int paramStart)
    {
        var desc = Desc(path);
        if (desc == IntPtr.Zero) return IntPtr.Zero;
        IntPtr inst;
        if (Fmod.FMOD_Studio_EventDescription_CreateInstance(desc, out inst) != 0) return IntPtr.Zero;
        for (int i = paramStart; i + 1 < t.Length; i += 2)
            Fmod.FMOD_Studio_EventInstance_SetParameterValue(inst, Fmod.U(t[i]), F(t[i + 1]));
        return inst;
    }

    static IntPtr Desc(string path)
    {
        IntPtr d;
        if (descs.TryGetValue(path, out d)) return d;
        int r = Fmod.FMOD_Studio_System_GetEvent(system, Fmod.U(path), out d);
        if (r != 0) { Log("missing event " + path + " (" + r + ")"); d = IntPtr.Zero; }
        else Fmod.FMOD_Studio_EventDescription_LoadSampleData(d);
        descs[path] = d;
        return d;
    }

    static void SetVolume(float v)
    {
        IntPtr bus;
        if (Fmod.FMOD_Studio_System_GetBus(system, Fmod.U("bus:/"), out bus) == 0) Fmod.FMOD_Studio_Bus_SetVolume(bus, v);
    }

    /// <summary>True if the DLL is a 64-bit (x64) PE image.</summary>
    public static bool Is64(string dll)
    {
        using (var r = new BinaryReader(File.OpenRead(dll)))
        {
            r.BaseStream.Seek(0x3C, SeekOrigin.Begin);
            int pe = r.ReadInt32();
            r.BaseStream.Seek(pe + 4, SeekOrigin.Begin);
            return r.ReadUInt16() == 0x8664;
        }
    }

    static float F(string s) { return float.Parse(s, CultureInfo.InvariantCulture); }
    static void Check(int r, string what) { if (r != 0) throw new Exception(what + " failed with FMOD error " + r); }
    static void Log(string s) { if (log != null) log.WriteLine(DateTime.Now.ToString("HH:mm:ss ") + s); }
}
