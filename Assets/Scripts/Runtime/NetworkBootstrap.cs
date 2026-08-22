using System;
using FishNet.Managing;
using UnityEngine;

namespace CarRacing.Runtime
{
    public sealed class NetworkBootstrap : MonoBehaviour
    {
        [SerializeField] private NetworkManager networkManager;

        private void Start()
        {
            if (networkManager == null)
            {
                Debug.LogError("NetworkBootstrap requires a NetworkManager reference.", this);
                return;
            }

            bool isServer = Array.IndexOf(Environment.GetCommandLineArgs(), "-server") >= 0;
            Debug.Log($"[FrameSync] Stage=Bootstrap Role={(isServer ? "Server" : "Client")}");
            if (isServer)
            {
                networkManager.ServerManager.StartConnection();
            }
            else
            {
                networkManager.ClientManager.StartConnection();
            }
        }
    }
}
