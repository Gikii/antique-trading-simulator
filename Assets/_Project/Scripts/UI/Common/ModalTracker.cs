using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>A panel that Escape should close before it reaches the game menu.</summary>
    public interface IModalPanel
    {
        void Close();
    }

    /// <summary>
    /// The modal that is open, if any.
    /// </summary>
    public static class ModalTracker
    {
        private static IModalPanel _current;

        /// <summary>Null when nothing is open, or when the open modal's object was destroyed.</summary>
        public static IModalPanel Current
        {
            get
            {
                if (_current is MonoBehaviour behaviour && behaviour == null)
                    _current = null;

                return _current;
            }
        }

        public static bool IsAnyOpen => Current != null;

        public static void SetOpen(IModalPanel modal)
        {
            if (modal != null) _current = modal;
        }

        public static void SetClosed(IModalPanel modal)
        {
            if (_current == modal) _current = null;
        }

        /// <summary>Closes the open modal. False if there was none.</summary>
        public static bool CloseCurrent()
        {
            IModalPanel modal = Current;
            if (modal == null) return false;

            _current = null;
            modal.Close();
            return true;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => _current = null;
    }
}
