using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

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

        public static List<AntiqueDefinition> GetByType(AntiqueType type)
        {
            EnsureLoaded();
            return _definitionsById.Values.Where(def => def.Type == type).ToList();
        }

        public static List<AntiqueDefinition> GetByTimePeriod(TimePeriod period)
        {
            EnsureLoaded();
            return _definitionsById.Values.Where(def => def.TimePeriod == period).ToList();
        }

        public static List<AntiqueDefinition> GetByCountry(Country country)
        {
            EnsureLoaded();
            return _definitionsById.Values.Where(def => def.Country == country).ToList();
        }

        public static List<AntiqueType> GetAvailableTypes()
        {
            EnsureLoaded();
            return _definitionsById.Values.Select(def => def.Type).Distinct().OrderBy(t => t.ToString()).ToList();
        }

        public static List<TimePeriod> GetAvailableTimePeriods()
        {
            EnsureLoaded();
            return _definitionsById.Values.Select(def => def.TimePeriod).Distinct().OrderBy(p => (int)p).ToList();
        }

        public static List<Country> GetAvailableCountries()
        {
            EnsureLoaded();
            return _definitionsById.Values.Select(def => def.Country).Distinct().OrderBy(c => c.ToString()).ToList();
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