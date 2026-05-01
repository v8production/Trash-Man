using UnityEngine;

public class BossStat : Stat
{

    void Start()
    {
        _hp = 100;
        _maxHp = 100;
        _attack = 10;
    }

    protected override void OnDead(Stat attacker)
    {
        Debug.Log("Grolar Dead");
        base.OnDead(attacker);
    }
}