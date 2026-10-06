using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Economy;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// "Recent transactions" table. Collapsed: the latest few entries of the selected period.
    /// Expanded ("Show all"): all of them, paged. The owning tab decides the mode and hides
    /// the rest of its column while expanded; this list only reports toggle clicks.
    /// </summary>
    public class TransactionListUI : MonoBehaviour
    {
        [SerializeField] private RectTransform rowContainer;
        [Tooltip("Inactive row inside the container, cloned for every visible entry.")]
        [SerializeField] private TransactionRowUI rowTemplate;
        [SerializeField] private TMP_Text emptyText;

        [Header("Show all")]
        [SerializeField] private Button toggleButton;
        [SerializeField] private TMP_Text toggleButtonText;

        [Header("Paging (expanded only)")]
        [SerializeField] private GameObject paginationRow;
        [SerializeField] private Button previousButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private TMP_Text pageText;

        [Min(1)] [SerializeField] private int collapsedRows = 7;
        [Min(1)] [SerializeField] private int expandedRowsPerPage = 20;

        private readonly List<TransactionRowUI> _rows = new List<TransactionRowUI>();
        private readonly List<LedgerEntry> _entries = new List<LedgerEntry>();
        private bool _expanded;
        private int _page;

        public event Action OnToggleClicked;

        private void Awake()
        {
            if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
            if (toggleButton != null) toggleButton.onClick.AddListener(() => OnToggleClicked?.Invoke());
            if (previousButton != null) previousButton.onClick.AddListener(() => ChangePage(-1));
            if (nextButton != null) nextButton.onClick.AddListener(() => ChangePage(1));
        }

        /// <param name="newestFirst">Entries of the selected period, newest first.</param>
        public void Set(IReadOnlyList<LedgerEntry> newestFirst, bool expanded)
        {
            if (expanded != _expanded) _page = 0;
            _expanded = expanded;

            _entries.Clear();
            if (newestFirst != null) _entries.AddRange(newestFirst);

            Render();
        }

        private int PageSize => _expanded ? expandedRowsPerPage : collapsedRows;
        private int PageCount => _expanded ? Mathf.Max(1, Mathf.CeilToInt(_entries.Count / (float)expandedRowsPerPage)) : 1;

        private void ChangePage(int delta)
        {
            _page = Mathf.Clamp(_page + delta, 0, PageCount - 1);
            Render();
        }

        private void Render()
        {
            _page = Mathf.Clamp(_page, 0, PageCount - 1);
            int start = _expanded ? _page * expandedRowsPerPage : 0;
            int count = Mathf.Clamp(_entries.Count - start, 0, PageSize);

            EnsureRows(count);
            for (int i = 0; i < _rows.Count; i++)
            {
                bool visible = i < count;
                _rows[i].gameObject.SetActive(visible);
                if (!visible) continue;

                var entry = _entries[start + i];
                _rows[i].Set(UIFormat.GameDate(entry.Day), entry.Category.ToString(), entry.Description, entry.Amount);
            }

            if (emptyText != null)
            {
                emptyText.gameObject.SetActive(_entries.Count == 0);
                emptyText.text = "No transactions in this period.";
            }

            if (toggleButtonText != null) toggleButtonText.text = _expanded ? "Show less" : "Show all";
            if (toggleButton != null) toggleButton.interactable = _expanded || _entries.Count > collapsedRows;

            if (paginationRow != null) paginationRow.SetActive(_expanded);
            if (pageText != null) pageText.text = $"{_page + 1} / {PageCount}";
            if (previousButton != null) previousButton.interactable = _page > 0;
            if (nextButton != null) nextButton.interactable = _page < PageCount - 1;
        }

        private void EnsureRows(int count)
        {
            if (rowTemplate == null || rowContainer == null) return;

            while (_rows.Count < count)
            {
                var row = Instantiate(rowTemplate, rowContainer);
                row.name = $"TransactionRow{_rows.Count + 1}";
                _rows.Add(row);
            }
        }
    }
}
