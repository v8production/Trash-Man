using UnityEngine;
using UnityEngine.UI;

public class UI_Boss : UI_Scene
{
    enum GameObjects
    {
        HPBar,
    }

    private BossStat _stat;

    public override void Init()
    {
        base.Init();
        Bind<GameObject>(typeof(GameObjects));
    }

    public void SetStat(BossStat stat)
    {
        _stat = stat;
    }

    void Update()
    {
        if (_stat == null || _stat.MaxHp <= 0)
            return;

        SetHpRatio(_stat.Hp / (float)_stat.MaxHp);
    }

    void SetHpRatio(float ratio)
    {
        GameObject target = GetObject((int)GameObjects.HPBar);
        if (target == null)
            return;

        Slider slider = target.GetComponent<Slider>();
        if (slider == null)
            return;

        slider.value = ratio;
    }
}
