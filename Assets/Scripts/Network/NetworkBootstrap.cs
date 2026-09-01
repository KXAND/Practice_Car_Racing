using System;
using FishNet.Managing;
using UnityEngine;

namespace CarRacing.Network
{
    public sealed class NetworkBootstrap : MonoBehaviour
    {
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private FishNetFrameSync frameSync;

        private void Start()
        {
            if (networkManager == null || frameSync == null)
            {
                Debug.LogError("NetworkBootstrap requires NetworkManager and FishNetFrameSync references.", this);
                return;
            }

            string[] arguments = Environment.GetCommandLineArgs();
            bool isServer = Array.IndexOf(arguments, "-server") >= 0;
            bool useScriptedInput = Array.IndexOf(arguments, "-scriptedInput") >= 0;
            int? delayOverride = TryGetIntegerArgument(arguments, "-upstreamDelayTicks", "--dt", out bool invalidDelayOverride);
            int? dropOverride = TryGetIntegerArgument(arguments, "-dropEveryNthInput", "--di", out bool invalidDropOverride);
            if (invalidDelayOverride)
            {
                Debug.LogWarning("[FrameSync] Invalid -upstreamDelayTicks value; falling back to 0.");
                delayOverride = 0;
            }

            if (invalidDropOverride)
            {
                Debug.LogWarning("[FrameSync] Invalid -dropEveryNthInput value; falling back to 0.");
                dropOverride = 0;
            }

            frameSync.ApplyLaunchOverrides(
                useScriptedInput,
                delayOverride,
                dropOverride);
            string role = isServer ? "Server" : useScriptedInput ? "ScriptedClient" : "KeyboardClient";
            Debug.Log($"[FrameSync] Stage=Bootstrap Role={role}");
            if (isServer)
            {
                networkManager.ServerManager.StartConnection();
            }
            else
            {
                networkManager.ClientManager.StartConnection();
            }
        }

        private static int? TryGetIntegerArgument(string[] arguments, string name, string alias, out bool invalid)
        {
            int index = Array.IndexOf(arguments, name);
            if (index < 0)
            {
                index = Array.IndexOf(arguments, alias);
            }

            invalid = false;
            if (index < 0)
            {
                return null;
            }

            if (index + 1 >= arguments.Length || !int.TryParse(arguments[index + 1], out int value))
            {
                invalid = true;
                return null;
            }

            return value;
        }
    }
}
