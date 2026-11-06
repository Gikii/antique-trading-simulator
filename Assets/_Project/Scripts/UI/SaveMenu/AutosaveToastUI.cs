using System.Collections;
using AntiqueTradingSimulator.Saving;
using TMPro;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// A short "Autosaved · day 37" note after each autosave, fading in and out. Manual saves are
    /// reported by the save list itself. Never blocks clicks.
    /// </summary>
    [RequireComponent(typeof(CanvasGroup))]
    public class AutosaveToastUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private SaveManager saveManager;

        [Min(0.1f)] [SerializeField] private float visibleSeconds = 2.5f;
        [Min(0.01f)] [SerializeField] private float fadeSeconds = 0.3f;

        private CanvasGroup _group;
        private Coroutine _showing;

        private void Awake()
        {
            _group = GetComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            if (saveManager == null) saveManager = FindFirstObjectByType<SaveManager>();
        }

        private void OnEnable()
        {
            if (saveManager != null)
            {
                saveManager.OnSaved += HandleSaved;
                saveManager.OnSaveFailed += HandleSaveFailed;
            }
        }

        private void OnDisable()
        {
            if (saveManager != null)
            {
                saveManager.OnSaved -= HandleSaved;
                saveManager.OnSaveFailed -= HandleSaveFailed;
            }
        }

        private void HandleSaved(SaveSlot slot)
        {
            if (!slot.IsAutosave) return;
            Show($"Autosaved · day {saveManager.LastSaveDay}");
        }

        private void HandleSaveFailed(string reason)
        {
            // Only autosaves fail without anyone looking; a manual save shows its error in the list.
            if (saveManager.IsAutosaving) Show("Autosave failed — see the console");
        }

        private void Show(string message)
        {
            if (messageText != null) messageText.text = message;

            if (_showing != null) StopCoroutine(_showing);
            _showing = StartCoroutine(ShowRoutine());
        }

        // Unscaled time: the toast must fade even while the game clock is paused.
        private IEnumerator ShowRoutine()
        {
            yield return Fade(_group.alpha, 1f);

            float until = Time.unscaledTime + visibleSeconds;
            while (Time.unscaledTime < until) yield return null;

            yield return Fade(1f, 0f);
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
