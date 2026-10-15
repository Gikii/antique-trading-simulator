using AntiqueTradingSimulator.UI;
using System.Collections.Generic;
using UnityEngine;
using AntiqueTradingSimulator.Core;

public abstract class InfoBarListUI : MonoBehaviour
{
    [SerializeField] protected RectTransform listContainer;
    [SerializeField] protected InfoBarRowUI rowPrefab;
    [SerializeField] protected int maxRows = 3;

    [SerializeField] protected TimeManager timeManager;

    protected virtual bool ShowOverflowRow => true;
    protected virtual string EmptyLabel => "Nothing to show";

    private readonly List<InfoBarRowUI> _rows = new();
    private bool _subscribed;
    private bool _actionHooked;

    protected virtual bool HasAction => false;
    protected virtual string ActionTooltip => null;

    protected virtual void OnActionClicked() { }


    protected int CurrentDay => timeManager != null ? timeManager.CurrentDay : 0;

    protected struct RowData {
        public string Label;
        public string Detail;
        public string Tooltip; 
    }
    protected abstract void BuildLines(List<RowData> lines);
    protected abstract void SubscribeToSources();
    protected abstract void UnsubscribeFromSources();

    protected virtual void Awake()
    {
        if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();
    }

    protected virtual void Start()
    {
        HookActionButton();
    }

    protected virtual void OnEnable()
    {
        Subscribe();
        Refresh();
    }

    protected virtual void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (_subscribed) return;

        if (timeManager != null)
            timeManager.OnDayChanged += HandleDayChanged;

        SubscribeToSources();
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;

        if (timeManager != null)
            timeManager.OnDayChanged -= HandleDayChanged;

        UnsubscribeFromSources();
        _subscribed = false;
    }

    private void HandleDayChanged(int day) => Refresh();

    public void Refresh()
    {
        ClearRows();

        if (listContainer == null || rowPrefab == null)
            return;

        var lines = new List<RowData>();
        BuildLines(lines);

        if (lines.Count == 0)
        {
            AddRow(new RowData
            {
                Label = UIFormat.Colorize(EmptyLabel, UIFormat.MutedColor),
                Detail = ""
            });

            return;
        }

        int max = Mathf.Max(1, maxRows);
        bool overflow = ShowOverflowRow && lines.Count > max;
        int shown = overflow ? max - 1 : Mathf.Min(lines.Count, max);

        for (int i = 0; i < shown; i++)
            AddRow(lines[i]);

        if (overflow)
            AddRow(new RowData
            {
                Label = UIFormat.Colorize($"+{lines.Count - shown} more", UIFormat.MutedColor),
                Detail = ""
            });
    }

    protected void AddRow(RowData data)
    {
        if (listContainer == null || rowPrefab == null) return;

        InfoBarRowUI row = Instantiate(rowPrefab, listContainer);
        row.Setup(data.Label, data.Detail, data.Tooltip);
        _rows.Add(row);
    }

    protected void ClearRows()
    {
        foreach (InfoBarRowUI row in _rows)
        {
            if (row != null)
                Destroy(row.gameObject);
        }

        _rows.Clear();
    }

    private void HookActionButton()
    {
        if (_actionHooked || !HasAction) return;

        var tab = GetComponent<InfoBarTabUI>();
        var button = tab != null ? tab.ActionButton : null;
        if (button == null) return;

        button.onClick.AddListener(OnActionClicked);
        _actionHooked = true;

        string tooltipText = ActionTooltip;
        if (string.IsNullOrEmpty(tooltipText)) return;

        var tooltip = TooltipTrigger.On(button);
        if (tooltip != null && string.IsNullOrEmpty(tooltip.Text))
            tooltip.Text = tooltipText;
    }

}
