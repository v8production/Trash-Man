public class Define
{
    public enum InputMode
    {
        Player,
        UI,
        Cinematic,
    }

    public enum MonsterAnimState
    {
        IDLE,
        RUN,
        ATTACK,
        HURT,
    }

    public enum Scene
    {
        Unknown,
        Intro,
        Lobby,
        Fusion,
        Game,

    }
    public enum Layer
    {
        Monster = 8,
        Ground = 9,
        Block = 10,

    }

    public enum Sound
    {
        Bgm,
        Effect,
        MaxCount,
    }

    public enum UIEvent
    {
        Click,
        Drag,
    }

    public enum MouseEvent
    {
        Move,
        Press,
        PointerDown,
        PointerUp,
        Click,
    }
    public enum CameraMode
    {
        QuarterView,
    }

    public static float epsilon = 1e-8f;

    public enum TitanRole
    {
        Body = 1,
        LeftArm = 2,
        RightArm = 3,
        LeftLeg = 4,
        RightLeg = 5
    }
}
