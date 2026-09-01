using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace AntiqueTradingSimulator.Events
{
    public class EventDefinition : ScriptableObject
    {
        [Header("News generation")]
        public bool GeneratesOfficialNews = true;
        [Range(0f, 1f)] public float OfficialCredibility = 1f;

        public bool CanSpawnAsRumorOnly = false;
        [Range(0f, 1f)] public float RumorCredibility = 0.5f;

        public bool CanLeakEarly = false;
        [Range(0, 5)] public int LeakDaysBefore = 0;
        [Range(0f, 1f)] public float LeakCredibility = 0.9f;
    }
}
