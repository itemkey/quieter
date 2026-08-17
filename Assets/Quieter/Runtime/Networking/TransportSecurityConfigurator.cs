using System;
using System.IO;
using System.Reflection;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Quieter.Networking
{
    public static class TransportSecurityConfigurator
    {
        public static bool ConfigureClient(UnityTransport transport, ServerEndpoint endpoint)
        {
            if (!endpoint.UseDtls)
            {
                return true;
            }

            return Invoke(
                transport,
                "SetClientSecrets",
                endpoint.ExpectedServerName,
                endpoint.PinnedCaCertificate);
        }

        public static bool ConfigureServerFromEnvironment(UnityTransport transport)
        {
            var certificate = Environment.GetEnvironmentVariable("QUIETER_DTLS_CERTIFICATE");
            var privateKey = Environment.GetEnvironmentVariable("QUIETER_DTLS_PRIVATE_KEY");
            certificate = ReadSecretFile("QUIETER_DTLS_CERTIFICATE_FILE", certificate);
            privateKey = ReadSecretFile("QUIETER_DTLS_PRIVATE_KEY_FILE", privateKey);
            if (string.IsNullOrWhiteSpace(certificate) || string.IsNullOrWhiteSpace(privateKey))
            {
                Debug.LogWarning("DTLS is disabled: server certificate variables are not configured.");
                return false;
            }

            return Invoke(transport, "SetServerSecrets", certificate, privateKey);
        }

        private static string ReadSecretFile(string variableName, string fallback)
        {
            var path = Environment.GetEnvironmentVariable(variableName);
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path)
                ? File.ReadAllText(path)
                : fallback;
        }

        private static bool Invoke(UnityTransport transport, string methodName, params object[] arguments)
        {
            var method = typeof(UnityTransport).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public,
                null,
                Array.ConvertAll(arguments, argument => argument.GetType()),
                null);
            if (method == null)
            {
                Debug.LogError($"Installed Unity Transport does not expose {methodName}.");
                return false;
            }

            try
            {
                method.Invoke(transport, arguments);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"Could not configure transport security: {exception.Message}");
                return false;
            }
        }
    }
}
