using UnityEngine;

[System.Serializable]
public struct TitanAggregatedInput
{
    public Vector2 MousePosition;
    public Vector2 MouseDelta;
    public bool RightMouseHeld;
    public bool RightMousePressedThisFrame;
    public bool RightMouseAttachBuffered;

    public float TorsoForward;
    public float TorsoStrafe;
    public float TorsoTurn;
    public float TorsoWaist;

    public float LeftArmElbow;
    public float RightArmElbow;
    public float LeftLegKnee;
    public float RightLegKnee;
    public float LeftLegAnkle;
    public float RightLegAnkle;
}
