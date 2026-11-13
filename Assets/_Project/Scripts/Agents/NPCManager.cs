using System;
using System.Collections.Generic;
using UnityEngine;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.News;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>
    /// Owns the population of NPCTrader instances: spawns them from configured profile
    /// Ids, registers each with NewsManager so it can receive information, and drives
    /// their daily decision loop off TimeManager.OnWeekChanged.
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

        [Header("Reputation (taken from the player's CompanyManager if empty)")]
        [Tooltip("Leave empty so NPCs and the player earn and lose reputation by the same rules.")]
        [SerializeField] private ReputationSettings reputationSettings;

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
            if (reputationSettings == null)
            {
                var company = FindFirstObjectByType<CompanyManager>();
                if (company != null) reputationSettings = company.ReputationSettings;
            }

            SpawnInitialNPCs();
        }

        void OnEnable()
        {
            if (timeManager != null)
            {
                timeManager.OnWeekChanged += HandleWeekChanged;
                timeManager.OnTick += HandleTick;
            }
            if (contractManager != null) contractManager.OnContractExpired += HandleContractExpired;
        }

        void OnDisable()
        {
            if (timeManager != null)
            {
                timeManager.OnWeekChanged -= HandleWeekChanged;
                timeManager.OnTick -= HandleTick;
            }
            if (contractManager != null) contractManager.OnContractExpired -= HandleContractExpired;
        }

        // Day 1 gets its snapshot too (a loaded game replaces the history afterwards).
        void Start() => RecordSnapshots(CurrentWeek);

        private int CurrentWeek => timeManager != null ? timeManager.CurrentWeek : 1;

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

        private void HandleTick(int week, int tick)
        {
            // Seasonal activity (the London Season is busy, autumn is quiet). Set every tick so traders
            // spawned or restored mid-week pick it up too; NPCTrader reads it when planning its week.
            float activity = SeasonalActivity();

            for (int i = 0; i < _npcs.Count; i++)
            {
                if (_npcs[i] == null) continue;
                _npcs[i].ActivityScale = activity;
                _npcs[i].RunScheduledActions(week, tick);
            }
        }

        private float SeasonalActivity()
        {
            return economyManager != null && economyManager.TimeManager != null
                ? economyManager.CurrentMarketSeason.NpcActivity
                : 1f;
        }

        private void HandleWeekChanged(int newWeek)
        {
            foreach (var npc in _npcs)
                npc.FlushRemainingSlots(); ;

            // Yesterday's sales become reputation, then today's numbers are written down —
            // the same end-of-day steps the player's company takes.
            foreach (var npc in _npcs)
                npc.Record.FlushSales(newWeek - 1);
            RecordSnapshots(newWeek);
        }

        private void RecordSnapshots(int week)
        {
            var everyone = economyManager != null ? economyManager.Inventories : null;
            foreach (var npc in _npcs)
            {
                float share = TraderStats.MarketShare(npc.Inventory, everyone);
                npc.Record.Record(npc.Record.Capture(week, npc.Inventory, share));
            }
        }

        /// <summary>
        /// A contract ran out: every NPC that had taken it (claimed an exclusive one, or committed
        /// to an open one) failed it. Its reserved antiques are freed on its next day, as before.
        /// </summary>
        private void HandleContractExpired(Contract contract)
        {
            if (contract == null) return;

            foreach (var npc in _npcs)
            {
                bool took = contract.ClaimedByTraderId == npc.Id || npc.Inventory.IsCommittedToContract(contract.ContractId);
                if (took) npc.Record.ContractFailed(contract, CurrentWeek);
            }
        }

        public NPCTrader SpawnNPC(string traderName, string profileId, float? startingCash = null)
        {
            var npc = new NPCTrader(traderName, profileId, startingCash ?? defaultStartingCash, economyManager, contractManager, transportManager, reputationSettings);
            RegisterNPC(npc);
            Debug.Log("Created NPC Trader " + traderName);
            return npc;
        }

        public bool RemoveNPC(string npcId)
        {
            if (!_npcsById.TryGetValue(npcId, out var npc)) return false;

            _npcsById.Remove(npcId);
            _npcs.Remove(npc);
            npc.Detach();
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
                    economyManager, contractManager, transportManager, reputationSettings);

                // Before RegisterNPC, which keys everything on the Id this restores.
                npc.RestoreState(npcState);

                if (_npcsById.ContainsKey(npc.Id))
                {
                    npc.Detach();
                    Debug.LogWarning($"NPCManager: duplicate NPC Id '{npc.Id}' in the save — keeping the first one.");
                    continue;
                }

                RegisterNPC(npc);
            }

            Debug.Log($"NPCManager: restored {_npcs.Count} NPC trader(s) from the save.");
        }

    }
}