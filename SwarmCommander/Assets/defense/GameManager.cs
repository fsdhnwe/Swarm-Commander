using UnityEngine;
using UnityEngine.SceneManagement;

// 遊戲狀態管理，場景裡放一個空物件掛這個腳本就好
// 負責：接收「主堡被摧毀」的通知，觸發遊戲結束流程
public partial class GameManager : MonoBehaviour
{
    public static GameManager Instance;

    [Header("遊戲結束畫面 (在 Inspector 拖入 Canvas 或 Panel)")]
    [Tooltip("被摧毀時要顯示的 UI 物件，例如「任務失敗」的 Panel；留空則只印 Console 訊息")]
    public GameObject gameOverUI;

    [Header("遊戲結束後幾秒可以重新開始")]
    public float restartDelay = 3f;

    private bool isGameOver = false;

    void Awake()
    {
        Instance = this;

        // 遊戲開始時先確保遊戲結束畫面是隱藏的
        if (gameOverUI != null)
            gameOverUI.SetActive(false);
    }

    // 給 Headquarters.cs 呼叫
    public void TriggerGameOver()
    {
        if (isGameOver) return; // 避免重複觸發
        isGameOver = true;

        Debug.Log("=== 主堡被摧毀！任務失敗 ===");

        // 顯示遊戲結束 UI
        if (gameOverUI != null)
            gameOverUI.SetActive(true);

        // 停止時間（讓場景「凍結」，敵方無人機停止移動）
        Time.timeScale = 0f;

        // 幾秒後可以重新開始（timeScale = 0 所以要用 unscaledTime）
        StartCoroutine(RestartAfterDelay());
    }

    private System.Collections.IEnumerator RestartAfterDelay()
    {
        yield return new WaitForSecondsRealtime(restartDelay);

        // 恢復時間，重新載入當前場景
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // 給 UI 按鈕呼叫（如果之後做「立即重新開始」按鈕）
    public void RestartNow()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
