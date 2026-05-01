using UnityEngine;

public class TitanStat : Stat
{
    [SerializeField]
    protected int _gauge;
    [SerializeField]
    protected int _maxGauge;

    public int Gauge { get { return _gauge; } set { _gauge = value; } }
    public int MaxGauge { get { return _maxGauge; } set { _maxGauge = value; } }

    void Start()
    {
        _hp = 100;
        _maxHp = 100;
        _attack = 10;
        _gauge = 100;
        _maxGauge = 100;
    }

    protected override void OnDead(Stat attacker)
    {
        Debug.Log("Player Dead");
        base.OnDead(attacker);
    }
}