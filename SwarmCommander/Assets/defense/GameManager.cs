using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("Mission Complete UI")]
    [Tooltip("Panel shown after the command center is destroyed. Use this for the next-level button.")]
    public GameObject missionCompleteUI;

    [Header("Game Over UI")]
    [Tooltip("Panel shown when the player has no drones and cannot afford any new drone.")]
    public GameObject gameOverUI;

    [Header("Defeat Check")]
    public bool checkDefeatByMoneyAndDrones = true;
    public float defeatCheckInterval = 0.25f;
    public float defeatCheckStartDelay = 1f;
    public DroneAbilityPanelUI abilityPanel;
    public SelectionManager defeatSelectionManager;

    [Header("Next Level")]
    [Tooltip("When true, LoadNextLevel loads by scene name. When false, it loads by build index.")]
    public bool loadNextLevelBySceneName = true;

    [Tooltip("Scene name to load after this level, for example Level2 or Level3.")]
    public string nextLevelSceneName;

    [Tooltip("Build index to load when loadNextLevelBySceneName is false. Use -1 to load active scene index + 1.")]
    public int nextLevelBuildIndex = -1;

    [Header("Main Menu")]
    public string mainMenuSceneName = "MainMenu";

    [Header("Optional Auto Restart")]
    [Tooltip("Mostly for testing. Leave this off when using a mission-complete panel.")]
    public bool autoRestartCurrentLevel = false;

    public float restartDelay = 3f;

    private bool isMissionComplete;
    private bool isGameOver;

    private void Awake()
    {
        Instance = this;

        if (missionCompleteUI != null)
            missionCompleteUI.SetActive(false);

        if (gameOverUI != null && gameOverUI != missionCompleteUI)
            gameOverUI.SetActive(false);

        if (abilityPanel == null)
            abilityPanel = FindAnyObjectByType<DroneAbilityPanelUI>();

        if (defeatSelectionManager == null)
            defeatSelectionManager = FindAnyObjectByType<SelectionManager>();

        StartCoroutine(DefeatCheckLoop());
    }

    // Called by Headquarters.cs when the command center is destroyed.
    public void TriggerGameOver()
    {
        TriggerMissionComplete();
    }

    public void TriggerMissionComplete()
    {
        if (isMissionComplete) return;
        isMissionComplete = true;

        Debug.Log("[GameManager] Mission complete. Command center destroyed.");

        if (missionCompleteUI != null)
            missionCompleteUI.SetActive(true);

        if (defeatSelectionManager != null)
            defeatSelectionManager.InputBlocked = true;

        Time.timeScale = 0f;

        if (autoRestartCurrentLevel)
            StartCoroutine(RestartAfterDelay());
    }

    private void CheckPlayerDefeat()
    {
        if (abilityPanel == null)
            abilityPanel = FindAnyObjectByType<DroneAbilityPanelUI>();

        if (defeatSelectionManager == null)
            defeatSelectionManager = FindAnyObjectByType<SelectionManager>();

        if (abilityPanel == null || defeatSelectionManager == null)
            return;

        bool hasAliveDrone = defeatSelectionManager.GetAliveDroneCount() > 0;
        bool canAffordDrone = abilityPanel.CanAffordAnyAvailableDrone();

        if (!hasAliveDrone && !canAffordDrone)
            TriggerPlayerDefeated();
    }

    public void TriggerPlayerDefeated()
    {
        if (isMissionComplete || isGameOver) return;
        isGameOver = true;

        Debug.Log("[GameManager] Game over. No drones remain and no affordable drone is available.");

        if (gameOverUI != null)
            gameOverUI.SetActive(true);

        if (defeatSelectionManager != null)
            defeatSelectionManager.InputBlocked = true;

        Time.timeScale = 0f;
    }

    private IEnumerator RestartAfterDelay()
    {
        yield return new WaitForSecondsRealtime(restartDelay);
        RestartNow();
    }

    private IEnumerator DefeatCheckLoop()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, defeatCheckStartDelay));

        while (true)
        {
            if (checkDefeatByMoneyAndDrones && !isMissionComplete && !isGameOver)
                CheckPlayerDefeat();

            yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, defeatCheckInterval));
        }
    }

    public void LoadNextLevel()
    {
        Time.timeScale = 1f;

        if (loadNextLevelBySceneName && !string.IsNullOrWhiteSpace(nextLevelSceneName))
        {
            SceneManager.LoadScene(nextLevelSceneName);
            return;
        }

        int targetBuildIndex = nextLevelBuildIndex >= 0
            ? nextLevelBuildIndex
            : SceneManager.GetActiveScene().buildIndex + 1;

        if (targetBuildIndex >= 0 && targetBuildIndex < SceneManager.sceneCountInBuildSettings)
        {
            SceneManager.LoadScene(targetBuildIndex);
            return;
        }

        Debug.LogWarning("[GameManager] No valid next level is configured.");
    }

    public void RestartNow()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    public void LoadMainMenu()
    {
        LoadSceneByName(mainMenuSceneName);
    }

    public void LoadSceneByName(string sceneName)
    {
        if (string.IsNullOrWhiteSpace(sceneName))
        {
            Debug.LogWarning("[GameManager] Cannot load an empty scene name.");
            return;
        }

        Time.timeScale = 1f;
        SceneManager.LoadScene(sceneName);
    }
}
