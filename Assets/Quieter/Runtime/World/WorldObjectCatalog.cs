using System;
using System.Collections.Generic;
using Quieter.Inventory;
using UnityEngine;

namespace Quieter.World
{
    [CreateAssetMenu(menuName = "Quieter/World Object Catalog", fileName = "WorldObjectCatalog")]
    public sealed class WorldObjectCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [Min(1)] public ushort typeId = 1;
            public GameObject prefab;
            public Color placeholderColor = Color.gray;
        }

        [Serializable]
        public sealed class ResourceEntry
        {
            [Min(1)] public ushort itemId;
            public ResourceCategory category;
            public DepositGenerationPool generationPool;
            public ResourceVisualArchetype visualArchetype;
            public ToolKind requiredTool = ToolKind.Pickaxe;
            [Min(1)] public int worldWeight = 1;
            public Color rockColor = Color.gray;
            public Color veinColor = Color.white;
            [Range(1, 5)] public byte minimumHardness = 1;
            [Range(1, 5)] public byte maximumHardness = 5;
            public ushort primaryImpurityItemId;
            public ushort rareImpurityItemId;
            public bool prefersHighGround;
            public bool prefersLowGround;
            public bool stronglyRegional;
        }

        [SerializeField] private List<Entry> entries = new();
        [SerializeField] private List<ResourceEntry> resources = new();
        private Dictionary<ushort, Entry> lookup;
        private Dictionary<ushort, ResourceEntry> resourceLookup;

        public IReadOnlyList<ResourceEntry> Resources => resources;

        public bool TryGetResource(ushort itemId, out ResourceEntry definition)
        {
            BuildLookup();
            return resourceLookup.TryGetValue(itemId, out definition);
        }

        public GameObject CreatePresentation(WorldObjectSpawn spawn, Transform parent)
        {
            BuildLookup();
            lookup.TryGetValue(spawn.TypeId.Value, out var entry);

            GameObject instance;
            if (spawn.Resource.IsResourceNode)
            {
                instance = CreateResourcePresentation(spawn, parent);
                instance.name = $"ResourceNode_{spawn.Resource.Kind}_{spawn.InstanceId:X}";
                instance.transform.localScale = spawn.Scale;
                instance.AddComponent<ResourceNodeView>().Initialize(spawn.InstanceId, spawn.Resource);
                return instance;
            }

            if (entry != null && entry.prefab != null)
            {
                instance = Instantiate(entry.prefab, spawn.Position, spawn.Rotation, parent);
            }
            else
            {
                instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
                instance.transform.SetParent(parent, true);
                instance.transform.SetPositionAndRotation(spawn.Position, spawn.Rotation);
                var renderer = instance.GetComponent<Renderer>();
                if (renderer != null)
                {
                    var material = new Material(FindLitShader())
                    {
                        color = entry?.placeholderColor ?? Color.gray,
                    };
                    renderer.sharedMaterial = material;
                }
            }

            instance.name = $"WorldObject_{spawn.TypeId.Value}_{spawn.InstanceId:X}";
            instance.transform.localScale = spawn.Scale;
            return instance;
        }

#if UNITY_EDITOR
        public void ConfigureDefaults()
        {
            entries = new List<Entry>
            {
                new() { typeId = 1, placeholderColor = new Color(0.25f, 0.58f, 0.2f) },
                new() { typeId = 2, placeholderColor = new Color(0.38f, 0.4f, 0.45f) },
            };
            resources = new List<ResourceEntry>
            {
                new() { itemId = 7, category = ResourceCategory.MetallicOre, worldWeight = 70,
                    rockColor = new Color(0.34f, 0.2f, 0.13f), veinColor = new Color(0.58f, 0.27f, 0.12f), minimumHardness = 2, maximumHardness = 5 },
                new() { itemId = 8, category = ResourceCategory.MetallicOre, worldWeight = 50,
                    rockColor = new Color(0.27f, 0.29f, 0.27f), veinColor = new Color(0.05f, 0.72f, 0.58f), minimumHardness = 2, maximumHardness = 4, primaryImpurityItemId = 11, rareImpurityItemId = 12 },
                new() { itemId = 9, category = ResourceCategory.MetallicOre, worldWeight = 30,
                    rockColor = new Color(0.43f, 0.44f, 0.46f), veinColor = new Color(0.73f, 0.76f, 0.78f), minimumHardness = 2, maximumHardness = 4, primaryImpurityItemId = 8 },
                new() { itemId = 10, category = ResourceCategory.MetallicOre, worldWeight = 30,
                    rockColor = new Color(0.25f, 0.27f, 0.32f), veinColor = new Color(0.37f, 0.43f, 0.55f), minimumHardness = 2, maximumHardness = 4, primaryImpurityItemId = 11 },
                new() { itemId = 11, category = ResourceCategory.MetallicOre, worldWeight = 15,
                    rockColor = new Color(0.2f, 0.22f, 0.25f), veinColor = new Color(0.82f, 0.87f, 0.94f), minimumHardness = 3, maximumHardness = 5, primaryImpurityItemId = 10, rareImpurityItemId = 12, prefersHighGround = true },
                new() { itemId = 12, category = ResourceCategory.MetallicOre, worldWeight = 4,
                    rockColor = new Color(0.78f, 0.75f, 0.66f), veinColor = new Color(1f, 0.68f, 0.05f), minimumHardness = 3, maximumHardness = 5, primaryImpurityItemId = 11, prefersHighGround = true, stronglyRegional = true },
                new() { itemId = 13, category = ResourceCategory.Coal, worldWeight = 55,
                    rockColor = new Color(0.055f, 0.055f, 0.065f), veinColor = new Color(0.16f, 0.16f, 0.18f), minimumHardness = 1, maximumHardness = 3, primaryImpurityItemId = 15, stronglyRegional = true },
                new() { itemId = 14, category = ResourceCategory.Salt, worldWeight = 24,
                    rockColor = new Color(0.72f, 0.7f, 0.63f), veinColor = new Color(0.96f, 0.94f, 0.86f), minimumHardness = 1, maximumHardness = 3, prefersLowGround = true, stronglyRegional = true },
                new() { itemId = 15, category = ResourceCategory.Mineral, worldWeight = 18,
                    rockColor = new Color(0.4f, 0.38f, 0.2f), veinColor = new Color(0.95f, 0.8f, 0.05f), minimumHardness = 1, maximumHardness = 3, rareImpurityItemId = 16, prefersHighGround = true },
                new() { itemId = 16, category = ResourceCategory.MetallicOre, worldWeight = 6,
                    rockColor = new Color(0.27f, 0.2f, 0.19f), veinColor = new Color(0.78f, 0.035f, 0.035f), minimumHardness = 2, maximumHardness = 4, primaryImpurityItemId = 15, prefersHighGround = true, stronglyRegional = true },
                new() { itemId = 17, category = ResourceCategory.NonOre,
                    generationPool = DepositGenerationPool.NonOre,
                    visualArchetype = ResourceVisualArchetype.LayeredLimestone,
                    worldWeight = 45, rockColor = new Color(0.72f, 0.68f, 0.55f),
                    veinColor = new Color(0.9f, 0.86f, 0.71f), minimumHardness = 2,
                    maximumHardness = 4, primaryImpurityItemId = 20, stronglyRegional = true },
                new() { itemId = 18, category = ResourceCategory.NonOre,
                    generationPool = DepositGenerationPool.NonOre,
                    visualArchetype = ResourceVisualArchetype.ClayBed,
                    requiredTool = ToolKind.Shovel,
                    worldWeight = 40, rockColor = new Color(0.48f, 0.22f, 0.12f),
                    veinColor = new Color(0.66f, 0.31f, 0.17f), minimumHardness = 1,
                    maximumHardness = 2, prefersLowGround = true, stronglyRegional = true },
                new() { itemId = 19, category = ResourceCategory.NonOre,
                    generationPool = DepositGenerationPool.NonOre,
                    visualArchetype = ResourceVisualArchetype.GypsumCrystals,
                    worldWeight = 18, rockColor = new Color(0.76f, 0.75f, 0.7f),
                    veinColor = new Color(0.98f, 0.96f, 0.88f), minimumHardness = 1,
                    maximumHardness = 3, rareImpurityItemId = 15,
                    stronglyRegional = true },
                new() { itemId = 20, category = ResourceCategory.NonOre,
                    generationPool = DepositGenerationPool.NonOre,
                    visualArchetype = ResourceVisualArchetype.FlintNodules,
                    worldWeight = 25, rockColor = new Color(0.61f, 0.59f, 0.51f),
                    veinColor = new Color(0.12f, 0.15f, 0.16f), minimumHardness = 2,
                    maximumHardness = 4, stronglyRegional = true },
                new() { itemId = 21, category = ResourceCategory.NonOre,
                    generationPool = DepositGenerationPool.NonOre,
                    visualArchetype = ResourceVisualArchetype.MarbleBlocks,
                    worldWeight = 8, rockColor = new Color(0.82f, 0.82f, 0.79f),
                    veinColor = new Color(0.42f, 0.46f, 0.5f), minimumHardness = 3,
                    maximumHardness = 5, prefersHighGround = true, stronglyRegional = true },
            };
            lookup = null;
            resourceLookup = null;
        }
#endif

        private void BuildLookup()
        {
            if (lookup != null && resourceLookup != null)
            {
                return;
            }

            lookup = new Dictionary<ushort, Entry>();
            foreach (var entry in entries)
            {
                if (entry != null && entry.typeId != 0)
                {
                    lookup[entry.typeId] = entry;
                }
            }

            resourceLookup = new Dictionary<ushort, ResourceEntry>();
            foreach (var resource in resources)
            {
                if (resource != null && resource.itemId != 0)
                {
                    resourceLookup[resource.itemId] = resource;
                }
            }
        }

        private GameObject CreateResourcePresentation(WorldObjectSpawn spawn, Transform parent)
        {
            BuildLookup();
            if (spawn.Resource.Kind == WorldObjectKind.Tree)
            {
                return CreateTreePresentation(spawn, parent);
            }
            if (spawn.Resource.Kind == WorldObjectKind.FiberPlant)
            {
                return CreateFiberPlantPresentation(spawn, parent);
            }
            resourceLookup.TryGetValue(spawn.Resource.ResourceItemId, out var definition);
            var isBranch = spawn.Resource.IsLoosePickup && spawn.Resource.ResourceItemId == 2;
            var archetype = definition?.visualArchetype ?? ResourceVisualArchetype.VeinedRock;
            var primitive = isBranch || spawn.Resource.ResourceItemId == 13
                || spawn.Resource.ResourceItemId == 14
                || archetype == ResourceVisualArchetype.LayeredLimestone
                || archetype == ResourceVisualArchetype.ClayBed
                || archetype == ResourceVisualArchetype.MarbleBlocks
                    ? PrimitiveType.Cube
                    : PrimitiveType.Sphere;
            var instance = GameObject.CreatePrimitive(primitive);
            instance.transform.SetParent(parent, true);
            instance.transform.SetPositionAndRotation(spawn.Position, spawn.Rotation);
            SetColor(instance, definition?.rockColor ?? new Color(0.42f, 0.43f, 0.45f));

            if (spawn.Resource.Kind == WorldObjectKind.Deposit)
            {
                var coverage = 0.12f + (int)spawn.Resource.Richness * 0.035f;
                switch (archetype)
                {
                    case ResourceVisualArchetype.LayeredLimestone:
                        for (var index = 0; index < 3; index++)
                        {
                            CreateDecoration(instance.transform, PrimitiveType.Cube,
                                $"LimestoneLayer_{index}",
                                new Vector3((index - 1) * 0.08f, -0.3f + index * 0.3f, 0.48f),
                                new Vector3(0.9f - index * 0.08f, 0.08f + coverage * 0.2f, 0.08f),
                                Quaternion.Euler(0f, 0f, index % 2 == 0 ? 3f : -4f),
                                definition?.veinColor ?? Color.white);
                        }
                        break;
                    case ResourceVisualArchetype.ClayBed:
                        CreateDecoration(instance.transform, PrimitiveType.Cube, "ClayStratum",
                            new Vector3(0f, 0.34f, 0f), new Vector3(0.92f, 0.08f, 0.92f),
                            Quaternion.identity, definition?.veinColor ?? Color.white);
                        break;
                    case ResourceVisualArchetype.GypsumCrystals:
                        for (var index = 0; index < 4; index++)
                        {
                            CreateDecoration(instance.transform, PrimitiveType.Cube,
                                $"GypsumCrystal_{index}",
                                new Vector3(-0.28f + index * 0.18f, 0.32f, 0.08f * (index % 2)),
                                new Vector3(0.1f, 0.55f + index * 0.07f, 0.12f),
                                Quaternion.Euler(8f * index, 18f * index, 12f - index * 7f),
                                definition?.veinColor ?? Color.white);
                        }
                        break;
                    case ResourceVisualArchetype.FlintNodules:
                        for (var index = 0; index < 4; index++)
                        {
                            CreateDecoration(instance.transform, PrimitiveType.Sphere,
                                $"FlintNodule_{index}",
                                new Vector3(-0.34f + index * 0.23f, -0.08f + (index % 2) * 0.28f, 0.43f),
                                Vector3.one * (0.16f + coverage * 0.2f), Quaternion.identity,
                                definition?.veinColor ?? Color.white);
                        }
                        break;
                    case ResourceVisualArchetype.MarbleBlocks:
                        CreateDecoration(instance.transform, PrimitiveType.Cube, "MarbleVein",
                            new Vector3(0f, 0.04f, 0.48f), new Vector3(0.85f, coverage, 0.07f),
                            Quaternion.Euler(0f, 0f, 24f), definition?.veinColor ?? Color.white);
                        CreateDecoration(instance.transform, PrimitiveType.Cube, "MarbleBlock",
                            new Vector3(0.32f, 0.38f, -0.18f), new Vector3(0.44f, 0.38f, 0.5f),
                            Quaternion.Euler(4f, 17f, -6f), definition?.rockColor ?? Color.gray);
                        break;
                    default:
                        CreateDecoration(instance.transform, PrimitiveType.Cube, "MineralVein",
                            new Vector3(0f, 0.12f, 0.48f), new Vector3(0.72f, coverage, 0.08f),
                            Quaternion.identity, definition?.veinColor ?? Color.white);
                        break;
                }
            }

            return instance;
        }

        private static GameObject CreateTreePresentation(WorldObjectSpawn spawn, Transform parent)
        {
            var root = new GameObject("Tree");
            root.transform.SetParent(parent, true);
            root.transform.SetPositionAndRotation(spawn.Position, spawn.Rotation);
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.48f, 0f);
            collider.size = new Vector3(0.42f, 0.96f, 0.42f);
            CreateDecoration(root.transform, PrimitiveType.Cylinder, "Trunk",
                new Vector3(0f, 0.32f, 0f), new Vector3(0.22f, 0.32f, 0.22f),
                Quaternion.identity, new Color(0.32f, 0.17f, 0.075f));
            CreateDecoration(root.transform, PrimitiveType.Sphere, "CanopyLower",
                new Vector3(0f, 0.7f, 0f), new Vector3(0.78f, 0.42f, 0.78f),
                Quaternion.identity, new Color(0.16f, 0.43f, 0.12f));
            CreateDecoration(root.transform, PrimitiveType.Sphere, "CanopyUpper",
                new Vector3(0.08f, 0.9f, -0.04f), new Vector3(0.58f, 0.34f, 0.58f),
                Quaternion.identity, new Color(0.2f, 0.5f, 0.14f));
            return root;
        }

        private static GameObject CreateFiberPlantPresentation(WorldObjectSpawn spawn, Transform parent)
        {
            var root = new GameObject("FiberPlant");
            root.transform.SetParent(parent, true);
            root.transform.SetPositionAndRotation(spawn.Position, spawn.Rotation);
            var collider = root.AddComponent<SphereCollider>();
            collider.radius = 0.52f;
            for (var index = 0; index < 5; index++)
            {
                CreateDecoration(root.transform, PrimitiveType.Cube, $"FiberLeaf_{index}",
                    new Vector3((index - 2) * 0.11f, -0.02f + (index % 2) * 0.08f, 0f),
                    new Vector3(0.1f, 0.7f, 0.08f),
                    Quaternion.Euler(0f, index * 37f, -28f + index * 14f),
                    new Color(0.35f, 0.62f, 0.19f));
            }
            return root;
        }

        private static void CreateDecoration(
            Transform parent,
            PrimitiveType primitive,
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Quaternion localRotation,
            Color color)
        {
            var decoration = GameObject.CreatePrimitive(primitive);
            decoration.name = name;
            decoration.transform.SetParent(parent, false);
            decoration.transform.localPosition = localPosition;
            decoration.transform.localScale = localScale;
            decoration.transform.localRotation = localRotation;
            foreach (var collider in decoration.GetComponentsInChildren<Collider>())
            {
                Destroy(collider);
            }
            SetColor(decoration, color);
        }

        private static void SetColor(GameObject target, Color color)
        {
            var renderer = target.GetComponentInChildren<Renderer>();
            if (renderer == null) return;
            renderer.sharedMaterial = new Material(FindLitShader()) { color = color };
        }

        private static Shader FindLitShader()
        {
            return Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Hidden/InternalErrorShader");
        }
    }
}
