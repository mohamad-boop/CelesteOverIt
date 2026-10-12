using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;
using UnityEngine;

namespace CelesteOverIt
{
    /// <summary>One frame of an animation, with Madeline's per-frame hair metadata from Sprites.xml.</summary>
    public class AnimFrame
    {
        public Sprite Sprite;
        public bool HasHair = true;
        public Vector2 HairOffset;
        public int BangsFrame;
    }

    public class AnimData
    {
        public string Id;
        public AnimFrame[] Frames;
        public float Delay;
        public string Goto;
    }

    /// <summary>
    /// Loads Madeline straight out of the user's Celeste install: decodes the Gameplay atlas
    /// (Monocle "Packer" .meta + RLE .data), packs the player frames into a small texture, and
    /// builds the "player" animation set from Content/Graphics/Sprites.xml.
    /// </summary>
    public class CelesteAssets
    {
        public Texture2D Texture;
        public Dictionary<string, AnimData> Anims = new Dictionary<string, AnimData>();
        public Sprite HairSprite;
        public Sprite[] BangsSprites;

        /// <summary>Non-player textures also pulled from the atlas (packed untrimmed, at full frame size).</summary>
        public static readonly string[] Extras = { "objects/jumpthru/wood", "objects/refill/", "collectables/strawberry/normal" };

        static bool IsExtra(string name)
        {
            foreach (var p in Extras) if (name.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Numbered frames of an Extras texture (e.g. "objects/refill/idle"), pivoted at the frame centre.</summary>
        public Sprite[] Strip(string prefix)
        {
            var list = new List<Sprite>();
            for (int i = 0; ; i++)
            {
                Entry e;
                if (!entries.TryGetValue(prefix + i.ToString("00"), out e)) break;
                var rect = new Rect(e.PackX, texH - e.PackY - e.RealH, e.RealW, e.RealH);
                list.Add(Sprite.Create(Texture, rect, new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect));
            }
            return list.ToArray();
        }

        public Sprite Single(string name)
        {
            var e = entries[name];
            var rect = new Rect(e.PackX, texH - e.PackY - e.RealH, e.RealW, e.RealH);
            return Sprite.Create(Texture, rect, new Vector2(0.5f, 0.5f), ppu, 0, SpriteMeshType.FullRect);
        }

        class Entry
        {
            public string Name;
            public int X, Y, W, H, DrawX, DrawY, RealW, RealH;
            public int PackX, PackY;
            public bool Full;
            public int PackW { get { return Full ? RealW : W; } }
            public int PackH { get { return Full ? RealH : H; } }
        }

        Dictionary<string, Entry> entries;
        int texH;
        float ppu;

        /// <summary>A sub-rectangle (frame space, y down) of an Extras texture, pivoted at its top-left.</summary>
        public Sprite Sub(string name, int x, int y, int w, int h)
        {
            var e = entries[name];
            var rect = new Rect(e.PackX + x, texH - e.PackY - y - h, w, h);
            return Sprite.Create(Texture, rect, new Vector2(0, 1), ppu, 0, SpriteMeshType.FullRect);
        }

        public static string FindCelesteDir(string configured, string gameDir)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(configured)) candidates.Add(configured);
            var parent = Path.GetDirectoryName(gameDir.TrimEnd('\\', '/'));
            if (parent != null)
            {
                candidates.Add(Path.Combine(parent, "Celeste - Copy"));
                candidates.Add(Path.Combine(parent, "Celeste"));
            }
            candidates.Add(@"C:\Program Files (x86)\Steam\steamapps\common\Celeste");
            candidates.Add(@"C:\Program Files\Steam\steamapps\common\Celeste");
            candidates.Add(@"C:\Program Files\Epic Games\Celeste");
            candidates.Add(@"C:\Program Files (x86)\itch\apps\celeste");
            // Other Steam libraries (e.g. D:\SteamLibrary)
            foreach (var steam in new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" })
            {
                var vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                if (!File.Exists(vdf)) continue;
                try
                {
                    foreach (var line in File.ReadAllLines(vdf))
                    {
                        var t = line.Trim();
                        if (!t.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase)) continue;
                        var lib = t.Substring(6).Trim().Trim('"').Replace(@"\\", @"\");
                        candidates.Add(Path.Combine(lib, @"steamapps\common\Celeste"));
                    }
                }
                catch (Exception) { }
            }
            foreach (var c in candidates)
                if (File.Exists(Path.Combine(c, @"Content\Graphics\Atlases\Gameplay.meta"))) return c;
            return null;
        }

        public static CelesteAssets Load(string celesteDir, float pixelSize)
        {
            var assets = new CelesteAssets();
            string atlasDir = Path.Combine(celesteDir, @"Content\Graphics\Atlases");

            // --- Read atlas metadata ---------------------------------------------------------
            var entries = new Dictionary<string, Entry>();
            string dataName = null;
            using (var r = new BinaryReader(File.OpenRead(Path.Combine(atlasDir, "Gameplay.meta"))))
            {
                r.ReadInt32();
                r.ReadString();
                r.ReadInt32();
                int files = r.ReadInt16();
                for (int f = 0; f < files; f++)
                {
                    string dp = r.ReadString();
                    int count = r.ReadInt16();
                    for (int i = 0; i < count; i++)
                    {
                        var e = new Entry();
                        e.Name = r.ReadString().Replace('\\', '/');
                        e.Full = IsExtra(e.Name);
                        e.X = r.ReadInt16(); e.Y = r.ReadInt16(); e.W = r.ReadInt16(); e.H = r.ReadInt16();
                        e.DrawX = -r.ReadInt16(); e.DrawY = -r.ReadInt16(); e.RealW = r.ReadInt16(); e.RealH = r.ReadInt16();
                        // Madeline (and the ledge platform art) live in the first, and in vanilla only, data file.
                        if (f == 0 && (e.Name.StartsWith("characters/player/", StringComparison.Ordinal) || e.Full))
                        {
                            entries[e.Name] = e;
                            dataName = dp;
                        }
                    }
                }
            }
            if (dataName == null) throw new Exception("No characters/player/ sprites found in Gameplay.meta");

            // --- Pack the needed sprites into a small texture (shelf packing) -------------------
            var list = new List<Entry>(entries.Values);
            list.Sort((a, b) => b.PackH != a.PackH ? b.PackH.CompareTo(a.PackH) : string.CompareOrdinal(a.Name, b.Name));
            const int pad = 2, texW = 1024;
            int cx = pad, cy = pad, shelfH = 0;
            foreach (var e in list)
            {
                if (cx + e.PackW + pad > texW) { cx = pad; cy += shelfH + pad; shelfH = 0; }
                e.PackX = cx; e.PackY = cy;
                cx += e.PackW + pad;
                shelfH = Math.Max(shelfH, e.PackH);
            }
            int texH = 64;
            while (texH < cy + shelfH + pad) texH *= 2;

            // --- Decode the RLE atlas page, copying only the packed rectangles ------------------
            byte[] data = File.ReadAllBytes(Path.Combine(atlasDir, dataName + ".data"));
            int srcW = BitConverter.ToInt32(data, 0), srcH = BitConverter.ToInt32(data, 4);
            bool hasAlpha = data[8] != 0;
            // Map each source row to the entries that cover it, so we only write pixels we need.
            var rowEntries = new List<Entry>[srcH];
            foreach (var e in list)
                for (int y = e.Y; y < e.Y + e.H; y++)
                {
                    if (rowEntries[y] == null) rowEntries[y] = new List<Entry>();
                    rowEntries[y].Add(e);
                }

            var pixels = new Color32[texW * texH];
            int pos = 9, px = 0, total = srcW * srcH;
            while (px < total && pos < data.Length)
            {
                int run = data[pos];
                byte a, rr = 0, gg = 0, bb = 0;
                if (hasAlpha)
                {
                    a = data[pos + 1];
                    if (a > 0) { bb = data[pos + 2]; gg = data[pos + 3]; rr = data[pos + 4]; pos += 5; }
                    else pos += 2;
                }
                else
                {
                    a = 255; bb = data[pos + 1]; gg = data[pos + 2]; rr = data[pos + 3]; pos += 4;
                }
                if (a > 0)
                {
                    // Atlas is premultiplied; Unity sprite shaders want straight alpha.
                    if (a < 255)
                    {
                        rr = (byte)Math.Min(255, rr * 255 / a);
                        gg = (byte)Math.Min(255, gg * 255 / a);
                        bb = (byte)Math.Min(255, bb * 255 / a);
                    }
                    var col = new Color32(rr, gg, bb, a);
                    for (int i = 0; i < run; i++)
                    {
                        int p = px + i, sy = p / srcW, sx = p - sy * srcW;
                        var rowList = rowEntries[sy];
                        if (rowList == null) continue;
                        for (int k = 0; k < rowList.Count; k++)
                        {
                            var e = rowList[k];
                            if (sx < e.X || sx >= e.X + e.W) continue;
                            int dx = e.PackX + (sx - e.X), dy = e.PackY + (sy - e.Y);
                            if (e.Full) { dx += e.DrawX; dy += e.DrawY; }
                            pixels[(texH - 1 - dy) * texW + dx] = col;
                        }
                    }
                }
                px += run;
            }

            var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Point;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            tex.name = "CelesteOverIt_Madeline";
            assets.Texture = tex;
            assets.entries = entries;
            assets.texH = texH;

            float ppu = 1f / pixelSize;
            assets.ppu = ppu;
            Func<Entry, float, float, Sprite> makeSprite = (e, ox, oy) =>
            {
                var rect = new Rect(e.PackX, texH - e.PackY - e.H, e.W, e.H);
                // Monocle origin (ox, oy) is in frame space (y down); convert to a pivot inside the trimmed rect.
                var pivot = new Vector2((ox - e.DrawX) / e.W, (e.H - (oy - e.DrawY)) / e.H);
                var s = Sprite.Create(tex, rect, pivot, ppu, 0, SpriteMeshType.FullRect);
                s.name = e.Name;
                return s;
            };

            // --- Hair -----------------------------------------------------------------------------
            Entry hair;
            if (!entries.TryGetValue("characters/player/hair00", out hair)) throw new Exception("hair00 missing");
            assets.HairSprite = makeSprite(hair, 5, 5);
            var bangs = new List<Sprite>();
            for (int i = 0; ; i++)
            {
                Entry b;
                if (!entries.TryGetValue("characters/player/bangs" + i.ToString("00"), out b)) break;
                bangs.Add(makeSprite(b, 5, 5));
            }
            assets.BangsSprites = bangs.ToArray();

            // --- Animations from Sprites.xml --------------------------------------------------------
            var doc = new XmlDocument();
            doc.Load(Path.Combine(celesteDir, @"Content\Graphics\Sprites.xml"));
            var player = doc.SelectSingleNode("/Sprites/player");
            float originX = 16, originY = 32;
            var originNode = player.SelectSingleNode("Origin") as XmlElement;
            if (originNode != null)
            {
                originX = float.Parse(originNode.GetAttribute("x"), CultureInfo.InvariantCulture);
                originY = float.Parse(originNode.GetAttribute("y"), CultureInfo.InvariantCulture);
            }

            var hairMeta = new Dictionary<string, string[]>();
            foreach (XmlElement fr in player.SelectNodes("Metadata/Frames"))
                hairMeta[fr.GetAttribute("path")] = fr.GetAttribute("hair").Split('|');

            var spriteCache = new Dictionary<string, Sprite>();
            foreach (XmlNode node in player.ChildNodes)
            {
                var el = node as XmlElement;
                if (el == null || (el.Name != "Anim" && el.Name != "Loop")) continue;
                string id = el.GetAttribute("id"), path = el.GetAttribute("path");

                // Subtextures of the path, in index order (Monocle tries increasing zero padding).
                var sub = new List<Entry>();
                for (int i = 0; ; i++)
                {
                    Entry found = null;
                    string idx = i.ToString();
                    for (int padN = 0; padN < 4 && found == null; padN++)
                    {
                        entries.TryGetValue("characters/player/" + path + idx, out found);
                        idx = "0" + idx;
                    }
                    if (found == null) break;
                    sub.Add(found);
                }
                if (sub.Count == 0) continue;

                var indices = new List<int>();
                string framesAttr = el.GetAttribute("frames");
                if (string.IsNullOrEmpty(framesAttr)) for (int i = 0; i < sub.Count; i++) indices.Add(i);
                else
                {
                    foreach (var part in framesAttr.Split(','))
                    {
                        var p = part.Trim();
                        int dash = p.IndexOf('-'), star = p.IndexOf('*');
                        if (star > 0)
                        {
                            // "n*k" = frame n repeated k times
                            int n = int.Parse(p.Substring(0, star)), k = int.Parse(p.Substring(star + 1));
                            for (int i = 0; i < k; i++) indices.Add(n);
                        }
                        else if (dash > 0)
                        {
                            int a = int.Parse(p.Substring(0, dash)), b = int.Parse(p.Substring(dash + 1));
                            for (int i = a; a <= b ? i <= b : i >= b; i += a <= b ? 1 : -1) indices.Add(i);
                        }
                        else indices.Add(int.Parse(p));
                    }
                }

                string[] meta;
                hairMeta.TryGetValue(path, out meta);
                var frames = new List<AnimFrame>();
                foreach (int i in indices)
                {
                    if (i < 0 || i >= sub.Count) continue;
                    var e = sub[i];
                    Sprite s;
                    if (!spriteCache.TryGetValue(e.Name, out s)) spriteCache[e.Name] = s = makeSprite(e, originX, originY);
                    var af = new AnimFrame();
                    af.Sprite = s;
                    if (meta != null && i < meta.Length) ParseHair(meta[i], af);
                    frames.Add(af);
                }

                var anim = new AnimData();
                anim.Id = id;
                anim.Frames = frames.ToArray();
                string delay = el.GetAttribute("delay");
                anim.Delay = string.IsNullOrEmpty(delay) ? 0f : float.Parse(delay, CultureInfo.InvariantCulture);
                anim.Goto = el.Name == "Loop" ? id : (el.HasAttribute("goto") ? el.GetAttribute("goto") : null);
                assets.Anims[id] = anim;
            }
            return assets;
        }

        static void ParseHair(string s, AnimFrame af)
        {
            s = s.Trim();
            if (s.Length == 0) return;
            if (s == "x") { af.HasHair = false; return; }
            int colon = s.IndexOf(':');
            if (colon >= 0)
            {
                af.BangsFrame = int.Parse(s.Substring(colon + 1).Trim());
                s = s.Substring(0, colon);
            }
            var xy = s.Split(',');
            af.HairOffset = new Vector2(int.Parse(xy[0].Trim()), int.Parse(xy[1].Trim()));
        }
    }
}

