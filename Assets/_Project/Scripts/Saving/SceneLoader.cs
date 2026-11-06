using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.Saving
{
    /// <summary>
    /// Loads a scene behind a full-screen "Loading…" cover. The cover is drawn for one frame
    /// before the load starts (so the old screen never freezes half-updated) and stays until the
    /// new scene has run its first frame — when GameBootstrap has already restored a save.
    /// Built from code and kept across the scene change, so neither scene needs anything for it.
    /// </summary>
    public class SceneLoader : MonoBehaviour
    {
        private const int CoverSortingOrder = 30000;

        private static SceneLoader _active;

        /// <summary>True while a scene is loading; further requests are ignored until it finishes.</summary>
        public static bool IsLoading => _active != null;

        public static void Load(string sceneName)
        {
            if (IsLoading)
            {
                Debug.LogWarning($"SceneLoader: already loading a scene — '{sceneName}' ignored.");
                return;
            }

            var go = new GameObject(nameof(SceneLoader));
            DontDestroyOnLoad(go);
            _active = go.AddComponent<SceneLoader>();
            _active.BuildCover();
            _active.StartCoroutine(_active.LoadRoutine(sceneName));
        }

        private IEnumerator LoadRoutine(string sceneName)
        {
            // Let the cover render before the (mostly synchronous) scene activation starts.
            yield return null;

            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (operation == null)
            {
                Debug.LogError($"SceneLoader: scene '{sceneName}' could not be loaded — is it in the build settings?");
                Finish();
                yield break;
            }

            while (!operation.isDone) yield return null;

            // One frame in the new scene: every Start (GameBootstrap's included) has run.
            yield return null;
            Finish();
        }

        private void Finish()
        {
            _active = null;
            Destroy(gameObject);
        }

        private void BuildCover()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CoverSortingOrder;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // Blocks clicks while the scene changes.
            gameObject.AddComponent<GraphicRaycaster>();

            var theme = AntiqueTradingSimulator.UI.UITheme.Load();

            var background = new GameObject("Background", typeof(RectTransform));
            background.transform.SetParent(transform, false);
            Stretch((RectTransform)background.transform);
            var image = background.AddComponent<Image>();
            image.color = theme != null ? theme.ScreenBase : Color.black;

            var label = new GameObject("Label", typeof(RectTransform));
            label.transform.SetParent(transform, false);
            Stretch((RectTransform)label.transform);
            var text = label.AddComponent<TextMeshProUGUI>();
            text.text = "Loading…";
            text.fontSize = 36f;
            text.alignment = TextAlignmentOptions.Center;
            text.color = theme != null ? theme.Accent : Color.white;
            text.raycastTarget = false;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => _active = null;
    }
}
