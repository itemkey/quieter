using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Quieter.Core;
using UnityEngine;

namespace Quieter.Networking
{
    [CreateAssetMenu(menuName = "Quieter/Server Endpoint", fileName = "ServerEndpoint")]
    public sealed class ServerEndpoint : ScriptableObject
    {
        [SerializeField] private string displayName = "Домашний сервер Quieter";
        [SerializeField] private string address = "127.0.0.1";
        [SerializeField] private ushort port = QuieterConstants.DefaultGamePort;
        [SerializeField] private bool useDtls;
        [TextArea(3, 12)]
        [SerializeField] private string pinnedCaCertificate = string.Empty;
        [SerializeField] private string expectedServerName = "quieter-server";

        public string DisplayName => displayName;
        public string Address => address;
        public ushort Port => port;
        public bool UseDtls => useDtls;
        public string PinnedCaCertificate => pinnedCaCertificate;
        public string ExpectedServerName => expectedServerName;

#if UNITY_EDITOR
        public void Configure(string newDisplayName, string newAddress, ushort newPort)
        {
            displayName = newDisplayName;
            address = newAddress;
            port = newPort;
        }

        public void ConfigureSecurity(bool enabled, string serverName, string caCertificate)
        {
            useDtls = enabled;
            expectedServerName = string.IsNullOrWhiteSpace(serverName)
                ? "quieter-server"
                : serverName;
            pinnedCaCertificate = caCertificate ?? string.Empty;
        }
#endif
    }

    public interface IServerDirectory
    {
        Task<IReadOnlyList<ServerEndpoint>> GetServersAsync();
    }

    public sealed class StaticServerDirectory : IServerDirectory
    {
        private readonly ServerEndpoint endpoint;

        public StaticServerDirectory(ServerEndpoint endpoint)
        {
            this.endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        }

        public Task<IReadOnlyList<ServerEndpoint>> GetServersAsync()
        {
            IReadOnlyList<ServerEndpoint> endpoints = new[] { endpoint };
            return Task.FromResult(endpoints);
        }
    }
}
