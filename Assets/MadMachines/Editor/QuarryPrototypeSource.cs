using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

namespace MadMachines.Editor
{
    internal sealed class QuarryPrototypeSource
    {
        private const string SourceFolder = "Docs/Maps/Quarry_01";
        private const int Resolution = 1025;
        private const int RawLength = Resolution * Resolution * 2;
        private const float Height = 210f;

        public Vector3 Size { get; }
        public Dictionary<string, Marker> Markers { get; }
        public float[,] Heights { get; }

        public readonly struct Marker
        {
            public readonly Vector2 Position;
            public readonly string Type;

            public Marker(Vector2 position, string type)
            {
                Position = position;
                Type = type;
            }
        }

        private QuarryPrototypeSource(Vector3 size, Dictionary<string, Marker> markers, float[,] heights)
        {
            Size = size;
            Markers = markers;
            Heights = heights;
        }

        public static string ProjectPath(string relativePath)
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", relativePath));
        }

        public static QuarryPrototypeSource Read()
        {
            string folder = ProjectPath(SourceFolder);
            var definition = ParseDefinition(File.ReadAllText(Path.Combine(folder, "quarry_01.json")));
            using var raw = new FileStream(Path.Combine(folder, "Quarry_01_heightmap_le.raw"), FileMode.Open, FileAccess.Read);
            return new QuarryPrototypeSource(definition.Size, definition.Markers, ReadHeights(raw));
        }

        internal static Vector2[][] ReadRoutes()
        {
            string path = ProjectPath(SourceFolder + "/quarry_01.json");
            return ParseDefinition(File.ReadAllText(path), includeRoutes: true).Routes;
        }

        internal static (Vector3 Size, Dictionary<string, Marker> Markers, Vector2[][] Routes) ParseDefinition(
            string json, bool includeRoutes = false)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                JsonElement world = root.GetProperty("world");
                var size = new Vector3(Number(world, "size_x_m"), Number(world, "height_max_m"), Number(world, "size_z_m"));
                if (root.GetProperty("schema_version").GetString() != "0.1" || root.GetProperty("map_id").GetString() != "Quarry_01" ||
                    world.GetProperty("origin").GetString() != "south_west_corner" || !size.Equals(new Vector3(1500f, Height, 1500f)) ||
                    Number(world, "height_min_m") != 0f || world.GetProperty("terrain_heightmap_resolution").GetInt32() != Resolution)
                    throw new InvalidDataException("Ожидается Quarry_01 (схема 0.1), юго-западное начало и сетка 1025×1025 на 1500×210×1500 м.");

                var layout = ReadLayout(root, world, size, includeRoutes);
                return (size, layout.Markers, layout.Routes);
            }
            catch (Exception exception) when (exception is JsonException || exception is KeyNotFoundException ||
                exception is InvalidOperationException || exception is FormatException || exception is InvalidDataException)
            {
                throw new InvalidDataException("Некорректный quarry_01.json. " + exception.Message, exception);
            }
        }

        private static (Dictionary<string, Marker> Markers, Vector2[][] Routes) ReadLayout(
            JsonElement root, JsonElement world, Vector3 size, bool includeRoutes)
        {
            float centerX = Number(world, "center_x_m");
            float centerZ = Number(world, "center_z_m");
            JsonElement quarry = root.GetProperty("quarry");
            float rotation = Number(quarry, "rotation_deg") * Mathf.Deg2Rad;
            float scaleX = Number(quarry, "ellipse_scale_x");
            float scaleZ = Number(quarry, "ellipse_scale_z");
            float variation = Number(quarry, "wall_variation_m");
            if (scaleX <= 0f || scaleZ <= 0f || variation < 0f ||
                centerX <= 0f || centerX >= size.x || centerZ <= 0f || centerZ >= size.z)
                throw new InvalidDataException("Проверьте центр и параметры эллипса карьера.");

            Vector2 ToWorld(float radius, float angle, bool followTerraces)
            {
                // Match the reference generator's to_xz, including terrace displacement on the rings.
                if (followTerraces)
                    radius -= variation * (0.68f * Mathf.Sin(3f * angle + 0.5f) +
                        0.43f * Mathf.Sin(5f * angle - 1.1f) + 0.28f * Mathf.Sin(9f * angle + 1.5f));
                if (radius < 0f)
                    throw new InvalidDataException("Радиус точки или маршрута после смещения отрицательный.");

                float u = radius * Mathf.Cos(angle) * scaleX;
                float v = radius * Mathf.Sin(angle) * scaleZ;
                return new Vector2(centerX + u * Mathf.Cos(rotation) - v * Mathf.Sin(rotation),
                    centerZ + u * Mathf.Sin(rotation) + v * Mathf.Cos(rotation));
            }

            JsonElement roads = root.GetProperty("roads");
            JsonElement ringRoads = roads.GetProperty("ring_roads");
            var ringRadii = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (JsonElement road in ringRoads.EnumerateArray())
            {
                string id = road.GetProperty("id").GetString();
                float radius = Number(road, "radius_m");
                if (string.IsNullOrWhiteSpace(id) || radius <= 0f || ringRadii.ContainsKey(id))
                    throw new InvalidDataException("Кольцевой проезд должен иметь уникальный ID и положительный радиус.");
                ringRadii.Add(id, radius);
            }

            var markers = new Dictionary<string, Marker>(StringComparer.Ordinal);
            foreach (JsonElement point in root.GetProperty("points_of_interest").EnumerateArray())
            {
                string id = point.GetProperty("id").GetString();
                string type = point.GetProperty("type").GetString();
                if (string.IsNullOrWhiteSpace(id) || id.Contains("/") || string.IsNullOrWhiteSpace(type))
                    throw new InvalidDataException("У маркера должны быть ID без '/' и тип.");

                float angle = Number(point, "angle_deg") * Mathf.Deg2Rad;
                float radius;
                bool followsRing = point.TryGetProperty("road_id", out JsonElement roadId);
                if (followsRing)
                {
                    string road = roadId.GetString();
                    if (road == null || !ringRadii.TryGetValue(road, out radius))
                        throw new InvalidDataException($"Маркер {id} ссылается на неизвестный кольцевой проезд.");
                }
                else
                {
                    radius = Number(point, "radius_m");
                }

                Vector2 position = ToWorld(radius, angle, followsRing);
                RequireOnMap(position, size, id);
                if (markers.ContainsKey(id))
                    throw new InvalidDataException($"Повторяющийся ID маркера {id}.");
                markers.Add(id, new Marker(position, type));
            }

            foreach (string id in new[] { "spawn_a", "spawn_b" })
            {
                if (!markers.TryGetValue(id, out Marker marker) || marker.Type != "spawn")
                    throw new InvalidDataException($"Отсутствует обязательный маркер старта {id}.");
            }
            if (!includeRoutes) return (markers, null);

            if (ringRadii.Count != 4)
                throw new InvalidDataException("Для пробного заезда нужны четыре кольцевых проезда R1–R4.");
            JsonElement spiral = roads.GetProperty("main_spiral");
            float start = Number(spiral, "radius_start_m");
            float end = Number(spiral, "radius_end_m");
            float angleStart = Number(spiral, "start_angle_deg") * Mathf.Deg2Rad;
            float sweep = Number(spiral, "turns") * 2f * Mathf.PI;
            if (start <= 0f || end <= start || !(sweep > 0f && sweep <= 6f * Mathf.PI))
                throw new InvalidDataException("Некорректные радиусы или число оборотов серпантина M0.");

            var routes = new Vector2[5][];
            routes[0] = new Vector2[3300];
            for (int i = 0; i < routes[0].Length; i++)
            {
                float t = i / (routes[0].Length - 1f);
                Vector2 position = ToWorld(Mathf.Lerp(start, end, t), angleStart + sweep * t, false);
                RequireOnMap(position, size, "M0");
                routes[0][i] = position;
            }

            int index = 1;
            foreach (JsonElement road in ringRoads.EnumerateArray())
            {
                string id = road.GetProperty("id").GetString();
                float radius = ringRadii[id];
                var ring = new Vector2[1150];
                for (int i = 0; i < ring.Length; i++)
                {
                    float angle = -Mathf.PI + 2f * Mathf.PI * i / ring.Length;
                    Vector2 position = ToWorld(radius, angle, true);
                    RequireOnMap(position, size, id);
                    ring[i] = position;
                }
                routes[index++] = ring;
            }
            return (markers, routes);
        }

        private static void RequireOnMap(Vector2 position, Vector3 size, string id)
        {
            if (!(position.x >= 0f && position.x <= size.x && position.y >= 0f && position.y <= size.z))
                throw new InvalidDataException($"Точка маршрута или маркер {id} выходит за границы Terrain.");
        }

        private static float Number(JsonElement element, string name)
        {
            float value = element.GetProperty(name).GetSingle();
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidDataException($"Поле {name} должно быть конечным числом.");
            return value;
        }

        internal static float[,] ReadHeights(Stream raw)
        {
            if (raw.Length != RawLength)
                throw new InvalidDataException($"RAW: ожидалось {RawLength:N0} байт (1025×1025 R16), получено {raw.Length:N0}.");

            var heights = new float[Resolution, Resolution];
            using var reader = new BinaryReader(raw, System.Text.Encoding.UTF8, leaveOpen: true);
            // The RAW starts at Z=0. X changes fastest and BinaryReader reads UInt16 as little-endian.
            for (int z = 0; z < Resolution; z++)
            {
                for (int x = 0; x < Resolution; x++)
                    heights[z, x] = reader.ReadUInt16() / (float)ushort.MaxValue;
            }

            // Headerless RAW has no orientation tag. Compare asymmetric landmarks of this source map.
            if (heights[512, 512] * Height > 1f || Mathf.Abs(heights[0, 0] * Height - 180f) > 4f ||
                Mathf.Abs(heights[256, 256] * Height - 164.18f) > 3f ||
                Mathf.Abs(heights[768, 256] * Height - 180.01f) > 3f ||
                Mathf.Abs(heights[256, 768] * Height - 180f) > 3f ||
                Mathf.Abs(heights[512, 384] * Height - 15.84f) > 3f)
                throw new InvalidDataException("RAW: высоты контрольных точек Quarry_01 не совпали. Проверьте файл, порядок байтов little-endian и направление осей X/Z.");

            return heights;
        }
    }
}
