using UnityEngine;

// 掛在「彈藥 Prefab」上 (SAM 用飛彈外觀的 Prefab，AAA 用子彈外觀的 Prefab，都掛這個腳本)
//
// 兩種飛行模式 (由 DefenseInterceptor 依照 WeaponData.isGuided 設定):
// - 導引 (SAM)：飛行中持續追蹤目標當前位置，會轉彎
// - 非導引 (AAA)：發射瞬間鎖定方向後直線飛行，不會轉彎
//
// aimError：瞄準誤差(公尺)，由通訊完整度決定。
// - 導引彈：每幀追蹤的目標位置會加上隨機偏移，誤差越大越容易追丟
// - 非導引彈：發射瞬間鎖定的方向會加上隨機角度偏移，誤差越大越容易打偏
public class SimpleProjectile : MonoBehaviour
{
    [HideInInspector] public Transform target;
    [HideInInspector] public float speed = 30f;
    [HideInInspector] public int damage = 50;
    [HideInInspector] public bool isGuided = true;
    [HideInInspector] public float aimError = 0f;

    [Header("命中判定距離")]
    public float hitDistance = 0.5f;

    [Header("存活時間 (沒命中也會在這之後消失，避免飛出場景外一直存在)")]
    public float lifeTime = 5f;

    // 非導引彈專用：發射瞬間就固定好的飛行方向
    private Vector3 fixedDirection;

    void Start()
    {
        Destroy(gameObject, lifeTime);

        // 非導引彈：在發射的那一刻，把方向「鎖死」，之後飛行不再改變
        // 瞄準誤差會在這裡加上一次性的隨機角度偏移
        if (!isGuided && target != null)
        {
            Vector3 idealDirection = (target.position - transform.position).normalized;
            Vector3 errorOffset = Random.insideUnitSphere * aimError;
            Vector3 aimedPoint = target.position + errorOffset;

            fixedDirection = (aimedPoint - transform.position).normalized;
            // 同樣用 Y 軸朝向飛行方向，與導引彈保持一致
            transform.rotation = Quaternion.FromToRotation(Vector3.up, fixedDirection);
        }
    }

    void Update()
    {
        if (target == null)
        {
            // 導引彈失去目標就消失；非導引彈本來就不依賴目標，直線飛到 lifeTime結束即可
            if (isGuided)
            {
                Destroy(gameObject);
                return;
            }
        }

        if (isGuided)
        {
            HandleGuidedMovement();
        }
        else
        {
            HandleUnguidedMovement();
        }
    }

    // 導引：每一幀重新計算方向，持續追著目標飛 (SAM)
    // aimError 越大，追蹤的目標位置誤差越大，越容易追丟
    private void HandleGuidedMovement()
    {
        Vector3 trackedPos = target.position + Random.insideUnitSphere * aimError;

        Vector3 direction = (trackedPos - transform.position).normalized;
        transform.position += direction * speed * Time.deltaTime;
        // 用 Y 軸朝向目標（Cylinder / 飛彈模型頭部是 Y 軸方向）
        // 如果換成正式飛彈模型且頭部是 Z 軸，改回 transform.LookAt(trackedPos) 即可
        transform.rotation = Quaternion.FromToRotation(Vector3.up, direction);

        // 命中判定仍以目標「真實位置」為準，避免誤差導致永遠打不到的極端狀況
        float dist = Vector3.Distance(transform.position, target.position);
        if (dist <= hitDistance + aimError * 0.5f)
        {
            HitTarget();
        }
    }

    // 非導引：沿著發射時鎖定的方向直線飛行 (AAA)
    // 只有「飛行路徑剛好經過目標當前位置附近」才會命中，目標移動或瞄準誤差可以導致打不到
    private void HandleUnguidedMovement()
    {
        transform.position += fixedDirection * speed * Time.deltaTime;

        if (target != null)
        {
            float dist = Vector3.Distance(transform.position, target.position);
            if (dist <= hitDistance)
            {
                HitTarget();
            }
        }
    }

    private void HitTarget()
    {
        if (target != null)
        {
            DroneHealth droneHealth = target.GetComponent<DroneHealth>();
            if (droneHealth != null)
            {
                droneHealth.TakeDamage(damage);
            }
            Debug.Log($"擊中 {target.name}！造成 {damage} 傷害");
        }

        Destroy(gameObject);
    }
}