using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Titan
{
    [System.Serializable]
    public struct TitanAggregatedInput
    {
        public Vector2 MousePosition;

        public float BodyForward;
        public float BodyStrafe;
        public float BodyTurn;
        public float BodyWaist;

        public float LeftArmElbow;
        public float RightArmElbow;
        public float LeftLegKnee;
        public float RightLegKnee;
    }

    public class TitanInputAggregationManager : MonoBehaviour
    {
        [SerializeField] private bool autoCaptureInUpdate = true;

        private TitanAggregatedInput current;

        public TitanAggregatedInput Current => current;

        private void Update()
        {
            if (!autoCaptureInUpdate)
            {
                return;
            }

            Capture();
        }

        public void Capture()
        {
            current.MousePosition = TitanInputUtility.ReadMousePosition();

            current.BodyForward = TitanInputUtility.GetAxis(
                KeyCode.UpArrow,
                KeyCode.DownArrow,
                Key.UpArrow,
                Key.DownArrow);

            current.BodyStrafe = TitanInputUtility.GetAxis(
                KeyCode.RightArrow,
                KeyCode.LeftArrow,
                Key.RightArrow,
                Key.LeftArrow);

            current.BodyTurn = TitanInputUtility.GetAxis(
                KeyCode.Period,
                KeyCode.Comma,
                Key.Period,
                Key.Comma);

            current.BodyWaist = TitanInputUtility.GetAxis(
                KeyCode.D,
                KeyCode.A,
                Key.D,
                Key.A);

            float ws = TitanInputUtility.GetAxis(KeyCode.W, KeyCode.S, Key.W, Key.S);
            current.LeftArmElbow = ws;
            current.RightArmElbow = ws;
            current.LeftLegKnee = ws;
            current.RightLegKnee = ws;
        }
    }
}
