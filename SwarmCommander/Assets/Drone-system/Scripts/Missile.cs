using UnityEngine;

[RequireComponent(typeof(TrailRenderer))]
public class Missile : MonoBehaviour
{
    public float speed = 40f;
    public float damage = 50f;
    public TargetableObject target;
    public Vector3 rotationOffset = new Vector3(0f, 90f, 0f);
    public GameObject hitEffectPrefab; // 爆炸特效

    [Header("Trail")]
    public float trailTime = 0.5f;
    public float trailStartWidth = 0.15f;
    public float trailEndWidth = 0.0f;
    public Color trailColor = Color.white;

    private TrailRenderer _trail;

    void Awake()
    {
        _trail = GetComponent<TrailRenderer>();
        _trail.time = trailTime;
        _trail.startWidth = trailStartWidth;
        _trail.endWidth = trailEndWidth;
        _trail.startColor = trailColor;
        _trail.endColor = new Color(trailColor.r, trailColor.g, trailColor.b, 0f);
    }

    void Update()
    {
        if (target == null || !target.IsAlive)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 targetPos = target.AimPosition;
        Vector3 dir = (targetPos - transform.position).normalized;

        transform.position += dir * speed * Time.deltaTime;
        transform.rotation = Quaternion.LookRotation(dir) * Quaternion.Euler(rotationOffset);

        if (Vector3.Distance(transform.position, targetPos) < 0.5f)
        {
            target.TakeDamage(damage);

            if (hitEffectPrefab != null) {
                GameObject effectInstance = Instantiate(hitEffectPrefab, transform.position, Quaternion.identity);
                Destroy(effectInstance, 2.0f);
            }

            // 讓 trail 殘留淡出，不要瞬間消失
            _trail.transform.parent = null;
            Destroy(gameObject);
            Destroy(_trail.gameObject, trailTime);
        }
    }
}
