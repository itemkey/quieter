using UnityEngine;
using UnityEngine.Rendering;

namespace Quieter.World
{
    public sealed class WorldChunkView : MonoBehaviour
    {
        private Mesh generatedMesh;
        private Material generatedMaterial;

        public void Build(
            WorldDefinition definition,
            ChunkData data,
            WorldObjectCatalog catalog,
            bool renderVisuals)
        {
            name = $"Chunk_{data.Coordinate.X}_{data.Coordinate.Z}";
            var side = data.SamplesPerSide;
            var vertices = new Vector3[side * side];
            var triangles = new int[(side - 1) * (side - 1) * 6];
            var colors = new Color[vertices.Length];
            var spacing = definition.SampleSpacing;
            var minimum = definition.WorldMinimum;
            var originX = minimum.x + data.Coordinate.X * definition.ChunkSize;
            var originZ = minimum.z + data.Coordinate.Z * definition.ChunkSize;

            for (var z = 0; z < side; z++)
            {
                for (var x = 0; x < side; x++)
                {
                    var index = z * side + x;
                    var height = data.HeightAt(x, z) * definition.HeightStep;
                    vertices[index] = new Vector3(x * spacing, height, z * spacing);
                    var shade = Mathf.InverseLerp(4f, 48f, height);
                    colors[index] = Color.Lerp(
                        new Color(0.17f, 0.28f, 0.12f),
                        new Color(0.5f, 0.53f, 0.4f),
                        shade);
                }
            }

            var triangle = 0;
            for (var z = 0; z < side - 1; z++)
            {
                for (var x = 0; x < side - 1; x++)
                {
                    var bottomLeft = z * side + x;
                    var topLeft = (z + 1) * side + x;
                    triangles[triangle++] = bottomLeft;
                    triangles[triangle++] = topLeft;
                    triangles[triangle++] = bottomLeft + 1;
                    triangles[triangle++] = bottomLeft + 1;
                    triangles[triangle++] = topLeft;
                    triangles[triangle++] = topLeft + 1;
                }
            }

            generatedMesh = new Mesh
            {
                name = $"ChunkMesh_{data.Coordinate.X}_{data.Coordinate.Z}",
                indexFormat = vertices.Length > ushort.MaxValue
                    ? IndexFormat.UInt32
                    : IndexFormat.UInt16,
                vertices = vertices,
                triangles = triangles,
                colors = colors,
            };
            generatedMesh.RecalculateNormals();
            generatedMesh.RecalculateBounds();

            transform.position = new Vector3(originX, 0f, originZ);
            var collider = gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = generatedMesh;

            if (renderVisuals)
            {
                var filter = gameObject.AddComponent<MeshFilter>();
                filter.sharedMesh = generatedMesh;
                var renderer = gameObject.AddComponent<MeshRenderer>();
                var shader = Shader.Find("Universal Render Pipeline/Lit")
                    ?? Shader.Find("Standard")
                    ?? Shader.Find("Hidden/InternalErrorShader");
                generatedMaterial = new Material(shader)
                {
                    name = "GeneratedTerrainMaterial",
                    color = ((data.Coordinate.X + data.Coordinate.Z) & 1) == 0
                        ? new Color(0.22f, 0.38f, 0.16f)
                        : new Color(0.18f, 0.32f, 0.13f),
                };
                renderer.sharedMaterial = generatedMaterial;
            }

            if (data.Objects.Count == 0)
            {
                return;
            }

            var objectRoot = new GameObject("Objects").transform;
            objectRoot.SetParent(transform, false);
            foreach (var spawn in data.Objects)
            {
                if (renderVisuals && catalog != null)
                {
                    catalog.CreatePresentation(spawn, objectRoot);
                    continue;
                }

                var physicsObject = new GameObject(
                    $"WorldObjectCollider_{spawn.TypeId.Value}_{spawn.InstanceId:X}");
                physicsObject.transform.SetParent(objectRoot, true);
                physicsObject.transform.SetPositionAndRotation(spawn.Position, spawn.Rotation);
                physicsObject.transform.localScale = spawn.Scale;
                physicsObject.AddComponent<BoxCollider>();
            }
        }

        private void OnDestroy()
        {
            if (generatedMesh != null)
            {
                Destroy(generatedMesh);
            }

            if (generatedMaterial != null)
            {
                Destroy(generatedMaterial);
            }
        }
    }
}
