using System;
using System.Collections.Generic;
using UnityEngine;
using AntiqueTradingSimulator.Economy;
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

        [Header("Initial NPC population — one entry per NPC, referencing an NpcBehaviorProfile.Id")]
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

            SpawnInitialNPCs();
        }

        void OnEnable() { if (timeManager != null) timeManager.OnDayChanged += HandleDayChanged; }
        void OnDisable() { if (timeManager != null) timeManager.OnDayChanged -= HandleDayChanged; }

        private void SpawnInitialNPCs()
        {
            for (int i = 0; i < initialProfileIds.Count; i++)
                SpawnNPC($"Trader {i + 1}", initialProfileIds[i], defaultStartingCash);
        }

        private void HandleDayChanged(int newDay)
        {
            foreach (var npc in _npcs)
                npc.EvaluateDay(newDay);
        }

        public NPCTrader SpawnNPC(string traderName, string profileId, float? startingCash = null)
        {
            var npc = new NPCTrader(traderName, profileId, startingCash ?? defaultStartingCash, economyManager);
            RegisterNPC(npc);
            return npc;
        }

        public bool RemoveNPC(string npcId)
        {
            if (!_npcsById.TryGetValue(npcId, out var npc)) return false;

            _npcsById.Remove(npcId);
            _npcs.Remove(npc);
            newsManager?.UnregisterReceiver(npc);
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
            OnNPCAdded?.Invoke(npc);
        }
    }
}