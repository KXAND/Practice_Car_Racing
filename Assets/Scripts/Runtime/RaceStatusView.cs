using CarRacing.Simulation;
using UnityEngine;

namespace CarRacing.Runtime
{
    public sealed class RaceStatusView : MonoBehaviour
    {
        private RaceState state;
        private FrameSyncDebugInfo debugInfo;
        private bool hasState;
        private bool matchEnded;

        public void Render(RaceState state)
        {
            Render(
                state,
                new FrameSyncDebugInfo(
                    0,
                    state.Frame,
                    state.Frame,
                    0,
                    0,
                    0,
                    RaceStateHasher.Compute(state),
                    false));
        }

        public void Render(RaceState state, FrameSyncDebugInfo debugInfo)
        {
            this.state = state;
            this.debugInfo = debugInfo;
            hasState = true;
        }

        public void SetMatchEnded()
        {
            matchEnded = true;
        }

        private void OnGUI()
        {
            if (!hasState)
            {
                return;
            }

            string phase = matchEnded
                ? "Match Ended - Opponent Disconnected"
                : state.Phase == RacePhase.Finished ? "Finished - Victory!" : "Racing";
            string statusText =
                $"{phase}\nRole: Client   Player: {debugInfo.PlayerId}\nLap: {state.Lap}/1\nW: Drive   S: Brake / Reverse\nA/D: Steer\n" +
                $"Local Frame: {debugInfo.LocalFrame}\nConfirmed Frame: {debugInfo.ConfirmedFrame}\n" +
                $"Prediction Depth: {debugInfo.PredictionDepth}\nRollbacks: {debugInfo.RollbackCount}\n" +
                $"Last Rollback: {debugInfo.LastRollbackLength}\nConfirmed Hash: {debugInfo.ConfirmedHash:X16}\n" +
                $"Desync: {(debugInfo.HashMismatch ? "YES" : "No")}";
            GUIStyle labelStyle = CreateLabelStyle();
            float textHeight = labelStyle.CalcHeight(new GUIContent(statusText), 280f);
            GUI.Box(new Rect(16f, 16f, 310f, textHeight + 36f), string.Empty);
            GUI.Label(new Rect(30f, 28f, 280f, textHeight), statusText, labelStyle);
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
