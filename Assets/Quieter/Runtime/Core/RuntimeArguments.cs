using System;
using System.IO;

namespace Quieter.Core
{
    public sealed class RuntimeArguments
    {
        public bool IsServer { get; private set; }
        public bool IsHost { get; private set; }
        public bool UseDevelopmentAuthentication { get; private set; }
        public string Address { get; private set; } = string.Empty;
        public ushort Port { get; private set; } = QuieterConstants.DefaultGamePort;
        public string ProfileServiceUrl { get; private set; } = string.Empty;
        public string ProfileServiceToken { get; private set; } = string.Empty;

        public static RuntimeArguments Parse(string[] args)
        {
            var result = new RuntimeArguments();

            for (var index = 0; index < args.Length; index++)
            {
                var value = args[index];
                switch (value)
                {
                    case "--server":
                        result.IsServer = true;
                        break;
                    case "--host":
                        result.IsHost = true;
                        break;
                    case "--development-auth":
                        result.UseDevelopmentAuthentication = true;
                        break;
                    case "--address":
                        result.Address = Next(args, ref index, value);
                        break;
                    case "--port":
                        if (ushort.TryParse(Next(args, ref index, value), out var port))
                        {
                            result.Port = port;
                        }
                        break;
                    case "--profile-service":
                        result.ProfileServiceUrl = Next(args, ref index, value).TrimEnd('/');
                        break;
                    case "--profile-token":
                        result.ProfileServiceToken = Next(args, ref index, value);
                        break;
                }
            }

#if UNITY_SERVER
            result.IsServer = true;
#endif
            result.ProfileServiceUrl = Environment.GetEnvironmentVariable("QUIETER_PROFILE_SERVICE_URL")
                ?? result.ProfileServiceUrl;
            result.ProfileServiceToken = Environment.GetEnvironmentVariable("QUIETER_PROFILE_SERVICE_TOKEN")
                ?? result.ProfileServiceToken;
            var tokenFile = Environment.GetEnvironmentVariable("QUIETER_PROFILE_SERVICE_TOKEN_FILE");
            if (!string.IsNullOrWhiteSpace(tokenFile) && File.Exists(tokenFile))
            {
                result.ProfileServiceToken = File.ReadAllText(tokenFile).Trim();
            }

            return result;
        }

        private static string Next(string[] args, ref int index, string option)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Missing value for {option}.");
            }

            index++;
            return args[index];
        }
    }
}
