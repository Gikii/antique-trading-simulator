using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Company;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// "Recent reputation/credibility changes" list. Collapsed: the latest few changes.
    /// Expanded ("Show all"): the whole history, paged. The owning tab decides which mode is
    /// on (both lists switch together) — this list only reports clicks on its toggle button.
    /// </summary>
    public class ReputationChangeListUI : MonoBehaviour
    {
        [SerializeField] private RectTransform rowContainer;
        [Tooltip("Inactive row inside the container, cloned for every visible change.")]
        [SerializeField] private ReputationChangeRowUI rowTemplate;
        [SerializeField] private TMP_Text emptyText;

        [Header("Show all")]
        [SerializeField] private Button toggleButton;
        [SerializeField] private TMP_Text toggleButtonText;

        [Header("Paging (expanded only)")]
        [SerializeField] private GameObject paginationRow;
        [SerializeField] private Button previousButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private TMP_Text pageText;

        [Min(1)] [SerializeField] private int collapsedRows = 5;
        [Min(1)] [SerializeField] private int expandedRowsPerPage = 18;

        private readonly List<ReputationChangeRowUI> _rows = new List<ReputationChangeRowUI>();
        private readonly List<ReputationChange> _changes = new List<ReputationChange>();
        private ReputationKind _kind;
        private bool _expanded;
        private int _page;

        /// <summary>Raised when the Show all / Show less button is clicked.</summary>
        public event Action OnToggleClicked;

        private void Awake()
        {
            if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
            if (toggleButton != null) toggleButton.onClick.AddListener(() => OnToggleClicked?.Invoke());
            if (previousButton != null) previousButton.onClick.AddListener(() => ChangePage(-1));
            if (nextButton != null) nextButton.onClick.AddListener(() => ChangePage(1));
        }

        /// <param name="newestFirst">All changes of this list's kind, newest first.</param>
        public void Set(ReputationKind kind, IReadOnlyList<ReputationChange> newestFirst, bool expanded)
        {
            _kind = kind;
            if (expanded != _expanded) _page = 0;
            _expanded = expanded;

            _changes.Clear();
            if (newestFirst != null) _changes.AddRange(newestFirst);

            Render();
        }

        private int PageSize => _expanded ? expandedRowsPerPage : collapsedRows;
        private int PageCount => _expanded ? Mathf.Max(1, Mathf.CeilToInt(_changes.Count / (float)expandedRowsPerPage)) : 1;

        private void ChangePage(int delta)
        {
            _page = Mathf.Clamp(_page + delta, 0, PageCount - 1);
            Render();
        }

        private void Render()
        {
            _page = Mathf.Clamp(_page, 0, PageCount - 1);
            int start = _expanded ? _page * expandedRowsPerPage : 0;
            int count = Mathf.Clamp(_changes.Count - start, 0, PageSize);

            EnsureRows(count);
            for (int i = 0; i < _rows.Count; i++)
            {
                bool visible = i < count;
                _rows[i].gameObject.SetActive(visible);
                if (!visible) continue;

                var change = _changes[start + i];
                string delta = _kind == ReputationKind.Reputation
                    ? UIFormat.SignedNumber(change.Delta)
                    : UIFormat.SignedPercent(change.Delta);
                _rows[i].Set(UIFormat.GameDate(change.Day), delta, change.Delta, change.Reason);
            }

            if (emptyText != null)
            {
                emptyText.gameObject.SetActive(_changes.Count == 0);
                emptyText.text = "No changes yet.";
            }

            if (toggleButtonText != null) toggleButtonText.text = _expanded ? "Show less" : "Show all";
            if (toggleButton != null) toggleButton.interactable = _expanded || _changes.Count > collapsedRows;

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
                row.name = $"ChangeRow{_rows.Count + 1}";
                _rows.Add(row);
            }
        }
    }
}
