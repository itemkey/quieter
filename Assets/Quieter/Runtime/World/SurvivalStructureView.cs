using Quieter.Inventory;
using UnityEngine;

namespace Quieter.World
{
    public static class SurvivalStructureRules
    {
        public const ushort LeanToItemId = 44;
        public const ushort HearthItemId = 45;
        public const ushort UnlinedWastePitItemId = 46;
        public const ushort HoldingCellItemId = 47;
        public const ushort BedItemId = 48;
        public const ushort FloorItemId = 50;
        public const ushort WallItemId = 51;
        public const ushort RoofItemId = 52;
        public const ushort DoorwayItemId = 53;
        public const ushort DoorItemId = 54;
        public const ushort ChestItemId = 55;
        public const ushort CartographyTableItemId = 56;
        public const ushort LatrineItemId = 57;
        public const ushort WashBasinItemId = 58;
        public const ushort BarrelItemId = 59;
        public const ushort WellItemId = 60;
        public const ushort DrainItemId = 61;
        public const ushort LinedWastePitItemId = 62;
        public const int MaximumFuelUnits = 20;
        public const ushort WastePitCapacityMilliliters = 60000;
        public const ushort WashBasinCapacityMilliliters = 6000;
        public const ushort WaterBarrelCapacityMilliliters = 60000;

        public static bool SupportsPlacement(ushort itemId) => itemId is
            ResourceBalance.ResearchTableItemId or LeanToItemId or HearthItemId
                or UnlinedWastePitItemId or HoldingCellItemId or BedItemId
                or FloorItemId or WallItemId or RoofItemId or DoorwayItemId
                or DoorItemId or ChestItemId or CartographyTableItemId
                or LatrineItemId or WashBasinItemId or BarrelItemId
                or WellItemId or DrainItemId or LinedWastePitItemId;

        public static bool IsWastePit(ushort itemId) => itemId is
            UnlinedWastePitItemId or LinedWastePitItemId;

        public static float WasteLeakageMultiplier(ushort itemId) => itemId switch
        {
            UnlinedWastePitItemId => 1f,
            LinedWastePitItemId => 0.08f,
            _ => 0f,
        };

        public static bool IsSanitaryFixture(ushort itemId) => itemId is
            LatrineItemId or WashBasinItemId;

        public static bool StoresWater(ushort itemId) => itemId is
            WashBasinItemId or BarrelItemId;

        public static ushort WaterStorageCapacity(ushort itemId) => itemId switch
        {
            WashBasinItemId => WashBasinCapacityMilliliters,
            BarrelItemId => WaterBarrelCapacityMilliliters,
            _ => 0,
        };

        public static bool CanConnectSanitation(
            ushort upstreamItemId,
            Vector3 upstreamPosition,
            ushort downstreamItemId,
            Vector3 downstreamPosition)
        {
            if (upstreamItemId is not (LatrineItemId or WashBasinItemId or DrainItemId)
                || downstreamItemId != DrainItemId && !IsWastePit(downstreamItemId))
                return false;
            var planar = Vector2.Distance(
                new Vector2(upstreamPosition.x, upstreamPosition.z),
                new Vector2(downstreamPosition.x, downstreamPosition.z));
            var maximum = IsWastePit(downstreamItemId) ? 3f : 2.35f;
            return planar <= maximum
                && downstreamPosition.y <= upstreamPosition.y + 0.35f
                && upstreamPosition.y - downstreamPosition.y <= 1.25f;
        }

        public static bool SupportsLock(ushort itemId) => itemId is
            HoldingCellItemId or DoorItemId or ChestItemId;

        public static bool IsModularBuildingPart(ushort itemId) => itemId is
            FloorItemId or WallItemId or RoofItemId or DoorwayItemId or DoorItemId;

        public static Vector3 SnapPlacement(Vector3 position, ushort itemId)
        {
            if (!IsModularBuildingPart(itemId) && itemId != DrainItemId) return position;
            position.x = Mathf.Round(position.x * 2f) * 0.5f;
            position.z = Mathf.Round(position.z * 2f) * 0.5f;
            return position;
        }

        public static float SnapYaw(float yaw, ushort itemId) =>
            IsModularBuildingPart(itemId) || itemId == DrainItemId
                ? Mathf.Repeat(Mathf.Round(yaw / 90f) * 90f, 360f)
                : Mathf.Repeat(yaw, 360f);

        public static bool AllowsModularOverlap(ushort placedItemId, ushort existingItemId) =>
            IsModularBuildingPart(placedItemId)
            && IsModularBuildingPart(existingItemId)
            && placedItemId != existingItemId;

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
            HoldingCellItemId => new Vector3(2.15f, 1.55f, 2.15f),
            BedItemId => new Vector3(0.95f, 0.45f, 1.15f),
            FloorItemId => new Vector3(2f, 0.1f, 2f),
            WallItemId => new Vector3(2f, 1.5f, 0.1f),
            RoofItemId => new Vector3(2f, 0.1f, 2f),
            DoorwayItemId => new Vector3(2f, 1.5f, 0.1f),
            DoorItemId => new Vector3(0.72f, 1.4f, 0.09f),
            ChestItemId => new Vector3(0.75f, 0.55f, 0.45f),
            CartographyTableItemId => new Vector3(1.05f, 0.75f, 0.65f),
            LatrineItemId => new Vector3(0.65f, 0.65f, 0.65f),
            WashBasinItemId => new Vector3(0.58f, 0.65f, 0.45f),
            BarrelItemId => new Vector3(0.55f, 0.85f, 0.55f),
            WellItemId => new Vector3(1.15f, 0.7f, 1.15f),
            DrainItemId => new Vector3(1f, 0.16f, 0.3f),
            LinedWastePitItemId => new Vector3(1f, 0.3f, 1f),
            _ => new Vector3(0.82f, 0.5f, 0.42f),
        };

        public static bool IsInsideHoldingCell(
            Vector3 subjectPosition, Vector3 cellPosition, float cellYaw)
        {
            var local = Quaternion.Euler(0f, -cellYaw, 0f) * (subjectPosition - cellPosition);
            return Mathf.Abs(local.x) <= 1.72f && Mathf.Abs(local.z) <= 1.72f
                && local.y >= -0.45f && local.y <= 2.8f;
        }

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

        public void ApplyState(ItemStackState input, bool locked = false)
        {
            Burning = ItemId == SurvivalStructureRules.HearthItemId && !input.IsEmpty;
            var glow = transform.Find("FireGlow");
            if (glow != null) glow.gameObject.SetActive(Burning);
            var door = transform.Find("DoorCollision");
            if (door != null)
            {
                var collider = door.GetComponent<Collider>();
                if (collider != null) collider.enabled = locked;
            }
            var doorVisual = transform.Find("DoorVisual");
            if (doorVisual != null) doorVisual.gameObject.SetActive(locked);
            var modularDoor = transform.Find("Door");
            if (modularDoor != null && ItemId == SurvivalStructureRules.DoorItemId)
                modularDoor.gameObject.SetActive(locked);
        }

        private static void BuildColliders(Transform root, ushort itemId)
        {
            if (itemId == SurvivalStructureRules.HoldingCellItemId)
            {
                AddSolidPart(root, "BackCollision", new Vector3(0f, 1.45f, -1.9f),
                    new Vector3(4f, 2.9f, 0.14f), Quaternion.identity);
                AddSolidPart(root, "LeftCollision", new Vector3(-1.9f, 1.45f, 0f),
                    new Vector3(0.14f, 2.9f, 4f), Quaternion.identity);
                AddSolidPart(root, "RightCollision", new Vector3(1.9f, 1.45f, 0f),
                    new Vector3(0.14f, 2.9f, 4f), Quaternion.identity);
                AddSolidPart(root, "FrontLeftCollision", new Vector3(-1.32f, 1.45f, 1.9f),
                    new Vector3(1.36f, 2.9f, 0.14f), Quaternion.identity);
                AddSolidPart(root, "FrontRightCollision", new Vector3(1.32f, 1.45f, 1.9f),
                    new Vector3(1.36f, 2.9f, 0.14f), Quaternion.identity);
                AddSolidPart(root, "RoofCollision", new Vector3(0f, 2.9f, 0f),
                    new Vector3(4f, 0.14f, 4f), Quaternion.identity);
                AddSolidPart(root, "DoorCollision", new Vector3(0f, 1.45f, 1.9f),
                    new Vector3(1.28f, 2.9f, 0.14f), Quaternion.identity);
                return;
            }
            if (itemId == SurvivalStructureRules.BedItemId)
            {
                AddSolidPart(root, "BedCollision", new Vector3(0f, 0.38f, 0f),
                    new Vector3(1.8f, 0.76f, 2.15f), Quaternion.identity);
                return;
            }
            if (itemId == SurvivalStructureRules.DoorwayItemId)
            {
                AddSolidPart(root, "DoorwayLeft", new Vector3(-1.45f, 1.5f, 0f),
                    new Vector3(1.1f, 3f, 0.2f), Quaternion.identity);
                AddSolidPart(root, "DoorwayRight", new Vector3(1.45f, 1.5f, 0f),
                    new Vector3(1.1f, 3f, 0.2f), Quaternion.identity);
                AddSolidPart(root, "DoorwayTop", new Vector3(0f, 2.72f, 0f),
                    new Vector3(1.8f, 0.56f, 0.2f), Quaternion.identity);
                return;
            }
            if (itemId == SurvivalStructureRules.RoofItemId)
            {
                AddSolidPart(root, "RoofCollision", new Vector3(0f, 3f, 0f),
                    new Vector3(4.2f, 0.2f, 4.2f), Quaternion.identity);
                return;
            }
            if (itemId == SurvivalStructureRules.DoorItemId)
            {
                AddSolidPart(root, "DoorCollision", new Vector3(0f, 1.4f, 0f),
                    new Vector3(1.44f, 2.8f, 0.18f), Quaternion.identity);
                return;
            }
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
            if (itemId == SurvivalStructureRules.HoldingCellItemId)
            {
                BuildHoldingCellVisual(root);
                return;
            }
            if (itemId == SurvivalStructureRules.BedItemId)
            {
                var timber = new Color(0.31f, 0.18f, 0.07f);
                CreatePart(root, "BedFrame", PrimitiveType.Cube,
                    new Vector3(0f, 0.31f, 0f), new Vector3(1.8f, 0.22f, 2.15f), timber);
                CreatePart(root, "BedTick", PrimitiveType.Cube,
                    new Vector3(0f, 0.49f, 0f), new Vector3(1.64f, 0.22f, 1.98f),
                    new Color(0.49f, 0.42f, 0.25f));
                CreatePart(root, "BedPillow", PrimitiveType.Cube,
                    new Vector3(0f, 0.66f, -0.72f), new Vector3(1.25f, 0.18f, 0.42f),
                    new Color(0.7f, 0.66f, 0.53f));
                return;
            }
            if (BuildModularVisual(root, itemId)) return;
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

        private static void BuildHoldingCellVisual(Transform root)
        {
            var timber = new Color(0.25f, 0.14f, 0.055f);
            CreatePart(root, "Floor", PrimitiveType.Cube, new Vector3(0f, 0.08f, 0f),
                new Vector3(4f, 0.16f, 4f), new Color(0.22f, 0.18f, 0.12f));
            CreatePart(root, "Roof", PrimitiveType.Cube, new Vector3(0f, 2.9f, 0f),
                new Vector3(4f, 0.16f, 4f), timber);
            for (var side = -1; side <= 1; side += 2)
            {
                for (var index = -4; index <= 4; index++)
                {
                    CreatePart(root, $"SideBar_{side}_{index}", PrimitiveType.Cylinder,
                        new Vector3(side * 1.9f, 1.48f, index * 0.43f),
                        new Vector3(0.055f, 1.45f, 0.055f), timber);
                }
            }
            for (var index = -4; index <= 4; index++)
            {
                CreatePart(root, $"BackBar_{index}", PrimitiveType.Cylinder,
                    new Vector3(index * 0.43f, 1.48f, -1.9f),
                    new Vector3(0.055f, 1.45f, 0.055f), timber);
                if (Mathf.Abs(index) <= 1) continue;
                CreatePart(root, $"FrontBar_{index}", PrimitiveType.Cylinder,
                    new Vector3(index * 0.43f, 1.48f, 1.9f),
                    new Vector3(0.055f, 1.45f, 0.055f), timber);
            }
            CreatePart(root, "DoorVisual", PrimitiveType.Cube,
                new Vector3(0f, 1.45f, 1.9f), new Vector3(1.28f, 2.75f, 0.08f),
                new Color(0.18f, 0.1f, 0.035f));
        }

        private static bool BuildModularVisual(Transform root, ushort itemId)
        {
            var timber = new Color(0.32f, 0.19f, 0.07f);
            switch (itemId)
            {
                case SurvivalStructureRules.FloorItemId:
                    CreatePart(root, "Floor", PrimitiveType.Cube,
                        new Vector3(0f, 0.1f, 0f), new Vector3(4f, 0.2f, 4f), timber);
                    return true;
                case SurvivalStructureRules.WallItemId:
                    CreatePart(root, "Wall", PrimitiveType.Cube,
                        new Vector3(0f, 1.5f, 0f), new Vector3(4f, 3f, 0.2f), timber);
                    return true;
                case SurvivalStructureRules.RoofItemId:
                    CreatePart(root, "Roof", PrimitiveType.Cube,
                        new Vector3(0f, 3f, 0f), new Vector3(4.2f, 0.2f, 4.2f),
                        new Color(0.25f, 0.145f, 0.055f));
                    return true;
                case SurvivalStructureRules.DoorwayItemId:
                    CreatePart(root, "LeftJamb", PrimitiveType.Cube,
                        new Vector3(-1.45f, 1.5f, 0f), new Vector3(1.1f, 3f, 0.2f), timber);
                    CreatePart(root, "RightJamb", PrimitiveType.Cube,
                        new Vector3(1.45f, 1.5f, 0f), new Vector3(1.1f, 3f, 0.2f), timber);
                    CreatePart(root, "Lintel", PrimitiveType.Cube,
                        new Vector3(0f, 2.72f, 0f), new Vector3(1.8f, 0.56f, 0.2f), timber);
                    return true;
                case SurvivalStructureRules.DoorItemId:
                    CreatePart(root, "Door", PrimitiveType.Cube,
                        new Vector3(0f, 1.4f, 0f), new Vector3(1.44f, 2.8f, 0.18f),
                        new Color(0.24f, 0.115f, 0.035f));
                    return true;
                case SurvivalStructureRules.ChestItemId:
                    CreatePart(root, "Chest", PrimitiveType.Cube,
                        new Vector3(0f, 0.46f, 0f), new Vector3(1.5f, 0.92f, 0.9f), timber);
                    CreatePart(root, "ChestLid", PrimitiveType.Cube,
                        new Vector3(0f, 0.94f, 0f), new Vector3(1.56f, 0.12f, 0.96f),
                        new Color(0.22f, 0.11f, 0.03f));
                    return true;
                case SurvivalStructureRules.CartographyTableItemId:
                    CreatePart(root, "TableTop", PrimitiveType.Cube,
                        new Vector3(0f, 0.78f, 0f), new Vector3(2.1f, 0.14f, 1.3f), timber);
                    for (var side = -1; side <= 1; side += 2)
                        CreatePart(root, $"TableLeg{side}", PrimitiveType.Cube,
                            new Vector3(side * 0.78f, 0.39f, 0f),
                            new Vector3(0.14f, 0.78f, 0.9f), timber);
                    return true;
                case SurvivalStructureRules.LatrineItemId:
                    CreatePart(root, "LatrineBox", PrimitiveType.Cube,
                        new Vector3(0f, 0.48f, 0f), new Vector3(1.3f, 0.82f, 1.3f), timber);
                    CreatePart(root, "LatrineOpening", PrimitiveType.Cylinder,
                        new Vector3(0f, 0.91f, 0f), new Vector3(0.28f, 0.05f, 0.28f),
                        new Color(0.06f, 0.045f, 0.03f));
                    return true;
                case SurvivalStructureRules.WashBasinItemId:
                    CreatePart(root, "Basin", PrimitiveType.Cylinder,
                        new Vector3(0f, 0.72f, 0f), new Vector3(0.58f, 0.16f, 0.48f),
                        new Color(0.48f, 0.36f, 0.23f));
                    return true;
                case SurvivalStructureRules.BarrelItemId:
                    CreatePart(root, "Barrel", PrimitiveType.Cylinder,
                        new Vector3(0f, 0.85f, 0f), new Vector3(0.55f, 0.85f, 0.55f), timber);
                    return true;
                case SurvivalStructureRules.WellItemId:
                    for (var index = 0; index < 10; index++)
                    {
                        var angle = index / 10f * Mathf.PI * 2f;
                        CreatePart(root, $"WellStone{index}", PrimitiveType.Cube,
                            new Vector3(Mathf.Cos(angle) * 0.82f, 0.42f,
                                Mathf.Sin(angle) * 0.82f), new Vector3(0.5f, 0.42f, 0.3f),
                            new Color(0.34f, 0.33f, 0.3f),
                            Quaternion.Euler(0f, -index * 36f, 0f));
                    }
                    return true;
                case SurvivalStructureRules.DrainItemId:
                    CreatePart(root, "Drain", PrimitiveType.Cube,
                        new Vector3(0f, 0.08f, 0f), new Vector3(2f, 0.16f, 0.6f),
                        new Color(0.36f, 0.28f, 0.19f));
                    return true;
                case SurvivalStructureRules.LinedWastePitItemId:
                    CreatePart(root, "LinedPit", PrimitiveType.Cylinder,
                        new Vector3(0f, 0.12f, 0f), new Vector3(0.95f, 0.12f, 0.95f),
                        new Color(0.24f, 0.23f, 0.2f));
                    return true;
                default:
                    return false;
            }
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
