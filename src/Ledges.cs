using System;
using System.Collections.Generic;
using UnityEngine;

namespace CelesteOverIt
{
    /// <summary>
    /// Getting Over It's mountain was built for a hammer, not for a 11px climber with 110 stamina.
    /// This finds tall, steep rock faces near Madeline and bolts Celeste-style wooden jump-through
    /// ledges onto them at regular heights, so every big wall has places to stand and recover.
    /// Generated lazily in world-space chunks; deterministic, so ledges are always in the same spots.
    /// </summary>
    public class Ledges
    {
        public struct Ledge
        {
            public float XMin, XMax, Top, Bottom;
            public int Open;              // +1: wall on the left, ledge sticks out to the right; -1: mirrored; 0: free-floating
            public int Id;                // 0 for generated ledges, >0 for editor platforms
        }

        int nextId = 1;

        /// <summary>Add a free-floating jump-through platform (level editor / built-in strawberry routes).</summary>
        public Ledge AddPlatform(float xMin, float xMax, float top)
        {
            var l = new Ledge();
            l.XMin = xMin; l.XMax = xMax; l.Top = top; l.Bottom = top - CollisionThickness * ps;
            l.Id = nextId++;
            Register(l);
            return l;
        }

        public void RemovePlatform(int id)
        {
            foreach (var list in byChunk.Values) list.RemoveAll(x => x.Id == id);
        }

        void Register(Ledge l)
        {
            float cs = ChunkSize;
            for (int cx = Mathf.FloorToInt(l.XMin / cs); cx <= Mathf.FloorToInt(l.XMax / cs); cx++)
                for (int cy = Mathf.FloorToInt(l.Bottom / cs); cy <= Mathf.FloorToInt(l.Top / cs); cy++)
                {
                    List<Ledge> list;
                    if (!byChunk.TryGetValue(Key(cx, cy), out list)) byChunk[Key(cx, cy)] = list = new List<Ledge>();
                    list.Add(l);
                }
        }

        const int ChunkPx = 128;
        const int LedgeOut = 14, LedgeIn = 2, CollisionThickness = 4;

        readonly float ps;
        readonly int spacing;
        ContactFilter2D filter;
        readonly Collider2D[] buf = new Collider2D[4];
        readonly HashSet<long> done = new HashSet<long>();
        readonly Dictionary<long, List<Ledge>> byChunk = new Dictionary<long, List<Ledge>>();
        readonly Queue<long> pending = new Queue<long>();
        readonly HashSet<long> queued = new HashSet<long>();
        public Action<Ledge> OnCreated;
        public int Count;

        public Ledges(float pixelSize, int spacingPx, int layerMask)
        {
            ps = pixelSize;
            spacing = spacingPx;
            filter = new ContactFilter2D();
            filter.useTriggers = false;
            filter.SetLayerMask(layerMask);
        }

        float ChunkSize { get { return ChunkPx * ps; } }
        static long Key(int cx, int cy) { return ((long)cx << 32) ^ (uint)cy; }

        /// <summary>Queue chunks around Madeline and generate a couple per frame.</summary>
        public void Ensure(Vector2 pos, int radius, int budget)
        {
            if (spacing <= 0) return;
            int cx = Mathf.FloorToInt(pos.x / ChunkSize), cy = Mathf.FloorToInt(pos.y / ChunkSize);
            for (int r = 0; r <= radius; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
                        long k = Key(cx + dx, cy + dy);
                        if (done.Contains(k) || queued.Contains(k)) continue;
                        queued.Add(k);
                        pending.Enqueue(k);
                    }
            for (int i = 0; i < budget && pending.Count > 0; i++)
            {
                long k = pending.Dequeue();
                queued.Remove(k);
                if (done.Add(k)) Generate((int)(k >> 32), (int)(uint)k);
            }
        }

        public bool Overlaps(float xMin, float yMin, float xMax, float yMax)
        {
            float c = ChunkSize;
            int cx0 = Mathf.FloorToInt(xMin / c), cx1 = Mathf.FloorToInt(xMax / c);
            int cy0 = Mathf.FloorToInt(yMin / c), cy1 = Mathf.FloorToInt(yMax / c);
            for (int cx = cx0; cx <= cx1; cx++)
                for (int cy = cy0; cy <= cy1; cy++)
                {
                    List<Ledge> list;
                    if (!byChunk.TryGetValue(Key(cx, cy), out list)) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var l = list[i];
                        if (xMax > l.XMin && xMin < l.XMax && yMax > l.Bottom && yMin < l.Top) return true;
                    }
                }
            return false;
        }

        bool Solid(float x, float y) { return Physics2D.OverlapPoint(new Vector2(x, y), filter, buf) > 0; }

        bool Empty(float x0, float y0, float x1, float y1)
        {
            var c = new Vector2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
            var s = new Vector2(Math.Abs(x1 - x0), Math.Abs(y1 - y0));
            return Physics2D.OverlapBox(c, s, 0f, filter, buf) == 0;
        }

        void Generate(int cx, int cy)
        {
            float c = ChunkSize, x0 = cx * c, y0 = cy * c, sp = spacing * ps;
            for (int k = Mathf.CeilToInt(y0 / sp); k * sp < y0 + c; k++)
            {
                float top = k * sp;
                float row = top - ps * 0.5f;
                bool prev = Solid(x0 - ps * 0.5f, row);
                for (int i = 0; i < ChunkPx; i++)
                {
                    float x = x0 + (i + 0.5f) * ps;
                    bool cur = Solid(x, row);
                    if (cur != prev)
                    {
                        if (cur) TryLedge(x, top, -1);          // rock starts here, open space to the left
                        else TryLedge(x - ps, top, +1);         // rock ends here, open space to the right
                    }
                    prev = cur;
                }
            }
        }

        bool FaceNear(float rowY, float faceX, int open, int rangePx)
        {
            for (int d = 0; d <= rangePx; d++)
            {
                for (int s = -1; s <= 1; s += 2)
                {
                    if (d == 0 && s > 0) continue;
                    float x = faceX + s * d * ps;
                    if (Solid(x, rowY) && !Solid(x + open * ps, rowY)) return true;
                }
            }
            return false;
        }

        void TryLedge(float faceX, float top, int open)
        {
            // A wall: the face continues steeply both above and below this height.
            if (!FaceNear(top + 16 * ps, faceX, open, 8) || !FaceNear(top - 16 * ps, faceX, open, 8)) return;
            if (!FaceNear(top + 32 * ps, faceX, open, 16) || !FaceNear(top - 32 * ps, faceX, open, 16)) return;

            float edge = faceX + open * ps * 0.5f;   // where rock meets air
            // Room for Madeline to stand on it...
            if (!Empty(edge + open * ps, top + ps * 0.5f, edge + open * LedgeOut * ps, top + 14 * ps)) return;
            // ...and no natural footing right below (otherwise she doesn't need it).
            if (!Empty(edge + open * 3 * ps, top - 20 * ps, edge + open * LedgeOut * ps, top - ps)) return;

            var l = new Ledge();
            float a = edge - open * LedgeIn * ps, b = edge + open * LedgeOut * ps;
            l.XMin = Math.Min(a, b);
            l.XMax = Math.Max(a, b);
            l.Top = top;
            l.Bottom = top - CollisionThickness * ps;
            l.Open = open;
            if (Overlaps(l.XMin - 8 * ps, l.Bottom, l.XMax + 8 * ps, l.Top)) return;   // neighbour already there

            Register(l);
            Count++;
            if (OnCreated != null) OnCreated(l);
        }
    }
}
