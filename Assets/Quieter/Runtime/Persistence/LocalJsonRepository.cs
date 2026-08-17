using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Quieter.World;
using UnityEngine;

namespace Quieter.Persistence
{
    public sealed class LocalJsonRepository : IWorldRepository, IPlayerProfileRepository
    {
        [Serializable]
        private sealed class State
        {
            public bool HasWorld;
            public WorldDefinition World;
            public List<StoredPlayer> Players = new();
        }

        [Serializable]
        private sealed class StoredPlayer
        {
            public string SteamId;
            public string DisplayName;
            public Vector3 Position;
            public string CreatedAtUtc;
            public string LastSeenAtUtc;
        }

        private readonly string path;
        private readonly object sync = new();
        private State state;

        public LocalJsonRepository(string customPath = null)
        {
            path = customPath ?? Path.Combine(Application.persistentDataPath, "quieter-local-state.json");
        }

        public Task<WorldDefinition> GetOrCreateWorldAsync(CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                if (!state.HasWorld)
                {
                    var seedBytes = Guid.NewGuid().ToByteArray();
                    state.World = WorldDefinition.CreateDefault(BitConverter.ToInt64(seedBytes, 0));
                    state.HasWorld = true;
                    Save();
                }
                else if (state.World.WorldId == 0)
                {
                    state.World.WorldId = 1;
                    Save();
                }

                return Task.FromResult(state.World);
            }
        }

        public Task<PlayerProfile> LoginAsync(
            ulong steamId,
            string displayName,
            Vector3 defaultSpawn,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                var id = steamId.ToString();
                var player = state.Players.Find(candidate => candidate.SteamId == id);
                var now = DateTime.UtcNow;

                if (player == null)
                {
                    player = new StoredPlayer
                    {
                        SteamId = id,
                        DisplayName = SanitizeName(displayName),
                        Position = defaultSpawn,
                        CreatedAtUtc = now.ToString("O"),
                    };
                    state.Players.Add(player);
                }

                player.DisplayName = SanitizeName(displayName);
                player.LastSeenAtUtc = now.ToString("O");
                Save();
                return Task.FromResult(ToProfile(player));
            }
        }

        public Task SavePositionAsync(
            ulong steamId,
            Vector3 position,
            CancellationToken cancellationToken = default)
        {
            lock (sync)
            {
                EnsureLoaded();
                var id = steamId.ToString();
                var player = state.Players.Find(candidate => candidate.SteamId == id);
                if (player != null)
                {
                    player.Position = position;
                    player.LastSeenAtUtc = DateTime.UtcNow.ToString("O");
                    Save();
                }
            }

            return Task.CompletedTask;
        }

        private void EnsureLoaded()
        {
            if (state != null)
            {
                return;
            }

            if (File.Exists(path))
            {
                try
                {
                    state = JsonUtility.FromJson<State>(File.ReadAllText(path));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"Could not read local persistence state: {exception.Message}");
                }
            }

            state ??= new State();
            state.Players ??= new List<StoredPlayer>();
        }

        private void Save()
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(state, true));
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporary, path);
        }

        private static PlayerProfile ToProfile(StoredPlayer player)
        {
            return new PlayerProfile
            {
                SteamId = ulong.Parse(player.SteamId),
                DisplayName = player.DisplayName,
                Position = player.Position,
                CreatedAtUtc = ParseDate(player.CreatedAtUtc),
                LastSeenAtUtc = ParseDate(player.LastSeenAtUtc),
            };
        }

        private static DateTime ParseDate(string value)
        {
            return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : DateTime.UtcNow;
        }

        private static string SanitizeName(string value)
        {
            value = string.IsNullOrWhiteSpace(value) ? "Steam Player" : value.Trim();
            return value.Length <= 32 ? value : value.Substring(0, 32);
        }
    }
}
