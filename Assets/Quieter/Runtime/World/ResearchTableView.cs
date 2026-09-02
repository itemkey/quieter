using Quieter.Inventory;
using UnityEngine;

namespace Quieter.World
{
    public sealed class ResearchTableView : MonoBehaviour
    {
        public ulong ObjectId { get; private set; }
        public ItemStackState Input { get; private set; }
        public bool IsBusy { get; private set; }

        public void Initialize(ulong objectId)
        {
            ObjectId = objectId;
        }

        public void ApplyState(ItemStackState input, bool busy)
        {
            Input = input;
            IsBusy = busy;
        }

        public static ResearchTableView Create(
            ulong objectId,
            Vector3 position,
            float yaw,
            Transform parent,
            bool renderVisuals)
        {
            var root = new GameObject($"ResearchTable_{objectId:X}");
            root.transform.SetParent(parent, true);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.55f, 0f);
            collider.size = new Vector3(1.7f, 1.1f, 0.95f);
            var view = root.AddComponent<ResearchTableView>();
            view.Initialize(objectId);
            if (renderVisuals) BuildVisual(root.transform);
            return view;
        }

        public static GameObject CreatePreview(Transform parent)
        {
            var root = new GameObject("ResearchTablePreview");
            root.transform.SetParent(parent, false);
            BuildVisual(root.transform);
            foreach (var collider in root.GetComponentsInChildren<Collider>())
            {
                Destroy(collider);
            }
            return root;
        }

        public static void Tint(GameObject root, Color color)
        {
            if (root == null) return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>())
            {
                var material = new Material(renderer.material) { color = color };
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_AlphaClip", 0f);
                material.renderQueue = 3000;
                renderer.material = material;
            }
        }

        private static void BuildVisual(Transform root)
        {
            CreatePart(root, "Top", new Vector3(0f, 0.92f, 0f),
                new Vector3(1.75f, 0.16f, 0.95f), new Color(0.42f, 0.24f, 0.1f));
            CreatePart(root, "Shelf", new Vector3(0f, 0.42f, 0f),
                new Vector3(1.35f, 0.09f, 0.72f), new Color(0.34f, 0.19f, 0.075f));
            for (var x = -1; x <= 1; x += 2)
            {
                for (var z = -1; z <= 1; z += 2)
                {
                    CreatePart(root, $"Leg_{x}_{z}", new Vector3(x * 0.7f, 0.45f, z * 0.34f),
                        new Vector3(0.13f, 0.9f, 0.13f), new Color(0.29f, 0.15f, 0.055f));
                }
            }
            CreatePart(root, "SampleTray", new Vector3(0.42f, 1.04f, 0f),
                new Vector3(0.42f, 0.05f, 0.42f), new Color(0.2f, 0.22f, 0.2f));
        }

        private static void CreatePart(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Color color)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            foreach (var collider in part.GetComponents<Collider>()) Destroy(collider);
            var renderer = part.GetComponent<Renderer>();
            if (renderer == null) return;
            renderer.material = new Material(
                Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Hidden/InternalErrorShader"))
            {
                color = color,
            };
        }
    }
}
