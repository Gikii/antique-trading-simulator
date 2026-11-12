using System;
using System.Collections.Generic;

namespace AntiqueTradingSimulator.Auctions
{
    /// <summary>
    /// Runs an auction's pending lots one after another. Live (player in the room) every lot is first
    /// presented — bidding opens when the player is ready (BeginBidding) — and then driven by real
    /// seconds, with a pause between lots to show the result; otherwise every lot is played out instantly.
    /// Who bids on a lot and what happens when the hammer falls are decided by the AuctionManager
    /// through the two callbacks, so this class only handles the order and the pacing.
    /// </summary>
    public class AuctionSession
    {
        private readonly AuctionSettings _settings;
        private readonly Func<AuctionLot, List<AuctionBidder>> _createBidders;
        private readonly Action<AuctionLot, LotBidding> _settleLot;
        private readonly string _playerId;
        private readonly string _playerName;
        private float _pauseLeft;
        private float _presentationLeft;

        public Auction Auction { get; }

        /// <summary>True while the player is in the room (bids are paced in real time).</summary>
        public bool IsLive { get; private set; }

        /// <summary>The player's paddle for the current lot; null when the player isn't in the room.</summary>
        public AuctionBidder Player { get; private set; }

        /// <summary>The lot being auctioned, or the one just closed during the pause before the next.</summary>
        public LotBidding Current { get; private set; }

        public bool IsFinished { get; private set; }

        /// <summary>Seconds until the next lot opens (only meaningful between lots).</summary>
        public float SecondsToNextLot => Math.Max(0f, _pauseLeft);

        public bool IsBetweenLots => !IsFinished && (Current == null || Current.IsClosed);

        /// <summary>Live only: the current lot is being presented and bidding hasn't opened yet.</summary>
        public bool IsPresenting { get; private set; }

        /// <summary>Seconds until bidding opens on its own (0 when it waits for the player).</summary>
        public float PresentationSecondsLeft => IsPresenting && _settings.PresentationAutoStartSeconds > 0f
            ? Math.Max(0f, _presentationLeft)
            : 0f;

        /// <summary>A lot is up: live sessions present it first (IsPresenting), then raise OnBiddingStarted.</summary>
        public event Action<LotBidding> OnLotOpened;
        public event Action<LotBidding> OnBiddingStarted;
        public event Action<AuctionLot, LotBidding> OnLotClosed;
        public event Action<AuctionSession> OnFinished;

        public AuctionSession(Auction auction, AuctionSettings settings, bool live, string playerId, string playerName,
            Func<AuctionLot, List<AuctionBidder>> createBidders, Action<AuctionLot, LotBidding> settleLot)
        {
            Auction = auction;
            _settings = settings;
            IsLive = live;
            _playerId = playerId;
            _playerName = playerName;
            _createBidders = createBidders;
            _settleLot = settleLot;
        }

        /// <summary>Opens the first pending lot. Live sessions then advance with <see cref="Advance"/>.</summary>
        public void Start() => OpenNextLot();

        public void Advance(float seconds)
        {
            if (IsFinished) return;

            if (IsPresenting)
            {
                if (_settings.PresentationAutoStartSeconds > 0f)
                {
                    _presentationLeft -= seconds;
                    if (_presentationLeft <= 0f) BeginBidding();
                }
                return;
            }

            if (Current != null && !Current.IsClosed)
            {
                Current.Advance(seconds);
                return;
            }

            _pauseLeft -= seconds;
            if (_pauseLeft <= 0f) OpenNextLot();
        }

        /// <summary>Ends the presentation of the current lot: bidding opens.</summary>
        public void BeginBidding()
        {
            if (!IsPresenting || Current == null) return;

            IsPresenting = false;
            OnBiddingStarted?.Invoke(Current);
        }

        /// <summary>
        /// The player sits this lot out: the NPCs bid on it without them and it closes at once.
        /// The session then continues with the usual pause before the next lot.
        /// </summary>
        public void SkipCurrentLot()
        {
            if (IsFinished || Current == null || Current.IsClosed) return;

            IsPresenting = false;
            if (Player != null) Current.PlayerPass(Player);
            Current.RunToEnd();
        }

        /// <summary>Plays every remaining lot out instantly.</summary>
        public void RunToEnd()
        {
            IsPresenting = false;
            int guard = 0;
            while (!IsFinished && guard++ < 1000)
            {
                if (Current != null && !Current.IsClosed)
                    Current.RunToEnd();
                else
                    OpenNextLot();
            }
        }

        /// <summary>
        /// The player walks out: the rest of the session is played out without them. A bid they
        /// lead with stands — they may still win the current lot if nobody tops it.
        /// </summary>
        public void LeaveRoom()
        {
            if (IsFinished) return;

            if (Player != null && Current != null && !Current.IsClosed)
                Current.PlayerPass(Player);

            IsLive = false;
            RunToEnd();
        }

        private void OpenNextLot()
        {
            _pauseLeft = 0f;

            var lot = Auction.FirstPendingLot;
            if (lot == null)
            {
                Finish();
                return;
            }

            var bidders = _createBidders != null ? _createBidders(lot) : null;
            bidders ??= new List<AuctionBidder>();

            Player = null;
            if (IsLive)
            {
                Player = new AuctionBidder { Id = _playerId, Name = _playerName, Kind = BidderKind.Player };
                bidders.Insert(0, Player);
            }

            var bidding = new LotBidding(lot, bidders, _settings, SeedFor(lot));
            bidding.OnClosed += HandleLotClosed;
            Current = bidding;

            IsPresenting = IsLive;
            _presentationLeft = _settings.PresentationAutoStartSeconds;

            OnLotOpened?.Invoke(bidding);
        }

        private void HandleLotClosed(LotBidding bidding)
        {
            bidding.OnClosed -= HandleLotClosed;
            _settleLot?.Invoke(bidding.Lot, bidding);

            // Never leave a closed lot pending — the session would open it again.
            if (bidding.Lot.IsPending) bidding.Lot.Status = LotStatus.Unsold;

            _pauseLeft = IsLive ? _settings.BetweenLotsSeconds : 0f;
            OnLotClosed?.Invoke(bidding.Lot, bidding);
        }

        private void Finish()
        {
            if (IsFinished) return;

            IsFinished = true;
            IsPresenting = false;
            Current = null;
            Player = null;
            OnFinished?.Invoke(this);
        }

        private int SeedFor(AuctionLot lot)
        {
            unchecked
            {
                int hash = (int)2166136261;
                string id = Auction.Id ?? "auction";
                for (int i = 0; i < id.Length; i++)
                    hash = (hash ^ id[i]) * 16777619;
                return hash ^ (lot.LotNumber * 397);
            }
        }
    }
}
