using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.News;

namespace AntiqueTradingSimulator.UI
{
    public class InfoBarNewsUI : InfoBarListUI
    {
        [Header("Dependencies")]
        [SerializeField] private NewsManager newsManager;
        [SerializeField] private PlayerTrader playerTrader;

        [SerializeField] private bool hideOnceEventStarted = true;

        private CompanyUpgrades _upgrades;

        protected override string EmptyLabel => "No news yet";

        protected override bool HasAction => true;
        protected override string ActionTooltip => "Read the news\n<size=85%>News & Events</size>";
        protected override void OnActionClicked() => InfoBarNavigation.OpenNews();


        private int CurrentWeek => timeManager != null ? timeManager.CurrentWeek : 0;

        protected override void Awake()
        {
            base.Awake();

            if (newsManager == null) newsManager = FindFirstObjectByType<NewsManager>();
            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
        }

        protected override void SubscribeToSources()
        {
            if (newsManager != null)
            {
                newsManager.OnNewsPublished += HandleNewsPublished;
                newsManager.OnNewsRestored += Refresh;
            }

            _upgrades = playerTrader != null && playerTrader.Company != null
                ? playerTrader.Company.Upgrades
                : null;

            if (_upgrades != null)
                _upgrades.OnUpgraded += HandleUpgraded;
        }

        protected override void UnsubscribeFromSources()
        {
            if (newsManager != null)
            {
                newsManager.OnNewsPublished -= HandleNewsPublished;
                newsManager.OnNewsRestored -= Refresh;
            }

            if (_upgrades != null)
            {
                _upgrades.OnUpgraded -= HandleUpgraded;
                _upgrades = null;
            }
        }

        private void HandleNewsPublished(NewsItem news) => Refresh();

        private void HandleUpgraded(CompanyUpgradeType type)
        {
            if (type == CompanyUpgradeType.InformationNetwork)
                Refresh();
        }

        protected override void BuildLines(List<RowData> lines)
        {
            if (newsManager == null)
                return;

            int thisWeek = CurrentWeek;
            InfoAccessLevel accessLevel = NewsPresentation.GetAccessLevel(playerTrader);

            List<NewsItem> latest = newsManager.PublishedNews
                .Reverse()
                .Where(news => news != null)
                .Where(news => !hideOnceEventStarted || !news.EventStarted(thisWeek))
                .Where(news => NewsPresentation.CanSee(news, accessLevel))
                .OrderByDescending(news => news.WeekPublished)
                .Take(Mathf.Max(1, maxRows))
                .ToList();

            foreach (NewsItem news in latest)
                lines.Add(BuildLine(news, thisWeek));
        }

        private RowData BuildLine(NewsItem news, int thisWeek)
        {
            string when = NewsPresentation.RelativeWeeks(news.WeekPublished - thisWeek).ToLowerInvariant();
            string type = NewsPresentation.TypeLabel(news.Type);
            string title = NewsPresentation.GetTitle(news);

            string tooltip = $"{title}\n" +
                             $"{type} · {NewsPresentation.FormatWeek(news.WeekPublished, timeManager)}\n\n" +
                             NewsPresentation.GetNewsDescription(news);

            return new RowData
            {
                Label = title,
                Detail = UIFormat.Colorize($"{type} · {when}", NewsPresentation.TypeColor(news.Type)),
                Tooltip = tooltip
            };
        }
    }
}
