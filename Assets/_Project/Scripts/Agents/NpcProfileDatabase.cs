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

        public static void ClearCache() => _profilesById = null;
    }
}