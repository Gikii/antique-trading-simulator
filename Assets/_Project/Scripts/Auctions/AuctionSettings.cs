using AntiqueTradingSimulator.Logistics;
using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Auctions
{
    /// <summary>
    /// Tunable numbers for auctions: the schedule, what gets put up for sale and for how much,
    /// fees, the pace of live bidding and how a result feeds back into the market.
    /// If none is assigned to AuctionManager, an in-memory default is used.
    /// </summary>
    [CreateAssetMenu(fileName = "AuctionSettings", menuName = "AntiqueTradingSimulator/Auctions/Auction Settings")]
    public class AuctionSettings : ScriptableObject
    {
        [Header("Schedule")]
        [Tooltip("Day of the first auction.")]
        [Min(1)] public int FirstAuctionDay = 3;

        [Tooltip("An auction is held every this many days.")]
        [Min(1)] public int IntervalDays = 3;

        [Tooltip("The catalog is published this many days before the auction.")]
        [Min(0)] public int AnnounceDaysAhead = 3;

        [Tooltip("Hour (tick) of the auction day at which the session starts.")]
        [Min(0)] public int StartTick = 14;

        [Tooltip("Real seconds the player has to join once the session starts. The game keeps running meanwhile.")]
        [Min(1f)] public float JoinWindowSeconds = 15f;

        [Tooltip("How many finished auctions are kept for the results history.")]
        [Min(1)] public int MaxHistory = 10;

        [Header("Catalog")]
        [Min(1)] public int LotsPerAuction = 4;

        [Tooltip("Range of a lot's market value at the start of the campaign (£).")]
        public Vector2 StartValueRange = new Vector2(300f, 1500f);

        [Tooltip("Multiplier of the value range over the campaign: x = progress 0..1 (day 1 → last day), y = multiplier.")]
        public AnimationCurve ValueGrowth = AnimationCurve.EaseInOut(0f, 1f, 1f, 6f);

        [Tooltip("The upper end of the range never exceeds what the second-richest bidder can spend × this, " +
                 "so a lot always has at least two bidders who can afford it.")]
        [Min(0.1f)] public float BudgetCapFactor = 1f;

        [Tooltip("Lots never go below this market value (£).")]
        [Min(1f)] public float MinLotValue = 100f;

        [Tooltip("Condition range of lots consigned by the auction house — auctions carry fine pieces.")]
        public Vector2 LotConditionRange = new Vector2(0.8f, 1f);

        [Tooltip("Price factor range of lots consigned by the auction house.")]
        public Vector2 LotPriceFactorRange = new Vector2(1f, 1.2f);

        [Tooltip("Where the auction house ships from — sets transport cost and time for the winner.")]
        public ShippingZone AuctionHouseZone = ShippingZone.Domestic;

        [Header("Provenance")]
        [Range(0f, 1f)] public float ProvenanceChance = 0.5f;

        [Tooltip("Bidders value a lot with documented provenance this much higher (0.1 = +10%).")]
        [Min(0f)] public float ProvenancePremium = 0.1f;

        public List<string> ProvenanceTexts = new List<string>
        {
            "From the estate of Lord Ashcombe.",
            "Formerly in the collection of the Duke of Carrow.",
            "Acquired from a Viennese private collection in 1862.",
            "Exhibited at the Great Exhibition, London 1851.",
            "From the collection of the late Baron von Hesse.",
            "Sold by the descendants of a Parisian court jeweller.",
        };

        [Header("Estimates and prices")]
        [Tooltip("Low estimate = market value × this.")]
        [Min(0f)] public float EstimateLowFactor = 0.85f;

        [Tooltip("High estimate = market value × this (provenance adds its premium).")]
        [Min(0f)] public float EstimateHighFactor = 1.2f;

        [Tooltip("Opening bid = low estimate × this. With no bid at the opening price the lot stays unsold.")]
        [Range(0.1f, 1f)] public float StartPriceFactor = 0.7f;

        [Tooltip("Each bid raises the price by this share of the current bid (rounded to 1-2-5 steps).")]
        [Range(0.01f, 0.5f)] public float IncrementRate = 0.05f;

        [Min(1f)] public float MinIncrement = 5f;

        [Header("Fees")]
        [Tooltip("Paid by the winner on top of the hammer price (0.1 = 10%).")]
        [Range(0f, 0.5f)] public float BuyersPremiumRate = 0.1f;

        [Tooltip("NPCs bid at most budget / (1 + this), keeping the rest for the buyer's premium and transport.")]
        [Range(0f, 0.5f)] public float NpcCostAllowance = 0.2f;

        [Header("Live bidding pace (real seconds)")]
        [Tooltip("Time between the auctioneer's calls while nobody bids: '… for the first time', " +
                 "'… for the second time', '… for the third time — sold!'. Restarts after every bid.")]
        [Min(0.2f)] public float CallIntervalSeconds = 5f;

        [Tooltip("Extra wait before the first call of a lot, so bidders can open.")]
        [Min(0f)] public float OpeningGraceSeconds = 2f;

        [Tooltip("How long an NPC takes to respond to a bid (before hesitation). Always answers before the lot closes.")]
        public Vector2 NpcResponseDelay = new Vector2(1.2f, 3.5f);

        [Tooltip("Seconds the result stays on screen before the next lot is presented.")]
        [Min(0f)] public float BetweenLotsSeconds = 4f;

        [Tooltip("Each lot is presented before bidding opens. 0 = bidding opens only when the player presses " +
                 "'Start bidding'; above 0 = it also opens on its own after this many seconds.")]
        [Min(0f)] public float PresentationAutoStartSeconds = 0f;

        [Header("Market feedback")]
        [Tooltip("Demand change per 100% of hammer price above (or below) the market value. " +
                 "0.5 → a lot sold 20% above value adds +0.1 demand to its type.")]
        [Min(0f)] public float DemandSignalStrength = 0.5f;

        [Tooltip("Largest demand change a single lot can cause, in either direction.")]
        [Min(0f)] public float MaxDemandSignal = 0.5f;

        [Tooltip("Demand drop for a lot nobody wanted.")]
        [Min(0f)] public float UnsoldDemandPenalty = 0.1f;

        public int AuctionDayFor(int index) => FirstAuctionDay + index * IntervalDays;
    }
}
