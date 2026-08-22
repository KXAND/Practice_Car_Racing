using System.Collections.Generic;
using CarRacing.Simulation;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

namespace CarRacing.Runtime
{
    public sealed class FishNetFrameSync : NetworkBehaviour
    {
        private const uint CountdownTicks = RaceSimulation.TickRate * 2;

        [SerializeField] private RaceInputSource inputSource;
        [SerializeField] private TwoPlayerRaceView raceView;

        private readonly PlayerSlotRegistry playerSlots = new();
        private readonly HashSet<int> spawnedConnectionIds = new();
        private FrameSyncServer server;
        private FrameSyncClient client;
        private uint startNetworkTick;
        private bool matchStarted;
        private bool matchEnded;
        private bool loggedClientInput;
        private bool loggedServerInput;
        private bool loggedClientRender;
        private bool loggedRejectedInput;

        public override void OnStartServer()
        {
            server = new FrameSyncServer();
            NetworkManager.ServerManager.OnRemoteConnectionState += ServerManager_OnRemoteConnectionState;
            NetworkManager.SceneManager.OnClientLoadedStartScenes += SceneManager_OnClientLoadedStartScenes;
            TimeManager.OnTick += TimeManager_OnServerTick;
            Debug.Log($"[FrameSync] Stage=ServerSimulationReady Spawned={NetworkObject.IsSpawned}");
        }

        public override void OnStopServer()
        {
            NetworkManager.ServerManager.OnRemoteConnectionState -= ServerManager_OnRemoteConnectionState;
            NetworkManager.SceneManager.OnClientLoadedStartScenes -= SceneManager_OnClientLoadedStartScenes;
            TimeManager.OnTick -= TimeManager_OnServerTick;
            Debug.Log("[FrameSync] Stage=ServerStopped");
        }

        public override void OnStartClient()
        {
            TimeManager.OnTick += TimeManager_OnClientTick;
            Debug.Log($"[FrameSync] Stage=ClientNetworkObjectReady InputSource={(inputSource == null ? "missing" : "ready")} RaceView={(raceView == null ? "missing" : "ready")}");
        }

        public override void OnStopClient()
        {
            TimeManager.OnTick -= TimeManager_OnClientTick;
        }

        private void TimeManager_OnServerTick()
        {
            if (!matchStarted || TimeManager.Tick < startNetworkTick)
            {
                return;
            }

            ConfirmedFrame confirmed = server.AdvanceFrame();
            BroadcastConfirmed(
                confirmed.Frame,
                confirmed.Player0.Steering,
                confirmed.Player0.Throttle,
                confirmed.Player0.Brake,
                confirmed.Player1.Steering,
                confirmed.Player1.Throttle,
                confirmed.Player1.Brake);
        }

        private void TimeManager_OnClientTick()
        {
            if (!matchStarted || client == null || inputSource == null || TimeManager.Tick < startNetworkTick)
            {
                return;
            }

            QuantizedInput sampledInput = inputSource.ReadInput();
            if (!loggedClientInput && (sampledInput.Steering != 0 || sampledInput.Throttle != 0 || sampledInput.Brake != 0))
            {
                loggedClientInput = true;
                Debug.Log($"[FrameSync] Stage=ClientInputCaptured Player={client.PlayerId}");
            }

            InputFrame input = client.CreateLocalInput(sampledInput, client.NextConfirmedFrame);
            SubmitInput(input.Frame, input.PlayerId, input.Input.Steering, input.Input.Throttle, input.Input.Brake);
        }

        private void ServerManager_OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Started)
            {
                byte playerId = default;
                bool assigned = !matchEnded && playerSlots.TryAssign(connection.ClientId, out playerId);
                Debug.Log($"[FrameSync] Stage=RemoteConnected Connection={connection.ClientId} Assigned={assigned} Player={playerId}");
            }
            else if (args.ConnectionState == RemoteConnectionState.Stopped)
            {
                if (!matchEnded && playerSlots.TryGetPlayerId(connection.ClientId, out _))
                {
                    matchEnded = true;
                    playerSlots.EndMatch();
                    matchStarted = false;
                    NotifyMatchEnded();
                    Debug.Log($"[FrameSync] Stage=MatchEnded DisconnectedPlayerConnection={connection.ClientId}");
                }

                matchStarted = false;
                Debug.Log($"[FrameSync] Stage=RemoteDisconnected Connection={connection.ClientId}");
            }
        }

        private void SceneManager_OnClientLoadedStartScenes(NetworkConnection connection, bool asServer)
        {
            if (!asServer)
            {
                return;
            }

            NetworkManager.SceneManager.AddConnectionToScene(connection, gameObject.scene);
            Debug.Log($"[FrameSync] Stage=ConnectionAddedToRaceScene Connection={connection.ClientId}");
        }

        public override void OnSpawnServer(NetworkConnection connection)
        {
            if (!playerSlots.TryGetPlayerId(connection.ClientId, out byte playerId))
            {
                Debug.LogWarning($"[FrameSync] Stage=PlayerSpawnSkipped Connection={connection.ClientId}");
                return;
            }

            AssignPlayer(connection, playerId);
            Debug.Log($"[FrameSync] Stage=PlayerSpawned Connection={connection.ClientId} Player={playerId}");
            spawnedConnectionIds.Add(connection.ClientId);
            if (!matchEnded && !matchStarted && spawnedConnectionIds.Count == 2 && playerSlots.Count == 2)
            {
                int startFrame = server.NextFrame;
                startNetworkTick = TimeManager.Tick + CountdownTicks;
                matchStarted = true;
                Debug.Log($"[FrameSync] Stage=CountdownStarted Players=2 StartFrame={startFrame} NetworkTick={startNetworkTick}");
                BeginMatch(startFrame, startNetworkTick);
            }
        }

        [ServerRpc(RequireOwnership = false)]
        private void SubmitInput(int frame, byte playerId, sbyte steering, byte throttle, byte brake, NetworkConnection sender = null)
        {
            if (sender == null || !playerSlots.TryGetPlayerId(sender.ClientId, out byte assignedPlayerId))
            {
                return;
            }

            QuantizedInput input = new(steering, throttle, brake);
            bool accepted = server.TrySubmit(new InputFrame(frame, playerId, input), assignedPlayerId);
            if (!accepted && !loggedRejectedInput)
            {
                loggedRejectedInput = true;
                Debug.LogWarning($"[FrameSync] Stage=ServerInputRejected Player={assignedPlayerId} Frame={frame}");
            }
            else if (!loggedServerInput && (input.Steering != 0 || input.Throttle != 0 || input.Brake != 0))
            {
                loggedServerInput = true;
                Debug.Log($"[FrameSync] Stage=ServerInputAccepted Player={assignedPlayerId} Frame={frame}");
            }
        }

        [TargetRpc]
        private void AssignPlayer(NetworkConnection connection, byte playerId)
        {
            client = new FrameSyncClient(playerId);
            Debug.Log($"[FrameSync] Stage=ClientAssigned Player={playerId}");
        }

        [ObserversRpc]
        private void BeginMatch(int startFrame, uint networkTick)
        {
            if (client != null)
            {
                client = new FrameSyncClient(client.PlayerId, startFrame);
            }

            startNetworkTick = networkTick;
            matchStarted = true;
            Debug.Log($"[FrameSync] Stage=ClientMatchStarted Player={client?.PlayerId} StartFrame={startFrame} NetworkTick={networkTick}");
        }

        [ObserversRpc]
        private void NotifyMatchEnded()
        {
            matchEnded = true;
            matchStarted = false;
            raceView.SetMatchEnded();
            Debug.Log("[FrameSync] Stage=ClientMatchEnded");
        }

        [ObserversRpc]
        private void BroadcastConfirmed(
            int frame,
            sbyte player0Steering,
            byte player0Throttle,
            byte player0Brake,
            sbyte player1Steering,
            byte player1Throttle,
            byte player1Brake)
        {
            if (client == null)
            {
                return;
            }

            client.Receive(new ConfirmedFrame(
                frame,
                new QuantizedInput(player0Steering, player0Throttle, player0Brake),
                new QuantizedInput(player1Steering, player1Throttle, player1Brake)));
            while (client.TryAdvance())
            {
                raceView.Render(client.State, client.PlayerId);
                if (!loggedClientRender)
                {
                    loggedClientRender = true;
                    Debug.Log($"[FrameSync] Stage=ClientFirstConfirmedFrame Frame={frame}");
                }
            }
        }
    }
}
