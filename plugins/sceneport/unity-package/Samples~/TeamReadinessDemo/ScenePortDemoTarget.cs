using UnityEngine;

namespace ScenePort.Samples.TeamReadinessDemo
{
    public sealed class ScenePortDemoTarget : MonoBehaviour
    {
        [SerializeField] private string readinessLabel = "Ready for ScenePort";
        [SerializeField] private Color accentColor = Color.cyan;
        [SerializeField] private int interactionCount;

        public string ReadinessLabel => readinessLabel;
        public Color AccentColor => accentColor;
        public int InteractionCount => interactionCount;

        public void RecordInteraction()
        {
            interactionCount += 1;
        }
    }
}
