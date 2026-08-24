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
        [SerializeField, Min(0)] private int upstreamDelayTicks;
        [SerializeField, Min(0)] private int dropEveryNthInput;

        private readonly PlayerSlotRegistry playerSlots = new();
        private readonly HashSet<int> spawnedConnectionIds = new();
        private FrameSyncServer server;
        private FrameSyncClient client;
        private UpstreamInputConditioner inputConditioner;
        private uint startNetworkTick;
        private bool matchStarted;
        private bool matchEnded;
        private bool loggedClientInput;
        private bool loggedServerInput;
        private bool loggedClientRender;
        private bool loggedRejectedInput;
        private bool loggedFinish;

        public void ConfigureFromCommandLine(bool useScriptedInput, int? delayOverride, int? dropOverride)
        {
            if (useScriptedInput)
            {
                inputSource = gameObject.AddComponent<ScriptedRaceInput>();
            }

            if (delayOverride.HasValue)
            {
                upstreamDelayTicks = NormalizeDelayOverride(delayOverride.Value);
            }

            if (dropOverride.HasValue)
            {
                dropEveryNthInput = NormalizeDropOverride(dropOverride.Value);
            }
        }

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
            if ((confirmed.Frame + 1) % RaceSimulation.TickRate == 0)
            {
                BroadcastConfirmedHash(confirmed.Frame, TwoPlayerRaceStateHasher.Compute(server.State));
            }
        }

        private void TimeManager_OnClientTick()
        {
            if (!matchStarted || client == null || inputSource == null || inputConditioner == null || TimeManager.Tick < startNetworkTick)
            {
                return;
            }

            QuantizedInput sampledInput = inputSource.ReadInput(client.NextLocalInputFrame);
            if (!loggedClientInput && (sampledInput.Steering != 0 || sampledInput.Throttle != 0 || sampledInput.Brake != 0))
            {
                loggedClientInput = true;
                Debug.Log($"[FrameSync] Stage=ClientInputCaptured Player={client.PlayerId}");
            }

            InputFrame input = client.SampleLocalInput(sampledInput);
            if (!inputConditioner.Enqueue(input, (int)TimeManager.Tick))
            {
                Debug.Log($"[FrameSync] Stage=InputDropped Player={client.PlayerId} Frame={input.Frame}");
            }
            while (inputConditioner.TryDequeueDue((int)TimeManager.Tick, out InputFrame dueInput))
            {
                SubmitInput(dueInput.Frame, dueInput.PlayerId, dueInput.Input.Steering, dueInput.Input.Throttle, dueInput.Input.Brake);
            }

            client.PredictNextFrame();
            raceView.Render(client.State, client.PlayerId, client.GetDebugInfo());
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
                inputConditioner = new UpstreamInputConditioner(upstreamDelayTicks, dropEveryNthInput);
                if (inputConditioner.WasDelayClamped)
                {
                    Debug.LogWarning($"[FrameSync] Invalid upstream delay {upstreamDelayTicks}; falling back to 0. Valid range is 0-1 ticks.");
                }

                Debug.Log($"[FrameSync] Stage=WeakConfig Player={client.PlayerId} Delay={inputConditioner.DelayTicks} DropEvery={inputConditioner.DropEveryNthInput}");
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

            int rollbackCount = client.RollbackCount;
            int hashCheckCount = client.HashCheckCount;
            client.Receive(new ConfirmedFrame(
                frame,
                new QuantizedInput(player0Steering, player0Throttle, player0Brake),
                new QuantizedInput(player1Steering, player1Throttle, player1Brake)));
            while (client.TryAdvance())
            {
                raceView.Render(client.State, client.PlayerId, client.GetDebugInfo());
                LogRollbackIfApplied(rollbackCount);
                rollbackCount = client.RollbackCount;
                LogFinishedIfNeeded();
                if (!loggedClientRender)
                {
                    loggedClientRender = true;
                    Debug.Log($"[FrameSync] Stage=ClientFirstConfirmedFrame Frame={frame}");
                }
            }

            LogHashCheckIfApplied(hashCheckCount);
        }

        [ObserversRpc]
        private void BroadcastConfirmedHash(int frame, ulong hash)
        {
            if (client == null)
            {
                return;
            }

            int hashCheckCount = client.HashCheckCount;
            client.ReceiveServerHash(frame, hash);
            raceView.Render(client.State, client.PlayerId, client.GetDebugInfo());
            LogHashCheckIfApplied(hashCheckCount);
        }

        private void LogRollbackIfApplied(int previousRollbackCount)
        {
            if (client.RollbackCount == previousRollbackCount)
            {
                return;
            }

            Debug.Log($"[FrameSync] Stage=Rollback Player={client.PlayerId} Frame={client.LastRollbackFrame} Length={client.LastRollbackLength} Total={client.RollbackCount}");
        }

        private void LogHashCheckIfApplied(int previousHashCheckCount)
        {
            if (client.HashCheckCount == previousHashCheckCount)
            {
                return;
            }

            Debug.Log($"[FrameSync] Stage=HashCheck Player={client.PlayerId} Frame={client.LastHashCheckFrame} Local={client.ConfirmedHash:X16} Server={client.LastServerHash:X16} Match={!client.HashMismatch}");
        }

        private void LogFinishedIfNeeded()
        {
            RaceState localState = client.PlayerId == 0 ? client.State.Player0 : client.State.Player1;
            if (loggedFinish || localState.Phase != RacePhase.Finished)
            {
                return;
            }

            loggedFinish = true;
            Debug.Log($"[FrameSync] Stage=RaceFinished Player={client.PlayerId} Frame={localState.Frame} Lap={localState.Lap}");
        }

        private int NormalizeDelayOverride(int value)
        {
            if (value >= 0 && value <= 1)
            {
                return value;
            }

            Debug.LogWarning($"[FrameSync] Invalid upstream delay {value}; falling back to 0. Valid range is 0-1 ticks.");
            return 0;
        }

        private int NormalizeDropOverride(int value)
        {
            if (value >= 0)
            {
                return value;
            }

            Debug.LogWarning($"[FrameSync] Invalid drop interval {value}; falling back to 0.");
            return 0;
        }
    }
}
