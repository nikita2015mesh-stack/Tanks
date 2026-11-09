using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class BuildT3485
{
    const string Folder = "Assets/T-34-85";
    const string PrefabPath = Folder + "/T-34-85.prefab";

    [MenuItem("Tools/T-34-85/Build model in scene")]
    static void Build()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath))
        {
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            return;
        }

        AssetDatabase.CreateFolder("Assets", "T-34-85");
        Material olive = Paint("Armor", new Color(.29f, .34f, .18f), .22f);
        Material dark = Paint("Dark steel", new Color(.12f, .13f, .10f), .38f);
        Material rubber = Paint("Rubber", new Color(.065f, .068f, .059f), 0);
        Material links = Paint("Track steel", new Color(.21f, .20f, .17f), .48f);
        Material lens = Paint("Headlamp glass", new Color(.65f, .74f, .68f), .15f);

        GameObject draft = GameObject.Find("T-34-85");
        Transform root = new GameObject("T-34-85").transform;
        Transform hull = Part(root, "Hull", Vector3.zero);
        var armor = new Geometry();
        armor.Rings(
            HullRing(.47f, 1.02f, -2.73f, 2.57f, .20f),
            HullRing(1.03f, 1.38f, -2.99f, 3.08f, .16f),
            HullRing(1.67f, 1.20f, -2.50f, 2.12f, .18f));
        armor.Tube(new Vector3(0, 1.67f, .32f), new Vector3(0, 1.79f, .32f), 1.00f, 1.00f, 32);
        armor.Box(new Vector3(-.38f, 1.49f, 2.44f), new Vector3(.69f, .085f, .56f), Quaternion.Euler(34, 0, 0));
        for (int side = -1; side <= 1; side += 2)
        {
            armor.Box(new Vector3(side * 1.20f, 1.25f, .16f), new Vector3(.52f, .075f, 5.72f), Quaternion.identity);
            armor.Box(new Vector3(side * 1.22f, 1.44f, -1.34f), new Vector3(.31f, .28f, 1.04f), Quaternion.identity);
            armor.Tube(new Vector3(side * 1.40f, 1.45f, -2.56f), new Vector3(side * 1.40f, 1.45f, -1.68f), .20f, .20f, 20);
        }
        armor.Attach(hull, "Armor plates", olive);
        var fittings = new Geometry();
        for (int i = 0; i < 12; i++)
            fittings.Box(new Vector3(0, 1.68f, -2.30f + i * .08f), new Vector3(1.29f, .04f, .035f), Quaternion.identity);
        fittings.Tube(new Vector3(0, 1.38f, 2.67f), new Vector3(0, 1.38f, 2.92f), .085f, .073f, 14);
        fittings.Tube(new Vector3(-1.02f, 1.36f, 2.53f), new Vector3(-1.02f, 1.36f, 2.72f), .14f, .13f, 20);
        fittings.Attach(hull, "Grille and fittings", dark);
        var lights = new Geometry();
        lights.Tube(new Vector3(-1.02f, 1.36f, 2.715f), new Vector3(-1.02f, 1.36f, 2.74f), .106f, .106f, 20);
        lights.Attach(hull, "Headlamp", lens);

        Tracks(root, -1, olive, rubber, links, dark);
        Tracks(root, 1, olive, rubber, links, dark);

        Transform turret = Part(root, "Turret pivot (Y)", new Vector3(0, 1.77f, .32f));
        var casting = new Geometry();
        casting.Rings(TurretRing(.03f, .82f, .83f), TurretRing(.15f, 1.05f, 1.05f),
            TurretRing(.65f, 1.01f, .98f), TurretRing(.83f, .83f, .89f), TurretRing(.87f, .72f, .81f));
        casting.Tube(new Vector3(-.39f, .83f, -.39f), new Vector3(-.39f, 1.02f, -.39f), .27f, .25f, 24);
        casting.Tube(new Vector3(-.39f, 1.02f, -.39f), new Vector3(-.39f, 1.09f, -.39f), .25f, .21f, 24);
        casting.Tube(new Vector3(.36f, .84f, -.18f), new Vector3(.36f, .91f, -.18f), .25f, .23f, 24);
        casting.Box(new Vector3(.49f, .91f, .38f), new Vector3(.16f, .12f, .20f), Quaternion.identity);
        casting.Attach(turret, "Cast turret and hatches", olive);
        var detail = new Geometry();
        detail.Tube(new Vector3(.62f, .83f, -.71f), new Vector3(.62f, 1.30f, -.75f), .019f, .008f, 8);
        for (int side = -1; side <= 1; side += 2)
            detail.Box(new Vector3(side * .85f, .62f, -.28f), new Vector3(.07f, .046f, .53f), Quaternion.identity);
        detail.Attach(turret, "Antenna and handrails", dark);
        Transform gun = Part(turret, "Gun elevation pivot (X)", new Vector3(0, .43f, .95f));
        var barrel = new Geometry();
        barrel.Tube(new Vector3(-.46f, 0, .02f), new Vector3(.46f, 0, .02f), .32f, .32f, 24);
        barrel.Tube(new Vector3(0, 0, .18f), new Vector3(0, 0, .48f), .171f, .145f, 24);
        barrel.Tube(new Vector3(0, 0, .48f), new Vector3(0, 0, 2.68f), .097f, .066f, 24);
        barrel.Attach(gun, "Mantlet and barrel", olive);
        var muzzle = new Geometry();
        muzzle.Tube(new Vector3(0, 0, 2.67f), new Vector3(0, 0, 2.70f), .049f, .049f, 20);
        muzzle.Attach(gun, "Muzzle bore", dark);

        if (draft && draft.transform.childCount == 1 && draft.transform.GetChild(0).name == "Hull")
            Undo.DestroyObjectImmediate(draft);
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Build T-34-85");
        AssetDatabase.SaveAssets();
        PrefabUtility.SaveAsPrefabAssetAndConnect(root.gameObject, PrefabPath, InteractionMode.UserAction);
        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        EditorSceneManager.SaveScene(root.gameObject.scene);
        Selection.activeGameObject = root.gameObject;
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    static void Tracks(Transform root, int side, Material olive, Material rubber, Material steel, Material dark)
    {
        Transform track = Part(root, side < 0 ? "Left track" : "Right track", new Vector3(side * 1.245f, 0, 0));
        var belt = new Geometry();
        var rims = new Geometry();
        var tires = new Geometry();
        var bolts = new Geometry();
        const float front = 2.47f, rear = -2.55f, height = .61f, radius = .56f;
        float straight = front - rear;
        float arc = Mathf.PI * radius;
        float loop = 2 * (straight + arc);
        const int count = 76;
        for (int i = 0; i < count; i++)
        {
            float d = i * loop / count;
            Vector3 p;
            float tilt;
            if (d < straight)
            {
                p = new Vector3(0, height + radius, rear + d);
                tilt = 0;
            }
            else if (d < straight + arc)
            {
                float a = (d - straight) / radius;
                p = new Vector3(0, height + radius * Mathf.Cos(a), front + radius * Mathf.Sin(a));
                tilt = a * Mathf.Rad2Deg;
            }
            else if (d < 2 * straight + arc)
            {
                p = new Vector3(0, height - radius, front - (d - straight - arc));
                tilt = 180;
            }
            else
            {
                float a = (d - 2 * straight - arc) / radius;
                p = new Vector3(0, height - radius * Mathf.Cos(a), rear - radius * Mathf.Sin(a));
                tilt = 180 + a * Mathf.Rad2Deg;
            }
            Quaternion rotation = Quaternion.Euler(tilt, 0, 0);
            belt.Box(p, new Vector3(.52f, .095f, loop / count * .97f), rotation);
            belt.Box(p + rotation * new Vector3(0, .06f, 0), new Vector3(.45f, .035f, .043f), rotation);
        }
        for (int i = 0; i < 7; i++)
        {
            bool end = i == 0 || i == 6;
            float z = i == 0 ? rear : i == 6 ? front : -1.94f + (i - 1) * .97f;
            float y = end ? height : .52f;
            float r = end ? .36f : .445f;
            tires.Tube(new Vector3(-.20f, y, z), new Vector3(.20f, y, z), r, r, 28);
            rims.Tube(new Vector3(side * .195f, y, z), new Vector3(side * .225f, y, z), r * .82f, r * .77f, 28);
            rims.Tube(new Vector3(side * .223f, y, z), new Vector3(side * .265f, y, z), r * .31f, r * .27f, 18);
            for (int j = 0; j < 10; j++)
            {
                float a = j * 2 * Mathf.PI / 10;
                var bolt = new Vector3(side * .222f, y + Mathf.Cos(a) * r * .59f, z + Mathf.Sin(a) * r * .59f);
                bolts.Tube(bolt, bolt + Vector3.right * side * .015f, .028f, .023f, 6);
            }
        }
        belt.Attach(track, "Individual links", steel);
        tires.Attach(track, "Road wheels and end wheels", rubber);
        rims.Attach(track, "Wheel rims and hubs", olive);
        bolts.Attach(track, "Wheel bolts", dark);
    }

    static Material Paint(string name, Color color, float metallic)
    {
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", .26f);
        AssetDatabase.CreateAsset(material, Folder + "/" + name + ".mat");
        return material;
    }

    static Transform Part(Transform parent, string name, Vector3 position)
    {
        Transform part = new GameObject(name).transform;
        part.SetParent(parent, false);
        part.localPosition = position;
        return part;
    }

    static Vector3[] HullRing(float y, float x, float back, float front, float bevel) => new[]
    {
        new Vector3(-x+bevel,y,back), new Vector3(x-bevel,y,back),
        new Vector3(x,y,back+bevel), new Vector3(x,y,front-bevel),
        new Vector3(x-bevel,y,front), new Vector3(-x+bevel,y,front),
        new Vector3(-x,y,front-bevel), new Vector3(-x,y,back+bevel)
    };

    static Vector3[] TurretRing(float y, float width, float length)
    {
        Vector2[] outline = {
            new(-.65f,-1.27f), new(.65f,-1.27f), new(.91f,-1.05f), new(1,-.58f),
            new(.98f,.12f), new(.83f,.65f), new(.54f,1.10f), new(.28f,1.20f),
            new(-.28f,1.20f), new(-.54f,1.10f), new(-.83f,.65f), new(-.98f,.12f),
            new(-1,-.58f), new(-.91f,-1.05f)
        };
        var ring = new Vector3[outline.Length];
        for (int i = 0; i < ring.Length; i++) ring[i] = new Vector3(outline[i].x * width, y, outline[i].y * length);
        return ring;
    }

    sealed class Geometry
    {
        readonly List<Vector3> vertices = new();
        readonly List<int> triangles = new();

        public void Box(Vector3 center, Vector3 size, Quaternion rotation)
        {
            Vector3[] Ring(float y) => new[]
            {
                new Vector3(-.5f,y,-.5f), new Vector3(.5f,y,-.5f),
                new Vector3(.5f,y,.5f), new Vector3(-.5f,y,.5f)
            };
            var rings = new[] { Ring(-.5f), Ring(.5f) };
            foreach (Vector3[] ring in rings)
                for (int i = 0; i < ring.Length; i++)
                    ring[i] = center + rotation * Vector3.Scale(ring[i], size);
            Rings(rings);
        }

        public void Tube(Vector3 start, Vector3 end, float radiusA, float radiusB, int sides)
        {
            Quaternion orientation = Quaternion.FromToRotation(Vector3.up, (end - start).normalized);
            var a = new Vector3[sides];
            var b = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float angle = 2 * Mathf.PI * i / sides;
                Vector3 offset = orientation * new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                a[i] = start + offset * radiusA;
                b[i] = end + offset * radiusB;
            }
            Rings(a, b);
        }

        public void Rings(params Vector3[][] rings)
        {
            Vector3 center = Vector3.zero;
            foreach (var ring in rings)
                foreach (Vector3 point in ring) center += point;
            int n = rings[0].Length;
            center /= rings.Length * n;
            Face(center, rings[0]);
            for (int j = 1; j < rings.Length; j++)
                for (int i = 0; i < n; i++)
                {
                    int k = (i + 1) % n;
                    Face(center, rings[j-1][i], rings[j-1][k], rings[j][k], rings[j][i]);
                }
            Face(center, rings[rings.Length - 1]);
        }

        void Face(Vector3 interior, params Vector3[] points)
        {
            int first = vertices.Count;
            vertices.AddRange(points);
            bool reverse = Vector3.Dot(Vector3.Cross(points[1]-points[0], points[2]-points[0]), points[0]-interior) < 0;
            for (int i = 1; i < points.Length - 1; i++)
            {
                triangles.Add(first);
                triangles.Add(first + (reverse ? i + 1 : i));
                triangles.Add(first + (reverse ? i : i + 1));
            }
        }

        public void Attach(Transform parent, string name, Material material)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + name + ".asset"));
            Transform part = Part(parent, name, Vector3.zero);
            part.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
