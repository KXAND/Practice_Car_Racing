using CarRacing.Input;
using CarRacing.Network.FrameSync;
using CarRacing.Race;
using CarRacing.Scene;
using CarRacing.UI;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Utility.Template;
using UnityEngine;

namespace CarRacing.Network
{
    public sealed class FishNetFrameSync : TickNetworkBehaviour
    {
        [SerializeField] private RaceInputSource inputSource;
        [SerializeField] private RaceCarUpdater raceCarUpdater;
        [SerializeField] private RaceStatusHud statusHud;
        [SerializeField, Min(0)] private int upstreamDelayTicks;
        [SerializeField, Min(0)] private int dropEveryNthInput;

        private FrameSyncServer server;
        private FrameSyncClient client;
        private UpstreamInputConditioner inputConditioner;
        private uint startNetworkTick;
        private bool hasStartNetworkTick;
        private bool loggedClientInput;
        private bool loggedServerInput;
        private bool loggedClientRender;
        private bool loggedRejectedInput;
        private bool loggedFinish;

        public override void OnStartNetwork()
        {
            SetTickCallbacks(TickCallback.Tick);
        }

        public void ApplyLaunchOverrides(bool useScriptedInput, int? delayOverride, int? dropOverride)
        {
            if (useScriptedInput)
            {
                inputSource = gameObject.AddComponent<ScriptedRaceInput>();
            }

            if (delayOverride.HasValue)
            {
                upstreamDelayTicks = delayOverride.Value;
            }

            if (dropOverride.HasValue)
            {
                dropEveryNthInput = dropOverride.Value;
            }
        }

        protected override void TimeManager_OnTick()
        {
            // 避免服务端先广播 F0 确认帧，客户端收到后才进入 AdvanceClientFrame()，第一笔输入变成 F1/F3
            if (IsClientStarted)
            {
                AdvanceClientFrame();
            }

            if (IsServerStarted)
            {
                AdvanceServerFrame();
            }
        }

        #region Server

        public override void OnStartServer()
        {
            hasStartNetworkTick = false;
            server = new FrameSyncServer();
            NetworkManager.ServerManager.OnRemoteConnectionState += HandleRemoteConnectionStateChanged;
            NetworkManager.SceneManager.OnClientLoadedStartScenes += AddClientToRaceScene;
            Debug.Log($"[FrameSync] Stage=ServerSimulationReady Spawned={NetworkObject.IsSpawned}");
        }

        public override void OnStopServer()
        {
            NetworkManager.ServerManager.OnRemoteConnectionState -= HandleRemoteConnectionStateChanged;
            NetworkManager.SceneManager.OnClientLoadedStartScenes -= AddClientToRaceScene;
            hasStartNetworkTick = false;
            Debug.Log("[FrameSync] Stage=ServerStopped");
        }
        // 当连接到服务端时
        public override void OnSpawnServer(NetworkConnection connection)
        {
            if (!server.TryGetPlayerId(connection.ClientId, out byte playerId))
            {
                Debug.LogWarning($"[FrameSync] Stage=PlayerSpawnSkipped Connection={connection.ClientId}");
                return;
            }

            TargetClientAssignPlayer(connection, playerId);
            Debug.Log($"[FrameSync] Stage=PlayerSpawned Connection={connection.ClientId} Player={playerId}");
            RaceState previousReadyState = server.State;
            server.MarkPlayerReady(connection.ClientId);
            LogServerStateTransition(previousReadyState, server.State);
            ScheduleRaceStart();
        }

        private void AdvanceServerFrame()
        {
            if (server.State.Phase == RacePhase.WaitingForPlayers)
            {
                if (!hasStartNetworkTick || TimeManager.Tick < startNetworkTick)
                {
                    return;
                }

                RaceState previousStartState = server.State;
                bool started = server.StartRace();
                LogServerStateTransition(previousStartState, server.State);
                if (!started)
                {
                    return;
                }

                hasStartNetworkTick = false;
            }

            if (server.State.Phase == RacePhase.Aborted)
            {
                return;
            }

            RaceState previousFrameState = server.State;
            RaceFrame confirmed = server.AdvanceFrame();
            LogServerStateTransition(previousFrameState, server.State);
            ObserversBroadcastConfirmedFrame(confirmed);
            if ((confirmed.FrameIndex + 1) % GlobalSettings.HashIntervalFrames == 0)
            {
                ObserversBroadcastConfirmedHash(confirmed.FrameIndex, server.State.DeterministicHash());
            }
        }

        private void HandleRemoteConnectionStateChanged(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Started)
            {
                RaceState previousAssignState = server.State;
                bool assigned = server.TryAssignPlayer(connection.ClientId, out byte playerId);
                Debug.Log($"[FrameSync] Stage=RemoteConnected Connection={connection.ClientId} Assigned={assigned} Player={playerId}");
                LogServerStateTransition(previousAssignState, server.State);
            }
            else if (args.ConnectionState == RemoteConnectionState.Stopped)
            {
                RaceState previousDisconnectState = server.State;
                bool disconnected = server.DisconnectPlayer(connection.ClientId);
                LogServerStateTransition(previousDisconnectState, server.State);
                if (disconnected)
                {
                    if (server.State.Phase == RacePhase.WaitingForPlayers)
                    {
                        bool wasStartScheduled = hasStartNetworkTick;
                        hasStartNetworkTick = false;
                        if (wasStartScheduled)
                        {
                            ObserversCancelRaceStart();
                        }
                    }

                }

                Debug.Log($"[FrameSync] Stage=RemoteDisconnected Connection={connection.ClientId}");
            }
        }

        private void ScheduleRaceStart()
        {
            if (hasStartNetworkTick || !server.CanStartRace())
            {
                return;
            }

            startNetworkTick = TimeManager.Tick + GlobalSettings.StartDelayTicks;
            hasStartNetworkTick = true;
            Debug.Log($"[FrameSync] Stage=MatchStartScheduled Players={GlobalSettings.TotalPlayerCount} NetworkTick={startNetworkTick}");
            ObserversBeginMatch(startNetworkTick);
        }

        /// <summary>
        /// On Client Loaded Start Scenes, server Add Client Connection To RaceScene
        /// </summary>
        /// <param name="connection"></param>
        /// <param name="asServer"></param>
        private void AddClientToRaceScene(NetworkConnection connection, bool asServer)
        {
            if (!asServer)
            {
                return;
            }

            NetworkManager.SceneManager.AddConnectionToScene(connection, gameObject.scene);
            Debug.Log($"[FrameSync] Stage=ConnectionAddedToRaceScene Connection={connection.ClientId}");
        }

        private static void LogServerStateTransition(RaceState previousState, RaceState currentState)
        {
            if (previousState.Phase == currentState.Phase &&
                previousState.Player0.Status == currentState.Player0.Status &&
                previousState.Player1.Status == currentState.Player1.Status &&
                previousState.WinnerPlayerId == currentState.WinnerPlayerId)
            {
                return;
            }

            Debug.Log($"[FrameSync] Stage=ServerRaceStateTransition FromPhase={previousState.Phase} ToPhase={currentState.Phase} FromPlayer0={previousState.Player0.Status} ToPlayer0={currentState.Player0.Status} FromPlayer1={previousState.Player1.Status} ToPlayer1={currentState.Player1.Status} FromWinner={previousState.WinnerPlayerId} ToWinner={currentState.WinnerPlayerId}");
        }

        [ServerRpc(RequireOwnership = false)]
        private void ServerSubmitInput(int frameIndex, byte playerId, int steering, int throttle, int brake, NetworkConnection sender = null)
        {
            if (sender == null || !server.TryGetPlayerId(sender.ClientId, out byte assignedPlayerId))
            {
                return;
            }

            QuantizedInput input = new(steering, throttle, brake);
            bool accepted = server.TrySubmit(new InputFrame(frameIndex, playerId, input), assignedPlayerId);
            if (!accepted && !loggedRejectedInput)
            {
                loggedRejectedInput = true;
                Debug.LogWarning($"[FrameSync] Stage=ServerInputRejected Player={assignedPlayerId} Frame={frameIndex}");
            }
            else if (!loggedServerInput && (input.Steering != 0 || input.Throttle != 0 || input.Brake != 0))
            {
                loggedServerInput = true;
                Debug.Log($"[FrameSync] Stage=ServerInputAccepted Player={assignedPlayerId} Frame={frameIndex}");
            }
        }

        #endregion

        #region Client

        public override void OnStartClient()
        {
            Debug.Log($"[FrameSync] Stage=ClientNetworkObjectReady InputSource={(inputSource == null ? "missing" : "ready")} RaceView={(raceCarUpdater == null ? "missing" : "ready")}");
        }

        public override void OnStopClient()
        {
            hasStartNetworkTick = false;
            inputConditioner = null;
            client = null;
        }

        private void AdvanceClientFrame()
        {
            if (client == null)
            {
                return;
            }

            if (inputSource == null || inputConditioner == null)
            {
                statusHud.Render(client.CurrentState, client.GetDebugInfo());
                return;
            }

            if (client.CurrentState.Phase == RacePhase.WaitingForPlayers)
            {
                if (!hasStartNetworkTick || TimeManager.Tick < startNetworkTick)
                {
                    statusHud.Render(client.CurrentState, client.GetDebugInfo());
                    return;
                }

                client.StartRacePrediction();
                hasStartNetworkTick = false;

                Debug.Log($"[FrameSync] Stage=ClientRacePredictionStarted Player={client.PlayerId} Frame={client.CurrentState.FrameIndex}");
            }

            if (client.CurrentState.Phase == RacePhase.Aborted)
            {
                return;
            }

            // sample input F2(frame 2)
            QuantizedInput sampledInput = inputSource.ReadInput(client.NextLocalInputFrameIndex);
            if (!loggedClientInput && (sampledInput.Steering != 0 || sampledInput.Throttle != 0 || sampledInput.Brake != 0))
            {
                loggedClientInput = true;
                Debug.Log($"[FrameSync] Stage=ClientInputCaptured Player={client.PlayerId}");
            }

            // push F2, advance F0
            InputFrame input = client.Predict(sampledInput);

            // submit to server w/ conditional drop/delay
            if (!inputConditioner.ScheduleInput(input, (int)TimeManager.Tick))
            {
                Debug.Log($"[FrameSync] Stage=InputDropped Player={client.PlayerId} Frame={input.FrameIndex}");
            }
            while (inputConditioner.PopReadyInput((int)TimeManager.Tick, out InputFrame dueInput))
            {
                ServerSubmitInput(dueInput.FrameIndex, dueInput.PlayerId, dueInput.Input.Steering, dueInput.Input.Throttle, dueInput.Input.Brake);
            }

            // update scene
            raceCarUpdater.UpdateState(client.CurrentState, client.PlayerId);
            statusHud.Render(client.CurrentState, client.GetDebugInfo());
        }

        [TargetRpc]
        private void TargetClientAssignPlayer(NetworkConnection connection, byte playerId)
        {
            client = new FrameSyncClient(playerId);
            Debug.Log($"[FrameSync] Stage=ClientAssigned Player={playerId}");
        }

        [ObserversRpc]
        private void ObserversBeginMatch(uint networkTick)
        {
            if (client != null)
            {
                inputConditioner = new UpstreamInputConditioner(upstreamDelayTicks, dropEveryNthInput);
                if (inputConditioner.WasDelayClamped)
                {
                    Debug.LogWarning($"[FrameSync] Invalid upstream delay {upstreamDelayTicks}; falling back to 0. Valid range is 0-1 ticks.");
                }

                if (inputConditioner.WasDropClamped)
                {
                    Debug.LogWarning($"[FrameSync] Invalid drop interval {dropEveryNthInput}; falling back to 0.");
                }

                Debug.Log($"[FrameSync] Stage=WeakConfig Player={client.PlayerId} Delay={inputConditioner.DelayTicks} DropEvery={inputConditioner.DropEveryNthInput}");
            }

            startNetworkTick = networkTick;
            hasStartNetworkTick = true;
            if (client != null)
            {
                Debug.Log($"[FrameSync] Stage=ClientMatchScheduled Player={client.PlayerId} NetworkTick={networkTick}");
            }
        }

        [ObserversRpc]
        private void ObserversCancelRaceStart()
        {
            hasStartNetworkTick = false;
            inputConditioner = null;
            if (client == null)
            {
                return;
            }

            Debug.Log($"[FrameSync] Stage=ClientMatchStartCancelled Player={client.PlayerId}");
        }

        [ObserversRpc]
        private void ObserversBroadcastConfirmedFrame(RaceFrame frame)
        {
            if (client == null)
            {
                return;
            }

            int rollbackCount = client.RollbackCount;
            bool applied = client.ApplyConfirmed(frame);
            if (applied)
            {
                raceCarUpdater.UpdateState(client.CurrentState, client.PlayerId);
                statusHud.Render(client.CurrentState, client.GetDebugInfo());
                if (client.RollbackCount != rollbackCount)
                {
                    Debug.Log($"[FrameSync] Stage=Rollback Player={client.PlayerId} Frame={client.LastRollbackFrameIndex} Length={client.LastRollbackLength} Total={client.RollbackCount}");
                }

                PlayerRaceState localState = client.CurrentState.GetPlayer(client.PlayerId);
                if (!loggedFinish && localState.Status == PlayerStatus.Finished)
                {
                    loggedFinish = true;
                    Debug.Log($"[FrameSync] Stage=RaceFinished Player={client.PlayerId} Frame={client.CurrentState.FrameIndex} Lap={localState.Lap}");
                }

                if (!loggedClientRender)
                {
                    loggedClientRender = true;
                    Debug.Log($"[FrameSync] Stage=ClientFirstConfirmedFrame Frame={frame.FrameIndex}");
                }
            }

        }

        // Server broadcast confirmedFrame hash to all Clients per TickRate
        [ObserversRpc]
        private void ObserversBroadcastConfirmedHash(int frameIndex, ulong hash)
        {
            if (client == null)
            {
                return;
            }

            int hashCheckCount = client.HashCheckCount;
            if (!client.PushConfirmedHash(frameIndex, hash))
            {
                Debug.LogError($"[FrameSync] Stage=ProtocolViolation HashBeforeConfirmedFrame Player={client.PlayerId} Frame={frameIndex}");
                return;
            }

            if (client.HashCheckCount != hashCheckCount)// complete a hash check, write a log
            {
                Debug.Log($"[FrameSync] Stage=HashCheck Player={client.PlayerId} Frame={client.LastHashCheckFrameIndex} Local={client.ConfirmedHash:X16} Server={client.LastServerHash:X16} Match={!client.HashMismatch}");
            }
        }

        #endregion

    }
}
