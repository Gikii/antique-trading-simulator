using System.Collections.Generic;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Company → Development: a card per company upgrade (cloned from an inactive template)
    /// and a details panel for the selected one. Buying (from the details panel) asks first —
    /// cost now, the new weekly upkeep, what the level gives — then goes through
    /// CompanyManager.TryUpgrade. Refreshes on company, cash and warehouse changes.
    /// </summary>
    public class CompanyDevelopmentTab : MonoBehaviour
    {
        [Header("Dependencies (found automatically if empty)")]
        [SerializeField] private PlayerTrader playerTrader;

        [Header("Summary strip")]
        [SerializeField] private TMP_Text summaryText;

        [Header("Upgrade list")]
        [SerializeField] private RectTransform cardContainer;
        [Tooltip("Inactive card inside the container, cloned for every upgrade.")]
        [SerializeField] private UpgradeCardUI cardTemplate;
        [SerializeField] private ScrollRect scrollRect;

        [Header("Details")]
        [SerializeField] private UpgradeDetailsUI details;

        private readonly List<UpgradeCardUI> _cards = new List<UpgradeCardUI>();
        private CompanyUpgradeType _selected = CompanyUpgradeType.Warehouse;
        private CompanyManager _company;
        private TraderInventory _inventory;

        private void Awake()
        {
            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            if (cardTemplate != null) cardTemplate.gameObject.SetActive(false);
            if (details != null) details.Initialize(HandleUpgradeClicked);
        }

        private void OnEnable()
        {
            Bind();
            Refresh();
        }

        // PlayerTrader creates its company in Awake; covers a page that was active at scene load.
        private void Start()
        {
            Bind();
            Refresh();
        }

        private void OnDisable() => Unbind();

        private void Bind()
        {
            if (_company != null || playerTrader == null || playerTrader.Company == null) return;

            _company = playerTrader.Company;
            _inventory = playerTrader.Inventory;

            _company.OnCompanyChanged += Refresh;
            if (_inventory != null)
            {
                _inventory.OnCashChanged += HandleCashChanged;
                _inventory.OnWarehouseChanged += Refresh;
                _inventory.OnHoldingChanged += HandleHoldingChanged;
            }

            BuildCards();
        }

        private void Unbind()
        {
            if (_company != null) _company.OnCompanyChanged -= Refresh;
            if (_inventory != null)
            {
                _inventory.OnCashChanged -= HandleCashChanged;
                _inventory.OnWarehouseChanged -= Refresh;
                _inventory.OnHoldingChanged -= HandleHoldingChanged;
            }

            _company = null;
            _inventory = null;
        }

        private void HandleCashChanged(float cash) => Refresh();

        // Used slots change the warehouse upkeep.
        private void HandleHoldingChanged(string listingId, Antique antique) => Refresh();

        private void BuildCards()
        {
            if (_cards.Count > 0 || cardTemplate == null || cardContainer == null) return;

            foreach (var type in CompanyUpgrades.AllTypes)
            {
                var card = Instantiate(cardTemplate, cardContainer);
                card.name = $"{type}Card";
                card.gameObject.SetActive(true);
                card.Initialize(type, Select);
                _cards.Add(card);
            }

            if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;
        }

        // ------------------------------------------------------------------ actions

        /// <summary>Selects an upgrade for the details panel (also used by CompanyView.OpenUpgrade).</summary>
        public void Select(CompanyUpgradeType type)
        {
            _selected = type;
            if (isActiveAndEnabled)
                Refresh();
        }

        private ContractAcceptDialog _confirmDialog;

        /// <summary>
        /// The gameplay confirmation (pauses the clock, Escape cancels), copied lazily next to the
        /// other gameplay modals. Null if the scene has no ConfirmDialog — then buying doesn't ask.
        /// </summary>
        private ContractAcceptDialog ConfirmDialog
        {
            get
            {
                if (_confirmDialog == null)
                {
                    var modal = FindFirstObjectByType<BuyModalUI>(FindObjectsInactive.Include);
                    Transform parent = modal != null ? modal.transform.parent : transform.root;
                    _confirmDialog = ContractAcceptDialog.Create(parent);
                }

                return _confirmDialog;
            }
        }

        private void HandleUpgradeClicked(CompanyUpgradeType type)
        {
            _selected = type;

            var info = _company != null ? _company.GetUpgradeInfo(type) : null;
            if (info == null || !info.CanUpgrade)
            {
                Debug.Log($"CompanyDevelopmentTab: upgrade {type} not bought — {info?.BlockReason}");
                Refresh();
                return;
            }

            var dialog = ConfirmDialog;
            if (dialog == null)
            {
                Buy(type);
                return;
            }

            dialog.Open($"Upgrade {info.Name}?", UpgradePresentation.ConfirmMessage(info),
                $"Upgrade for {UIFormat.Money(info.NextCost)}", () => Buy(type));
        }

        private void Buy(CompanyUpgradeType type)
        {
            // Re-checked inside TryUpgrade: the clock was paused while asking, but cash may still have moved.
            if (_company != null && !_company.TryUpgrade(type))
                Debug.Log($"CompanyDevelopmentTab: upgrade {type} not bought — {_company.GetUpgradeInfo(type)?.BlockReason}");
            Refresh();
        }

        // ------------------------------------------------------------------ refresh

        public void Refresh()
        {
            if (_company == null) Bind();
            if (_company == null) return;

            float totalUpkeep = 0f;
            foreach (var card in _cards)
            {
                var info = _company.GetUpgradeInfo(card.Type);
                if (info == null) continue;

                card.Set(info, card.Type == _selected);
                totalUpkeep += info.CurrentUpkeep;
            }

            if (details != null)
                details.Show(_company.GetUpgradeInfo(_selected));

            if (summaryText != null)
            {
                var reputation = _company.Reputation;
                float cash = _inventory != null ? _inventory.Cash : 0f;
                summaryText.text =
                    $"{UIFormat.Colorize("Cash:", UIFormat.MutedColor)} {UIFormat.Money(cash)}     " +
                    $"{UIFormat.Colorize("Reputation:", UIFormat.MutedColor)} {UIFormat.Number(reputation.Reputation)} ({reputation.Title})     " +
                    $"{UIFormat.Colorize("Total upkeep:", UIFormat.MutedColor)} {UpgradePresentation.PerWeek(totalUpkeep)}";
            }
        }
    }
}
