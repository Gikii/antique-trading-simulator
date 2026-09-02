using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Events
{

    [CreateAssetMenu(fileName = "NewEvent", menuName = "AntiqueTradingSimulator/Event Definition")]
    public class EventDefinition : ScriptableObject
    {
        public string Id;
        public string DisplayName;

        [TextArea]
        public string Description;

        [Tooltip("How many days this event's effects stay active once triggered.")]
        public int DurationDays = 5;
        [Tooltip("Minimum number of days from 'now' this event can be scheduled to trigger.")]
        public int MinLeadDays = 1;
        [Tooltip("Maximum number of days from 'now' this event can be scheduled to trigger.")]
        public int MaxLeadDays = 5;

        [Header("News generation")]
        public bool GeneratesOfficialNews = true;
        [Range(0f, 1f)] public float OfficialCredibility = 1f;

        public bool CanSpawnAsRumorOnly = false;
        [Range(0f, 1f)] public float RumorCredibility = 0.5f;

        public bool CanLeakEarly = false;
        [Range(0, 5)] public int LeakDaysBefore = 0;
        [Range(0f, 1f)] public float LeakCredibility = 0.9f;

        [Tooltip("What this event does while active. Configure each effect's fields directly here.")]
        [SerializeReference]
        public List<EventEffect> Effects = new List<EventEffect>();
    }
}
