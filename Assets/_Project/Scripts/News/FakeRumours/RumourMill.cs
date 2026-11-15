using System;
using System.Collections.Generic;
using System.Linq;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.News;
using UnityEngine;

namespace AntiqueTradingSimulator.Rumours
{
    /// <summary>
    /// A rumour reaches every trader in its spread week (NewsManager.PublishFabricated) and is
    /// debunked when its claimed week passes: price hangover, market skepticism, trace roll and an
    /// official debunk notice. Added to the NewsManager GameObject by NewsManager; saved inside NewsState.
    /// </summary>
    [DisallowMultipleComponent]
    public class RumourMill : MonoBehaviour
    {
        [Tooltip("Left empty: the asset in Data/Resources, or built-in defaults.")]
        [SerializeField] private FakeRumourSettings settings;

        private static RumourMill _instance;

        private NewsManager _news;
        private Core.TimeManager _time;
        private EconomyManager _economy;
        private NPCManager _npcs;
        private PlayerTrader _player;

        private readonly List<RumourRecord> _records = new List<RumourRecord>();
        private readonly List<DemandHangover> _hangovers = new List<DemandHangover>();
        private float _skepticism;

        /// <summary>The running game's RumourMill, or null outside a game.</summary>
        public static RumourMill Current => _instance;

        /// <summary>Market skepticism of the running game, 0 when there is no RumourMill.</summary>
        public static float CurrentSkepticism => _instance != null ? _instance._skepticism : 0f;

        public FakeRumourSettings Settings => settings != null ? settings : FakeRumourSettings.Active;

        /// <summary>0–MaxSkepticism. Lowers NPC trust in every rumour, real or fake.</summary>
        public float Skepticism => _skepticism;

        public IReadOnlyList<RumourRecord> Records => _records;

        public event Action<RumourRecord> OnRumourPlanted;
        public event Action<RumourRecord> OnRumourDebunked;

        // ---------------------------------------------------------------- references

        internal NewsManager News
        {
            get
            {
                if (_news == null) _news = GetComponent<NewsManager>();
                if (_news == null) _news = FindFirstObjectByType<NewsManager>();
                return _news;
            }
        }

        internal Core.TimeManager Clock => _time != null ? _time : (_time = FindFirstObjectByType<Core.TimeManager>());
        internal EconomyManager Economy => _economy != null ? _economy : (_economy = FindFirstObjectByType<EconomyManager>());
        internal NPCManager Npcs => _npcs != null ? _npcs : (_npcs = FindFirstObjectByType<NPCManager>());
        internal PlayerTrader Player => _player != null ? _player : (_player = FindFirstObjectByType<PlayerTrader>());

        private CompanyManager Company => Player != null ? Player.Company : null;
        internal int CurrentWeek => Clock != null ? Clock.CurrentWeek : 1;

        // ---------------------------------------------------------------- unity

        private void Awake()
        {
            _instance = this;
            FakeRumourSettings.SetActive(settings);
        }

        private void OnEnable()
        {
            if (Clock != null) Clock.OnWeekChanged += HandleWeekChanged;
        }

        private void OnDisable()
        {
            if (_time != null) _time.OnWeekChanged -= HandleWeekChanged;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        // ---------------------------------------------------------------- player: quote and plant

        /// <summary>The player's own Information Network level.</summary>
        public InfoAccessLevel PlayerLevel => Player != null ? Player.AccessLevel : InfoAccessLevel.LocalPress;

        /// <summary>Spend options for the modal's stepper, lowest first.</summary>
        public List<float> SpendOptions(RumourClaim claim)
        {
            var options = new List<float>();
            if (claim == null || claim.IsEmpty) return options;

            SpendRange(claim, PlayerLevel, out float min, out float max);
            var s = Settings;
            int steps = Mathf.Max(1, s.SpendSteps);

            for (int i = 0; i <= steps; i++)
            {
                float spend = RoundSpend(Mathf.Lerp(min, max, i / (float)steps), s.SpendRounding);
                spend = Mathf.Clamp(spend, min, max);
                if (options.Count == 0 || spend > options[options.Count - 1]) options.Add(spend);
            }

            return options;
        }

        /// <summary>Cost, credibility, reach and trace risk of a player rumour. Blocker says why it can't be planted.</summary>
        public RumourQuote Quote(RumourClaim claim, int spreadDelayWeeks, int claimLeadWeeks, float spend)
        {
            var s = Settings;
            int week = CurrentWeek;

            var quote = new RumourQuote
            {
                Claim = claim,
                PlantWeek = week,
                SpreadWeek = week + Mathf.Clamp(spreadDelayWeeks, s.SpreadDelayWeeks.x, s.SpreadDelayWeeks.y)
            };
            quote.ClaimWeek = quote.SpreadWeek + Mathf.Clamp(claimLeadWeeks, s.ClaimLeadWeeks.x, s.ClaimLeadWeeks.y);

            if (claim == null || claim.IsEmpty)
            {
                quote.Blocker = "Name at least one type, country or century.";
                return quote;
            }

            InfoAccessLevel level = PlayerLevel;
            Fill(quote, spend, s.CapFor(level), Antique.PlayerOwnerId, level);

            if (quote.Cap <= 0f)
                quote.Blocker = "Unlocked with Information Network level 2.";
            else if (claim.TotalValues > s.MaxTotalValues)
                quote.Blocker = $"A rumour can name at most {s.MaxTotalValues} things.";
            else if (ActiveCount(Antique.PlayerOwnerId) >= s.SlotsFor(level))
                quote.Blocker = $"No free rumour slot ({ActiveCount(Antique.PlayerOwnerId)} of {s.SlotsFor(level)} in use).";
            else if (Player == null || Player.Inventory == null || !Core.Money.Covers(Player.Inventory.Cash, quote.Spend))
                quote.Blocker = "Not enough cash.";
            else if (Clock != null && quote.ClaimWeek > Clock.CampaignLength)
                quote.Blocker = "The claimed date is after the end of the campaign.";

            return quote;
        }

        /// <summary>Charges the player and plants the rumour. False (nothing changes) if the quote is blocked.</summary>
        public bool TryPlant(RumourClaim claim, int spreadDelayWeeks, int claimLeadWeeks, float spend, out string error)
        {
            var quote = Quote(claim, spreadDelayWeeks, claimLeadWeeks, spend);
            if (!quote.CanPlant)
            {
                error = quote.Blocker;
                return false;
            }

            Player.Inventory.RemoveCash(quote.Spend, LedgerCategory.Rumour, $"Planted rumour: {claim.Summary()}");
            string name = Company != null && !string.IsNullOrWhiteSpace(Company.CompanyName) ? Company.CompanyName : Player.TraderName;
            Plant(quote, Antique.PlayerOwnerId, name);

            error = null;
            return true;
        }

        // ---------------------------------------------------------------- NPCs

        /// <summary>
        /// Whether this NPC may plant a rumour now: the global NPC limit, its own slot and its
        /// cooldown. Checked by NPCTrader before it spends an action slot looking for a plan.
        /// </summary>
        public bool CanPlant(NPCTrader npc, int week)
        {
            var s = Settings;
            if (npc == null || s.MaxActiveNpcRumours <= 0) return false;
            if (ActiveNpcCount() >= s.MaxActiveNpcRumours) return false;
            if (ActiveCount(npc.Id) > 0) return false;

            int lastPlant = LastPlantWeek(npc.Id);
            return lastPlant == int.MinValue || week - lastPlant >= s.NpcCooldownWeeks;
        }

        /// <summary>
        /// Charges the NPC, publishes its rumour and starts its exit. False (nothing changes) if the
        /// NPC can no longer plant or cannot pay.
        /// </summary>
        public bool PlantFor(NPCTrader npc, RumourQuote quote)
        {
            if (npc == null || quote == null || quote.Claim == null || quote.Claim.IsEmpty) return false;
            if (!CanPlant(npc, quote.PlantWeek) || !Core.Money.Covers(npc.Inventory.Cash, quote.Spend)) return false;

            npc.Inventory.RemoveCash(quote.Spend, LedgerCategory.Rumour, $"Planted rumour: {quote.Claim.Summary()}");

            var item = Plant(quote, npc.Id, npc.TraderName);
            if (item == null) return false;

            // Sells from the week after it spreads, once the believers' purchases have moved the price.
            npc.BeginRumourExit(quote.Effects, quote.SpreadWeek + 1, quote.ClaimWeek);
            return true;
        }

        // ---------------------------------------------------------------- shared with NpcRumourPlanner

        /// <summary>Fills cost, spend, fatigue, credibility, reach and trace risk for a quote with a claim and weeks set.</summary>
        internal void Fill(RumourQuote quote, float spend, float cap, string planterId, InfoAccessLevel level)
        {
            var s = Settings;
            var claim = quote.Claim;

            quote.Effects = claim.ToNewsData();

            var affected = claim.AffectedDefinitions();
            quote.AffectedCount = affected.Count;
            quote.ScopeValue = ScopeValue(affected);
            quote.ClaimWeight = s.ClaimWeight(claim);

            float costMultiplier = s.CostMultiplierFor(level);
            quote.LevelDiscount = 1f - costMultiplier;
            quote.CostScale = s.CostScale * quote.ScopeValue * quote.ClaimWeight * costMultiplier;

            SpendRange(claim, level, out quote.MinSpend, out quote.MaxSpend);

            quote.Spend = Mathf.Clamp(spend, quote.MinSpend, quote.MaxSpend);
            quote.Cap = cap;
            quote.Fatigue = FatigueFor(claim.ScopeKeys(), quote.PlantWeek);
            quote.Credibility = s.CredibilityFor(quote.Spend, quote.CostScale, cap, quote.Fatigue);
            quote.ExpectedReactors = ExpectedReactors(quote.Credibility, claim.Rising, planterId, quote.ClaimWeek - quote.SpreadWeek);
            quote.TraceChance = s.TraceChanceFor(quote.Spend, quote.CostScale,
                RecentCount(planterId, quote.PlantWeek, null), s.TraceMultiplierFor(level));
        }

        /// <summary>Publishes a filled quote as a fake rumour and records it. The caller has already charged the planter.</summary>
        internal NewsItem Plant(RumourQuote quote, string planterId, string planterName)
        {
            if (News == null)
            {
                Debug.LogError("RumourMill: no NewsManager — the rumour was not published.", this);
                return null;
            }

            var claim = quote.Claim;
            var effects = new List<NewsEventData>(quote.Effects);

            // Worded by the same calls the news UI makes for a genuine rumour, so a planted one
            // cannot be told apart by its phrasing, and later changes to NewsWording reach both.
            var item = new NewsItem(
                effects,
                NewsWording.Headline(effects, TextBudget.Normal),
                NewsWording.RumourBody(effects),
                NewsType.Fake,
                quote.Credibility,
                quote.SpreadWeek,
                quote.ClaimWeek,
                guaranteedDelivery: true)
            {
                PlantedBy = planterId
            };

            if (item.PlantedByPlayer)
                item.PlayerDeliveryWeek = quote.PlantWeek;
            else
                item.PlayerSuspectsFake = UnityEngine.Random.value < PlayerNewsReliability();

            var record = new RumourRecord
            {
                NewsId = item.Id,
                PlanterId = planterId,
                PlanterName = planterName,
                Claim = claim.Capture(),
                Title = NewsWording.Headline(effects, TextBudget.Normal),
                Scope = claim.Summary(),
                PlantWeek = quote.PlantWeek,
                SpreadWeek = quote.SpreadWeek,
                ClaimWeek = quote.ClaimWeek,
                Spend = quote.Spend,
                CostScale = quote.CostScale,
                Credibility = quote.Credibility,
                Bullish = claim.Rising,
                ScopeKeys = claim.ScopeKeys()
            };
            _records.Add(record);

            News.PublishFabricated(item, planterId, quote.SpreadWeek);

            Debug.Log($"RumourMill: {planterName} planted '{record.Title}' (credibility {record.Credibility:P0}, spend {record.Spend:F0}), " +
                      $"spreads week {record.SpreadWeek}, debunked week {record.ClaimWeek}.");

            OnRumourPlanted?.Invoke(record);
            return item;
        }

        /// <summary>Expected number of NPCs (except the planter) that act on a rumour of this credibility.</summary>
        internal float ExpectedReactors(float credibility, bool bullish, string planterId, int claimLeadWeeks)
        {
            if (Npcs == null) return 0f;

            float total = 0f;
            foreach (var npc in Npcs.NPCs)
            {
                if (npc == null || npc.Id == planterId) continue;

                var profile = npc.Profile;
                if (profile == null || (!bullish && profile.NeverSells)) continue;

                total += ActChance(profile, credibility, claimLeadWeeks);
            }

            return total;
        }

        /// <summary>Same rule as NPCTrader.ReceiveNews, times the share of reaction delays that land before the claimed date.</summary>
        private float ActChance(NpcBehaviorProfile profile, float credibility, int claimLeadWeeks)
        {
            float act = Mathf.Clamp01(profile.RumorTrust * (1f - _skepticism) * credibility * (0.5f + profile.RiskTolerance));

            if (profile.AccessLevel >= InfoAccessLevel.Expert)
                act *= 1f - Settings.ExpertDetection * (1f - credibility);

            int minDelay = Mathf.Min(profile.MinReactionDelayWeeks, profile.MaxReactionDelayWeeks);
            int maxDelay = Mathf.Max(profile.MinReactionDelayWeeks, profile.MaxReactionDelayWeeks);
            int inTime = Mathf.Clamp(claimLeadWeeks - minDelay, 0, maxDelay - minDelay + 1);

            return act * inTime / (maxDelay - minDelay + 1);
        }

        internal int ActiveCount(string planterId) =>
            _records.Count(r => !r.Debunked && r.PlanterId == planterId);

        internal int ActiveNpcCount() =>
            _records.Count(r => !r.Debunked && r.PlanterId != Antique.PlayerOwnerId);

        /// <summary>Latest plant week of this planter among kept records, or int.MinValue.</summary>
        internal int LastPlantWeek(string planterId)
        {
            int last = int.MinValue;
            foreach (var r in _records)
                if (r.PlanterId == planterId && r.PlantWeek > last) last = r.PlantWeek;
            return last;
        }

        /// <summary>Other fakes by the same planter within the recent window before <paramref name="week"/>.</summary>
        private int RecentCount(string planterId, int week, string excludeNewsId)
        {
            int window = Settings.RecentWindowWeeks;
            return _records.Count(r => r.PlanterId == planterId && r.NewsId != excludeNewsId &&
                                       r.PlantWeek <= week && r.PlantWeek > week - window);
        }

        private float FatigueFor(List<string> scopeKeys, int week)
        {
            var s = Settings;
            int earlier = _records.Count(r => r.PlantWeek <= week && r.PlantWeek > week - s.FatigueWindowWeeks &&
                                              r.ScopeKeys != null && r.ScopeKeys.Any(scopeKeys.Contains));
            return Mathf.Pow(s.FatigueMultiplier, earlier);
        }

        private float PlayerNewsReliability()
        {
            var upgrades = Company != null ? Company.Upgrades : null;
            return upgrades != null ? upgrades.GetStat(CompanyUpgradeType.Experts, UpgradeStat.NewsReliability, 0f) : 0f;
        }

        // ---------------------------------------------------------------- scope helpers

        internal void SpendRange(RumourClaim claim, InfoAccessLevel level, out float min, out float max)
        {
            var s = Settings;
            float value = ScopeValue(claim.AffectedDefinitions());
            float costScale = s.CostScale * value * s.ClaimWeight(claim) * s.CostMultiplierFor(level);

            min = Mathf.Max(Core.Money.OneShilling, RoundSpend(s.MinFeeShare * value, s.SpendRounding));
            max = Mathf.Max(min, RoundSpend(s.MaxSpendInK * costScale, s.SpendRounding));
        }

        /// <summary>Average current reference price of the given antique definitions.</summary>
        internal float ScopeValue(List<AntiqueDefinition> definitions)
        {
            var market = Economy != null ? Economy.Market : null;
            float sum = 0f;
            int count = 0;

            foreach (var definition in definitions)
            {
                if (definition == null) continue;

                var typeState = market != null ? market.GetTypeState(definition.Id) : null;
                sum += typeState != null ? PriceEngine.CalculateReferencePrice(definition.BasePrice, typeState) : definition.BasePrice;
                count++;
            }

            return count > 0 ? sum / count : 0f;
        }

        internal static List<AntiqueDefinition> DefinitionsInScope(NewsEventData data) => data.targetScope switch
        {
            EventEffect.TargetScope.AntiqueType => AntiqueDatabase.GetByType(data.AntiqueType),
            EventEffect.TargetScope.Country => AntiqueDatabase.GetByCountry(data.Country),
            EventEffect.TargetScope.Century => AntiqueDatabase.GetByCentury(data.Century),
            _ => new List<AntiqueDefinition>()
        };

        /// <summary>Rounds to <paramref name="step"/>, or to a round amount for the size (Core.Money.NiceStep) when 0.</summary>
        private static float RoundSpend(float value, float step) =>
            step > 0f ? Mathf.Round(value / step) * step : Core.Money.RoundToNice(value);

        // ---------------------------------------------------------------- weekly

        private void HandleWeekChanged(int week)
        {
            RevertHangovers(week);

            _skepticism = Mathf.Max(0f, _skepticism - Settings.SkepticismDecayPerWeek);

            foreach (var record in _records.ToList())
                if (!record.Debunked && week >= record.ClaimWeek)
                    Debunk(record, week);

            PruneRecords(week);
        }

        private void Debunk(RumourRecord record, int week)
        {
            var s = Settings;
            record.Debunked = true;

            var item = News != null ? News.FindById(record.NewsId) : null;
            if (item != null) item.Debunked = true;

            if (Npcs != null)
                foreach (var npc in Npcs.NPCs)
                    npc?.ForgetNews(record.NewsId);

            if (item?.NewsData != null)
                ApplyHangover(item.NewsData, record.Credibility, week);

            _skepticism = Mathf.Clamp(_skepticism + s.SkepticismPerDebunk * record.Credibility, 0f, s.MaxSkepticism);

            float traceChance = s.TraceChanceFor(record.Spend, record.CostScale, RecentCount(record.PlanterId, record.PlantWeek, record.NewsId));
            record.Traced = UnityEngine.Random.value < traceChance;
            if (record.Traced) ApplyTracePenalty(record, week);

            PublishDebunkNotice(record, week);

            Debug.Log($"RumourMill: '{record.Title}' debunked in week {week}" +
                      (record.Traced ? $", traced to {record.PlanterName}." : ", source unknown."));

            OnRumourDebunked?.Invoke(record);
        }

        private void ApplyHangover(List<NewsEventData> effects, float credibility, int week)
        {
            var s = Settings;
            var market = Economy != null ? Economy.Market : null;
            if (market == null || s.HangoverWeeks <= 0 || s.HangoverDemand <= 0f) return;

            var byDefinition = new Dictionary<string, float>();
            foreach (var data in effects)
            {
                if (data == null || data.IsTransportDisruption) continue;
                float amount = (data.affectsPriceUp ? -1f : 1f) * s.HangoverDemand * credibility;

                foreach (var definition in DefinitionsInScope(data))
                {
                    if (definition == null) continue;
                    byDefinition.TryGetValue(definition.Id, out float sum);
                    byDefinition[definition.Id] = sum + amount;
                }
            }

            foreach (var pair in byDefinition)
            {
                var typeState = market.GetTypeState(pair.Key);
                if (typeState == null || Mathf.Approximately(pair.Value, 0f)) continue;

                typeState.TempDemandMod += pair.Value;
                market.RecalculatePricesForDefinition(pair.Key);

                _hangovers.Add(new DemandHangover { DefinitionId = pair.Key, Amount = pair.Value, EndWeek = week + s.HangoverWeeks });
            }
        }

        private void RevertHangovers(int week)
        {
            var market = Economy != null ? Economy.Market : null;

            for (int i = _hangovers.Count - 1; i >= 0; i--)
            {
                var hangover = _hangovers[i];
                if (hangover.EndWeek > week) continue;

                var typeState = market != null ? market.GetTypeState(hangover.DefinitionId) : null;
                if (typeState != null)
                {
                    typeState.TempDemandMod -= hangover.Amount;
                    market.RecalculatePricesForDefinition(hangover.DefinitionId);
                }

                _hangovers.RemoveAt(i);
            }
        }

        private void ApplyTracePenalty(RumourRecord record, int week)
        {
            var s = Settings;
            string reason = $"Traced as the source of a fake rumour ({record.Title})";

            if (record.PlanterId == Antique.PlayerOwnerId)
            {
                if (Company == null) return;
                Company.AddCredibility(-s.TracedCredibilityLoss, reason);
                Company.AddReputation(-s.TracedReputationLoss, reason);
                return;
            }

            var npc = Npcs != null ? Npcs.GetById(record.PlanterId) : null;
            if (npc?.Record == null) return;

            npc.Record.Reputation.AddCredibility(-s.TracedCredibilityLoss, reason, week);
            npc.Record.Reputation.AddReputation(-s.TracedReputationLoss, reason, week);
        }

        /// <summary>Official notice with no market data: everyone receives it, NPCs have nothing to trade on.</summary>
        private void PublishDebunkNotice(RumourRecord record, int week)
        {
            if (News == null) return;

            string source = record.Traced
                ? (record.PlanterId == Antique.PlayerOwnerId
                    ? "Investigators traced it to your company."
                    : $"Investigators traced it to {record.PlanterName}.")
                : "Its source is unknown.";

            // The scope reads inside a sentence, where the headline would not.
            string scope = string.IsNullOrEmpty(record.Scope)
                ? (record.Claim != null ? record.Claim.Restore().Summary() : "the market")
                : record.Scope;

            var notice = new NewsItem(
                new List<NewsEventData>(),
                $"Rumour debunked: {scope}",
                $"The rumour about {scope} was false: its claimed date has passed and nothing happened. {source}",
                NewsType.Official,
                1f,
                week,
                week + Settings.DebunkNoticeWeeks,
                guaranteedDelivery: true)
            {
                DebunksNewsId = record.NewsId
            };

            News.PublishManual(notice);
        }

        /// <summary>Drops debunked records older than every window that reads them.</summary>
        private void PruneRecords(int week)
        {
            var s = Settings;
            int keep = Mathf.Max(s.FatigueWindowWeeks, Mathf.Max(s.RecentWindowWeeks, s.NpcCooldownWeeks));
            _records.RemoveAll(r => r.Debunked && r.PlantWeek < week - keep);
        }

        // ---------------------------------------------------------------- save / load

        public RumourState CaptureState()
        {
            var state = new RumourState { Skepticism = _skepticism };

            foreach (var record in _records)
                if (record != null) state.Rumours.Add(record.Copy());

            foreach (var hangover in _hangovers)
                if (hangover != null)
                    state.Hangovers.Add(new DemandHangover { DefinitionId = hangover.DefinitionId, Amount = hangover.Amount, EndWeek = hangover.EndWeek });

            return state;
        }

        /// <summary>Hangovers are not re-applied: the restored market already holds them.</summary>
        public void RestoreState(RumourState state)
        {
            _records.Clear();
            _hangovers.Clear();
            _skepticism = 0f;

            if (state == null) return;

            _skepticism = Mathf.Clamp(state.Skepticism, 0f, 1f);

            if (state.Rumours != null)
                foreach (var record in state.Rumours)
                    if (record != null && !string.IsNullOrEmpty(record.NewsId)) _records.Add(record.Copy());

            if (state.Hangovers != null)
                foreach (var hangover in state.Hangovers)
                    if (hangover != null && !string.IsNullOrEmpty(hangover.DefinitionId)) _hangovers.Add(hangover);
        }
    }
}
