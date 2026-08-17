using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using Quieter.Core;
using Quieter.Networking;
using Quieter.Persistence;
using UnityEngine;

namespace Quieter.Tests
{
    public sealed class SessionPipelineTests
    {
        [Test]
        public async Task TestAuthenticator_AllowsTwoDistinctClientsAndLateJoin()
        {
            using var server = new DevelopmentAuthenticationProvider();
            using var firstClient = new DevelopmentAuthenticationProvider();
            var first = await firstClient.CreatePayloadAsync(default);
            ServerAuthenticationResult firstResult = default;
            server.Validate(first.SteamId, first.Ticket, result => firstResult = result);
            ServerAuthenticationResult duplicateResult = default;
            server.Validate(first.SteamId, first.Ticket, result => duplicateResult = result);
            server.EndSession(first.SteamId);
            ServerAuthenticationResult reconnectResult = default;
            server.Validate(first.SteamId, first.Ticket, result => reconnectResult = result);

            using var lateClient = new DevelopmentAuthenticationProvider();
            var late = await lateClient.CreatePayloadAsync(default);
            ServerAuthenticationResult lateResult = default;
            server.Validate(late.SteamId, late.Ticket, result => lateResult = result);

            Assert.That(firstResult.Success, Is.True);
            Assert.That(duplicateResult.Success, Is.False);
            Assert.That(reconnectResult.Success, Is.True);
            Assert.That(lateResult.Success, Is.True);
            Assert.That(late.SteamId, Is.Not.EqualTo(first.SteamId));
        }

        [Test]
        public void IncompatibleProtocolOrGenerator_IsRejectedBeforeAuthentication()
        {
            var wrongProtocol = ConnectionCompatibility.CreatePayload(
                (ushort)(QuieterConstants.ProtocolVersion + 1),
                QuieterConstants.GeneratorVersion);
            var wrongGenerator = ConnectionCompatibility.CreatePayload(
                QuieterConstants.ProtocolVersion,
                (ushort)(QuieterConstants.GeneratorVersion + 1));

            Assert.That(ConnectionCompatibility.Validate(
                wrongProtocol,
                QuieterConstants.ProtocolVersion,
                QuieterConstants.GeneratorVersion).Accepted, Is.False);
            Assert.That(ConnectionCompatibility.Validate(
                wrongGenerator,
                QuieterConstants.ProtocolVersion,
                QuieterConstants.GeneratorVersion).Accepted, Is.False);
        }

        [Test]
        public async Task Reconnect_RestoresLastSavedPosition()
        {
            var path = Path.Combine(Application.temporaryCachePath, $"quieter-test-{System.Guid.NewGuid():N}.json");
            try
            {
                var repository = new LocalJsonRepository(path);
                const ulong steamId = 76561198000000001;
                await repository.LoginAsync(steamId, "Player", new Vector3(0f, 8f, 0f));
                var saved = new Vector3(12f, 9f, -4f);
                await repository.SavePositionAsync(steamId, saved);

                var reconnected = await repository.LoginAsync(
                    steamId,
                    "Player",
                    new Vector3(0f, 8f, 0f));

                Assert.That(reconnected.Position, Is.EqualTo(saved));
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
