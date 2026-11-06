# Repository Guidelines

## Project Structure

- `BuildT3485.cs` is a Unity editor script that generates the T-34-85 meshes, materials, and prefab.
- `TankPrototypeControls.cs` is a runtime `MonoBehaviour` for driving, aiming, firing, the follow camera, and the hangar and battle UI.
- `T3485-Unity.unitypackage` distributes the editor script under `Assets/Editor/`. `.t34-package/` contains its unpacked entries. If you update the distributed generator, keep the packaged script and the root source in sync.

## Build and Run in Unity

1. Open a host Unity project. Import `T3485-Unity.unitypackage` or copy `BuildT3485.cs` into `Assets/Editor/` when iterating on the source.
2. In a scratch scene, select **Tools → T-34-85 → Build model in scene**. The generator saves the scene and creates `Assets/T-34-85/T-34-85.prefab`. Use a project without that prefab to verify generation, since the menu selects the existing prefab when present.
3. For runtime work, enable Unity's Input System, copy `TankPrototypeControls.cs` under `Assets/` outside `Editor/`, and attach it to the tank root. Assign the turret and follow camera in the Inspector. Firing requires a direct turret child named `Gun Barrel`.

## Coding Style

Use four spaces and put opening braces on their own lines. Use PascalCase for types, methods, and class constants, and camelCase for fields and locals. Use `[SerializeField]` for private settings exposed in the Inspector. Preserve serialized field and scene object names when existing references or lookups depend on them.

## Testing

The snapshot includes no automated tests or coverage threshold. Check the Unity Console after compilation and prefab generation. In Play Mode, select **В БОЙ** and verify WASD driving, Q/E and mouse aiming, left-click firing, and Escape releasing the cursor.

## Commits and Pull Requests

Git history is unavailable in this snapshot. Use short, imperative commit subjects such as `Fix shell spawn position`. In the PR, describe what changed and how you verified it in Unity. Link related issues and include screenshots or a short clip for visible changes.
