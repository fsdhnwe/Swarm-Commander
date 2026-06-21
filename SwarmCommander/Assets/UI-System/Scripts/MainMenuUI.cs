using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour
{
    private const string ResolutionWidthKey = "Settings.ResolutionWidth";
    private const string ResolutionHeightKey = "Settings.ResolutionHeight";
    private const string FullscreenKey = "Settings.Fullscreen";

    [Header("Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject settingsPanel;

    [Header("Scene")]
    [SerializeField] private string startSceneName;
    [SerializeField] private int startSceneBuildIndex = 1;
    [SerializeField] private bool loadBySceneName = true;

    [Header("Camera Settings")]
    [SerializeField] private TopDownCameraController cameraController;
    [SerializeField] private Slider cameraMoveSpeedSlider;
    [SerializeField] private TMP_Text cameraMoveSpeedText;
    [SerializeField] private float defaultCameraMoveSpeed = 20f;

    [Header("Display Settings")]
    [SerializeField] private TMP_Dropdown resolutionDropdown;
    [SerializeField] private Toggle fullscreenToggle;

    private readonly List<Resolution> availableResolutions = new();
    private bool isInitializing;

    private void Awake()
    {
        if (cameraController == null)
            cameraController = FindAnyObjectByType<TopDownCameraController>();
    }

    private void Start()
    {
        isInitializing = true;
        InitializeCameraSettings();
        InitializeResolutionSettings();
        ShowMainPanel();
        isInitializing = false;
    }

    public void StartGame()
    {
        ApplySettings();

        if (loadBySceneName && !string.IsNullOrWhiteSpace(startSceneName))
            SceneManager.LoadScene(startSceneName);
        else
            SceneManager.LoadScene(startSceneBuildIndex);
    }

    public void OpenSettings()
    {
        if (mainPanel != null)
            mainPanel.SetActive(false);

        if (settingsPanel != null)
            settingsPanel.SetActive(true);
    }

    public void CloseSettings()
    {
        ApplySettings();
        ShowMainPanel();
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    public void ApplySettings()
    {
        ApplyCameraMoveSpeed();
        ApplyResolution();
        PlayerPrefs.Save();
    }

    public void HandleCameraMoveSpeedChanged(float value)
    {
        UpdateCameraMoveSpeedText(value);
        if (!isInitializing)
            ApplyCameraMoveSpeed();
    }

    public void HandleResolutionChanged(int index)
    {
        if (!isInitializing)
            ApplyResolution();
    }

    public void HandleFullscreenChanged(bool fullscreen)
    {
        if (!isInitializing)
            ApplyResolution();
    }

    private void ShowMainPanel()
    {
        if (mainPanel != null)
            mainPanel.SetActive(true);

        if (settingsPanel != null)
            settingsPanel.SetActive(false);
    }

    private void InitializeCameraSettings()
    {
        float savedMoveSpeed = PlayerPrefs.GetFloat(
            TopDownCameraController.MoveSpeedPrefsKey,
            cameraController != null ? cameraController.moveSpeed : defaultCameraMoveSpeed);

        if (cameraMoveSpeedSlider != null)
        {
            cameraMoveSpeedSlider.SetValueWithoutNotify(savedMoveSpeed);
            cameraMoveSpeedSlider.onValueChanged.RemoveListener(HandleCameraMoveSpeedChanged);
            cameraMoveSpeedSlider.onValueChanged.AddListener(HandleCameraMoveSpeedChanged);
        }

        UpdateCameraMoveSpeedText(savedMoveSpeed);
        ApplyCameraMoveSpeed();
    }

    private void InitializeResolutionSettings()
    {
        if (resolutionDropdown != null)
        {
            BuildResolutionOptions();
            resolutionDropdown.onValueChanged.RemoveListener(HandleResolutionChanged);
            resolutionDropdown.onValueChanged.AddListener(HandleResolutionChanged);
        }

        if (fullscreenToggle != null)
        {
            bool fullscreen = PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) == 1;
            fullscreenToggle.SetIsOnWithoutNotify(fullscreen);
            fullscreenToggle.onValueChanged.RemoveListener(HandleFullscreenChanged);
            fullscreenToggle.onValueChanged.AddListener(HandleFullscreenChanged);
        }
    }

    private void BuildResolutionOptions()
    {
        availableResolutions.Clear();
        resolutionDropdown.ClearOptions();

        Resolution[] resolutions = Screen.resolutions;
        int savedWidth = PlayerPrefs.GetInt(ResolutionWidthKey, Screen.currentResolution.width);
        int savedHeight = PlayerPrefs.GetInt(ResolutionHeightKey, Screen.currentResolution.height);
        int selectedIndex = 0;
        List<string> labels = new();

        for (int i = 0; i < resolutions.Length; i++)
        {
            Resolution resolution = resolutions[i];
            bool duplicate = availableResolutions.Exists(r => r.width == resolution.width && r.height == resolution.height);
            if (duplicate) continue;

            int optionIndex = availableResolutions.Count;
            availableResolutions.Add(resolution);
            labels.Add($"{resolution.width} x {resolution.height}");

            if (resolution.width == savedWidth && resolution.height == savedHeight)
                selectedIndex = optionIndex;
        }

        if (availableResolutions.Count == 0)
        {
            Resolution fallback = Screen.currentResolution;
            availableResolutions.Add(fallback);
            labels.Add($"{fallback.width} x {fallback.height}");
        }

        resolutionDropdown.AddOptions(labels);
        resolutionDropdown.SetValueWithoutNotify(Mathf.Clamp(selectedIndex, 0, availableResolutions.Count - 1));
        resolutionDropdown.RefreshShownValue();
    }

    private void ApplyCameraMoveSpeed()
    {
        float speed = cameraMoveSpeedSlider != null
            ? cameraMoveSpeedSlider.value
            : PlayerPrefs.GetFloat(TopDownCameraController.MoveSpeedPrefsKey, defaultCameraMoveSpeed);

        PlayerPrefs.SetFloat(TopDownCameraController.MoveSpeedPrefsKey, speed);

        if (cameraController != null)
            cameraController.SetMoveSpeed(speed);

        UpdateCameraMoveSpeedText(speed);
    }

    private void UpdateCameraMoveSpeedText(float value)
    {
        if (cameraMoveSpeedText != null)
            cameraMoveSpeedText.text = value.ToString("0");
    }

    private void ApplyResolution()
    {
        if (resolutionDropdown == null || availableResolutions.Count == 0) return;

        int index = Mathf.Clamp(resolutionDropdown.value, 0, availableResolutions.Count - 1);
        Resolution resolution = availableResolutions[index];
        bool fullscreen = fullscreenToggle != null ? fullscreenToggle.isOn : Screen.fullScreen;

        Screen.SetResolution(resolution.width, resolution.height, fullscreen);
        PlayerPrefs.SetInt(ResolutionWidthKey, resolution.width);
        PlayerPrefs.SetInt(ResolutionHeightKey, resolution.height);
        PlayerPrefs.SetInt(FullscreenKey, fullscreen ? 1 : 0);
    }
}
