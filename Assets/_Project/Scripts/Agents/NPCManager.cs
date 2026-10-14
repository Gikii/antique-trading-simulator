using System;
using System.Collections.Generic;
using UnityEngine;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.News;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>
    /// Owns the population of NPCTrader instances: spawns them from configured profile
    /// Ids, registers each with NewsManager so it can receive information, and drives
    /// their daily decision loop off TimeManager.OnDayChanged.
    /// </summary>
    public class NPCManager : MonoBehaviour
    {
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private Core.TimeManager timeManager;
        [SerializeField] private NewsManager newsManager;
        [SerializeField] private ContractManager contractManager;
        [SerializeField] private TransportManager transportManager;

        [Header("Initial NPC population. One entry per NPC, referencing an NpcBehaviorProfile.Id")]
        [SerializeField] private List<string> initialProfileIds = new();
        [SerializeField] private float defaultStartingCash = 2000f;

        private readonly List<NPCTrader> _npcs = new();
        private readonly Dictionary<string, NPCTrader> _npcsById = new();

        public IReadOnlyList<NPCTrader> NPCs => _npcs;
        public event Action<NPCTrader> OnNPCAdded;
        public event Action<NPCTrader> OnNPCRemoved;

        void Awake()
        {
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<Core.TimeManager>();
            if (newsManager == null) newsManager = FindFirstObjectByType<NewsManager>();
            if (contractManager == null) contractManager = FindFirstObjectByType<ContractManager>();
            if (transportManager == null) transportManager = FindFirstObjectByType<TransportManager>();

            SpawnInitialNPCs();
        }

        void OnEnable() { if (timeManager != null) timeManager.OnDayChanged += HandleDayChanged; }
        void OnDisable() { if (timeManager != null) timeManager.OnDayChanged -= HandleDayChanged; }

        /// <summary>
        /// One NPC per entry of initialProfileIds. Name and starting cash come from the profile
        /// (TraderNames, StartingCash), falling back to "Trader N" and defaultStartingCash.
        /// </summary>
        private void SpawnInitialNPCs()
        {
            var usesPerProfile = new Dictionary<string, int>();

            for (int i = 0; i < initialProfileIds.Count; i++)
            {
                string profileId = initialProfileIds[i];
                var profile = NpcProfileDatabase.GetById(profileId);

                usesPerProfile.TryGetValue(profileId, out int used);
                usesPerProfile[profileId] = used + 1;

                float cash = profile != null && profile.StartingCash > 0f ? profile.StartingCash : defaultStartingCash;
                SpawnNPC(NameFor(profile, used, i), profileId, cash);
            }
        }

        private static string NameFor(NpcBehaviorProfile profile, int used, int index)
        {
            if (profile == null)
                return $"Trader {index + 1}";

            if (profile.TraderNames != null && used < profile.TraderNames.Count && !string.IsNullOrWhiteSpace(profile.TraderNames[used]))
                return profile.TraderNames[used];

            string baseName = string.IsNullOrWhiteSpace(profile.ProfileName) ? "Trader" : profile.ProfileName;
            return $"{baseName} {used + 1}";
        }

        private void HandleDayChanged(int newDay)
        {
            foreach (var npc in _npcs)
                npc.EvaluateDay(newDay);
        }

        public NPCTrader SpawnNPC(string traderName, string profileId, float? startingCash = null)
        {
            var npc = new NPCTrader(traderName, profileId, startingCash ?? defaultStartingCash, economyManager, contractManager, transportManager);
            RegisterNPC(npc);
            Debug.Log("Created NPC Trader " + traderName);
            return npc;
        }

        public bool RemoveNPC(string npcId)
        {
            if (!_npcsById.TryGetValue(npcId, out var npc)) return false;

            _npcsById.Remove(npcId);
            _npcs.Remove(npc);
            newsManager?.UnregisterReceiver(npc);
            contractManager?.UnregisterTrader(npcId);
            if (economyManager != null) economyManager.UnregisterInventory(npc.Inventory);
            OnNPCRemoved?.Invoke(npc);
            return true;
        }

        public NPCTrader GetById(string npcId)
        {
            _npcsById.TryGetValue(npcId, out var npc);
            return npc;
        }

        private void RegisterNPC(NPCTrader npc)
        {
            _npcs.Add(npc);
            _npcsById[npc.Id] = npc;
            newsManager?.RegisterReceiver(npc);
            contractManager?.RegisterTrader(npc.Id, npc.Inventory);
            if (economyManager != null) economyManager.RegisterInventory(npc.Inventory, npc.Id);
            OnNPCAdded?.Invoke(npc);
        }

        // ---------------------------------------------------------------- save / load

        public NPCManagerState CaptureState()
        {
            var state = new NPCManagerState();

            foreach (var npc in _npcs)
            {
                if (npc == null) continue;
                state.Npcs.Add(npc.CaptureState());
            }

            return state;
        }

        public void RestoreState(NPCManagerState state)
        {
            if (state == null) return;

            foreach (var npcId in new List<string>(_npcsById.Keys))
                RemoveNPC(npcId);

            if (state.Npcs == null) return;

            foreach (var npcState in state.Npcs)
            {
                if (npcState == null) continue;

                // Starting cash is set to 0. Restoring inventory sets it to the saved value.
                var npc = new NPCTrader(npcState.TraderName, npcState.ProfileId, 0f,
                    economyManager, contractManager, transportManager);

                // Before RegisterNPC, which keys everything on the Id this restores.
                npc.RestoreState(npcState);

                if (_npcsById.ContainsKey(npc.Id))
                {
                    Debug.LogWarning($"NPCManager: duplicate NPC Id '{npc.Id}' in the save — keeping the first one.");
                    continue;
                }

                RegisterNPC(npc);
            }

            Debug.Log($"NPCManager: restored {_npcs.Count} NPC trader(s) from the save.");
        }

    }
}