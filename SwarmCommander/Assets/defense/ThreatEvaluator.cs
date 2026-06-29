using UnityEngine;

// 威脅評估工具類（靜態，不需要掛在任何物件上）
// 給 DefenseInterceptor、MobileSAM、LaserDefense 呼叫，統一威脅評分邏輯
//
// 評分因素：
// - 無人機種類：ShahedDroneUnit（自殺型）基礎分高，因為一旦抵達就造成高傷害
// - 距離：越近威脅越高
// - Fighter 正在攻擊狀態：額外加分
// - 血量越低的目標：已被打傷，優先擊落效益高
//
// 第三關「更聰明 AI」可在此加入「已被幾個單位鎖定」的減分邏輯，避免集火浪費
public static class ThreatEvaluator
{
    [System.Serializable]
    public class ThreatWeights
    {
        [Tooltip("自殺型無人機 (ShahedDroneUnit) 基礎威脅分")]
        public float shahedBaseScore = 70f;

        [Tooltip("攻擊型無人機 (DroneUnit) 基礎威脅分")]
        public float fighterBaseScore = 50f;

        [Tooltip("未知種類無人機基礎威脅分")]
        public float unknownBaseScore = 30f;

        [Tooltip("距離權重：每少一單位距離增加多少分（distance=0 時加最多）")]
        public float distanceWeight = 2f;

        [Tooltip("Fighter 正在攻擊狀態時的額外加分")]
        public float attackingBonus = 20f;

        [Tooltip("目標血量百分比低於此值時額外加分（接近擊落）")]
        [Range(0f, 1f)]
        public float lowHPThreshold = 0.3f;

        [Tooltip("目標血量低於閾值時的額外加分")]
        public float lowHPBonus = 15f;

        [Tooltip("第三關：目標已被其他單位鎖定時的減分（避免集火浪費）")]
        public float alreadyTargetedPenalty = 25f;
    }

    // 計算單一目標的威脅分數
    // evaluatorPosition: 評估者（武器）的世界座標
    // target: 目標無人機的 Transform
    // weights: 評分權重（由各武器腳本的 Inspector 提供）
    // alreadyLockedCount: 已有幾個防守單位鎖定此目標（第三關使用，第二關傳 0）
    public static float EvaluateThreat(
        Vector3 evaluatorPosition,
        Transform target,
        ThreatWeights weights,
        int alreadyLockedCount = 0)
    {
        if (target == null) return -1f;

        float score = 0f;
        float distance = Vector3.Distance(evaluatorPosition, target.position);

        // 1. 種類基礎分
        ShahedDroneUnit shahed = target.GetComponent<ShahedDroneUnit>();
        DroneUnit fighter = target.GetComponent<DroneUnit>();

        if (shahed != null)
        {
            score += weights.shahedBaseScore;

            // Shahed 正在衝向目標時額外加分
            if (shahed.State == ShahedDroneUnit.ShahedState.Attack)
                score += weights.attackingBonus;
        }
        else if (fighter != null)
        {
            score += weights.fighterBaseScore;

            // Fighter 正在攻擊狀態時額外加分
            if (fighter.State == DroneUnit.DroneState.Attack)
                score += weights.attackingBonus;
        }
        else
        {
            score += weights.unknownBaseScore;
        }

        // 2. 距離加分（距離越近分越高）
        score += weights.distanceWeight * Mathf.Max(0f, 100f - distance);

        // 3. 低血量加分：DroneHealth.currentHP 是 private，若之後開放 CurrentHP 屬性可在此補回
        // （目前略過，不影響其他評分邏輯）

        // 4. 已被鎖定減分（第三關避免集火）
        score -= alreadyLockedCount * weights.alreadyTargetedPenalty;

        return score;
    }

    // 從清單中找出威脅分數最高的目標
    public static Transform FindHighestThreat(
        Vector3 evaluatorPosition,
        System.Collections.Generic.List<Transform> candidates,
        ThreatWeights weights,
        System.Collections.Generic.Dictionary<Transform, int> lockCountMap = null)
    {
        Transform bestTarget = null;
        float bestScore = float.NegativeInfinity;

        foreach (Transform t in candidates)
        {
            if (t == null || !t.gameObject.activeInHierarchy) continue;

            int lockedCount = 0;
            if (lockCountMap != null && lockCountMap.ContainsKey(t))
                lockedCount = lockCountMap[t];

            float score = EvaluateThreat(evaluatorPosition, t, weights, lockedCount);
            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = t;
            }
        }

        return bestTarget;
    }
}