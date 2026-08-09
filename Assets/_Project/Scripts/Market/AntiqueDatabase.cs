using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Market
{
    /// <summary>
    /// Loads and caches all AntiqueDefinition assets from Resources/Antiques/,
    /// and provides lookup by Id. Acts as the single source of truth for
    /// static antique data at runtime.
    /// </summary>
    public static class AntiqueDatabase
    {
        private static Dictionary<string, AntiqueDefinition> _definitionsById;

        private static void EnsureLoaded()
        {
            if (_definitionsById != null) return;

            _definitionsById = new Dictionary<string, AntiqueDefinition>();
            var definitions = Resources.LoadAll<AntiqueDefinition>("Antiques");

            foreach (var def in definitions)
            {
                if (_definitionsById.ContainsKey(def.Id))
                {
                    Debug.LogWarning($"AntiqueDatabase: duplicate Id '{def.Id}' found on '{def.name}' — skipping.");
                    continue;
                }
                _definitionsById.Add(def.Id, def);
            }
        }

        public static AntiqueDefinition GetById(string id)
        {
            EnsureLoaded();

            if (_definitionsById.TryGetValue(id, out var def))
                return def;

            Debug.LogError($"AntiqueDatabase: no AntiqueDefinition found with Id '{id}'.");
            return null;
        }

        public static List<AntiqueDefinition> GetAll()
        {
            EnsureLoaded();
            return new List<AntiqueDefinition>(_definitionsById.Values);
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