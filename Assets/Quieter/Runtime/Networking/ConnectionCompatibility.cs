using System;

namespace Quieter.Networking
{
    public readonly struct ConnectionCompatibilityResult
    {
        public readonly bool Accepted;
        public readonly string Error;

        public ConnectionCompatibilityResult(bool accepted, string error)
        {
            Accepted = accepted;
            Error = error;
        }
    }

    public static class ConnectionCompatibility
    {
        public const int PayloadSize = sizeof(ushort) * 2;

        public static byte[] CreatePayload(ushort protocolVersion, ushort generatorVersion)
        {
            var payload = new byte[PayloadSize];
            BitConverter.GetBytes(protocolVersion).CopyTo(payload, 0);
            BitConverter.GetBytes(generatorVersion).CopyTo(payload, sizeof(ushort));
            return payload;
        }

        public static ConnectionCompatibilityResult Validate(
            byte[] payload,
            ushort expectedProtocol,
            ushort expectedGenerator)
        {
            if (payload == null || payload.Length != PayloadSize)
            {
                return new ConnectionCompatibilityResult(
                    false,
                    "Некорректное приветствие клиента.");
            }

            var protocolVersion = BitConverter.ToUInt16(payload, 0);
            var generatorVersion = BitConverter.ToUInt16(payload, sizeof(ushort));
            if (protocolVersion != expectedProtocol)
            {
                return new ConnectionCompatibilityResult(
                    false,
                    $"Несовместимая версия протокола: {protocolVersion}.");
            }

            if (generatorVersion != expectedGenerator)
            {
                return new ConnectionCompatibilityResult(
                    false,
                    $"Несовместимая версия генератора: {generatorVersion}.");
            }

            return new ConnectionCompatibilityResult(true, string.Empty);
        }
    }
}

