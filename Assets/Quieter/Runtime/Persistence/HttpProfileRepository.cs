using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Quieter.World;
using UnityEngine;
using UnityEngine.Networking;

namespace Quieter.Persistence
{
    public sealed class HttpProfileRepository : IWorldRepository, IPlayerProfileRepository
    {
        [Serializable]
        private sealed class WorldResponse
        {
            public int worldId;
            public long seed;
            public ushort generatorVersion;
            public ushort chunkCountX;
            public ushort chunkCountZ;
            public ushort chunkSize;
            public ushort samplesPerSide;
            public float heightStep;
        }

        [Serializable]
        private sealed class LoginRequest
        {
            public string steamId;
            public string displayName;
            public float defaultX;
            public float defaultY;
            public float defaultZ;
        }

        [Serializable]
        private sealed class ProfileResponse
        {
            public string steamId;
            public string displayName;
            public float positionX;
            public float positionY;
            public float positionZ;
            public string createdAtUtc;
            public string lastSeenAtUtc;
        }

        [Serializable]
        private sealed class PositionRequest
        {
            public float x;
            public float y;
            public float z;
        }

        private readonly string baseUrl;
        private readonly string token;

        public HttpProfileRepository(string baseUrl, string token)
        {
            this.baseUrl = string.IsNullOrWhiteSpace(baseUrl)
                ? throw new ArgumentException("Profile service URL is required.", nameof(baseUrl))
                : baseUrl.TrimEnd('/');
            this.token = token ?? string.Empty;
        }

        public async Task<WorldDefinition> GetOrCreateWorldAsync(
            CancellationToken cancellationToken = default)
        {
            using var request = UnityWebRequest.Get(baseUrl + "/internal/world/current");
            var responseText = await SendAsync(request, cancellationToken);
            var response = JsonUtility.FromJson<WorldResponse>(responseText);
            return new WorldDefinition
            {
                WorldId = response.worldId,
                Seed = response.seed,
                GeneratorVersion = response.generatorVersion,
                ChunkCountX = response.chunkCountX,
                ChunkCountZ = response.chunkCountZ,
                ChunkSize = response.chunkSize,
                SamplesPerSide = response.samplesPerSide,
                HeightStep = response.heightStep,
            };
        }

        public async Task<PlayerProfile> LoginAsync(
            ulong steamId,
            string displayName,
            Vector3 defaultSpawn,
            CancellationToken cancellationToken = default)
        {
            var payload = new LoginRequest
            {
                steamId = steamId.ToString(),
                displayName = displayName,
                defaultX = defaultSpawn.x,
                defaultY = defaultSpawn.y,
                defaultZ = defaultSpawn.z,
            };
            using var request = CreateJsonRequest(
                baseUrl + "/internal/players/login",
                UnityWebRequest.kHttpVerbPOST,
                JsonUtility.ToJson(payload));
            var responseText = await SendAsync(request, cancellationToken);
            var response = JsonUtility.FromJson<ProfileResponse>(responseText);
            return new PlayerProfile
            {
                SteamId = ulong.Parse(response.steamId),
                DisplayName = response.displayName,
                Position = new Vector3(response.positionX, response.positionY, response.positionZ),
                CreatedAtUtc = ParseDate(response.createdAtUtc),
                LastSeenAtUtc = ParseDate(response.lastSeenAtUtc),
            };
        }

        public async Task SavePositionAsync(
            ulong steamId,
            Vector3 position,
            CancellationToken cancellationToken = default)
        {
            var payload = new PositionRequest { x = position.x, y = position.y, z = position.z };
            using var request = CreateJsonRequest(
                $"{baseUrl}/internal/players/{steamId}/position",
                UnityWebRequest.kHttpVerbPUT,
                JsonUtility.ToJson(payload));
            await SendAsync(request, cancellationToken);
        }

        private async Task<string> SendAsync(
            UnityWebRequest request,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(token))
            {
                request.SetRequestHeader("X-Quieter-Internal-Token", token);
            }

            request.timeout = 10;
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new InvalidOperationException(
                    $"Profile service returned {request.responseCode}: {request.error} {request.downloadHandler?.text}");
            }

            return request.downloadHandler?.text ?? string.Empty;
        }

        private static UnityWebRequest CreateJsonRequest(string url, string method, string json)
        {
            return new UnityWebRequest(url, method)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
            }.WithJsonHeader();
        }

        private static DateTime ParseDate(string value)
        {
            return DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : DateTime.UtcNow;
        }
    }

    internal static class UnityWebRequestExtensions
    {
        public static UnityWebRequest WithJsonHeader(this UnityWebRequest request)
        {
            request.SetRequestHeader("Content-Type", "application/json");
            return request;
        }
    }
}
