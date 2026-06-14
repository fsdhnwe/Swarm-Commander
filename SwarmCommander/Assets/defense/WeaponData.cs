using UnityEngine;

// 這是一個「資料容器」，不會直接掛在物件上
// 之後在 Project 視窗右鍵 → Create → Defense → Weapon Data 可以建立出來
// SAM 和 AAA 各建立一份，填不同數值即可，DefenseInterceptor.cs 會讀取這裡的數值
[CreateAssetMenu(fileName = "NewWeaponData", menuName = "Defense/Weapon Data")]
public class WeaponData : ScriptableObject
{
    [Header("基本資訊")]
    public string weaponName = "SAM Site";

    [Header("現實對應 (僅文件用途，不影響邏輯)")]
    [Tooltip("例如：NASAMS / IRIS-T (SAM)，Gepard (AAA)")]
    public string realWorldReference = "";

    [Header("範圍設定")]
    [Tooltip("偵測範圍：超過此距離不會注意到目標")]
    public float detectionRange = 30f;

    [Tooltip("攻擊範圍：目標進入此距離內才會開火")]
    public float fireRange = 25f;

    [Header("攻擊設定")]
    [Tooltip("冷卻時間(秒)：開火後要等多久才能再開火。\n" +
             "SAM 建議設長一點(例如 3~4)，AAA 建議設短(例如 0.3~0.5)模擬連發")]
    public float fireRate = 2f;

    [Tooltip("每次命中造成的傷害")]
    public int damage = 50;

    [Tooltip("飛彈/子彈飛行速度")]
    public float projectileSpeed = 30f;

    [Header("彈藥外觀 (依武器類型用不同 Prefab)")]
    [Tooltip("SAM 拖入「飛彈」外觀的 Prefab，AAA 拖入「子彈/曳光彈」外觀的 Prefab\n" +
             "兩者都要掛 SimpleProjectile.cs，外觀可以不同(大小/拖尾特效)")]
    public GameObject projectilePrefab;

    [Header("導引特性")]
    [Tooltip("是否為導引彈：\n" +
             "勾選 (SAM)：飛行中持續修正方向追蹤目標，幾乎必中\n" +
             "不勾 (AAA)：發射瞬間鎖定方向後直線飛行，不會轉彎，目標移動可能會被躲開")]
    public bool isGuided = true;

    [Header("通訊依賴度設定")]
    [Tooltip("從鎖定目標到實際開火的基礎反應延遲(秒)。\n" +
             "通訊完整度下降時，這個延遲會被放大(最多放大到約3倍)")]
    public float reactionDelayBase = 0.5f;

    [Tooltip("基礎瞄準誤差(公尺)，0 = 通訊正常時完全精準。\n" +
             "通訊完整度下降時，誤差會被放大(最多放大到約5倍)，模擬失去導引修正")]
    public float baseAimError = 0f;
}
