using System.Collections.Generic;
using UnityEngine;

namespace MadMachines
{
    public static class PlaygroundBuilder
    {
        private static readonly Color Sand = new Color(0.42f, 0.36f, 0.23f);
        private static readonly Color Road = new Color(0.16f, 0.21f, 0.24f);
        private static readonly Color Orange = new Color(1f, 0.4f, 0.08f);
        private static readonly Color Teal = new Color(0.08f, 0.65f, 0.57f);

        public static GameObject Build(WeaponDefinition[] weapons)
        {
            GameObject world = new GameObject("MAD MACHINES — Playground");
            Transform root = world.transform;
            ChallengeManager rules = world.AddComponent<ChallengeManager>();
            Box("Desert foundation", new Vector3(0, -1, 0), new Vector3(150, 2, 150), Sand, root);
            Box("North straight", new Vector3(0, 0.04f, 0), new Vector3(14, 0.08f, 108), Road, root);
            Box("East straight", new Vector3(36, 0.04f, 0), new Vector3(14, 0.08f, 108), Road, root);
            Box("North turn", new Vector3(18, 0.04f, 48), new Vector3(50, 0.08f, 14), Road, root);
            Box("South turn", new Vector3(18, 0.04f, -48), new Vector3(50, 0.08f, 14), Road, root);
            GameObject mud = Box("Loose gravel — low grip", new Vector3(36, 0.1f, 0), new Vector3(13, 0.12f, 20), new Color(0.52f, 0.33f, 0.16f), root);
            mud.AddComponent<SurfaceGrip>().Multiplier = 0.45f;
            Ramp(root, new Vector3(0, 0, 0), 0, 10, 15, 3);
            Ramp(root, new Vector3(36, 0, 20), 180, 10, 10, 2);
            Ramp(root, new Vector3(-29, 0, -8), 20, 16, 22, 6);
            Ramp(root, new Vector3(-48, 0, 20), -25, 12, 13, 8);
            for (int i = 0; i < 5; i++) Box("Suspension rumble", new Vector3(36, 0.12f, -20 - i * 2), new Vector3(10, 0.24f, 0.65f), Sand * 1.2f, root);
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI * 2 / 12;
                GameObject hill = Shape(PrimitiveType.Sphere, "Dune", new Vector3(Mathf.Cos(angle) * 64, -2.4f, Mathf.Sin(angle) * 64), new Vector3(18, 9 + i % 3 * 2, 20), Sand * 1.1f, root);
                hill.isStatic = true;
            }
            Box("West boundary", new Vector3(-75, 2, 0), new Vector3(2, 6, 150), Road, root);
            Box("East boundary", new Vector3(75, 2, 0), new Vector3(2, 6, 150), Road, root);
            Box("North boundary", new Vector3(0, 2, 75), new Vector3(150, 6, 2), Road, root);
            Box("South boundary", new Vector3(0, 2, -75), new Vector3(150, 6, 2), Road, root);
            for (int i = 0; i < 16; i++)
            {
                Vector3 position = new Vector3(-13 - i % 4 * 2.2f, 1 + i / 8 * 2, 14 + i / 4 % 2 * 2.2f);
                GameObject crate = Box("Impulse crate", position, Vector3.one * 1.8f, i % 2 == 0 ? Orange : Teal, root);
                crate.AddComponent<Rigidbody>().mass = 28;
            }
            for (int i = 0; i < 6; i++)
            {
                GameObject drum = Shape(PrimitiveType.Cylinder, "Physics barrel", new Vector3(22, 0.9f, -12 + i * 2.5f), new Vector3(1.2f, 0.9f, 1.2f), Orange, root);
                drum.AddComponent<Rigidbody>().mass = 18;
            }
            for (int i = 0; i < 12; i++)
                Box("Track marker", new Vector3(-7.3f, 0.18f, -44 + i * 8), new Vector3(0.7f, 0.35f, 3), i % 2 == 0 ? Orange : Color.white, root);

            VehiclePhysics car = BuildVehicle(root, weapons);
            Vector3[] gatePositions = { new Vector3(0, 0, -20), new Vector3(0, 0, 24), new Vector3(0, 0, 46), new Vector3(36, 0, 46), new Vector3(36, 0, 8), new Vector3(36, 0, -36), new Vector3(17, 0, -48), new Vector3(0, 0, -38) };
            float[] headings = { 0, 0, 45, 180, 180, 210, 270, 0 };
            Checkpoint[] gates = new Checkpoint[gatePositions.Length];
            for (int i = 0; i < gates.Length; i++)
            {
                GameObject gate = new GameObject("Checkpoint " + (i + 1));
                gate.transform.SetParent(root);
                gate.transform.position = gatePositions[i];
                gate.transform.rotation = Quaternion.Euler(0, headings[i], 0);
                BoxCollider trigger = gate.AddComponent<BoxCollider>();
                trigger.isTrigger = true;
                trigger.center = new Vector3(0, 3, 0);
                trigger.size = new Vector3(14, 9, 3);
                GameObject left = Box("Gate left", Vector3.zero, new Vector3(0.5f, 6, 0.5f), Teal, gate.transform);
                left.transform.localPosition = new Vector3(-7, 3, 0);
                GameObject right = Box("Gate right", Vector3.zero, new Vector3(0.5f, 6, 0.5f), Teal, gate.transform);
                right.transform.localPosition = new Vector3(7, 3, 0);
                GameObject top = Box("Gate header", Vector3.zero, new Vector3(14.5f, 0.5f, 0.5f), Teal, gate.transform);
                top.transform.localPosition = new Vector3(0, 6, 0);
                gates[i] = gate.AddComponent<Checkpoint>();
                gates[i].Configure(rules, i, new[] { left.GetComponent<Renderer>(), right.GetComponent<Renderer>(), top.GetComponent<Renderer>() });
                Label((i + 1).ToString("00"), gatePositions[i] + Vector3.up * 6.8f, root, 1.5f);
            }
            Vector3[] targetPositions = { new Vector3(-10, 2, -13), new Vector3(12, 2, 5), new Vector3(-10, 2, 33), new Vector3(20, 2, 57), new Vector3(48, 2, 29), new Vector3(24, 2, -8), new Vector3(48, 2, -34), new Vector3(15, 2, -59) };
            Target[] targets = new Target[targetPositions.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                GameObject target = Shape(PrimitiveType.Sphere, "Target " + (i + 1), targetPositions[i], Vector3.one * (i % 2 == 0 ? 3f : 2.2f), new Color(0.95f, 0.12f, 0.18f), root);
                Rigidbody targetBody = target.AddComponent<Rigidbody>();
                targetBody.isKinematic = true;
                targets[i] = target.AddComponent<Target>();
                targets[i].Configure(rules, i % 2 == 0 ? 100 : 200, i % 2 == 0 ? 0 : 4);
                string labelName = i % 2 == 0 ? "100" : "200 / MOVING";
                string labelText = i % 2 == 0 ? "100" : PlaygroundText.MovingTarget;
                Label(labelName, targetPositions[i] + Vector3.up * 2.5f, root, 0.7f, labelText);
            }
            rules.Configure(car, gates, targets);
            GameObject cameraObject = new GameObject("Playground Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.transform.SetParent(root);
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = car.transform.position + new Vector3(0, 5, -9);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.fieldOfView = 65;
            camera.farClipPlane = 350;
            camera.backgroundColor = new Color(0.22f, 0.38f, 0.5f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            VehicleCamera view = cameraObject.AddComponent<VehicleCamera>();
            view.Configure(car.transform);
            LocalPlayerInput input = world.AddComponent<LocalPlayerInput>();
            input.Configure(car, car.GetComponent<WeaponSystem>(), view, rules);
            world.AddComponent<PlaygroundHud>().Configure(input, car, car.GetComponent<WeaponSystem>(), rules);
            GameObject sun = new GameObject("Afternoon sun", typeof(Light));
            sun.transform.SetParent(root);
            sun.transform.rotation = Quaternion.Euler(48, -32, 0);
            Light light = sun.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 2.2f;
            light.shadows = LightShadows.Soft;
            RenderSettings.ambientLight = new Color(0.6f, 0.65f, 0.7f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.fog = true;
            RenderSettings.fogColor = camera.backgroundColor;
            RenderSettings.fogDensity = 0.003f;
            Label("MAD MACHINES", new Vector3(-13, 8, -30), root, 2, PlaygroundText.Title);
            Label("PHYSICS PLAYGROUND", new Vector3(-13, 6, -30), root, 0.8f, PlaygroundText.Subtitle);
            return world;
        }

        private static VehiclePhysics BuildVehicle(Transform root, WeaponDefinition[] weapons)
        {
            GameObject car = new GameObject("RUST ROCKET — player vehicle");
            car.transform.SetParent(root);
            car.transform.position = new Vector3(0, 1.5f, -42);
            car.layer = 2;
            Transform visualRoot = new GameObject("Vehicle visuals").transform;
            visualRoot.SetParent(car.transform, false);
            Transform bodyRoot = new GameObject("Body visuals").transform;
            bodyRoot.SetParent(visualRoot, false);
            GameObject hull = Box("Armoured tub", car.transform.position, new Vector3(1.9f, 0.65f, 3.3f), Teal, car.transform);
            hull.layer = 2;
            GameObject nose = Box("Orange bonnet", car.transform.position + new Vector3(0, 0.45f, 0.85f), new Vector3(1.7f, 0.4f, 1.2f), Orange, car.transform);
            nose.layer = 2;
            GameObject cab = Box("Driver cage", car.transform.position + new Vector3(0, 0.6f, -0.8f), new Vector3(1.4f, 0.75f, 1.2f), Road, car.transform);
            cab.layer = 2;
            SeparateBodyVisual(hull, bodyRoot);
            SeparateBodyVisual(nose, bodyRoot);
            SeparateBodyVisual(cab, bodyRoot);
            Transform[] wheels = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                GameObject wheel = Shape(PrimitiveType.Cylinder, "Suspended wheel " + i, car.transform.position, new Vector3(0.86f, 0.25f, 0.86f), new Color(0.055f, 0.07f, 0.085f), car.transform);
                wheel.layer = 2;
                RemoveCollider(wheel);
                Transform anchor = new GameObject("Wheel anchor " + i).transform;
                anchor.SetParent(visualRoot, false);
                wheel.transform.SetParent(anchor, false);
                wheel.transform.localPosition = Vector3.zero;
                wheels[i] = anchor;
            }
            GameObject turret = Shape(PrimitiveType.Cylinder, "Weapon mount", car.transform.position + Vector3.up * 0.9f, new Vector3(1, 0.25f, 1), Orange, car.transform);
            turret.layer = 2;
            RemoveCollider(turret);
            // Restore unit mount scale so gun dimensions and aim remain independent of the visual turret.
            GameObject mount = new GameObject("Turret yaw");
            mount.transform.SetParent(car.transform, false);
            mount.transform.localPosition = new Vector3(0, 1.1f, 0.2f);
            GameObject barrel = new GameObject("Gun pitch");
            barrel.transform.SetParent(mount.transform, false);
            GameObject tube = Box("Cannon barrel", Vector3.zero, new Vector3(0.3f, 0.3f, 2f), Road, barrel.transform);
            tube.transform.localPosition = new Vector3(0, 0, 1f);
            tube.layer = 2;
            RemoveCollider(tube);
            GameObject muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(barrel.transform, false);
            muzzle.transform.localPosition = new Vector3(0, 0, 2.2f);
            VehiclePhysics physics = car.AddComponent<VehiclePhysics>();
            physics.Configure();
            visualRoot.gameObject.AddComponent<VehicleVisuals>().Configure(physics, bodyRoot, wheels);
            car.AddComponent<WeaponSystem>().Configure(physics, mount.transform, barrel.transform, muzzle.transform, weapons);
            return physics;
        }

        private static void SeparateBodyVisual(GameObject colliderObject, Transform bodyRoot)
        {
            GameObject geometry = new GameObject(colliderObject.name + " geometry");
            geometry.layer = colliderObject.layer;
            geometry.transform.SetParent(bodyRoot, false);
            geometry.transform.localPosition = colliderObject.transform.localPosition;
            geometry.transform.localRotation = colliderObject.transform.localRotation;
            geometry.transform.localScale = colliderObject.transform.localScale;
            geometry.AddComponent<MeshFilter>().sharedMesh = colliderObject.GetComponent<MeshFilter>().sharedMesh;
            MeshRenderer renderer = colliderObject.GetComponent<MeshRenderer>();
            geometry.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            renderer.enabled = false;
        }

        private static void RemoveCollider(GameObject target)
        {
            Collider collider = target.GetComponent<Collider>();
            collider.enabled = false;
            if (Application.isPlaying) Object.Destroy(collider); else Object.DestroyImmediate(collider);
        }

        private static GameObject Box(string name, Vector3 position, Vector3 scale, Color color, Transform parent)
        {
            return Shape(PrimitiveType.Cube, name, position, scale, color, parent);
        }

        private static GameObject Shape(PrimitiveType type, string name, Vector3 position, Vector3 scale, Color color, Transform parent)
        {
            GameObject shape = GameObject.CreatePrimitive(type);
            shape.name = name;
            shape.transform.SetParent(parent);
            shape.transform.position = position;
            shape.transform.localScale = scale;
            shape.GetComponent<Renderer>().sharedMaterial = PlaygroundEffects.Material(color);
            return shape;
        }

        private static void Label(string name, Vector3 position, Transform root, float size, string text = null)
        {
            GameObject label = new GameObject(name);
            label.transform.SetParent(root);
            label.transform.position = position;
            TextMesh mesh = label.AddComponent<TextMesh>();
            mesh.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.GetComponent<Renderer>().sharedMaterial = mesh.font.material;
            mesh.text = text ?? name;
            mesh.characterSize = size;
            mesh.fontSize = 48;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.color = Color.white;
        }

        private static void Ramp(Transform root, Vector3 position, float yaw, float width, float length, float height)
        {
            GameObject ramp = new GameObject("Jump ramp");
            ramp.transform.SetParent(root);
            ramp.transform.position = position;
            ramp.transform.rotation = Quaternion.Euler(0, yaw, 0);
            Mesh mesh = new Mesh { name = "Wedge" };
            mesh.vertices = new[] { new Vector3(-width / 2, 0, -length / 2), new Vector3(width / 2, 0, -length / 2), new Vector3(-width / 2, 0, length / 2), new Vector3(width / 2, 0, length / 2), new Vector3(-width / 2, height, length / 2), new Vector3(width / 2, height, length / 2) };
            mesh.triangles = new[] { 0, 4, 1, 1, 4, 5, 0, 2, 4, 1, 5, 3, 2, 3, 5, 2, 5, 4, 0, 1, 2, 1, 3, 2 };
            mesh.RecalculateNormals();
            ramp.AddComponent<MeshFilter>().sharedMesh = mesh;
            ramp.AddComponent<MeshRenderer>().sharedMaterial = PlaygroundEffects.Material(Orange * 0.8f);
            ramp.AddComponent<MeshCollider>().sharedMesh = mesh;
        }
    }
}
