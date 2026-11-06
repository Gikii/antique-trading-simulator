using UnityEngine;

namespace AntiqueTradingSimulator.Saving
{
    /// <summary>
    /// Carries out the <see cref="GameSession"/> request in the freshly loaded game scene.
    /// Added to the scene automatically by GameSession — it is not placed in the scene by hand.
    /// Its Start runs after every other Start (execution order 1000), so the managers have built
    /// a fresh game and subscribed to each other before a save is restored over it.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class GameBootstrap : MonoBehaviour
    {
        private void Start()
        {
            var request = GameSession.TakePendingRequest();

            if (request.HasValue && request.Value.Kind == GameStartKind.LoadSlot)
                LoadSlot(request.Value.Slot);

            Destroy(gameObject);
        }

        private static void LoadSlot(SaveSlot slot)
        {
            var saveManager = FindFirstObjectByType<SaveManager>();
            if (saveManager == null)
            {
                Debug.LogError($"GameBootstrap: there is no SaveManager in the scene to load {slot.DisplayName} with — starting a new game instead.");
                return;
            }

            string failure = null;
            void RememberFailure(string reason) => failure = reason;

            saveManager.OnLoadFailed += RememberFailure;
            bool loaded = saveManager.Load(slot);
            saveManager.OnLoadFailed -= RememberFailure;

            if (loaded) return;

            // The scene now holds a fresh game. Back to the menu with the reason if there is one;
            // without a menu the fresh game simply stays.
            if (!GameSession.ReturnToMainMenu($"Couldn't load {slot.DisplayName}. {failure}"))
                Debug.LogWarning($"GameBootstrap: {slot.DisplayName} could not be loaded ({failure}) — continuing with a new game.");
        }
    }
}
