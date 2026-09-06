using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Serialization;

namespace AntiqueTradingSimulator.Events
{

    [CreateAssetMenu(fileName = "NewEvent", menuName = "AntiqueTradingSimulator/Event Definition")]
    public class EventDefinition : ScriptableObject
    {
        [FormerlySerializedAs("Id")]
        [ReadOnly, SerializeField] private string id = Guid.NewGuid().ToString("N");
        public string Id => id;
        public string DisplayName;

        [TextArea]
        public string Description;

        [Tooltip("How many days this event's temporary effects stay active once triggered.")]
        public int DurationDays = 5;
        [Tooltip("Minimum number of days from 'today' this event can be scheduled to trigger.")]
        public int MinLeadDays = 1;
        [Tooltip("Maximum number of days from 'today' this event can be scheduled to trigger.")]
        public int MaxLeadDays = 5;

        [Header("News generation")]
        public bool CanBeFakeNews = true;

        public bool CreateOfficialNews = true;
        [Range(0f, 1f)] public float OfficialCredibility = 1f;

        public bool CreateRumour = false;
        [Range(0f, 1f)] public float RumorCredibility = 0.5f;

        public bool CreateLeak = false;
        [Range(0, 5)] public int LeakDaysBefore = 0;
        [Range(0f, 1f)] public float LeakCredibility = 0.9f;

        [Tooltip("Effects of the event.")]
        [SerializeReference]
        public List<EventEffect> Effects = new List<EventEffect>();
    }
}
