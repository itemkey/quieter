using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Quieter.Core;
using Steamworks;
using UnityEngine;

namespace Quieter.Networking
{
    public readonly struct ClientAuthenticationPayload
    {
        public readonly ulong SteamId;
        public readonly string DisplayName;
        public readonly byte[] Ticket;

        public ClientAuthenticationPayload(ulong steamId, string displayName, byte[] ticket)
        {
            SteamId = steamId;
            DisplayName = displayName;
            Ticket = ticket ?? Array.Empty<byte>();
        }
    }

    public readonly struct ServerAuthenticationResult
    {
        public readonly bool Success;
        public readonly ulong SteamId;
        public readonly string Error;

        public ServerAuthenticationResult(bool success, ulong steamId, string error)
        {
            Success = success;
            SteamId = steamId;
            Error = error;
        }
    }

    public interface IClientAuthenticationProvider : IDisposable
    {
        bool IsReady { get; }
        string Status { get; }
        Task<ClientAuthenticationPayload> CreatePayloadAsync(CancellationToken cancellationToken);
        void Tick();
    }

    public interface IServerAuthenticationProvider : IDisposable
    {
        bool IsReady { get; }
        string Status { get; }
        void Validate(
            ulong claimedSteamId,
            byte[] ticket,
            Action<ServerAuthenticationResult> completion);
        void EndSession(ulong steamId);
        void Tick();
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    public sealed class DevelopmentAuthenticationProvider :
        IClientAuthenticationProvider,
        IServerAuthenticationProvider
    {
        private static long nextId = 76561199000000000L;
        private readonly ulong localId;
        private readonly HashSet<ulong> activeSessions = new();

        public DevelopmentAuthenticationProvider()
        {
            localId = (ulong)Interlocked.Increment(ref nextId);
        }

        public bool IsReady => true;
        public string Status => "Тестовая авторизация";

        public Task<ClientAuthenticationPayload> CreatePayloadAsync(CancellationToken cancellationToken)
        {
            var ticket = BitConverter.GetBytes(localId);
            return Task.FromResult(new ClientAuthenticationPayload(
                localId,
                $"DevPlayer-{localId % 10000}",
                ticket));
        }

        public void Validate(
            ulong claimedSteamId,
            byte[] ticket,
            Action<ServerAuthenticationResult> completion)
        {
            var valid = ticket != null
                && ticket.Length == sizeof(ulong)
                && BitConverter.ToUInt64(ticket, 0) == claimedSteamId;
            if (valid && !activeSessions.Add(claimedSteamId))
            {
                completion(new ServerAuthenticationResult(
                    false,
                    claimedSteamId,
                    "Этот тестовый аккаунт уже подключён."));
                return;
            }

            completion(new ServerAuthenticationResult(
                valid,
                claimedSteamId,
                valid ? string.Empty : "Недействительный тестовый билет."));
        }

        public void EndSession(ulong steamId) => activeSessions.Remove(steamId);
        public void Tick() { }
        public void Dispose() => activeSessions.Clear();
    }
#endif

    public sealed class SteamClientAuthenticationProvider : IClientAuthenticationProvider
    {
        private readonly uint appId;
        private bool initialized;
        private HAuthTicket currentTicket = HAuthTicket.Invalid;
        private Callback<GetAuthSessionTicketResponse_t> ticketCallback;
        private TaskCompletionSource<EResult> ticketCompletion;

        public SteamClientAuthenticationProvider(uint appId)
        {
            this.appId = appId;
            Initialize();
        }

        public bool IsReady => initialized;
        public string Status { get; private set; } = "Steam не инициализирован";

        public async Task<ClientAuthenticationPayload> CreatePayloadAsync(
            CancellationToken cancellationToken)
        {
            if (!initialized)
            {
                throw new InvalidOperationException(Status);
            }

            CancelCurrentTicket();
            var buffer = new byte[2048];
            var remoteIdentity = new SteamNetworkingIdentity();
            currentTicket = SteamUser.GetAuthSessionTicket(
                buffer,
                buffer.Length,
                out var ticketSize,
                ref remoteIdentity);
            if (currentTicket == HAuthTicket.Invalid || ticketSize == 0)
            {
                throw new InvalidOperationException("Steam не выдал билет сессии.");
            }

            ticketCompletion = new TaskCompletionSource<EResult>();
            using var registration = cancellationToken.Register(
                () => ticketCompletion.TrySetCanceled(cancellationToken));
            var result = await ticketCompletion.Task;
            if (result != EResult.k_EResultOK)
            {
                throw new InvalidOperationException($"Steam отклонил билет: {result}.");
            }

            Array.Resize(ref buffer, (int)ticketSize);
            return new ClientAuthenticationPayload(
                SteamUser.GetSteamID().m_SteamID,
                SteamFriends.GetPersonaName(),
                buffer);
        }

        public void Tick()
        {
            if (initialized)
            {
                SteamAPI.RunCallbacks();
            }
        }

        public void Dispose()
        {
            CancelCurrentTicket();
            ticketCallback?.Dispose();
            if (initialized)
            {
                SteamAPI.Shutdown();
                initialized = false;
            }
        }

        private void Initialize()
        {
            try
            {
                if (SteamAPI.RestartAppIfNecessary(new AppId_t(appId)))
                {
                    Status = "Steam перезапускает игру";
                    return;
                }

                initialized = SteamAPI.Init();
                Status = initialized
                    ? $"Steam: {SteamFriends.GetPersonaName()}"
                    : "Запустите Steam и повторите попытку";
                if (initialized)
                {
                    ticketCallback = Callback<GetAuthSessionTicketResponse_t>.Create(
                        response => ticketCompletion?.TrySetResult(response.m_eResult));
                }
            }
            catch (Exception exception)
            {
                initialized = false;
                Status = $"Steam недоступен: {exception.Message}";
            }
        }

        private void CancelCurrentTicket()
        {
            if (initialized && currentTicket != HAuthTicket.Invalid)
            {
                SteamUser.CancelAuthTicket(currentTicket);
            }

            currentTicket = HAuthTicket.Invalid;
            ticketCompletion = null;
        }
    }

    public sealed class SteamServerAuthenticationProvider : IServerAuthenticationProvider
    {
        private readonly Dictionary<ulong, Action<ServerAuthenticationResult>> pending = new();
        private readonly HashSet<ulong> active = new();
        private Callback<ValidateAuthTicketResponse_t> validationCallback;
        private bool initialized;

        public SteamServerAuthenticationProvider(ushort gamePort)
        {
            Initialize(gamePort);
        }

        public bool IsReady => initialized;
        public string Status { get; private set; } = "Steam GameServer не инициализирован";

        public void Validate(
            ulong claimedSteamId,
            byte[] ticket,
            Action<ServerAuthenticationResult> completion)
        {
            if (!initialized)
            {
                completion(new ServerAuthenticationResult(false, claimedSteamId, Status));
                return;
            }

            if (active.Contains(claimedSteamId) || pending.ContainsKey(claimedSteamId))
            {
                completion(new ServerAuthenticationResult(
                    false,
                    claimedSteamId,
                    "Этот Steam-аккаунт уже подключён."));
                return;
            }

            var result = SteamGameServer.BeginAuthSession(
                ticket,
                ticket?.Length ?? 0,
                new CSteamID(claimedSteamId));
            if (result != EBeginAuthSessionResult.k_EBeginAuthSessionResultOK)
            {
                completion(new ServerAuthenticationResult(
                    false,
                    claimedSteamId,
                    $"Steam отклонил начало сессии: {result}."));
                return;
            }

            pending.Add(claimedSteamId, completion);
        }

        public void EndSession(ulong steamId)
        {
            var hadPending = pending.Remove(steamId);
            var wasActive = active.Remove(steamId);
            if ((hadPending || wasActive) && initialized)
            {
                SteamGameServer.EndAuthSession(new CSteamID(steamId));
            }
        }

        public void Tick()
        {
            if (initialized)
            {
                GameServer.RunCallbacks();
            }
        }

        public void Dispose()
        {
            if (!initialized)
            {
                return;
            }

            foreach (var steamId in active)
            {
                SteamGameServer.EndAuthSession(new CSteamID(steamId));
            }

            active.Clear();
            pending.Clear();
            validationCallback?.Dispose();
            SteamGameServer.LogOff();
            GameServer.Shutdown();
            initialized = false;
        }

        private void Initialize(ushort gamePort)
        {
            try
            {
                var queryPort = (ushort)(gamePort + 1);
                initialized = GameServer.Init(
                    0,
                    gamePort,
                    queryPort,
                    EServerMode.eServerModeAuthentication,
                    Application.version);
                if (!initialized)
                {
                    Status = "Steam GameServer Init завершился ошибкой";
                    return;
                }

                validationCallback = Callback<ValidateAuthTicketResponse_t>.CreateGameServer(
                    OnValidationResponse);
                SteamGameServer.SetProduct("quieter");
                SteamGameServer.SetGameDescription("Quieter");
                SteamGameServer.SetModDir("quieter");
                SteamGameServer.SetDedicatedServer(true);
                SteamGameServer.SetMaxPlayerCount(QuieterConstants.DefaultMaxPlayers);
                SteamGameServer.LogOnAnonymous();
                Status = "Steam GameServer готов";
            }
            catch (Exception exception)
            {
                initialized = false;
                Status = $"Steam GameServer недоступен: {exception.Message}";
            }
        }

        private void OnValidationResponse(ValidateAuthTicketResponse_t response)
        {
            var steamId = response.m_SteamID.m_SteamID;
            if (!pending.Remove(steamId, out var completion))
            {
                return;
            }

            var success = response.m_eAuthSessionResponse
                == EAuthSessionResponse.k_EAuthSessionResponseOK;
            if (success)
            {
                active.Add(steamId);
            }
            else
            {
                SteamGameServer.EndAuthSession(response.m_SteamID);
            }

            completion(new ServerAuthenticationResult(
                success,
                steamId,
                success ? string.Empty : $"Steam verification: {response.m_eAuthSessionResponse}."));
        }
    }
}
