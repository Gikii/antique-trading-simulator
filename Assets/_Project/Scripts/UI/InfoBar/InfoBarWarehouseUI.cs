using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// WAREHOUSE tab of the InfoBar: used / total slots (with an optional fill bar that turns
    /// red when full), how many of those items are still in transit, and the daily upkeep.
    /// </summary>
    public class InfoBarWarehouseUI : MonoBehaviour
    {
        [Header("Dependencies (auto-found if empty)")]
        [SerializeField] private PlayerTrader playerTrader;

        [Header("Rows")]
        [SerializeField] private RectTransform listContainer;
        [SerializeField] private InfoBarRowUI rowPrefab;

        [Header("Fill bar (optional)")]
        [Tooltip("Child of a background bar; its width is set to the used share of the warehouse.")]
        [SerializeField] private RectTransform fillBar;
        [SerializeField] private Image fillImage;
        [SerializeField] private Color normalFillColor = new Color32(0xC9, 0x9A, 0x3C, 0xFF);
        [SerializeField] private Color fullFillColor = new Color32(0xB0, 0x3A, 0x2E, 0xFF);

        [Tooltip("At or above this share of capacity the slots line and bar turn into a warning.")]
        [Range(0f, 1f)] [SerializeField] private float warningThreshold = 1f;

        private readonly List<InfoBarRowUI> _rows = new();
        private TraderInventory _inventory;

        private void Awake()
        {
            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            if (fillImage == null && fillBar != null) fillImage = fillBar.GetComponent<Image>();
        }

        // Start: PlayerTrader creates its inventory and warehouse in its own Awake.
        private void Start()
        {
            Bind();
            Refresh();
        }

        private void OnEnable()
        {
            Bind();
            Refresh();
        }

        private void OnDisable() => Unbind();

        private void Bind()
        {
            var inventory = playerTrader != null ? playerTrader.Inventory : null;
            if (inventory == null || inventory == _inventory) return;

            Unbind();
            _inventory = inventory;
            _inventory.OnHoldingChanged += HandleHoldingChanged;
            _inventory.OnWarehouseChanged += Refresh;
            _inventory.OnWarehouseUpkeepCharged += HandleUpkeepCharged;
        }

        private void Unbind()
        {
            if (_inventory == null) return;

            _inventory.OnHoldingChanged -= HandleHoldingChanged;
            _inventory.OnWarehouseChanged -= Refresh;
            _inventory.OnWarehouseUpkeepCharged -= HandleUpkeepCharged;
            _inventory = null;
        }

        private void HandleHoldingChanged(string listingId, Antique antique) => Refresh();
        private void HandleUpkeepCharged(float amount) => Refresh();

        // ------------------------------------------------------------------
        // Content
        // ------------------------------------------------------------------

        public void Refresh()
        {
            ClearRows();

            var inventory = _inventory;
            var warehouse = inventory?.Warehouse;

            if (inventory == null || warehouse == null)
            {
                AddRow(UIFormat.Colorize("No warehouse", UIFormat.MutedColor), "");
                SetFill(0f, false);
                return;
            }

            int used = inventory.UsedSlots;
            int capacity = inventory.Capacity;
            float share = capacity > 0 ? (float)used / capacity : 1f;
            bool warning = share >= warningThreshold;

            string slots = $"{used} / {capacity}";
            if (inventory.IsOverCapacity)
                slots = UIFormat.Colorize($"{slots} · over capacity", UIFormat.NegativeColor);
            else if (warning)
                slots = UIFormat.Colorize($"{slots} · full", UIFormat.NegativeColor);

            AddRow("Slots", slots,
                inventory.IsOverCapacity
                    ? "More antiques than the warehouse can hold — you can't buy anything until you sell some."
                    : $"{inventory.FreeSlots} free. Items in transit already take up a slot.");

            int inTransit = inventory.GetInTransitHoldings().Count;
            AddRow("In transit", inTransit > 0 ? UIFormat.Colorize(inTransit.ToString(), UIFormat.InTransitColor) : "0");

            float upkeep = warehouse.CalculateDailyUpkeep(used);
            AddRow("Upkeep", $"{UIFormat.Money(upkeep)} / day",
                $"Capacity level {warehouse.CapacityLevel} · security level {warehouse.SecurityLevel}.\n" +
                "Upkeep grows with the warehouse size, security and the number of stored items.");

            SetFill(share, warning);
        }

        private void SetFill(float share, bool warning)
        {
            if (fillBar != null)
            {
                fillBar.anchorMin = new Vector2(0f, fillBar.anchorMin.y);
                fillBar.anchorMax = new Vector2(Mathf.Clamp01(share), fillBar.anchorMax.y);
                fillBar.offsetMin = new Vector2(0f, fillBar.offsetMin.y);
                fillBar.offsetMax = new Vector2(0f, fillBar.offsetMax.y);
            }

            if (fillImage != null)
                fillImage.color = warning ? fullFillColor : normalFillColor;
        }

        private void AddRow(string label, string detail, string tooltip = null)
        {
            if (listContainer == null || rowPrefab == null) return;

            InfoBarRowUI row = Instantiate(rowPrefab, listContainer);
            row.Setup(label, detail, tooltip);
            _rows.Add(row);
        }

        private void ClearRows()
        {
            foreach (var row in _rows)
                if (row != null)
                    Destroy(row.gameObject);

            _rows.Clear();
        }
    }
}
