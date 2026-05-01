using UnityEngine;

public class Stat : MonoBehaviour
{
    [SerializeField]
    protected int _hp;
    [SerializeField]
    protected int _maxHp;
    [SerializeField]
    protected int _attack;

    public int Hp { get { return _hp; } set { _hp = value; } }
    public int MaxHp { get { return _maxHp; } set { _maxHp = value; } }
    public int Attack { get { return _attack; } set { _attack = value; } }

    void Start()
    {
        _hp = 100;
        _maxHp = 100;
        _attack = 10;
    }

    public virtual void OnAttacked(Stat attacker)
    {
        Hp -= attacker.Attack;
        if (Hp <= 0)
        {
            OnDead(attacker);
        }
    }
    protected virtual void OnDead(Stat attacker)
    {

    }
}
