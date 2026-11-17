using System.Collections.Generic;
using UnityEngine;

namespace Unlikely.World
{
    /// <summary>
    /// A walkability grid for one room, read from its wall colliders, with A* paths across it.
    /// Used by people who have to find their way around furniture (HR's rounds, Ryker, security).
    /// Built the first time it's asked for while the room is active (inactive rooms have no physics).
    /// </summary>
    public sealed class NavGrid
    {
        private const float Cell = 0.25f;

        private static readonly Dictionary<string, NavGrid> cache = new Dictionary<string, NavGrid>();

        private readonly Vector2 origin;
        private readonly int w, h;
        private readonly bool[] free;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => cache.Clear();

        /// <summary>The grid for a room (null if the room isn't active, so its walls can't be read yet).</summary>
        public static NavGrid For(Room room)
        {
            if (room == null) return null;
            if (cache.TryGetValue(room.roomId, out var g)) return g;
            if (!room.gameObject.activeInHierarchy) return null;
            Physics2D.SyncTransforms();
            g = new NavGrid(room);
            cache[room.roomId] = g;
            return g;
        }

        private NavGrid(Room room)
        {
            var b = room.bounds;
            origin = new Vector2(room.transform.position.x + b.xMin, room.transform.position.y + b.yMin);
            w = Mathf.CeilToInt(b.width / Cell);
            h = Mathf.CeilToInt(b.height / Cell);
            free = new bool[w * h];
            var size = new Vector2(Cell * 1.4f, Cell * 0.9f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool blocked = false;
                    foreach (var c in Physics2D.OverlapBoxAll(Center(x, y), size, 0f))
                        if (!c.isTrigger && c.gameObject.name == "Walls" && c.transform.IsChildOf(room.transform)) { blocked = true; break; }
                    free[y * w + x] = !blocked;
                }
        }

        public Vector3 Center(int x, int y) => new Vector3(origin.x + (x + 0.5f) * Cell, origin.y + (y + 0.5f) * Cell, 0f);

        private bool In(int x, int y) => x >= 0 && y >= 0 && x < w && y < h;
        private bool Free(int x, int y) => In(x, y) && free[y * w + x];

        private void ToCell(Vector3 p, out int x, out int y)
        {
            x = Mathf.FloorToInt((p.x - origin.x) / Cell);
            y = Mathf.FloorToInt((p.y - origin.y) / Cell);
        }

        public bool IsWalkable(Vector3 p) { ToCell(p, out int x, out int y); return Free(x, y); }

        /// <summary>The nearest walkable point to p (p itself if it's walkable).</summary>
        public Vector3 Nearest(Vector3 p)
        {
            ToCell(p, out int cx, out int cy);
            if (Free(cx, cy)) return p;
            for (int r = 1; r < 40; r++)
                for (int dy = -r; dy <= r; dy++)
                    for (int dx = -r; dx <= r; dx++)
                        if ((Mathf.Abs(dx) == r || Mathf.Abs(dy) == r) && Free(cx + dx, cy + dy)) return Center(cx + dx, cy + dy);
            return p;
        }

        /// <summary>A random walkable point within radius metres of p.</summary>
        public Vector3 RandomNear(Vector3 p, float radius)
        {
            for (int i = 0; i < 30; i++)
            {
                var q = p + (Vector3)(Random.insideUnitCircle * radius);
                if (IsWalkable(q)) return q;
            }
            return Nearest(p);
        }

        /// <summary>Is the straight line from a to b clear of walls (on the grid)?</summary>
        public bool Clear(Vector3 a, Vector3 b)
        {
            float d = Vector2.Distance(a, b);
            int steps = Mathf.CeilToInt(d / (Cell * 0.5f));
            for (int i = 0; i <= steps; i++)
                if (!IsWalkable(Vector3.Lerp(a, b, steps == 0 ? 0 : (float)i / steps))) return false;
            return true;
        }

        /// <summary>A* from a to b. Returns waypoints (excluding a), smoothed; empty if there's no way.</summary>
        public List<Vector3> Path(Vector3 a, Vector3 b)
        {
            var result = new List<Vector3>();
            a = Nearest(a);
            b = Nearest(b);
            ToCell(a, out int sx, out int sy);
            ToCell(b, out int tx, out int ty);
            if (!Free(sx, sy) || !Free(tx, ty)) return result;
            if (Clear(a, b)) { result.Add(b); return result; }

            int n = w * h, start = sy * w + sx, goal = ty * w + tx;
            var g = new float[n];
            var came = new int[n];
            var closed = new bool[n];
            for (int i = 0; i < n; i++) { g[i] = float.MaxValue; came[i] = -1; }
            g[start] = 0f;
            var open = new SortedSet<(float f, int i)>();
            open.Add((H(sx, sy, tx, ty), start));
            int expanded = 0;
            while (open.Count > 0 && expanded++ < 20000)
            {
                var cur = open.Min;
                open.Remove(cur);
                int ci = cur.i;
                if (closed[ci]) continue;
                closed[ci] = true;
                if (ci == goal) break;
                int cx = ci % w, cy = ci / w;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int nx = cx + dx, ny = cy + dy;
                        if (!Free(nx, ny)) continue;
                        if (dx != 0 && dy != 0 && (!Free(cx + dx, cy) || !Free(cx, cy + dy))) continue; // no corner cutting
                        int ni = ny * w + nx;
                        float ng = g[ci] + (dx != 0 && dy != 0 ? 1.4142f : 1f);
                        if (ng >= g[ni]) continue;
                        g[ni] = ng;
                        came[ni] = ci;
                        open.Add((ng + H(nx, ny, tx, ty), ni));
                    }
            }
            if (came[goal] < 0) return result;

            var cells = new List<Vector3>();
            for (int i = goal; i != start && i >= 0; i = came[i]) cells.Add(Center(i % w, i / w));
            cells.Reverse();
            cells[cells.Count - 1] = b;
            // string-pull: skip waypoints we can see past
            Vector3 from = a;
            for (int i = 0; i < cells.Count; i++)
            {
                if (i + 1 < cells.Count && Clear(from, cells[i + 1])) continue;
                result.Add(cells[i]);
                from = cells[i];
            }
            return result;
        }

        private static float H(int x, int y, int tx, int ty)
        {
            int dx = Mathf.Abs(x - tx), dy = Mathf.Abs(y - ty);
            return Mathf.Max(dx, dy) + 0.4142f * Mathf.Min(dx, dy);
        }
    }
}
