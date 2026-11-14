using UnityEngine;

namespace MadMachines
{
    public static class VehicleSuspensionBuilder
    {
        // Does not reparent existing geometry: the editor caller does that through Undo.
        public static WheelSuspensionVisual CreateRig(Transform parent, Transform wheelAnchor, int index, Material material)
        {
            Transform root = CreateChild("Suspension " + index, parent);
            Transform spin = CreateChild("Wheel spin", wheelAnchor);
            Transform[] lower = new Transform[2];
            Transform[] upper = new Transform[2];
            for (int i = 0; i < 2; i++)
            {
                lower[i] = CreateLink("Lower arm " + i, root, material);
                upper[i] = CreateLink("Upper arm " + i, root, material);
            }
            Transform upright = CreateLink("Upright", root, material);
            Transform shockBody = CreateLink("Shock body", root, material);
            Transform shockRod = CreateLink("Shock rod", root, material);
            WheelSuspensionVisual rig = root.gameObject.AddComponent<WheelSuspensionVisual>();
            rig.Configure(spin, lower, upper, upright, shockBody, shockRod);
            return rig;
        }

        private static Transform CreateChild(string name, Transform parent)
        {
            GameObject child = new GameObject(name);
            child.layer = 2;
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static Transform CreateLink(string name, Transform parent, Material material)
        {
            GameObject link = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            link.name = name;
            link.layer = 2;
            link.transform.SetParent(parent, false);
            Collider collider = link.GetComponent<Collider>();
            collider.enabled = false;
            if (Application.isPlaying)
            {
                Object.Destroy(collider);
            }
            else
            {
                Object.DestroyImmediate(collider);
            }
            link.GetComponent<Renderer>().sharedMaterial = material;
            return link.transform;
        }
    }
}
