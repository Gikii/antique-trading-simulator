using System;
using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Auctions
{
    /// <summary>
    /// The English (ascending) auction of one lot, as a small event simulation on its own clock.
    ///
    /// After every bid each NPC still in the race gets a response time (a random delay that grows
    /// as the price nears its limit). When it comes, the NPC either tops the bid or withdraws.
    /// While nobody bids, the auctioneer calls "going once", "going twice" and closes the lot.
    ///
    /// Live mode: <see cref="Advance"/> moves the clock by real seconds (the UI shows each step).
    /// Instant mode: <see cref="RunToEnd"/> plays the same events with no waiting — used when the
    /// player isn't in the room. Both use the same seeded random numbers, so the outcome only
    /// differs when the player bids.
    /// </summary>
    public class LotBidding
    {
        private const int MaxEventsPerRun = 10000;

        // An NPC must answer before the lot can close, otherwise a willing bidder would lose it.
        private const float ResponseMargin = 0.05f;

        private readonly AuctionSettings _settings;
        private readonly System.Random _rng;
        private readonly List<AuctionBidder> _bidders;
        private float _lastActivityAt;

        public AuctionLot Lot { get; }
        public IReadOnlyList<AuctionBidder> Bidders => _bidders;

        /// <summary>Seconds since the lot was opened (live: real seconds; instant: simulated).</summary>
        public float Clock { get; private set; }

        public float CurrentBid { get; private set; }
        public AuctionBidder Leader { get; private set; }
        public int BidCount { get; private set; }
        public AuctioneerCall LastCall { get; private set; } = AuctioneerCall.None;

        public bool HasBid => Leader != null;
        public bool IsClosed => LastCall == AuctioneerCall.Closed;
        public bool IsSold => IsClosed && HasBid;

        /// <summary>What the next bid must be: the opening price, or the current bid plus one increment.</summary>
        public float AskingBid => HasBid ? AuctionMath.NextBid(CurrentBid, _settings) : Lot.StartPrice;

        /// <summary>When the auctioneer makes the next call if nobody bids before then.</summary>
        public float NextCallAt => _lastActivityAt + ((int)LastCall + 1) * _settings.CallIntervalSeconds;

        public float SecondsToNextCall => Mathf.Max(0f, NextCallAt - Clock);

        /// <summary>(bidder, amount)</summary>
        public event Action<AuctionBidder, float> OnBid;
        public event Action<AuctionBidder> OnWithdraw;
        public event Action<AuctioneerCall> OnCall;
        public event Action<LotBidding> OnClosed;

        public LotBidding(AuctionLot lot, List<AuctionBidder> bidders, AuctionSettings settings, int seed)
        {
            Lot = lot;
            _bidders = bidders ?? new List<AuctionBidder>();
            _settings = settings;
            _rng = new System.Random(seed);

            // A little extra time before the first call: "who will start me at …?"
            _lastActivityAt = _settings.OpeningGraceSeconds;
            ScheduleResponses();
        }

        // ---------------------------------------------------------------- running

        /// <summary>Live mode: moves the clock forward and plays every event that falls within.</summary>
        public void Advance(float seconds)
        {
            if (IsClosed) return;

            float target = Clock + Mathf.Max(0f, seconds);
            int guard = 0;

            while (!IsClosed && guard++ < MaxEventsPerRun)
            {
                float next = NextEventTime();
                if (next > target) break;

                Clock = Mathf.Max(Clock, next);
                ProcessDueEvent();
            }

            if (!IsClosed) Clock = target;
        }

        /// <summary>Instant mode: plays the lot out to the hammer without waiting.</summary>
        public void RunToEnd()
        {
            int guard = 0;
            while (!IsClosed && guard++ < MaxEventsPerRun)
            {
                Clock = Mathf.Max(Clock, NextEventTime());
                ProcessDueEvent();
            }

            if (!IsClosed)
            {
                Debug.LogWarning($"LotBidding: lot {Lot.LotNumber} did not close after {MaxEventsPerRun} events — closing it now.");
                Close();
            }
        }

        /// <summary>The player raises their paddle at the current asking bid. False if they can't now.</summary>
        public bool PlayerBid(AuctionBidder player)
        {
            if (IsClosed || player == null || !player.IsPlayer || !player.Active) return false;
            if (Leader == player) return false;

            PlaceBid(player, AskingBid);
            return true;
        }

        /// <summary>The player stops bidding on this lot (their standing bid, if leading, still counts).</summary>
        public void PlayerPass(AuctionBidder player)
        {
            if (player == null || !player.IsPlayer || !player.Active) return;
            if (Leader == player) return;

            player.Active = false;
            OnWithdraw?.Invoke(player);
        }

        // ---------------------------------------------------------------- events

        private float NextEventTime()
        {
            float next = NextCallAt;
            for (int i = 0; i < _bidders.Count; i++)
                if (_bidders[i].RespondAt < next)
                    next = _bidders[i].RespondAt;
            return next;
        }

        private void ProcessDueEvent()
        {
            // NPC answers before the auctioneer's call due at the same moment.
            AuctionBidder due = null;
            for (int i = 0; i < _bidders.Count; i++)
            {
                var bidder = _bidders[i];
                if (bidder.RespondAt <= Clock && (due == null || bidder.RespondAt < due.RespondAt))
                    due = bidder;
            }

            if (due != null)
            {
                due.RespondAt = float.PositiveInfinity;
                Respond(due);
                return;
            }

            if (NextCallAt <= Clock)
                Call();
        }

        private void Respond(AuctionBidder bidder)
        {
            if (!bidder.Active || bidder == Leader || bidder.IsPlayer) return;

            float asking = AskingBid;

            if (asking > bidder.Limit) TryFever(bidder);

            if (asking > bidder.Limit)
            {
                bidder.Active = false;
                OnWithdraw?.Invoke(bidder);
                return;
            }

            PlaceBid(bidder, asking);
        }

        /// <summary>Carried away by the room: once per lot, a bidder may push past their limit.</summary>
        private void TryFever(AuctionBidder bidder)
        {
            if (bidder.FeverUsed || bidder.FeverChance <= 0f) return;

            bidder.FeverUsed = true;
            if (_rng.NextDouble() >= bidder.FeverChance) return;

            float raised = bidder.Limit * (1f + bidder.FeverRaise);
            bidder.Limit = Mathf.Min(bidder.Cap, raised);
        }

        private void PlaceBid(AuctionBidder bidder, float amount)
        {
            CurrentBid = amount;
            Leader = bidder;
            BidCount++;
            _lastActivityAt = Clock;
            LastCall = AuctioneerCall.None;

            ScheduleResponses();
            OnBid?.Invoke(bidder, amount);
        }

        private void Call()
        {
            LastCall = (AuctioneerCall)((int)LastCall + 1);

            if (LastCall >= AuctioneerCall.Closed)
            {
                Close();
                return;
            }

            OnCall?.Invoke(LastCall);
        }

        private void Close()
        {
            LastCall = AuctioneerCall.Closed;
            for (int i = 0; i < _bidders.Count; i++)
                _bidders[i].RespondAt = float.PositiveInfinity;

            OnCall?.Invoke(AuctioneerCall.Closed);
            OnClosed?.Invoke(this);
        }

        private void ScheduleResponses()
        {
            float latest = 3f * _settings.CallIntervalSeconds - ResponseMargin;

            for (int i = 0; i < _bidders.Count; i++)
            {
                var bidder = _bidders[i];
                if (!bidder.Active || bidder.IsPlayer || bidder == Leader)
                {
                    bidder.RespondAt = float.PositiveInfinity;
                    continue;
                }

                float baseDelay = Mathf.Lerp(_settings.NpcResponseDelay.x, _settings.NpcResponseDelay.y, (float)_rng.NextDouble());
                float nearLimit = Mathf.Clamp01(AskingBid / Mathf.Max(1f, bidder.Limit));
                float delay = baseDelay * (1f + bidder.Hesitation * nearLimit);

                bidder.RespondAt = Clock + Mathf.Clamp(delay, 0.05f, Mathf.Max(0.05f, latest));
            }
        }
    }
}
