using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public sealed class TankPrototypeControls : MonoBehaviour
{
    [SerializeField] Transform turret;
    [SerializeField] Camera followCamera;
    [SerializeField, Min(0)] float forwardSpeed = 5f;
    [SerializeField, Min(0)] float reverseSpeed = 2f;
    [SerializeField, Min(0)] float hullTurnSpeed = 65f;
    [SerializeField, Min(0)] float turretTurnSpeed = 90f;
    [SerializeField, Min(0)] float mouseSensitivity = 0.16f;
    [SerializeField, Min(1)] float shellSpeed = 30f;
    [SerializeField, Min(0)] float reloadTime = 0.8f;

    float cameraYaw;
    float cameraPitch = 23f;
    bool ownsCursor;
    Transform barrel;
    float nextShotTime;
    bool showTitle = true;
    bool inBattle;
    const float MinSpread = 0.45f;
    const float MaxSpread = 3.5f;
    float spread = MinSpread;
    GUIStyle hangarTitle;
    GUIStyle hangarText;
    GUIStyle battleButton;
    GUIStyle healthText;

    void OnEnable()
    {
        cameraYaw = turret ? turret.eulerAngles.y : transform.eulerAngles.y;
        barrel = turret ? turret.Find("Gun Barrel") : null;
    }

    void OnDisable() => ReleaseCursor();

    void OnApplicationFocus(bool focused)
    {
        if (!focused)
            ReleaseCursor();
    }

    void Update()
    {
        if (!inBattle)
            return;

        Keyboard keyboard = Keyboard.current;
        float step = Time.deltaTime;
        float movement = 0;
        Mouse mouse = Mouse.current;
        bool fire = false;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            ReleaseCursor();
        else if (mouse != null && mouse.leftButton.wasPressedThisFrame && Application.isFocused)
        {
            fire = ownsCursor && Cursor.lockState == CursorLockMode.Locked;
            ownsCursor = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (mouse != null && ownsCursor && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 delta = mouse.delta.ReadValue() * mouseSensitivity;
            cameraYaw = Mathf.Repeat(cameraYaw + delta.x, 360f);
            cameraPitch = Mathf.Clamp(cameraPitch - delta.y, 8f, 60f);
        }

        if (keyboard != null)
        {
            float drive = Axis(keyboard.wKey.isPressed, keyboard.sKey.isPressed);
            float steer = Axis(keyboard.dKey.isPressed, keyboard.aKey.isPressed);
            movement = Mathf.Max(Mathf.Abs(drive), Mathf.Abs(steer));
            float aim = Axis(keyboard.eKey.isPressed, keyboard.qKey.isPressed);
            cameraYaw = Mathf.Repeat(cameraYaw + aim * turretTurnSpeed * step, 360f);
            transform.Rotate(0, steer * hullTurnSpeed * step, 0, Space.Self);
            float speed = drive >= 0 ? forwardSpeed : reverseSpeed;
            transform.position += transform.forward * (drive * speed * step);
        }

        float aimError = 0;
        if (turret)
        {
            Quaternion targetRotation = Quaternion.Euler(0, cameraYaw, 0);
            aimError = Quaternion.Angle(turret.rotation, targetRotation);
            turret.rotation = Quaternion.RotateTowards(turret.rotation, targetRotation, turretTurnSpeed * step);
        }

        float desiredSpread = Mathf.Lerp(MinSpread, MaxSpread,
            Mathf.Clamp01(movement * 0.65f + aimError / 36f));
        spread = Mathf.MoveTowards(spread, desiredSpread, (spread < desiredSpread ? 12f : 1.8f) * step);
        if (fire && barrel && Time.time >= nextShotTime)
        {
            nextShotTime = Time.time + reloadTime;
            StartCoroutine(FlyShell());
            spread = MaxSpread;
        }
    }

    IEnumerator FlyShell()
    {
        // The cylinder mesh runs from -1 to +1 along its local Y axis.
        Vector3 muzzle = barrel.TransformPoint(Vector3.up);
        Vector3 bore = (muzzle - barrel.position).normalized;
        Vector2 deviation = Random.insideUnitCircle * Mathf.Tan(spread * Mathf.Deg2Rad);
        Quaternion boreRotation = Quaternion.LookRotation(bore);
        Vector3 direction = boreRotation * new Vector3(deviation.x, deviation.y, 1).normalized;
        var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shell.name = "Tank shell";
        shell.GetComponent<Collider>().enabled = false;
        shell.transform.SetPositionAndRotation(muzzle + bore * 0.14f, Quaternion.LookRotation(direction));
        shell.transform.localScale = new Vector3(0.16f, 0.16f, 0.45f);
        var appearance = new MaterialPropertyBlock();
        appearance.SetColor("_BaseColor", new Color(1f, 0.37f, 0.045f));
        shell.GetComponent<Renderer>().SetPropertyBlock(appearance);
        Destroy(shell, 2.5f);

        while (shell)
        {
            yield return null;
            if (!shell)
                yield break;

            Vector3 position = shell.transform.position;
            float distance = shellSpeed * Time.deltaTime;
            bool hitTarget = false;
            foreach (RaycastHit hit in Physics.RaycastAll(position, direction, distance, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider.transform.IsChildOf(transform))
                {
                    hitTarget = true;
                    break;
                }
            }
            if (hitTarget)
            {
                Destroy(shell);
                yield break;
            }
            shell.transform.position = position + direction * distance;
        }
    }

    void LateUpdate()
    {
        if (!followCamera)
            return;

        Vector3 pivot = transform.position + Vector3.up * 1.2f;
        Quaternion orbit = inBattle
            ? Quaternion.Euler(cameraPitch, cameraYaw, 0)
            : Quaternion.Euler(19f, transform.eulerAngles.y + 135f, 0);
        followCamera.transform.SetPositionAndRotation(pivot - orbit * Vector3.forward * 8.5f, orbit);
    }

    void OnGUI()
    {
        if (inBattle)
        {
            DrawAimCircle();
            DrawHealthBar();
            return;
        }

        if (hangarTitle == null)
        {
            hangarTitle = new GUIStyle(GUI.skin.label) { fontSize = 29, fontStyle = FontStyle.Bold };
            hangarText = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            battleButton = new GUIStyle(GUI.skin.button) { fontSize = 20, fontStyle = FontStyle.Bold };
        }

        if (showTitle)
        {
            DrawTitleScreen();
            return;
        }

        const float width = 234f;
        float height = Mathf.Min(218f, Screen.height - 24f);
        var panel = new Rect(12f, (Screen.height - height) / 2f, width, height);
        Color previous = GUI.color;
        GUI.color = new Color(.075f, .095f, .10f, .94f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = new Color(.66f, .76f, .58f);
        GUI.Label(new Rect(panel.x + 16f, panel.y + 12f, 200f, 24f), "АНГАР", hangarText);
        GUI.color = Color.white;
        GUI.Label(new Rect(panel.x + 14f, panel.y + 38f, 200f, 42f), "ТАНЧИКИ", hangarTitle);
        GUI.Label(new Rect(panel.x + 16f, panel.y + 84f, 200f, 24f), "Т-34-85  /  Тестовый полигон", hangarText);
        Color previousBackground = GUI.backgroundColor;
        GUI.backgroundColor = new Color(.50f, .76f, .35f);
        if (GUI.Button(new Rect(panel.x + 16f, panel.yMax - 64f, width - 32f, 46f), "В БОЙ", battleButton))
        {
            inBattle = true;
            ownsCursor = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        GUI.backgroundColor = previousBackground;
        GUI.color = previous;
    }

    void DrawTitleScreen()
    {
        Color previous = GUI.color;
        GUI.color = new Color(.05f, .07f, .06f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float width = Mathf.Min(260f, Screen.width - 24f);
        float x = (Screen.width - width) * 0.5f;
        float y = Screen.height * 0.5f;
        GUI.Label(new Rect(x, y - 60f, width, 42f), "ТАНЧИКИ", hangarTitle);
        if (GUI.Button(new Rect(x, y, width, 46f), "В АНГАР", battleButton))
            showTitle = false;

        GUI.color = previous;
    }

    void DrawHealthBar()
    {
        if (Event.current.type != EventType.Repaint)
            return;

        healthText ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };

        float width = Mathf.Min(300f, Screen.width - 24f);
        var border = new Rect((Screen.width - width) * 0.5f, Screen.height - 49f, width, 29f);
        var fill = new Rect(border.x + 2f, border.y + 2f, border.width - 4f, border.height - 4f);
        Color previous = GUI.color;
        GUI.color = new Color(.07f, .11f, .08f);
        GUI.DrawTexture(border, Texture2D.whiteTexture);
        GUI.color = new Color(.18f, .57f, .24f);
        GUI.DrawTexture(fill, Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(fill, @"1000\1000", healthText);
        GUI.color = previous;
    }

    void DrawAimCircle()
    {
        if (Event.current.type != EventType.Repaint || !barrel || !followCamera)
            return;

        Vector3 muzzle = barrel.TransformPoint(Vector3.up);
        Vector3 bore = (muzzle - barrel.position).normalized;
        Vector3 projected = followCamera.WorldToScreenPoint(muzzle + bore * 35f);
        if (projected.z <= 0)
            return;

        float pixelsPerRadian = followCamera.pixelHeight * 0.5f /
            Mathf.Tan(followCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float radius = Mathf.Max(9f, Mathf.Tan(spread * Mathf.Deg2Rad) * pixelsPerRadian);
        float x = Mathf.Clamp(projected.x, radius + 3f, Screen.width - radius - 3f);
        float y = Mathf.Clamp(Screen.height - projected.y, radius + 3f, Screen.height - radius - 3f);
        Color previous = GUI.color;
        GUI.color = Color.Lerp(new Color(.72f, 1f, .48f), new Color(1f, .66f, .22f),
            Mathf.InverseLerp(MinSpread, MaxSpread, spread));
        const int points = 40;
        for (int i = 0; i < points; i++)
        {
            float angle = i * 2f * Mathf.PI / points;
            float dotX = x + Mathf.Cos(angle) * radius;
            float dotY = y + Mathf.Sin(angle) * radius;
            GUI.DrawTexture(new Rect(dotX - 1f, dotY - 1f, 2f, 2f), Texture2D.whiteTexture);
        }
        GUI.DrawTexture(new Rect(x - 1f, y - 1f, 3f, 3f), Texture2D.whiteTexture);
        GUI.color = previous;
    }

    void ReleaseCursor()
    {
        if (!ownsCursor)
            return;

        ownsCursor = false;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    static int Axis(bool positive, bool negative) => (positive ? 1 : 0) - (negative ? 1 : 0);

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Tools/Tank Prototype/Connect Controls")]
    static void ConnectControls()
    {
        GameObject tank = GameObject.Find("T-34-85");
        Transform turret = tank ? tank.transform.Find("Turret") : null;
        Camera camera = Camera.main;
        if (!tank || !turret || !camera || tank.scene != camera.gameObject.scene)
        {
            Debug.LogError("Open the tank scene with T-34-85, its Turret child, and the Main Camera.");
            return;
        }

        var controller = tank.GetComponent<TankPrototypeControls>();
        if (!controller)
            controller = UnityEditor.Undo.AddComponent<TankPrototypeControls>(tank);

        UnityEditor.Undo.RecordObject(controller, "Connect tank prototype controls");
        controller.turret = turret;
        controller.followCamera = camera;
        UnityEditor.EditorUtility.SetDirty(controller);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(tank.scene);
        UnityEditor.Selection.activeGameObject = tank;
        Debug.Log("Tank controls connected. Play: click Game, move mouse to aim, W/S drive, A/D steer, Esc releases cursor.");
    }

    [UnityEditor.MenuItem("Tools/Tank Prototype/Add Test Floor")]
    static void AddTestFloor()
    {
        if (Application.isPlaying || GameObject.Find("Tank Test Floor"))
            return;

        var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "Tank Test Floor";
        floor.transform.position = new Vector3(0, -0.47f, 0);
        floor.transform.localScale = new Vector3(8, 1, 8);
        for (int row = -3; row <= 3; row++)
        {
            for (int column = -3; column <= 3; column++)
            {
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                marker.name = "Distance marker";
                marker.transform.SetPositionAndRotation(new Vector3(column * 10 + 4, -0.41f, row * 10 + 4), Quaternion.identity);
                marker.transform.localScale = new Vector3(1.7f, 0.12f, 0.32f);
                marker.transform.SetParent(floor.transform, true);
            }
        }
        UnityEditor.Undo.RegisterCreatedObjectUndo(floor, "Add tank test floor");
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(floor.scene);
    }
#endif
}
