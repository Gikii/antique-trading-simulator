using AntiqueTradingSimulator.Dependency;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.News;
using System;
using System.Collections.Generic;
using UnityEngine;
using static AntiqueTradingSimulator.Events.EventEffect;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>Where a belief came from, which decides how much weight it carries.</summary>
    public enum BeliefSource
    {
        Official,
        Leak,
        Rumour,
        Fake
    }

    /// <summary>
    /// One thing a trader believes about one facet of the market: which way prices move, how big
    /// the move is believed to be, when it starts and when it is spent.
    /// </summary>
    public class Belief
    {
        public string NewsId;

        /// <summary>The event's name, which is what the magnitude memory is keyed on.</summary>
        public string OfficialName;

        /// <summary>Scope the news named: a type, a country or a century.</summary>
        public NewsEventData Facet;

        /// <summary>+1 for a rise, -1 for a fall. The only hard quantitative fact the news gives.</summary>
        public int Direction;

        /// <summary>This trader's own guess at the size of the move, as a fraction of price.</summary>
        public float EstimatedMove;

        /// <summary>How unsure the trader is of that guess, as a fraction of it.</summary>
        public float Spread;

        public int StartWeek;

        /// <summary>Week the move is spent. Read from a running event, estimated while announced.</summary>
        public int EndWeek;

        /// <summary>Credibility times the profile's trust in the source, scepticism included.</summary>
        public float Trust;

        public int HeardWeek;
        public BeliefSource Source;

        /// <summary>Set once the market has been checked against this belief at its start week.</summary>
        public bool Checked;

        /// <summary>Set once the realised move has been fed back into the magnitude memory.</summary>
        public bool Learned;

        /// <summary>Price of the facet when the belief was recorded, to measure the move against.</summary>
        public float PriceAtStart;
    }

    /// <summary>
    /// A trader's net view of one antique type over a horizon: how far it expects prices to move,
    /// how sure it is, and when the move peaks and fades.
    /// </summary>
    public readonly struct Outlook
    {
        /// <summary>Signed expected proportional change, e.g. 0.18 for +18%.</summary>
        public readonly float Expected;

        /// <summary>0..1, from corroboration, source quality and how tight the estimates are.</summary>
        public readonly float Confidence;

        public readonly int PeakWeek;
        public readonly int FadeWeek;

        /// <summary>Distinct news items pointing the way the outlook points.</summary>
        public readonly int Supporting;

        /// <summary>Distinct news items pointing the other way.</summary>
        public readonly int Opposing;

        public Outlook(float expected, float confidence, int peakWeek, int fadeWeek, int supporting, int opposing)
        {
            Expected = expected;
            Confidence = confidence;
            PeakWeek = peakWeek;
            FadeWeek = fadeWeek;
            Supporting = supporting;
            Opposing = opposing;
        }

        /// <summary>The part of the expectation the trader is willing to act on.</summary>
        public float Conviction => Expected * Confidence;

        public static Outlook None => new Outlook(0f, 0f, 0, 0, 0, 0);
    }

    /// <summary>
    /// Everything one trader knows about where prices are going, assembled into a single number
    /// per antique type. News, seasons, trends and the dependency graph all feed the same outlook,
    /// so corroborating items add up and contradicting ones cancel.
    /// </summary>
    public class TraderWorldView
    {
        private readonly string _ownerId;
        private readonly EconomyManager _economyManager;
        private readonly EventManager _eventManager;
        private readonly DependencyManager _dependencyManager;

        private readonly List<Belief> _beliefs = new List<Belief>();
        private readonly MagnitudeMemory _memory = new MagnitudeMemory();

        /// <summary>Trust left after a belief's predicted direction failed to appear.</summary>
        private const float WrongDirectionTrustFactor = 0.35f;

        /// <summary>How much of the dependency graph's influence a trader reads as an expectation.</summary>
        private const float DependencyWeight = 0.5f;

        public TraderWorldView(string ownerId, EconomyManager economyManager,
            EventManager eventManager = null, DependencyManager dependencyManager = null)
        {
            _ownerId = ownerId;
            _economyManager = economyManager;
            _eventManager = eventManager;
            _dependencyManager = dependencyManager;
        }

        public IReadOnlyList<Belief> Beliefs => _beliefs;
        public MagnitudeMemory Memory => _memory;

        // ---------------------------------------------------------------- taking news in

        /// <summary>
        /// Turns a believed news item into one belief per scope it names. The size of the move is
        /// the trader's own estimate, drawn once here and kept.
        /// </summary>
        public void Add(NewsItem news, int deliveryWeek, float trust, NpcBehaviorProfile profile)
        {
            if (news?.NewsData == null || news.NewsData.Count == 0) return;

            var estimate = _memory.For(news.OfficialName, profile);
            float noise = NoiseFor(news.Id, deliveryWeek);
            float move = Mathf.Max(0f, estimate.Mean * (1f + estimate.Spread * noise));

            int endWeek = news.EventTriggerWeek + MagnitudeMemory.AssumedEventWeeks;

            foreach (var data in news.NewsData)
            {
                if (data == null || data.targetScope == TargetScope.Other) continue;

                _beliefs.Add(new Belief
                {
                    NewsId = news.Id,
                    OfficialName = news.OfficialName,
                    Facet = data,
                    Direction = data.affectsPriceUp ? 1 : -1,
                    EstimatedMove = move,
                    Spread = estimate.Spread,
                    StartWeek = news.EventTriggerWeek,
                    EndWeek = endWeek,
                    Trust = Mathf.Clamp01(trust * news.Credibility),
                    HeardWeek = deliveryWeek,
                    Source = SourceOf(news.Type),
                    PriceAtStart = 0f
                });
            }
        }

        /// <summary>Drops every belief resting on a news item, for a debunked rumour.</summary>
        public int Forget(string newsId)
        {
            if (string.IsNullOrEmpty(newsId)) return 0;
            return _beliefs.RemoveAll(b => b.NewsId == newsId);
        }

        /// <summary>True while any belief still rests on that news item.</summary>
        public bool Holds(string newsId)
        {
            if (string.IsNullOrEmpty(newsId)) return false;

            for (int i = 0; i < _beliefs.Count; i++)
                if (_beliefs[i].NewsId == newsId) return true;

            return false;
        }

        // ---------------------------------------------------------------- the weekly pass

        /// <summary>
        /// Ages the beliefs: takes the real end week off a running event, checks predictions against
        /// what the market did, feeds the outcome into the magnitude memory and drops spent beliefs.
        /// </summary>
        public void AdvanceWeek(int week)
        {
            var market = _economyManager != null ? _economyManager.Market : null;

            for (int i = _beliefs.Count - 1; i >= 0; i--)
            {
                var belief = _beliefs[i];

                if (week < belief.StartWeek)
                {
                    if (belief.PriceAtStart <= 0f && market != null)
                        belief.PriceAtStart = FacetPrice(market, belief.Facet);
                    continue;
                }

                // A running event shows its last week, so the guessed duration is replaced by it.
                int knownEnd = RunningEventEnd(belief.OfficialName);
                if (knownEnd > 0) belief.EndWeek = knownEnd;

                float move = RealisedMove(market, belief);

                if (!belief.Checked)
                {
                    belief.Checked = true;
                    if (move * belief.Direction < 0f)
                        belief.Trust *= WrongDirectionTrustFactor;
                }

                if (week < belief.EndWeek) continue;

                if (!belief.Learned)
                {
                    belief.Learned = true;
                    _memory.Observe(belief.OfficialName, move);
                }

                _beliefs.RemoveAt(i);
            }
        }

        // ---------------------------------------------------------------- the outlook

        /// <summary>
        /// Net expected price change for one antique type over the horizon, from everything this
        /// trader believes. Beliefs in the same direction add, opposing ones cancel.
        /// </summary>
        public Outlook OutlookFor(string definitionId, int horizonWeeks, NpcBehaviorProfile profile)
        {
            if (string.IsNullOrEmpty(definitionId) || profile == null) return Outlook.None;

            int week = CurrentWeek;
            var definition = AntiqueDatabase.GetById(definitionId);

            float expected = 0f;
            float peakWeight = 0f;
            int peakWeek = week;
            int fadeWeek = 0;

            // Confidence is built per news item, so two beliefs from one item count once.
            float upMiss = 1f;
            float downMiss = 1f;
            int supporting = 0;
            int opposing = 0;
            string lastUpId = null;
            string lastDownId = null;

            if (definition != null)
            {
                for (int i = 0; i < _beliefs.Count; i++)
                {
                    var belief = _beliefs[i];
                    if (!Matches(definition, belief.Facet)) continue;

                    float weight = TimeWeight(week, horizonWeeks, belief);
                    if (weight <= 0f) continue;

                    expected += belief.Direction * belief.EstimatedMove * belief.Trust * weight;

                    if (weight > peakWeight)
                    {
                        peakWeight = weight;
                        peakWeek = Mathf.Max(week, belief.StartWeek);
                    }

                    if (belief.EndWeek > fadeWeek) fadeWeek = belief.EndWeek;

                    // A tight estimate counts for more than a vague one of the same trust.
                    float certainty = belief.Trust * (1f / (1f + Mathf.Max(0f, belief.Spread)));

                    if (belief.Direction > 0)
                    {
                        if (belief.NewsId != lastUpId) { supporting++; lastUpId = belief.NewsId; }
                        upMiss *= 1f - Mathf.Clamp01(certainty);
                    }
                    else
                    {
                        if (belief.NewsId != lastDownId) { opposing++; lastDownId = belief.NewsId; }
                        downMiss *= 1f - Mathf.Clamp01(certainty);
                    }
                }
            }

            expected += SeasonalOutlook(definitionId, profile);
            expected += TraderHelper.TrendOutlook(_economyManager?.Market, definitionId, profile);
            expected += DependencyOutlook(definitionId);

            float confidence = NewsConfidence(expected, upMiss, downMiss, supporting, opposing);
            if (supporting == 0 && opposing == 0) confidence = 1f;

            return new Outlook(expected, confidence, peakWeek, fadeWeek,
                expected >= 0f ? supporting : opposing,
                expected >= 0f ? opposing : supporting);
        }

        /// <summary>
        /// How sure the trader is: corroboration from independent items raises it, disagreement
        /// between them cuts it.
        /// </summary>
        private static float NewsConfidence(float expected, float upMiss, float downMiss, int supporting, int opposing)
        {
            float agreeing = expected >= 0f ? 1f - upMiss : 1f - downMiss;
            int forCount = expected >= 0f ? supporting : opposing;
            int againstCount = expected >= 0f ? opposing : supporting;

            if (forCount + againstCount == 0) return Mathf.Clamp01(agreeing);

            float lopsided = (forCount - againstCount) / (float)(forCount + againstCount);
            return Mathf.Clamp01(agreeing * Mathf.Max(0f, lopsided));
        }

        /// <summary>
        /// 0 before the event, 1 once it has started, falling back to 0 as it fades. A move that
        /// lands past the horizon is discounted rather than ignored.
        /// </summary>
        private static float TimeWeight(int week, int horizonWeeks, Belief belief)
        {
            if (belief.EndWeek > 0 && week >= belief.EndWeek) return 0f;

            int horizon = Mathf.Max(1, horizonWeeks);

            if (week < belief.StartWeek)
            {
                int wait = belief.StartWeek - week;
                return wait > horizon ? 0f : 1f - (wait - 1) / (float)horizon;
            }

            int span = Mathf.Max(1, belief.EndWeek - belief.StartWeek);
            return Mathf.Clamp01(1f - (week - belief.StartWeek) / (float)span);
        }

        // ---------------------------------------------------------------- belief sources

        /// <summary>The seasonal change this trader prices in, weighted by its foresight.</summary>
        private float SeasonalOutlook(string definitionId, NpcBehaviorProfile profile)
        {
            if (profile.SeasonalForesight <= 0f || profile.SeasonalHorizonWeeks <= 0 || _economyManager == null)
                return 0f;

            float ratio = _economyManager.SeasonalPriceOutlook(definitionId, profile.SeasonalHorizonWeeks);
            return profile.SeasonalForesight * (ratio - 1f);
        }

        /// <summary>What the dependency graph is currently doing to this type, read as an expectation.</summary>
        private float DependencyOutlook(string definitionId)
        {
            if (_dependencyManager == null) return 0f;

            var engine = _dependencyManager.Engine;
            if (engine == null) return 0f;

            return DependencyWeight * engine.CurrentInfluenceOn(definitionId);
        }

        // ---------------------------------------------------------------- helpers

        private int CurrentWeek => _economyManager != null && _economyManager.TimeManager != null
            ? _economyManager.TimeManager.CurrentWeek
            : 0;

        /// <summary>Last week of a running event by that name, or 0 when none is running.</summary>
        private int RunningEventEnd(string officialName)
        {
            if (_eventManager == null || string.IsNullOrEmpty(officialName)) return 0;

            var active = _eventManager.ActiveEvents;
            for (int i = 0; i < active.Count; i++)
            {
                var definition = active[i].Definition;
                if (definition != null && definition.DisplayName == officialName) return active[i].EndWeek;
            }

            return 0;
        }

        /// <summary>How far the belief's facet has moved since it was recorded.</summary>
        private static float RealisedMove(Market.Market market, Belief belief)
        {
            if (market == null || belief.PriceAtStart <= 0f) return 0f;

            float now = FacetPrice(market, belief.Facet);
            if (now <= 0f) return 0f;

            return (now - belief.PriceAtStart) / belief.PriceAtStart;
        }

        /// <summary>Mean reference price of the types a scope covers, as a yardstick for its moves.</summary>
        private static float FacetPrice(Market.Market market, NewsEventData facet)
        {
            if (market == null || facet == null) return 0f;

            var definitions = DefinitionsInScope(facet);
            if (definitions == null || definitions.Count == 0) return 0f;

            float total = 0f;
            int counted = 0;

            for (int i = 0; i < definitions.Count; i++)
            {
                var typeState = market.GetTypeState(definitions[i].Id);
                if (typeState == null) continue;

                total += PriceEngine.CalculateReferencePrice(definitions[i].BasePrice, typeState);
                counted++;
            }

            return counted > 0 ? total / counted : 0f;
        }

        private static List<AntiqueDefinition> DefinitionsInScope(NewsEventData facet) => facet.targetScope switch
        {
            TargetScope.AntiqueType => AntiqueDatabase.GetByType(facet.AntiqueType),
            TargetScope.Country => AntiqueDatabase.GetByCountry(facet.Country),
            TargetScope.Century => AntiqueDatabase.GetByCentury(facet.Century),
            _ => null
        };

        /// <summary>True when a scope the news named covers this antique type.</summary>
        public static bool Matches(AntiqueDefinition definition, NewsEventData facet)
        {
            if (definition == null || facet == null) return false;

            return facet.targetScope switch
            {
                TargetScope.AntiqueType => definition.Type == facet.AntiqueType,
                TargetScope.Country => definition.Country == facet.Country,
                TargetScope.Century => definition.Century == facet.Century,
                _ => false
            };
        }

        private static BeliefSource SourceOf(NewsType type) => type switch
        {
            NewsType.Official => BeliefSource.Official,
            NewsType.Leak => BeliefSource.Leak,
            NewsType.Fake => BeliefSource.Fake,
            _ => BeliefSource.Rumour
        };

        /// <summary>
        /// A -1..1 draw fixed by the trader, the news item and the week, so the estimate is the same
        /// however many items arrived first and in whatever order.
        /// </summary>
        private float NoiseFor(string newsId, int week)
        {
            unchecked
            {
                int seed = Fnv1a(_ownerId) ^ Fnv1a(newsId) ^ (week * 397);
                return (float)(new System.Random(seed).NextDouble() * 2.0 - 1.0);
            }
        }

        private static int Fnv1a(string text)
        {
            unchecked
            {
                int hash = (int)2166136261;
                if (text == null) return hash;

                for (int i = 0; i < text.Length; i++)
                    hash = (hash ^ text[i]) * 16777619;

                return hash;
            }
        }

        // ---------------------------------------------------------------- save / load

        public void Clear()
        {
            _beliefs.Clear();
            _memory.Clear();
        }

        public void AddRestored(Belief belief)
        {
            if (belief != null && belief.Facet != null) _beliefs.Add(belief);
        }
    }
}
