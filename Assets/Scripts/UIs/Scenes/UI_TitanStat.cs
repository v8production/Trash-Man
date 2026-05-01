using UnityEngine;
using UnityEngine.UI;

public class UI_TitanStat : UI_Scene
{
    enum GameObjects
    {
        HPBar,
        GaugeBar,
    }

    private TitanStat _stat;

    public override void Init()
    {
        base.Init();
        Bind<GameObject>(typeof(GameObjects));
    }

    public void SetStat(TitanStat stat)
    {
        _stat = stat;
    }

    void Update()
    {
        if (_stat == null)
            return;

        if (_stat.MaxHp > 0)
            SetRatio(GameObjects.HPBar, _stat.Hp / (float)_stat.MaxHp);

        if (_stat.MaxGauge > 0)
            SetRatio(GameObjects.GaugeBar, _stat.Gauge / (float)_stat.MaxGauge);
    }

    void SetRatio(GameObjects gameObject, float ratio)
    {
        GameObject target = GetObject((int)gameObject);

        Slider slider = target.GetComponent<Slider>();

        slider.value = ratio;
    }
}
