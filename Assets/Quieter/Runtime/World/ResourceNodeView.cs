using UnityEngine;

namespace Quieter.World
{
    public sealed class ResourceNodeView : MonoBehaviour
    {
        private Renderer[] renderers;
        private Collider[] colliders;
        private Vector3 fullScale;
        private Color[] fullColors;

        public ulong InstanceId { get; private set; }
        public ResourceNodeDescriptor Descriptor { get; private set; }
        public ushort RemainingReserves { get; private set; }
        public bool IsAvailable { get; private set; }

        public void Initialize(ulong instanceId, ResourceNodeDescriptor descriptor)
        {
            InstanceId = instanceId;
            Descriptor = descriptor;
            RemainingReserves = descriptor.InitialReserves;
            IsAvailable = true;
            fullScale = transform.localScale;
            renderers = GetComponentsInChildren<Renderer>(true);
            colliders = GetComponentsInChildren<Collider>(true);
            fullColors = new Color[renderers.Length];
            for (var index = 0; index < renderers.Length; index++)
            {
                fullColors[index] = renderers[index].material.color;
            }
        }

        public void ApplyState(ushort remainingReserves, bool available)
        {
            RemainingReserves = remainingReserves;
            IsAvailable = available;
            var depleted = remainingReserves == 0 && Descriptor.RespawnSeconds == 0;
            var temporarilyUnavailable = Descriptor.IsLoosePickup && !available;
            foreach (var collider in colliders)
            {
                if (collider != null) collider.enabled = available && !depleted;
            }

            if (temporarilyUnavailable)
            {
                foreach (var renderer in renderers)
                {
                    if (renderer != null) renderer.enabled = false;
                }
                return;
            }

            foreach (var renderer in renderers)
            {
                if (renderer != null) renderer.enabled = true;
            }

            if (depleted)
            {
                transform.localScale = new Vector3(fullScale.x, fullScale.y * 0.18f, fullScale.z);
                for (var index = 0; index < renderers.Length; index++)
                {
                    if (renderers[index] != null)
                    {
                        renderers[index].material.color = Color.Lerp(fullColors[index], Color.gray, 0.72f);
                    }
                }
                return;
            }

            transform.localScale = fullScale;
            for (var index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] != null) renderers[index].material.color = fullColors[index];
            }
        }
    }
}
