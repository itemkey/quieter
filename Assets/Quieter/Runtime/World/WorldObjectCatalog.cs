using System;
using System.Collections.Generic;
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

        [SerializeField] private List<Entry> entries = new();
        private Dictionary<ushort, Entry> lookup;

        public GameObject CreatePresentation(WorldObjectSpawn spawn, Transform parent)
        {
            BuildLookup();
            lookup.TryGetValue(spawn.TypeId.Value, out var entry);

            GameObject instance;
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
            lookup = null;
        }
#endif

        private void BuildLookup()
        {
            if (lookup != null)
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
        }

        private static Shader FindLitShader()
        {
            return Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Hidden/InternalErrorShader");
        }
    }
}
