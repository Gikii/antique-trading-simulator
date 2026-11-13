using System;
using System.Collections;
using System.Collections.Generic;
using AntiqueTradingSimulator.Saving;
using TMPro;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Short notes that fade in and out without blocking clicks or the game clock: "Autosaved · week 37",
    /// and the weekly notices (season weather, shipments held up or early, damage in transit — see
    /// WeeklyNotices). Messages queue up and are shown one after another. Anything can post one with
    /// <see cref="Post"/>. Manual saves are reported by the save list itself.
    ///
    /// The prefab's ContentSizeFitter sizes the box to the text, so multi-line messages just work
    /// (TextMeshPro rich text: &lt;b&gt;, &lt;color&gt;).
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class ToastUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private SaveManager saveManager;

        [Tooltip("How long an autosave note stays.")]
        [Min(0.1f)] [SerializeField] private float visibleSeconds = 2.5f;
        [Tooltip("How long weekly notices stay.")]
        [Min(0.1f)] [SerializeField] private float noticeSeconds = 5f;
        [Min(0.01f)] [SerializeField] private float fadeSeconds = 0.3f;

        [Tooltip("Show the weekly notices about seasons and the player's shipments.")]
        [SerializeField] private bool showWeeklyNotices = true;

        private const int MaxQueued = 6;

        private static event Action<string, float> Posted;

        /// <summary>Queues a message on the toast (if one is in the scene). seconds ≤ 0 = the default notice time.</summary>
        public static void Post(string message, float seconds = 0f) => Posted?.Invoke(message, seconds);

        private readonly Queue<(string Message, float Seconds)> _queue = new();
        private CanvasGroup _group;
        private Coroutine _showing;
        private WeeklyNotices _notices;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            if (saveManager == null) saveManager = FindFirstObjectByType<SaveManager>();
        }

        private void Start()
        {
            // After every Awake, so TransportManager has created its SeasonWeather.
            if (showWeeklyNotices)
                _notices = new WeeklyNotices(message => Enqueue(message, noticeSeconds));
        }

        private void OnEnable()
        {
            Posted += HandlePosted;

            if (saveManager != null)
            {
                saveManager.OnSaved += HandleSaved;
                saveManager.OnSaveFailed += HandleSaveFailed;
            }
        }

        private void OnDisable()
        {
            Posted -= HandlePosted;

            if (saveManager != null)
            {
                saveManager.OnSaved -= HandleSaved;
                saveManager.OnSaveFailed -= HandleSaveFailed;
            }

            // A disabled object stops its coroutines — don't leave a half-faded toast or a stale queue.
            _showing = null;
            _queue.Clear();
            if (_group != null) _group.alpha = 0f;
        }

        private void OnDestroy() => _notices?.Dispose();

        private void HandlePosted(string message, float seconds) =>
            Enqueue(message, seconds > 0f ? seconds : noticeSeconds);

        private void HandleSaved(SaveSlot slot)
        {
            if (!slot.IsAutosave) return;
            Enqueue($"Autosaved · week {saveManager.LastSaveDay}", visibleSeconds);
        }

        private void HandleSaveFailed(string reason)
        {
            // Only autosaves fail without anyone looking; a manual save shows its error in the list.
            if (saveManager.IsAutosaving) Enqueue("Autosave failed — see the console", visibleSeconds);
        }

        private void Enqueue(string message, float seconds)
        {
            if (string.IsNullOrEmpty(message) || !isActiveAndEnabled) return;

            if (_queue.Count >= MaxQueued) _queue.Dequeue();
            _queue.Enqueue((message, seconds));

            if (_showing == null)
                _showing = StartCoroutine(ShowQueue());
        }

        // Unscaled time: the toast must fade even while the game clock is paused.
        private IEnumerator ShowQueue()
        {
            while (_queue.Count > 0)
            {
                var (message, seconds) = _queue.Dequeue();
                if (messageText != null) messageText.text = message;

                yield return Fade(_group.alpha, 1f);

                float until = Time.unscaledTime + seconds;
                while (Time.unscaledTime < until) yield return null;

                yield return Fade(1f, 0f);
            }

            _showing = null;
        }

        private IEnumerator Fade(float from, float to)
        {
            for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime)
            {
                _group.alpha = Mathf.Lerp(from, to, t / fadeSeconds);
                yield return null;
            }
            _group.alpha = to;
        }
    }
}
