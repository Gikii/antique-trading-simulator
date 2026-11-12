using AntiqueTradingSimulator.Auctions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Strip under the HUD shown while an auction's join window is open: "Auction #3 is starting",
    /// a countdown bar (real seconds, the game keeps running) and a Join button. Joining closes any
    /// open modal first. The countdown stands still while the pause menu is open.
    ///
    /// This object stays active (only <see cref="panel"/> is toggled), so it also opens the live
    /// auction modal whenever the player enters an auction room — from here or from the Auctions view.
    /// </summary>
    public class AuctionJoinBannerUI : MonoBehaviour
    {
        [Header("Dependencies (auto-found if empty)")]
        [SerializeField] private AuctionManager auctionManager;
        [SerializeField] private GameMenuUI gameMenu;
        [SerializeField] private LiveAuctionModalUI liveModal;

        [Header("Banner")]
        [SerializeField] private GameObject panel;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text secondsText;
        [SerializeField] private ProgressBarUI countdownBar;
        [SerializeField] private Button joinButton;

        private bool _subscribed;

        private void Awake()
        {
            if (auctionManager == null) auctionManager = FindFirstObjectByType<AuctionManager>();
            if (gameMenu == null) gameMenu = FindFirstObjectByType<GameMenuUI>(FindObjectsInactive.Include);
            if (liveModal == null) liveModal = FindFirstObjectByType<LiveAuctionModalUI>(FindObjectsInactive.Include);

            if (joinButton != null) joinButton.onClick.AddListener(Join);
            if (panel != null) panel.SetActive(false);

            if (auctionManager != null)
                auctionManager.JoinCountdownPaused = () => gameMenu != null && gameMenu.IsOpen;
        }

        private void OnEnable()
        {
            if (_subscribed || auctionManager == null) return;

            auctionManager.OnJoinWindowOpened += HandleWindowOpened;
            auctionManager.OnJoinWindowClosed += HandleWindowClosed;
            auctionManager.OnLiveSessionStarted += HandleLiveSessionStarted;
            auctionManager.OnAuctionsRestored += HandleRestored;
            _subscribed = true;

            // A window may already be open (e.g. the object was re-enabled).
            if (auctionManager.JoinWindowAuction != null) HandleWindowOpened(auctionManager.JoinWindowAuction);
        }

        private void OnDisable()
        {
            if (!_subscribed || auctionManager == null) return;

            auctionManager.OnJoinWindowOpened -= HandleWindowOpened;
            auctionManager.OnJoinWindowClosed -= HandleWindowClosed;
            auctionManager.OnLiveSessionStarted -= HandleLiveSessionStarted;
            auctionManager.OnAuctionsRestored -= HandleRestored;
            _subscribed = false;
        }

        private void Update()
        {
            if (panel == null || !panel.activeSelf || auctionManager == null) return;

            var auction = auctionManager.JoinWindowAuction;
            if (auction == null)
            {
                panel.SetActive(false);
                return;
            }

            float total = Mathf.Max(0.01f, auctionManager.Settings.JoinWindowSeconds);
            float left = auctionManager.JoinSecondsLeft;

            if (countdownBar != null) countdownBar.SetValue(left / total);
            if (secondsText != null) secondsText.text = $"{Mathf.CeilToInt(left)} s";
        }

        private void HandleWindowOpened(Auction auction)
        {
            if (panel == null) return;

            panel.SetActive(true);
            if (titleText != null)
                titleText.text = $"{AuctionPresentation.Title(auction)} is starting · {auction.Lots.Count} lots";
            if (countdownBar != null) countdownBar.SetValue(1f);
        }

        private void HandleWindowClosed(Auction auction, bool joined)
        {
            if (panel != null) panel.SetActive(false);
        }

        private void HandleRestored()
        {
            if (panel != null && auctionManager.JoinWindowAuction == null) panel.SetActive(false);
        }

        private void HandleLiveSessionStarted(AuctionSession session)
        {
            if (liveModal == null)
            {
                Debug.LogWarning("AuctionJoinBannerUI: no LiveAuctionModalUI in the scene — run Tools → Antique Trading Simulator → Build Auctions UI.");
                return;
            }

            liveModal.Open(session);
        }

        private void Join()
        {
            if (auctionManager == null || auctionManager.JoinWindowAuction == null) return;

            // Leave whatever dialog is open (e.g. Buy) — the auction modal takes over the screen.
            ModalTracker.CloseCurrent();
            auctionManager.JoinAuction();
        }
    }
}
