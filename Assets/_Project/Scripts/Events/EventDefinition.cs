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

        [Tooltip("What this event does while active. Configure each effect's fields directly here.")]
        [SerializeReference]
        public List<EventEffect> Effects = new List<EventEffect>();
    }

}
