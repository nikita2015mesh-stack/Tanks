using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace MadMachines.Editor
{
    internal sealed class QuarryTerraceMesh
    {
        public const int TopHeight = 180;
        private const float Size = QuarryRoadImport.MapSize;
        private const float Epsilon = 0.0001f;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly SortedDictionary<int, List<int>> faces = new SortedDictionary<int, List<int>>();

        public Vector2 FloorStart { get; private set; }
        public IEnumerable<int> FaceHeights => faces.Keys;

        private readonly struct Edge
        {
            public readonly Vector2 A;
            public readonly Vector2 B;

            public Edge(Vector2 a, Vector2 b)
            {
                A = a;
                B = b;
            }

            public float XAt(float z) => A.x + (B.x - A.x) * ((z - A.y) / (B.y - A.y));
            public bool Crosses(float z) => (A.y > z) != (B.y > z);
        }

        private readonly struct Band
        {
            public readonly Edge Left;
            public readonly Edge Right;
            public readonly int Height;

            public Band(Edge left, Edge right, int height)
            {
                Left = left;
                Right = right;
                Height = height;
            }
        }

        public Mesh Build(SortedDictionary<int, List<List<Vector2>>> contours)
        {
            var edges = new List<Edge>
            {
                new Edge(Vector2.zero, new Vector2(0, Size)),
                new Edge(new Vector2(Size, 0), new Vector2(Size, Size))
            };
            var cuts = new List<float> { 0, Size };
            foreach (List<Vector2> polygon in contours.Values.SelectMany(level => level))
            {
                for (int i = 0; i < polygon.Count; i++)
                {
                    Vector2 point = polygon[i];
                    if (point.x < 0 || point.x > Size || point.y < 0 || point.y > Size)
                        throw new FormatException("Контуры плоскостей должны лежать в пределах рамки карты.");
                    cuts.Add(point.y);
                    edges.Add(new Edge(point, polygon[(i + 1) % polygon.Count]));
                }
            }

            // Within each interval between vertices or crossings, edges keep the same order.
            for (int i = 2; i < edges.Count; i++)
            for (int j = i + 1; j < edges.Count; j++)
                if (TryIntersect(edges[i], edges[j], out float z)) cuts.Add(z);
            cuts.Sort();

            float bestClearance = -1f;
            float start = cuts[0];
            List<Band> previous = null;
            foreach (float end in cuts)
            {
                if (end - start <= Epsilon) continue;
                float middle = (start + end) * 0.5f;
                Edge[] active = edges.Where(edge => edge.Crosses(middle)).OrderBy(edge => edge.XAt(middle)).ToArray();
                var bands = new List<Band>();
                for (int i = 1; i < active.Length; i++)
                {
                    Edge left = active[i - 1];
                    Edge right = active[i];
                    float a = left.XAt(middle);
                    float b = right.XAt(middle);
                    if (b - a <= Epsilon) continue;
                    var sample = new Vector2((a + b) * 0.5f, middle);
                    int height = HeightAt(contours, sample);
                    var band = new Band(left, right, height);
                    if (bands.Count > 0)
                    {
                        Band neighbor = bands[bands.Count - 1];
                        AddWall(new Vector2(left.XAt(start), start), new Vector2(left.XAt(end), end),
                            neighbor.Height, height);
                    }

                    bands.Add(band);
                    AddQuad(height, Point(left.XAt(start), start, height), Point(left.XAt(end), end, height),
                        Point(right.XAt(end), end, height), Point(right.XAt(start), start, height));

                    if (height != 0) continue;
                    float clearance = contours[0].Where(polygon => Contains(polygon, sample))
                        .Select(polygon => Clearance(polygon, sample)).DefaultIfEmpty(0f).Max();
                    if (clearance > bestClearance)
                    {
                        bestClearance = clearance;
                        FloorStart = sample;
                    }
                }

                if (previous != null) JoinBands(previous, bands, start);
                previous = bands;
                start = end;
            }

            if (bestClearance < 0f) throw new FormatException("Контур дна не образует площадку.");

            var mesh = new Mesh { name = "Ступени Quarry_01", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.subMeshCount = faces.Count;
            int subMesh = 0;
            foreach (List<int> indices in faces.Values) mesh.SetTriangles(indices, subMesh++, false);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void JoinBands(List<Band> before, List<Band> after, float z)
        {
            int a = 0;
            int b = 0;
            while (a < before.Count && b < after.Count)
            {
                Band south = before[a];
                Band north = after[b];
                float southRight = south.Right.XAt(z);
                float northRight = north.Right.XAt(z);
                float left = Mathf.Max(south.Left.XAt(z), north.Left.XAt(z));
                float right = Mathf.Min(southRight, northRight);
                if (right - left > Epsilon)
                    AddWall(new Vector2(left, z), new Vector2(right, z), north.Height, south.Height);
                if (southRight <= northRight + Epsilon) a++;
                if (northRight <= southRight + Epsilon) b++;
            }
        }

        private void AddWall(Vector2 a, Vector2 b, int left, int right)
        {
            if (left == right || (a - b).sqrMagnitude <= Epsilon * Epsilon) return;
            // Face the cut, so the wall is visible and collidable from the lower platform.
            if (left > right) (a, b) = (b, a);
            int bottom = Mathf.Min(left, right);
            int top = Mathf.Max(left, right);
            AddQuad(-1, Point(a.x, a.y, bottom), Point(b.x, b.y, bottom),
                Point(b.x, b.y, top), Point(a.x, a.y, top));
        }

        private void AddQuad(int material, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            if (!faces.TryGetValue(material, out List<int> triangles))
            {
                triangles = new List<int>();
                faces.Add(material, triangles);
            }

            int first = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            if (Vector3.Cross(b - a, c - a).sqrMagnitude > Epsilon * Epsilon)
            {
                triangles.Add(first);
                triangles.Add(first + 1);
                triangles.Add(first + 2);
            }
            if (Vector3.Cross(c - a, d - a).sqrMagnitude > Epsilon * Epsilon)
            {
                triangles.Add(first);
                triangles.Add(first + 2);
                triangles.Add(first + 3);
            }
        }

        private static Vector3 Point(float x, float z, int height) => new Vector3(x, height, z);

        private static int HeightAt(SortedDictionary<int, List<List<Vector2>>> contours, Vector2 point)
        {
            foreach (var entry in contours)
            foreach (List<Vector2> polygon in entry.Value)
                if (Contains(polygon, point)) return entry.Key;
            return TopHeight;
        }

        private static bool Contains(List<Vector2> polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0; i < polygon.Count; i++)
            {
                var edge = new Edge(polygon[i], polygon[(i + 1) % polygon.Count]);
                if (edge.Crosses(point.y) && point.x < edge.XAt(point.y)) inside = !inside;
            }
            return inside;
        }

        private static float Clearance(List<Vector2> polygon, Vector2 point)
        {
            float clearance = float.PositiveInfinity;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 delta = polygon[(i + 1) % polygon.Count] - a;
                if (delta.sqrMagnitude == 0f) continue;
                Vector2 closest = a + delta * Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude);
                clearance = Mathf.Min(clearance, (closest - point).sqrMagnitude);
            }
            return clearance;
        }

        private static bool TryIntersect(Edge a, Edge b, out float z)
        {
            Vector2 u = a.B - a.A;
            Vector2 v = b.B - b.A;
            Vector2 offset = b.A - a.A;
            float denominator = Cross(u, v);
            z = 0;
            if (Mathf.Abs(denominator) <= Epsilon) return false;
            float alongA = Cross(offset, v) / denominator;
            float alongB = Cross(offset, u) / denominator;
            if (alongA <= 0 || alongA >= 1 || alongB <= 0 || alongB >= 1) return false;
            z = a.A.y + u.y * alongA;
            return true;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
