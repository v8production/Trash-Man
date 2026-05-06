using UnityEngine;

[System.Serializable]
public struct TitanAggregatedInput
{
    public Vector2 MousePosition;
    public Vector2 MouseDelta;
    public bool RightMouseHeld;
    public bool RightMousePressedThisFrame;
    public bool RightMouseDetachBuffered;

    public float BodyForward;
    public float BodyStrafe;
    public float BodyTurn;
    public float BodyWaist;

    public float LeftArmElbow;
    public float RightArmElbow;
    public float LeftLegKnee;
    public float RightLegKnee;
    public float LeftLegAnkle;
    public float RightLegAnkle;
}
