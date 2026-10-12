using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CelesteOverIt
{
    /// <summary>Development only (Dev.DumpMap): writes every solid 2D collider's outline to a text file.</summary>
    public static class MapDump
    {
        public static void Write(string path, int mask)
        {
            var sb = new StringBuilder();
            var ci = CultureInfo.InvariantCulture;
            foreach (var col in Object.FindObjectsOfType<Collider2D>())
            {
                if (col.isTrigger || (mask & (1 << col.gameObject.layer)) == 0) continue;
                var t = col.transform;
                string head = LayerMask.LayerToName(col.gameObject.layer) + "|" + col.name.Replace('|', '_');
                var poly = col as PolygonCollider2D;
                if (poly != null)
                {
                    for (int p = 0; p < poly.pathCount; p++)
                    {
                        sb.Append("poly|").Append(head).Append('|');
                        foreach (var v in poly.GetPath(p))
                        {
                            var w = t.TransformPoint(v + poly.offset);
                            sb.Append(w.x.ToString("0.###", ci)).Append(',').Append(w.y.ToString("0.###", ci)).Append(' ');
                        }
                        sb.AppendLine();
                    }
                    continue;
                }
                var edge = col as EdgeCollider2D;
                if (edge != null)
                {
                    sb.Append("edge|").Append(head).Append('|');
                    foreach (var v in edge.points)
                    {
                        var w = t.TransformPoint(v + edge.offset);
                        sb.Append(w.x.ToString("0.###", ci)).Append(',').Append(w.y.ToString("0.###", ci)).Append(' ');
                    }
                    sb.AppendLine();
                    continue;
                }
                // boxes, circles, capsules, composites: bounds are good enough for a map
                var b = col.bounds;
                sb.Append("rect|").Append(head).Append('|')
                  .Append(b.min.x.ToString("0.###", ci)).Append(',').Append(b.min.y.ToString("0.###", ci)).Append(' ')
                  .Append(b.max.x.ToString("0.###", ci)).Append(',').Append(b.max.y.ToString("0.###", ci)).AppendLine();
            }
            File.WriteAllText(path, sb.ToString());
        }
    }
}
