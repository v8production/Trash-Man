using UnityEngine;

public class IntroScene : BaseScene
{
    private const string LogoPath = "UIs/Logo";
    private const string NewGameButtonPath = "UIs/Buttons/NewGame";
    private const string JoinButtonPath = "UIs/Buttons/Join";
    private const string QuitButtonPath = "UIs/Buttons/Quit";
    private const string DiscordButtonPath = "UIs/Buttons/DiscordConnect";

    protected override void Init()
    {
        base.Init();
        SceneType = Define.Scene.Intro;
        LoadManagers();
        BuildIntroUI();
    }

    private static void LoadManagers()
    {
        _ = Managers.Input;
    }

    private static void BuildIntroUI()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
            return;

        EnsureUIElement(canvas.transform, LogoPath, "Logo", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 140f), new Vector2(960f, 540f));
        EnsureUIElement(canvas.transform, NewGameButtonPath, "NewGame Button", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -110f), new Vector2(330f, 80f));
        EnsureUIElement(canvas.transform, JoinButtonPath, "Join Button", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -220f), new Vector2(330f, 80f));
        EnsureUIElement(canvas.transform, QuitButtonPath, "Quit Button", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -330f), new Vector2(330f, 80f));
        EnsureUIElement(canvas.transform, DiscordButtonPath, "Discord Connect Button", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(250f, 64f));
    }

    private static void EnsureUIElement(Transform parent, string resourcePath, string objectName, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        Transform existing = parent.Find(objectName);
        GameObject go = existing != null ? existing.gameObject : Managers.Resource.Instantiate(resourcePath, parent);
        if (go == null)
            return;

        go.name = objectName;

        RectTransform rectTransform = go.GetorAddComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.pivot = pivot;
        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.sizeDelta = sizeDelta;
        rectTransform.localScale = Vector3.one;
    }


    public override void Clear()
    {
    }
}
