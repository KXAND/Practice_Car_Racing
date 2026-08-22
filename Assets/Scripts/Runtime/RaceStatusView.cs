using CarRacing.Simulation;
using UnityEngine;

namespace CarRacing.Runtime
{
    public sealed class RaceStatusView : MonoBehaviour
    {
        private RaceState state;
        private bool hasState;

        public void Render(RaceState state)
        {
            this.state = state;
            hasState = true;
        }

        private void OnGUI()
        {
            if (!hasState)
            {
                return;
            }

            string phase = state.Phase == RacePhase.Finished ? "Finished - Victory!" : "Racing";
            GUI.Box(new Rect(16f, 16f, 310f, 156f), string.Empty);
            GUI.Label(
                new Rect(30f, 28f, 280f, 132f),
                $"{phase}\nLap: {state.Lap}/1\nW: Drive   S: Brake / Reverse\nA/D: Steer\nFrame: {state.Frame}\nHash: {RaceStateHasher.Compute(state):X16}",
                CreateLabelStyle());
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
