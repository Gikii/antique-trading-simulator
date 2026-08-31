using System;
using System.Collections.Generic;
using UnityEngine;
using AntiqueTradingSimulator.Economy;

namespace AntiqueTradingSimulator.Agents
{
    public class NPCManager : MonoBehaviour
    {
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private Core.TimeManager timeManager;

        [Header("Initial NPC population")]
        [SerializeField] private int initialNpcCount = 3;
        [SerializeField] private float defaultStartingCash = 2000f;

        private readonly List<NPCTrader> _npcs = new List<NPCTrader>();
        private readonly Dictionary<string, NPCTrader> _npcsById = new Dictionary<string, NPCTrader>();

        public IReadOnlyList<NPCTrader> NPCs => _npcs;

        public event Action<NPCTrader> OnNPCAdded;
        public event Action<NPCTrader> OnNPCRemoved;

        void Awake()
        {
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<Core.TimeManager>();

            SpawnInitialNPCs();
        }

        void OnEnable()
        {
            if (timeManager != null)
                timeManager.OnDayChanged += HandleDayChanged;
        }

        void OnDisable()
        {
            if (timeManager != null)
                timeManager.OnDayChanged -= HandleDayChanged;
        }

        private void SpawnInitialNPCs()
        {
            for (int i = 0; i < initialNpcCount; i++)
            {
                string name = $"Trader {i + 1}";

                SpawnNPC(name, defaultStartingCash);
            }
        }

        private void HandleDayChanged(int newDay)
        {
            if (economyManager == null || economyManager.Market == null) return;

            foreach (var npc in _npcs)
                npc.DecideTrade(economyManager.Market);
        }


        public NPCTrader SpawnNPC(string traderName, float? startingCash = null)
        {
            var npc = new NPCTrader(traderName, startingCash ?? defaultStartingCash, economyManager);
            RegisterNPC(npc);
            return npc;
        }

        public bool RemoveNPC(string npcId)
        {
            if (!_npcsById.TryGetValue(npcId, out var npc)) return false;

            _npcsById.Remove(npcId);
            _npcs.Remove(npc);
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
            OnNPCAdded?.Invoke(npc);
        }
    }
}