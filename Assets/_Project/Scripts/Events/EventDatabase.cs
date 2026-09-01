using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Events
{
    public static class EventDatabase
    {
        private static Dictionary<string, EventDefinition> _definitionsById;

        private static void EnsureLoaded()
        {
            if (_definitionsById != null) return;

            _definitionsById = new Dictionary<string, EventDefinition>();
            var definitions = Resources.LoadAll<EventDefinition>("Events");

            foreach (var def in definitions)
            {
                if (_definitionsById.ContainsKey(def.Id))
                {
                    Debug.LogWarning($"EventDatabase: duplicate Id '{def.Id}' found on '{def.name}' — skipping.");
                    continue;
                }
                _definitionsById.Add(def.Id, def);
            }
        }

        public static EventDefinition GetById(string id)
        {
            EnsureLoaded();

            if (_definitionsById.TryGetValue(id, out var def))
                return def;

            Debug.LogError($"EventDatabase: no EventDefinition found with Id '{id}'.");
            return null;
        }

        public static List<EventDefinition> GetAll()
        {
            EnsureLoaded();
            return new List<EventDefinition>(_definitionsById.Values);
        }

        /// <summary>
        /// Clears the cache. Useful for editor tooling or tests that need a fresh reload.
        /// </summary>
        public static void ClearCache()
        {
            _definitionsById = null;
        }

    }
}
