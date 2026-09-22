using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using AntiqueTradingSimulator.Events;

namespace AntiqueTradingSimulator.UI
{
    public class ActiveEventListItemUI : MonoBehaviour
    {
        private Button button;

        private TMP_Text titleText;
        private TMP_Text timeText;
        private TMP_Text locationText;
        private TMP_Text statusText;

        private ActiveEvent activeEvent;

        public ActiveEvent ActiveEvent => activeEvent;

        private void Awake()
        {
            CacheReferences();
        }

        private void CacheReferences()
        {
            button = GetComponent<Button>();

            titleText = transform
                .Find("MainInfo/TitleText")
                ?.GetComponent<TMP_Text>();

            timeText = transform
                .Find("MainInfo/MetaRow/TimeInfo/Text")
                ?.GetComponent<TMP_Text>();

            locationText = transform
                .Find("MainInfo/MetaRow/LocationInfo/Text")
                ?.GetComponent<TMP_Text>();

            statusText = transform
                .Find("Status/Text")
                ?.GetComponent<TMP_Text>();

            if (button == null)
                Debug.LogError("ActiveEventListItemUI: Button not found.", this);

            if (titleText == null)
                Debug.LogError("ActiveEventListItemUI: TitleText not found.", this);

            if (timeText == null)
                Debug.LogError("ActiveEventListItemUI: TimeInfo/Text not found.", this);

            if (locationText == null)
                Debug.LogError("ActiveEventListItemUI: LocationInfo/Text not found.", this);

            if (statusText == null)
                Debug.LogError("ActiveEventListItemUI: Status/Text not found.", this);
        }

        public void Setup(
            ActiveEvent eventData,
            int currentDay,
            Action<ActiveEvent> onClicked)
        {
            activeEvent = eventData;

            if (button == null)
                CacheReferences();

            EventDefinition definition = activeEvent.Definition;

            if (titleText != null)
            {
                titleText.text = definition != null
                    ? definition.DisplayName
                    : "Unknown Event";
            }

            if (timeText != null)
            {
                int daysRemaining =
                    Mathf.Max(0, activeEvent.EndDay - currentDay);

                timeText.text = daysRemaining == 1
                    ? "1 day remaining"
                    : $"{daysRemaining} days remaining";
            }

            if (locationText != null)
            {
                locationText.text = BuildTargetText(activeEvent);
            }

            if (statusText != null)
            {
                statusText.text = "ACTIVE";
            }

            if (button != null)
            {
                button.onClick.RemoveAllListeners();

                button.onClick.AddListener(() =>
                {
                    onClicked?.Invoke(activeEvent);
                });
            }
        }

        private static string BuildTargetText(ActiveEvent eventData)
        {
            if (eventData.EffectInstances == null ||
                eventData.EffectInstances.Count == 0)
            {
                return "Market";
            }

            News.NewsEventData data =
                eventData.EffectInstances[0].CreateNewsData();

            if (data == null)
                return "Market";

            switch (data.targetScope)
            {
                case EventEffect.TargetScope.AntiqueType:
                    return data.AntiqueType.ToString();

                case EventEffect.TargetScope.Country:
                    return data.Country.ToString();

                case EventEffect.TargetScope.Century:
                    return data.Century.ToString();

                case EventEffect.TargetScope.Other:
                default:
                    return "Market";
            }
        }
    }
}