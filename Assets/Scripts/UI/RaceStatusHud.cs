using CarRacing.Network.FrameSync;
using CarRacing.Race;
using UnityEngine;

namespace CarRacing.UI
{
    public sealed class RaceStatusHud : MonoBehaviour
    {
        private RaceState state;
        private FrameSyncDebugInfo debugInfo;
        private bool hasState;

        public void Render(RaceState state, FrameSyncDebugInfo debugInfo)
        {
            this.state = state;
            this.debugInfo = debugInfo;
            hasState = true;
        }

        private void OnGUI()
        {
            if (!hasState)
            {
                return;
            }

            PlayerRaceState localPlayerState = state.GetPlayer(debugInfo.PlayerId);
            string phase = GetPhaseText();
            string statusText =
                $"{phase}\nRole: Client   Player: {debugInfo.PlayerId}\nLap: {localPlayerState.Lap}/1\nW: Drive   S: Brake / Reverse\nA/D: Steer\n" +
                $"Local Frame: {debugInfo.LocalFrameIndex}\nConfirmed Frame: {debugInfo.ConfirmedFrameIndex}\n" +
                $"Prediction Depth: {debugInfo.PredictionDepth}\nRollbacks: {debugInfo.RollbackCount}\n" +
                $"Last Rollback: {debugInfo.LastRollbackLength}\nConfirmed Hash: {debugInfo.ConfirmedHash:X16}\n" +
                $"Desync: {(debugInfo.HashMismatch ? "YES" : "No")}";
            GUIStyle labelStyle = CreateLabelStyle();
            float textHeight = labelStyle.CalcHeight(new GUIContent(statusText), 280f);
            GUI.Box(new Rect(16f, 16f, 310f, textHeight + 36f), string.Empty);
            GUI.Label(new Rect(30f, 28f, 280f, textHeight), statusText, labelStyle);
        }

        private string GetPhaseText()
        {
            return state.Phase switch
            {
                RacePhase.WaitingForPlayers => "Waiting For Players",
                RacePhase.Racing => "Racing",
                RacePhase.WinnerDecided => $"Winner: Player {state.WinnerPlayerId}",
                RacePhase.Finished => "Finished",
                RacePhase.Aborted => "Match Aborted",
                _ => "Unknown"
            };
        }

        private static GUIStyle CreateLabelStyle()
        {
            GUIStyle style = new(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.UpperLeft
            };
            style.normal.textColor = Color.white;
            return style;
        }
    }
}
