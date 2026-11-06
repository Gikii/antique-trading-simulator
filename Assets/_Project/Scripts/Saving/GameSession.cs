using UnityEngine;
using UnityEngine.SceneManagement;

namespace AntiqueTradingSimulator.Saving
{
    public enum GameStartKind
    {
        NewGame,
        LoadSlot
    }

    /// <summary>What the game scene should do once it has started.</summary>
    public readonly struct GameStartRequest
    {
        public readonly GameStartKind Kind;
        public readonly SaveSlot Slot;

        public GameStartRequest(GameStartKind kind, SaveSlot slot = default)
        {
            Kind = kind;
            Slot = slot;
        }
    }

    /// <summary>
    /// Moves the player between the main menu and the game. Every way into a game — new game,
    /// continue, load (also from the pause menu) — loads the game scene fresh and leaves a
    /// request here; <see cref="GameBootstrap"/> carries it out once the scene has started. So a
    /// load always lands on a clean scene, never on top of a game in progress.
    /// </summary>
    public static class GameSession
    {
        public const string MainMenuScene = "MainMenu";
        public const string GameScene = "Main";

        private static GameStartRequest? _pending;

        /// <summary>A message for the main menu to show once (e.g. why a load failed). Null if none.</summary>
        public static string PendingMessage { get; private set; }

        /// <summary>True when the main menu scene is in the build settings.</summary>
        public static bool HasMainMenu => Application.CanStreamedLevelBeLoaded(MainMenuScene);

        /// <summary>True while a scene change is under way (buttons should ignore further clicks).</summary>
        public static bool IsChangingScene => SceneLoader.IsLoading;

        public static void StartNewGame()
        {
            if (IsChangingScene) return;

            _pending = new GameStartRequest(GameStartKind.NewGame);
            SceneLoader.Load(GameScene);
        }

        /// <summary>Starts the game scene and loads <paramref name="slot"/> into it.</summary>
        public static void Load(SaveSlot slot)
        {
            if (IsChangingScene) return;

            _pending = new GameStartRequest(GameStartKind.LoadSlot, slot);
            SceneLoader.Load(GameScene);
        }

        /// <summary>Loads the newest loadable save. False (and nothing happens) if there is none.</summary>
        public static bool ContinueLatest()
        {
            var latest = SaveFiles.Store.Latest();
            if (latest == null) return false;

            Load(latest.Slot);
            return true;
        }

        /// <summary>
        /// Leaves the game for the main menu, with an optional message for it to show. False (and
        /// nothing happens) while the main menu scene is not in the build settings.
        /// </summary>
        public static bool ReturnToMainMenu(string message = null)
        {
            if (IsChangingScene) return false;

            if (!HasMainMenu)
            {
                Debug.LogWarning($"GameSession: there is no '{MainMenuScene}' scene in the build settings to return to.");
                return false;
            }

            _pending = null;
            PendingMessage = message;
            SceneLoader.Load(MainMenuScene);
            return true;
        }

        /// <summary>The message for the main menu, cleared once read.</summary>
        public static string TakeMessage()
        {
            string message = PendingMessage;
            PendingMessage = null;
            return message;
        }

        /// <summary>What the game scene should do, cleared once read. Null = nothing asked (a new game).</summary>
        internal static GameStartRequest? TakePendingRequest()
        {
            var request = _pending;
            _pending = null;
            return request;
        }

        // Every time the game scene loads (also when Play is pressed with it open in the editor),
        // a bootstrap object is added to it to carry out the request.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != GameScene || mode != LoadSceneMode.Single) return;

            var bootstrap = new GameObject(nameof(GameBootstrap));
            SceneManager.MoveGameObjectToScene(bootstrap, scene);
            bootstrap.AddComponent<GameBootstrap>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            _pending = null;
            PendingMessage = null;
        }
    }
}
