using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>
    /// Static lookup for all NpcBehaviorProfile assets, loaded once from
    /// Resources/NpcProfiles and cached by Id. Mirrors EventDatabase/AntiqueDatabase.
    /// </summary>
    public static class NpcProfileDatabase
    {
        private static Dictionary<string, NpcBehaviorProfile> _profilesById;

        private static void EnsureLoaded()
        {
            if (_profilesById != null) return;

            _profilesById = new Dictionary<string, NpcBehaviorProfile>();
            var profiles = Resources.LoadAll<NpcBehaviorProfile>("NpcProfiles");

            foreach (var profile in profiles)
            {
                if (_profilesById.ContainsKey(profile.Id))
                {
                    Debug.LogWarning($"NpcProfileDatabase: duplicate Id '{profile.Id}' on '{profile.name}' — skipping.");
                    continue;
                }
                _profilesById.Add(profile.Id, profile);
            }
        }

        public static NpcBehaviorProfile GetById(string id)
        {
            EnsureLoaded();
            if (_profilesById.TryGetValue(id, out var profile)) return profile;

            Debug.LogError($"NpcProfileDatabase: no NpcBehaviorProfile with Id '{id}'.");
            return null;
        }

        public static List<NpcBehaviorProfile> GetAll()
        {
            EnsureLoaded();
            return new List<NpcBehaviorProfile>(_profilesById.Values);
        }

        /// <summary>
        /// Swaps every loaded profile for an in-memory copy, so a session can change profile values
        /// (autoplay parameter overrides) without touching the assets — in the editor a change to the
        /// asset itself would outlive play mode. Copies keep their Id. Call before NPCs are created.
        /// </summary>
        public static List<NpcBehaviorProfile> UseRuntimeCopies()
        {
            EnsureLoaded();
            foreach (var id in new List<string>(_profilesById.Keys))
            {
                var original = _profilesById[id];
                var copy = Object.Instantiate(original);
                copy.name = original.name;
                _profilesById[id] = copy;
            }
            return new List<NpcBehaviorProfile>(_profilesById.Values);
        }

        public static void ClearCache() => _profilesById = null;
    }
}