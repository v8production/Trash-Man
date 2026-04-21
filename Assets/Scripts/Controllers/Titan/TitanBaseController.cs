using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

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

public abstract class TitanBaseController : MonoBehaviour
{
    private static float s_nextInputLogTime;
    private const float InputLogIntervalSeconds = 0.20f;

    private static void MaybeLogInput(in TitanAggregatedInput input, string source)
    {
        if (!InputDebug.Enabled)
            return;

        if (Time.unscaledTime < s_nextInputLogTime)
            return;

        bool hasWs = Mathf.Abs(input.LeftArmElbow) > 0.001f;
        bool hasWaist = Mathf.Abs(input.BodyWaist) > 0.001f;
        bool hasArrows = Mathf.Abs(input.BodyForward) > 0.001f || Mathf.Abs(input.BodyStrafe) > 0.001f;
        bool hasTurn = Mathf.Abs(input.BodyTurn) > 0.001f;

        if (!hasWs && !hasWaist && !hasArrows && !hasTurn)
            return;

        s_nextInputLogTime = Time.unscaledTime + InputLogIntervalSeconds;
        InputDebug.Log($"{source} arrows(fwd={input.BodyForward}, strafe={input.BodyStrafe}) turn={input.BodyTurn} waist(A/D)={input.BodyWaist} ws(W/S)={input.LeftArmElbow}");
    }
    protected TitanRigManager rigManager => Managers.TitanRig;

    private static TitanAggregatedInput sharedInput;
    private static bool hasSharedInput;

    protected virtual void Awake()
    {
        Managers.TitanRig.EnsureBoundTo(gameObject);
    }

    protected TitanAggregatedInput GetInputSnapshot(bool autoCaptureIfEmpty = true)
    {
        if (autoCaptureIfEmpty && !hasSharedInput)
            CaptureSharedInput();

        return sharedInput;
    }

    public static void SetSharedInput(TitanAggregatedInput value)
    {
        sharedInput = value;
        hasSharedInput = true;
    }

    public static TitanAggregatedInput CaptureCurrentInputSnapshot(bool updateShared = true)
    {
        TitanAggregatedInput input = default;

        input.MousePosition = TitanInputUtility.ReadMousePosition();

        input.BodyForward = TitanInputUtility.GetAxis(
            KeyCode.UpArrow,
            KeyCode.DownArrow,
            Key.UpArrow,
            Key.DownArrow);

        input.BodyStrafe = TitanInputUtility.GetAxis(
            KeyCode.RightArrow,
            KeyCode.LeftArrow,
            Key.RightArrow,
            Key.LeftArrow);

        input.BodyTurn = TitanInputUtility.GetAxis(
            KeyCode.Period,
            KeyCode.Comma,
            Key.Period,
            Key.Comma);

        input.BodyWaist = TitanInputUtility.GetAxis(
            KeyCode.D,
            KeyCode.A,
            Key.D,
            Key.A);

        float ws = TitanInputUtility.GetAxis(KeyCode.W, KeyCode.S, Key.W, Key.S);
        input.LeftArmElbow = ws;
        input.RightArmElbow = ws;
        input.LeftLegKnee = ws;
        input.RightLegKnee = ws;

        if (updateShared)
        {
            sharedInput = input;
            hasSharedInput = true;
        }

        MaybeLogInput(input, updateShared ? "CaptureCurrentInputSnapshot(shared)" : "CaptureCurrentInputSnapshot");

        return input;
    }

    public static void CaptureSharedInput()
    {
        CaptureCurrentInputSnapshot(updateShared: true);
    }

    public abstract Define.TitanRole Role { get; }
    public abstract void TickRoleInput(float deltaTime);
}
