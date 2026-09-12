using System;
using System.Collections.Generic;
using UnityEngine;

namespace Quieter.UI
{
    /// <summary>
    /// Read-only context supplied to optional physical tracker integrations.
    /// No tracker source is registered by the base game in this release.
    /// </summary>
    public readonly struct MapTrackerContext
    {
        public MapTrackerContext(
            ulong mapItemInstanceId,
            Vector3 observerPosition,
            bool observerHasCompass)
        {
            MapItemInstanceId = mapItemInstanceId;
            ObserverPosition = observerPosition;
            ObserverHasCompass = observerHasCompass;
        }

        public ulong MapItemInstanceId { get; }
        public Vector3 ObserverPosition { get; }
        public bool ObserverHasCompass { get; }
    }

    public readonly struct MapTrackerMarker
    {
        public MapTrackerMarker(
            string trackerId,
            Vector3 worldPosition,
            float headingDegrees,
            string label,
            Color color)
        {
            TrackerId = trackerId ?? string.Empty;
            WorldPosition = worldPosition;
            HeadingDegrees = headingDegrees;
            Label = label ?? string.Empty;
            Color = color;
        }

        public string TrackerId { get; }
        public Vector3 WorldPosition { get; }
        public float HeadingDegrees { get; }
        public string Label { get; }
        public Color Color { get; }
    }

    /// <summary>
    /// Extension seam for future physical teammate trackers. Implementations
    /// decide whether their in-world item and signal rules permit a marker.
    /// </summary>
    public interface IMapTrackerSource
    {
        bool IsEnabled { get; }
        void CollectMarkers(
            MapTrackerContext context,
            ICollection<MapTrackerMarker> destination);
    }

    public static class MapTrackerSourceRegistry
    {
        private static readonly List<IMapTrackerSource> Sources = new();

        public static int RegisteredSourceCount => Sources.Count;

        public static bool Register(IMapTrackerSource source)
        {
            if (source == null || Sources.Contains(source)) return false;
            Sources.Add(source);
            return true;
        }

        public static bool Unregister(IMapTrackerSource source)
            => source != null && Sources.Remove(source);

        public static void CollectMarkers(
            MapTrackerContext context,
            ICollection<MapTrackerMarker> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (Sources.Count == 0) return;

            // Sources may unregister themselves in response to item/network state.
            // A snapshot keeps iteration deterministic and mutation-safe.
            var snapshot = Sources.ToArray();
            foreach (var source in snapshot)
            {
                if (source?.IsEnabled == true)
                    source.CollectMarkers(context, destination);
            }
        }
    }
}
