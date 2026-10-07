using System;
using System.Collections.Generic;

namespace AntiqueTradingSimulator.Market
{

    public class MarketFeed
    {
        public const int MaxMessages = 100;

        private readonly List<string> _messages = new List<string>(MaxMessages);

        public IReadOnlyList<string> Messages => _messages;

        public event Action<string> OnMessageAdded;

        public event Action OnCleared;

        public void Add(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            _messages.Insert(0, message);

            if (_messages.Count > MaxMessages)
                _messages.RemoveRange(MaxMessages, _messages.Count - MaxMessages);

            OnMessageAdded?.Invoke(message);
        }

        public void Clear()
        {
            if (_messages.Count == 0)
                return;

            _messages.Clear();
            OnCleared?.Invoke();
        }

        public void AddPurchase(string traderName, string antiqueName, float price)
        {
            if (string.IsNullOrWhiteSpace(antiqueName))
                return;

            string who = string.IsNullOrWhiteSpace(traderName) ? "Someone" : traderName;
            Add($"{who} purchased {antiqueName} for {FormatPrice(price)}");
        }

        public void AddListing(string antiqueName, float price)
        {
            if (string.IsNullOrWhiteSpace(antiqueName))
                return;

            Add($"New listing for {antiqueName} posted at {FormatPrice(price)}");
        }

        private static string FormatPrice(float price) => $"{price:F0} €";

        public List<string> CaptureState() => new List<string>(_messages);

        /// <summary>
        /// Replaces the feed with the saved messages. Raises OnCleared once so listeners rebuild.
        /// </summary>
        public void RestoreState(List<string> messages)
        {
            _messages.Clear();

            if (messages != null)
            {
                _messages.AddRange(messages);

                if (_messages.Count > MaxMessages)
                    _messages.RemoveRange(MaxMessages, _messages.Count - MaxMessages);
            }

            OnCleared?.Invoke();
        }
    }
}
