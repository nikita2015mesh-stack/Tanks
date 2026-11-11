using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UnityEngine;

namespace MadMachines.Editor
{
    internal sealed class QuarryRoadImport
    {
        private const string SourceFolder = "Docs/maps/Quarry/";
        private static readonly Regex PathToken = new Regex(
            @"\G[\s,]*(?<token>[mMlLhHvVcCzZ]|[-+]?(?:\d*\.\d+|\d+\.?)(?:[eE][-+]?\d+)?)");
        private static readonly Regex Translation = new Regex(
            @"^translate\(\s*([^,\s]+)[,\s]+([^,\s)]+)\s*\)$");

        [Serializable]
        private sealed class Export
        {
            public string mapId;
            public Calibration calibration;
            public JunctionReference[] junctions;
        }

        [Serializable]
        private sealed class Calibration
        {
            public float[] svgMapFrame;
        }

        [Serializable]
        private sealed class JunctionReference
        {
            public string id;
            public string[] roads;
        }

        internal sealed class Road
        {
            public readonly List<Vector2> Points = new List<Vector2>();
            public bool Closed;
        }

        internal sealed class Junction
        {
            public Vector2 Position;
            public Vector2[] Connections;
        }

        public const float MapSize = 1500f;
        public readonly Dictionary<string, Road> Roads = new Dictionary<string, Road>();
        public readonly Dictionary<string, Junction> Junctions = new Dictionary<string, Junction>();

        public static SortedDictionary<int, List<List<Vector2>>> LoadTerraces()
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", SourceFolder));
            float[] frame = JsonUtility.FromJson<Export>(File.ReadAllText(Path.Combine(folder, "карьер-дороги.json")))
                ?.calibration?.svgMapFrame;
            if (frame?.Length != 4 || !(frame[2] > frame[0] && frame[3] > frame[1]))
                throw new FormatException("Некорректная рамка карты Quarry_01.");

            XElement root = XDocument.Load(Path.Combine(folder, "карьер.svg")).Root;
            XNamespace svg = root.Name.Namespace;
            XNamespace inkscape = "http://www.inkscape.org/namespaces/inkscape";
            XElement group = root.Elements(svg + "g").SingleOrDefault(
                element => ((string)element.Attribute(inkscape + "label"))?.Trim() == "Контуры плоскостей");
            if (group == null) throw new FormatException("В SVG нет слоя «Контуры плоскостей».");
            RequireNoTransform(root);
            Vector2 groupOffset = ReadTranslation(group);
            var terraces = new SortedDictionary<int, List<List<Vector2>>>();
            foreach (XElement layer in group.Elements(svg + "g"))
            {
                string label = (string)layer.Attribute(inkscape + "label");
                Match match = Regex.Match(label ?? "", @"^\s*(\d+)\s*м\s*$");
                if (!match.Success) throw new FormatException($"Подпишите подслой «{label}» высотой в метрах.");
                int height = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                if (height >= QuarryTerraceMesh.TopHeight) throw new FormatException($"Высота выемки {height} м должна быть ниже 180 м.");
                if (!terraces.TryGetValue(height, out var contours))
                {
                    contours = new List<List<Vector2>>();
                    terraces.Add(height, contours);
                }

                Vector2 layerOffset = groupOffset + ReadTranslation(layer);
                foreach (XElement path in layer.Elements(svg + "path"))
                {
                    string id = (string)path.Attribute("id");
                    Vector2 offset = layerOffset + ReadTranslation(path);
                    Vector2 ToWorld(Vector2 p) => new Vector2(
                        (p.x + offset.x - frame[0]) * MapSize / (frame[2] - frame[0]),
                        (frame[3] - p.y - offset.y) * MapSize / (frame[3] - frame[1]));
                    Road contour = ReadRoad((string)path.Attribute("d"), ToWorld, id);
                    if (!contour.Closed) throw new FormatException($"Замкните контур {id} на уровне {height} м.");
                    contours.Add(contour.Points);
                }
                if (contours.Count == 0) throw new FormatException($"Подслой «{label}» не содержит контуров.");
            }

            if (!terraces.ContainsKey(0)) throw new FormatException("Нужен замкнутый контур дна на уровне 0 м.");
            return terraces;
        }

        public static QuarryRoadImport Load()
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", SourceFolder));
            // Reuse the accepted junction membership and provisional map frame, not the exported road points.
            Export export = JsonUtility.FromJson<Export>(File.ReadAllText(Path.Combine(folder, "карьер-дороги.json")));
            if (export?.mapId != "Quarry_01" || export.calibration?.svgMapFrame?.Length != 4 ||
                export.junctions?.Length != 10)
                throw new FormatException("В экспорте Quarry_01 отсутствуют рамка карты или связи J01–J10.");

            float[] frame = export.calibration.svgMapFrame;
            if (!(frame[2] > frame[0] && frame[3] > frame[1]))
                throw new FormatException("Некорректная рамка карты Quarry_01.");

            XElement root = XDocument.Load(Path.Combine(folder, "карьер.svg")).Root;
            XNamespace svg = root.Name.Namespace;
            XElement roadLayer = ById(root, "layer2");
            XElement junctionLayer = ById(root, "junction-candidates");
            RequireNoTransform(root);
            RequireNoTransform(junctionLayer);
            Vector2 translation = ReadTranslation(roadLayer);
            var result = new QuarryRoadImport();
            Vector2 ToWorld(Vector2 p) => new Vector2(
                (p.x - frame[0]) * MapSize / (frame[2] - frame[0]),
                (frame[3] - p.y) * MapSize / (frame[3] - frame[1]));

            for (int i = 1; i <= 10; i++)
            {
                string id = $"D{i:00}";
                XElement path = ById(roadLayer, id);
                RequireNoTransform(path);
                Road road = ReadRoad((string)path.Attribute("d"), p => ToWorld(p + translation), id);
                if (road.Closed != (i == 1))
                    throw new FormatException($"Неожиданное замыкание трассы {id}.");
                result.Roads.Add(id, road);
            }

            for (int i = 1; i <= 10; i++)
            {
                string id = $"J{i:00}";
                XElement marker = ById(junctionLayer, "candidate-" + id);
                XElement circle = marker.Elements(svg + "circle").First();
                RequireNoTransform(marker);
                RequireNoTransform(circle);
                Vector2 position = ToWorld(new Vector2(Number((string)circle.Attribute("cx")),
                    Number((string)circle.Attribute("cy"))));
                string[] roads = export.junctions.Single(junction => junction.id == id).roads;
                if (roads == null || roads.Distinct().Count() < 2)
                    throw new FormatException($"Для {id} нужны как минимум две дороги.");

                Vector2[] connections = roads.Select(road => ClosestPoint(result.Roads[road], position)).ToArray();
                if (connections.Any(p => Vector2.Distance(p, position) > 8f))
                    throw new FormatException($"Стык {id} дальше 8 м от указанной дороги. Проверьте SVG и связи.");
                result.Junctions.Add(id, new Junction { Position = position, Connections = connections });
            }

            return result;
        }

        private static XElement ById(XElement parent, string id) =>
            parent.Elements().Single(element => (string)element.Attribute("id") == id);

        private static void RequireNoTransform(XElement element)
        {
            if (element.Attribute("transform") != null)
                throw new FormatException($"Неподдерживаемый transform у {(string)element.Attribute("id")}.");
        }

        private static Vector2 ReadTranslation(XElement element)
        {
            string value = (string)element.Attribute("transform");
            if (value == null) return Vector2.zero;
            Match match = Translation.Match(value);
            if (!match.Success) throw new FormatException("Ожидался translate у слоя Roads.");
            return new Vector2(Number(match.Groups[1].Value), Number(match.Groups[2].Value));
        }

        private static float Number(string text) => float.Parse(text, CultureInfo.InvariantCulture);

        private static Road ReadRoad(string data, Func<Vector2, Vector2> toWorld, string id)
        {
            // The approved Inkscape paths use one subpath and only move, line, cubic and close.
            var tokens = new List<string>();
            for (int offset = 0; offset < data.Length;)
            {
                Match match = PathToken.Match(data, offset);
                if (!match.Success)
                {
                    if (string.IsNullOrWhiteSpace(data.Substring(offset))) break;
                    throw new FormatException($"Неподдерживаемая команда SVG в {id}.");
                }
                tokens.Add(match.Groups["token"].Value);
                offset += match.Length;
            }

            var road = new Road();
            int index = 0;
            char command = '\0';
            Vector2 current = Vector2.zero;
            Vector2 ReadPoint()
            {
                if (index + 1 >= tokens.Count) throw new FormatException($"Неполная точка в {id}.");
                Vector2 point = new Vector2(Number(tokens[index++]), Number(tokens[index++]));
                return char.IsLower(command) ? current + point : point;
            }

            while (index < tokens.Count)
            {
                if (char.IsLetter(tokens[index][0])) command = tokens[index++][0];
                if (road.Closed || (road.Points.Count == 0 && char.ToUpperInvariant(command) != 'M'))
                    throw new FormatException($"Ожидался один контур в {id}.");

                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                        if (road.Points.Count != 0) throw new FormatException($"Лишний контур в {id}.");
                        current = ReadPoint();
                        road.Points.Add(toWorld(current));
                        command = command == 'm' ? 'l' : 'L';
                        break;
                    case 'L':
                        current = ReadPoint();
                        Append(road.Points, toWorld(current));
                        break;
                    case 'H':
                        float x = Number(tokens[index++]);
                        current.x = command == 'h' ? current.x + x : x;
                        Append(road.Points, toWorld(current));
                        break;
                    case 'V':
                        float y = Number(tokens[index++]);
                        current.y = command == 'v' ? current.y + y : y;
                        Append(road.Points, toWorld(current));
                        break;
                    case 'C':
                        Vector2 controlA = ReadPoint();
                        Vector2 controlB = ReadPoint();
                        Vector2 end = ReadPoint();
                        SampleCubic(road.Points, toWorld(current), toWorld(controlA), toWorld(controlB), toWorld(end), 0);
                        current = end;
                        break;
                    case 'Z':
                        road.Closed = true;
                        break;
                    default:
                        throw new FormatException($"Неподдерживаемый контур {id}.");
                }
            }

            if (road.Closed && (road.Points[0] - road.Points[road.Points.Count - 1]).sqrMagnitude < 0.000001f)
                road.Points.RemoveAt(road.Points.Count - 1);
            if (road.Points.Count < (road.Closed ? 3 : 2))
                throw new FormatException($"Недостаточно точек в {id}.");
            return road;
        }

        private static void SampleCubic(List<Vector2> points, Vector2 a, Vector2 b, Vector2 c, Vector2 d, int depth)
        {
            if ((d - a).sqrMagnitude <= 9f && (b - Project(b, a, d)).sqrMagnitude <= 0.01f &&
                (c - Project(c, a, d)).sqrMagnitude <= 0.01f)
            {
                Append(points, d);
                return;
            }
            if (depth == 20) throw new FormatException("Не удалось дискретизировать кривую Quarry_01.");
            Vector2 ab = (a + b) * 0.5f;
            Vector2 bc = (b + c) * 0.5f;
            Vector2 cd = (c + d) * 0.5f;
            Vector2 abc = (ab + bc) * 0.5f;
            Vector2 bcd = (bc + cd) * 0.5f;
            Vector2 middle = (abc + bcd) * 0.5f;
            SampleCubic(points, a, ab, abc, middle, depth + 1);
            SampleCubic(points, middle, bcd, cd, d, depth + 1);
        }

        private static void Append(List<Vector2> points, Vector2 point)
        {
            if ((points[points.Count - 1] - point).sqrMagnitude >= 0.000001f) points.Add(point);
        }

        private static Vector2 ClosestPoint(Road road, Vector2 point)
        {
            Vector2 closest = road.Points[0];
            int count = road.Points.Count;
            for (int i = 0; i < (road.Closed ? count : count - 1); i++)
            {
                Vector2 candidate = Project(point, road.Points[i], road.Points[(i + 1) % count]);
                if ((candidate - point).sqrMagnitude < (closest - point).sqrMagnitude) closest = candidate;
            }
            return closest;
        }

        private static Vector2 Project(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 delta = b - a;
            return delta.sqrMagnitude == 0f ? a :
                a + delta * Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude);
        }
    }
}
