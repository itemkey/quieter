using Quieter.Inventory;
using UnityEngine;

namespace Quieter.World
{
    public static class SurvivalStructureRules
    {
        public const ushort LeanToItemId = 44;
        public const ushort HearthItemId = 45;
        public const ushort UnlinedWastePitItemId = 46;
        public const int MaximumFuelUnits = 20;
        public const ushort WastePitCapacityMilliliters = 60000;

        public static bool SupportsPlacement(ushort itemId) => itemId is
            ResourceBalance.ResearchTableItemId or LeanToItemId or HearthItemId
                or UnlinedWastePitItemId;

        public static bool IsFuel(ushort itemId) => itemId is 2 or 37;

        public static float FuelSecondsPerUnit(ushort itemId) => itemId switch
        {
            2 => 600f,
            37 => 1050f,
            _ => 0f,
        };

        public static ItemStackState AddFuel(ItemStackState current, ushort itemId)
        {
            if (!IsFuel(itemId) || (!current.IsEmpty && current.ItemId != itemId)
                || current.Quantity >= MaximumFuelUnits)
            {
                return current;
            }
            if (current.IsEmpty)
            {
                return new ItemStackState(itemId, 1, 10000);
            }
            current.Quantity++;
            if (current.Condition == 0) current.Condition = 10000;
            return current;
        }

        public static ItemStackState BurnFuel(ItemStackState fuel, float elapsedRealSeconds)
        {
            if (fuel.IsEmpty || !IsFuel(fuel.ItemId) || elapsedRealSeconds <= 0f)
                return fuel;
            var secondsPerUnit = FuelSecondsPerUnit(fuel.ItemId);
            var remaining = elapsedRealSeconds;
            if (fuel.Condition == 0) fuel.Condition = 10000;
            while (!fuel.IsEmpty && remaining > 0f)
            {
                var unitRemaining = secondsPerUnit * fuel.Condition / 10000f;
                if (remaining < unitRemaining)
                {
                    fuel.Condition = (ushort)Mathf.Clamp(
                        Mathf.CeilToInt((unitRemaining - remaining) / secondsPerUnit * 10000f),
                        1,
                        10000);
                    break;
                }
                remaining -= unitRemaining;
                fuel.Quantity--;
                if (fuel.Quantity == 0)
                {
                    fuel.Clear();
                    break;
                }
                fuel.Condition = 10000;
            }
            return fuel;
        }

        public static Vector3 PlacementHalfExtents(ushort itemId) => itemId switch
        {
            LeanToItemId => new Vector3(2.05f, 1.55f, 1.55f),
            HearthItemId => new Vector3(0.72f, 0.35f, 0.72f),
            UnlinedWastePitItemId => new Vector3(0.9f, 0.25f, 0.9f),
            _ => new Vector3(0.82f, 0.5f, 0.42f),
        };

        public static (float Biological, float Toxins) CalculatePitLeakage(
            Vector3 pitPosition,
            Vector3 waterPosition,
            float contentsLiters,
            float biologicalLoad,
            float toxinLoad,
            float rainIntensity)
        {
            var planarDistance = Vector2.Distance(
                new Vector2(pitPosition.x, pitPosition.z),
                new Vector2(waterPosition.x, waterPosition.z));
            if (contentsLiters <= 0f || planarDistance > 180f
                || waterPosition.y > pitPosition.y + 0.5f)
            {
                return (0f, 0f);
            }
            var fill = Mathf.Clamp01(contentsLiters
                / (WastePitCapacityMilliliters / 1000f));
            var proximity = 1f - planarDistance / 180f;
            var downhill = Mathf.Lerp(
                0.35f, 1f, Mathf.InverseLerp(-0.5f, 20f, pitPosition.y - waterPosition.y));
            var transport = Mathf.Lerp(0.12f, 1f, Mathf.Clamp01(rainIntensity));
            var exposure = fill * proximity * proximity * downhill * transport;
            return (
                Mathf.Clamp01(biologicalLoad) * exposure * 0.78f,
                Mathf.Clamp01(toxinLoad) * exposure * 0.42f);
        }
    }

    public sealed class SurvivalStructureView : MonoBehaviour
    {
        public ulong ObjectId { get; private set; }
        public ushort ItemId { get; private set; }
        public bool Burning { get; private set; }

        public static SurvivalStructureView Create(
            ulong objectId,
            ushort itemId,
            Vector3 position,
            float yaw,
            Transform parent,
            bool renderVisuals)
        {
            var root = new GameObject($"Structure_{itemId}_{objectId:X}");
            root.transform.SetParent(parent, true);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            BuildColliders(root.transform, itemId);
            var view = root.AddComponent<SurvivalStructureView>();
            view.ObjectId = objectId;
            view.ItemId = itemId;
            if (renderVisuals) BuildVisual(root.transform, itemId);
            return view;
        }

        public static GameObject CreatePreview(ushort itemId, Transform parent)
        {
            var root = new GameObject($"StructurePreview_{itemId}");
            root.transform.SetParent(parent, false);
            BuildVisual(root.transform, itemId);
            foreach (var collider in root.GetComponentsInChildren<Collider>())
                Object.Destroy(collider);
            return root;
        }

        public void ApplyState(ItemStackState input)
        {
            Burning = ItemId == SurvivalStructureRules.HearthItemId && !input.IsEmpty;
            var glow = transform.Find("FireGlow");
            if (glow != null) glow.gameObject.SetActive(Burning);
        }

        private static void BuildColliders(Transform root, ushort itemId)
        {
            if (itemId != SurvivalStructureRules.LeanToItemId)
            {
                var collider = root.gameObject.AddComponent<BoxCollider>();
                var extents = SurvivalStructureRules.PlacementHalfExtents(itemId);
                collider.center = new Vector3(0f, extents.y, 0f);
                collider.size = extents * 2f;
                return;
            }

            // Separate solid parts leave the living space and entrance walkable,
            // including on a headless server where visual meshes are not created.
            AddSolidPart(root, "RoofCollision", new Vector3(0f, 2.65f, 0f),
                new Vector3(3.9f, 0.12f, 3.1f), Quaternion.Euler(12f, 0f, 0f));
            AddSolidPart(root, "BackCollision", new Vector3(0f, 1.4f, -1.35f),
                new Vector3(3.8f, 2.8f, 0.1f), Quaternion.identity);
            for (var side = -1; side <= 1; side += 2)
            {
                AddSolidPart(root, $"PostCollision_{side}",
                    new Vector3(side * 1.75f, 1.48f, -1.1f),
                    new Vector3(0.16f, 2.96f, 0.16f), Quaternion.identity);
                AddSolidPart(root, $"LowPostCollision_{side}",
                    new Vector3(side * 1.75f, 1.21f, 1.1f),
                    new Vector3(0.16f, 2.42f, 0.16f), Quaternion.identity);
            }
        }

        private static void AddSolidPart(
            Transform parent, string name, Vector3 position, Vector3 size, Quaternion rotation)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation;
            part.AddComponent<BoxCollider>().size = size;
        }

        private static void BuildVisual(Transform root, ushort itemId)
        {
            if (itemId == SurvivalStructureRules.UnlinedWastePitItemId)
            {
                CreatePart(root, "PitOpening", PrimitiveType.Cylinder,
                    new Vector3(0f, 0.015f, 0f), new Vector3(0.72f, 0.025f, 0.72f),
                    new Color(0.075f, 0.055f, 0.035f));
                for (var index = 0; index < 8; index++)
                {
                    var angle = index / 8f * Mathf.PI * 2f;
                    CreatePart(root, $"PitBrace_{index}", PrimitiveType.Cube,
                        new Vector3(Mathf.Cos(angle) * 0.76f, 0.11f,
                            Mathf.Sin(angle) * 0.76f),
                        new Vector3(0.42f, 0.14f, 0.12f),
                        new Color(0.31f, 0.18f, 0.075f),
                        Quaternion.Euler(0f, -index * 45f, 0f));
                }
                return;
            }
            if (itemId == SurvivalStructureRules.HearthItemId)
            {
                for (var index = 0; index < 8; index++)
                {
                    var angle = index / 8f * Mathf.PI * 2f;
                    CreatePart(root, $"HearthStone_{index}", PrimitiveType.Sphere,
                        new Vector3(Mathf.Cos(angle) * 0.48f, 0.12f,
                            Mathf.Sin(angle) * 0.48f),
                        new Vector3(0.27f, 0.18f, 0.23f),
                        new Color(0.31f, 0.3f, 0.28f));
                }
                CreatePart(root, "Embers", PrimitiveType.Cylinder,
                    new Vector3(0f, 0.08f, 0f), new Vector3(0.38f, 0.05f, 0.38f),
                    new Color(0.2f, 0.09f, 0.035f));
                var glow = CreatePart(root, "FireGlow", PrimitiveType.Sphere,
                    new Vector3(0f, 0.36f, 0f), new Vector3(0.24f, 0.42f, 0.24f),
                    new Color(1f, 0.31f, 0.035f));
                glow.SetActive(false);
                return;
            }

            for (var side = -1; side <= 1; side += 2)
            {
                CreatePart(root, $"Post_{side}", PrimitiveType.Cylinder,
                    new Vector3(side * 1.75f, 1.48f, -1.1f),
                    new Vector3(0.16f, 1.48f, 0.16f), new Color(0.3f, 0.17f, 0.06f));
                CreatePart(root, $"LowPost_{side}", PrimitiveType.Cylinder,
                    new Vector3(side * 1.75f, 1.21f, 1.1f),
                    new Vector3(0.16f, 1.21f, 0.16f), new Color(0.3f, 0.17f, 0.06f));
            }
            CreatePart(root, "SlopedRoof", PrimitiveType.Cube,
                new Vector3(0f, 2.65f, 0f), new Vector3(3.9f, 0.12f, 3.1f),
                new Color(0.28f, 0.25f, 0.12f), Quaternion.Euler(12f, 0f, 0f));
            CreatePart(root, "BackWindbreak", PrimitiveType.Cube,
                new Vector3(0f, 1.4f, -1.35f), new Vector3(3.8f, 2.8f, 0.1f),
                new Color(0.34f, 0.29f, 0.13f));
        }

        private static GameObject CreatePart(
            Transform parent,
            string name,
            PrimitiveType primitive,
            Vector3 position,
            Vector3 scale,
            Color color,
            Quaternion? rotation = null)
        {
            var part = GameObject.CreatePrimitive(primitive);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.transform.localRotation = rotation ?? Quaternion.identity;
            foreach (var collider in part.GetComponents<Collider>()) Object.Destroy(collider);
            var renderer = part.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = new Material(
                    Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard")
                    ?? Shader.Find("Hidden/InternalErrorShader")) { color = color };
            }
            return part;
        }
    }
}
