using UnityEngine;

[System.Serializable]
public struct TitanAggregatedInput
{
    public Vector2 MousePosition;
    public Vector2 MouseDelta;

    public float BodyForward;
    public float BodyStrafe;
    public float BodyTurn;
    public float BodyWaist;

    public float LeftArmElbow;
    public float RightArmElbow;
    public float LeftLegKnee;
    public float RightLegKnee;
}
