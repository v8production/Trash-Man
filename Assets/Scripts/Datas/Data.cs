using System;
using System.Collections.Generic;
using UnityEngine;

public interface ILoader<Key, Value>
{
    Dictionary<Key, Value> MakeDict();
}

namespace Data
{
    [Serializable]
    public class Stat
    {
        public string title;
        public string subtitle;
        public int maxHp;
        public int currentHp;
        public int gauge;
    }

    [Serializable]
    public class StatData : ILoader<string, Stat>
    {
        public List<Stat> stats = new();

        public Dictionary<string, Stat> MakeDict()
        {
            Dictionary<string, Stat> dict = new();
            foreach (Stat stat in stats)
                dict.Add(stat.title, stat);
            return dict;
        }
    }
}
