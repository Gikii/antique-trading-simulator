using System;
using System.Collections.Generic;

namespace AntiqueTradingSimulator.Contracts
{
    /// <summary>
    /// Running log of what happens on the contract board: contracts appearing, leaving (and why),
    /// and exclusive contracts being taken on by someone. Newest message first.
    ///
    /// The counterpart of MarketFeed — owned by ContractManager, shown by ContractFeedUI in the
    /// Contracts view's side panel whenever no contract is selected, and saved with the board so
    /// loading a game doesn't wipe the history.
    /// </summary>
    public class ContractFeed
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

        // ---------------------------------------------------------------- messages

        /// <summary>"New Open contract for 3x Porcelain listed"</summary>
        public void AddListed(Contract contract)
        {
            if (contract == null) return;
            Add($"New {TypeWord(contract)} contract for {NameOf(contract)} listed");
        }

        /// <summary>"Open contract for 3x Porcelain expired" — its duration ran out unfulfilled.</summary>
        public void AddExpired(Contract contract)
        {
            if (contract == null) return;
            Add($"{TypeWord(contract)} contract for {NameOf(contract)} expired");
        }

        /// <summary>"Exclusive contract for 3x Porcelain fulfilled"</summary>
        public void AddFulfilled(Contract contract)
        {
            if (contract == null) return;
            Add($"{TypeWord(contract)} contract for {NameOf(contract)} fulfilled");
        }

        /// <summary>
        /// "A trader took on exclusive contract for 3x Porcelain" — deliberately anonymous: the
        /// player learns the contract is gone from the board, not who claimed it.
        /// </summary>
        public void AddExclusiveClaimed(Contract contract)
        {
            if (contract == null) return;
            Add($"A trader took on exclusive contract for {NameOf(contract)}");
        }

        /// <summary>The contract's name, the same string the details panel shows in NameText.</summary>
        private static string NameOf(Contract contract) => contract.DisplayName;

        /// <summary>"Open" / "Exclusive".</summary>
        private static string TypeWord(Contract contract) => contract.Type.ToString();

        // ---------------------------------------------------------------- save / load

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
