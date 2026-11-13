using System;
using System.Collections.Generic;
using System.ComponentModel;
using AntiqueTradingSimulator.Seasons;
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

        [Tooltip("Type of event.")]
        public EventType EventType = EventType.Minor;

        [TextArea]
        public string Description;

        [Tooltip("How many weeks this event's temporary effects stay active once triggered.")]
        public int DurationDays = 5;
        [Tooltip("Minimum number of weeks from 'now' this event can be scheduled to trigger.")]
        public int MinLeadDays = 1;
        [Tooltip("Maximum number of weeks from 'now' this event can be scheduled to trigger.")]
        public int MaxLeadDays = 5;

        [Tooltip("Whether or not the player can select when exactly the event will trigger between min/max lead weeks." +
            "If false then the event's trigger date will be randomised within the bounds of min/max lead days")]
        public bool PlayerSelectsLeadTime = true;


        [Header("Annual event (fixed date)")]
        [Tooltip("Happens every year in the week of the date below — always, never at random. Annual events are " +
                 "left out of the random Minor pool and scheduled Max Lead weeks ahead, so their news still comes out.")]
        public bool Annual;
        [Range(1, 12)] public int AnnualMonth = 12;
        [Range(1, 31)] public int AnnualDay = 1;

        [Tooltip("Seasons in which this event may trigger (checked for random Minor events against the trigger week). " +
                 "Everything = any time of year.")]
        public SeasonMask AllowedSeasons = SeasonMask.All;

        [Tooltip("Chance the event fails to occur")]
        [Range(0f, 1f)] public float FailureChance = 0f;

        [Header("News generation")]
        public bool CanBeFakeNews = true;

        public bool CreateOfficialNews = true;
        [Range(0, 5)] public int OfficialAnnouncementDaysBefore = 1;

        public bool CreateRumour = false;
        [Range(0f, 1f)] public float RumorCredibility = 0.5f;

        public bool CreateLeak = false;
        [Range(0f, 1f)] public float LeakCredibility = 0.9f;

        [Tooltip("Effects of the event.")]
        [SerializeReference]
        public List<EventEffect> Effects = new List<EventEffect>();
    }
}
