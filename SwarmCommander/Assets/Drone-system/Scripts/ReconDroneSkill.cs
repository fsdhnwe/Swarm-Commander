using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(DroneUnit))]
public class ReconDroneSkill : MonoBehaviour
{
    [Header("Scan")]
    public LayerMask scanTargetLayer = ~0;
    public float scanRadius = 20f;
    public float scanDuration = 1.5f;
    public float confirmedIntelDuration = 5f;
    public float staleIntelDuration = 10f;
    public bool scanWhenMoveArrives = true;

    [Header("Scan Visual Effect")]
    public GameObject scanEffectRoot;
    public ParticleSystem[] scanParticles;
    public bool hideScanEffectWhenIdle = true;
    public bool restartScanEffectOnScan = true;

    [Header("Shader Radius")]
    public Renderer scanEffectRenderer;
    public string radiusPropertyName = "_Radius";
    public bool driveAsNormalizedProgress = false;
    public AnimationCurve scanExpansionCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    [Header("Debug")]
    [Tooltip("開啟後會在 Console 印出特效播放的每個步驟，方便排查為什麼看不到效果。確認沒問題後可以關掉。")]
    public bool verboseLogging = true;

    private readonly Collider[] _scanBuffer = new Collider[128];
    private readonly HashSet<EnemyIntelVisibility> _revealedThisScan = new HashSet<EnemyIntelVisibility>();
    private DroneUnit _drone;
    private Coroutine _scanRoutine;
    private MaterialPropertyBlock _mpb;

    public bool IsScanning => _scanRoutine != null;

    void Awake()
    {
        _drone = GetComponent<DroneUnit>();

        if (scanEffectRoot == null && scanEffectRenderer != null)
            scanEffectRoot = scanEffectRenderer.gameObject;

        if ((scanParticles == null || scanParticles.Length == 0) && scanEffectRoot != null)
            scanParticles = scanEffectRoot.GetComponentsInChildren<ParticleSystem>(true);

        if (scanEffectRenderer != null)
            _mpb = new MaterialPropertyBlock();

        SetEffectRadius(0f, 0f);

        if (hideScanEffectWhenIdle)
            SetScanEffectActive(false);

        // ---- 設定健檢：這幾個警告是最常見的「掛上去看不到效果」原因 ----
        if (scanEffectRoot == null && scanEffectRenderer == null)
        {
            Debug.LogWarning(
                $"[{name}] scanEffectRoot 和 scanEffectRenderer 都沒有指定！" +
                $"SetScanEffectActive() 會完全沒有對象可以開關，特效物件不會被啟用。" +
                $"請把 Scan_Particle 物件拖進 Inspector 的 scanEffectRoot 欄位。", this);
        }

        if (scanParticles == null || scanParticles.Length == 0)
        {
            Debug.LogWarning(
                $"[{name}] scanParticles 是空的！BeginScanEffect() 不會 Play 任何 ParticleSystem。" +
                $"請確認 scanEffectRoot 底下真的有 ParticleSystem 元件，或手動把它拖進 scanParticles 陣列。", this);
        }
        else if (verboseLogging)
        {
            Debug.Log($"[{name}] Awake: 找到 {scanParticles.Length} 個 ParticleSystem -> " +
                string.Join(", ", System.Array.ConvertAll(scanParticles, p => p != null ? p.name : "null")), this);
        }
    }

    void OnEnable()
    {
        if (_drone != null)
            _drone.MoveArrived += HandleMoveArrived;
    }

    void OnDisable()
    {
        if (_drone != null)
            _drone.MoveArrived -= HandleMoveArrived;
    }

    public void StartScan()
    {
        if (verboseLogging)
            Debug.Log($"[{name}] StartScan() 被呼叫，time={Time.time:F2}", this);

        if (_scanRoutine != null)
            StopCoroutine(_scanRoutine);

        _scanRoutine = StartCoroutine(ScanRoutine());
    }

    void HandleMoveArrived(DroneUnit drone)
    {
        if (scanWhenMoveArrives)
            StartScan();
    }

    IEnumerator ScanRoutine()
    {
        _revealedThisScan.Clear();
        BeginScanEffect();

        if (scanDuration <= 0f)
        {
            SetEffectRadius(scanRadius, 1f);
            RevealTargetsInRadius(scanRadius);
            EndScanEffect();
            _scanRoutine = null;
            yield break;
        }

        float elapsed = 0f;

        while (elapsed < scanDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / scanDuration);
            float curvedT = scanExpansionCurve.Evaluate(t);
            float currentRadius = scanRadius * curvedT;

            SetEffectRadius(currentRadius, curvedT);
            RevealTargetsInRadius(currentRadius);

            yield return null;
        }

        SetEffectRadius(scanRadius, 1f);
        RevealTargetsInRadius(scanRadius);
        EndScanEffect();

        _scanRoutine = null;
    }

    void SetEffectRadius(float worldRadius, float normalizedProgress)
    {
        if (scanEffectRenderer == null) return;

        if (_mpb == null)
            _mpb = new MaterialPropertyBlock();

        float value = driveAsNormalizedProgress ? normalizedProgress : worldRadius;

        scanEffectRenderer.GetPropertyBlock(_mpb);
        _mpb.SetFloat(radiusPropertyName, value);
        scanEffectRenderer.SetPropertyBlock(_mpb);
    }

    void BeginScanEffect()
    {
        SetScanEffectActive(true);
        SetEffectRadius(0f, 0f);

        if (verboseLogging)
        {
            bool rootActiveInHierarchy = scanEffectRoot != null
                ? scanEffectRoot.activeInHierarchy
                : (scanEffectRenderer != null && scanEffectRenderer.gameObject.activeInHierarchy);

            Debug.Log($"[{name}] BeginScanEffect: 物件 active = {rootActiveInHierarchy}", this);

            if (!rootActiveInHierarchy)
            {
                GameObject target = scanEffectRoot != null ? scanEffectRoot : scanEffectRenderer.gameObject;
                LogParentChainActiveState(target);
            }
        }

        if (scanParticles == null || scanParticles.Length == 0)
        {
            Debug.LogWarning($"[{name}] BeginScanEffect: scanParticles 是空的，沒有任何粒子系統會被播放。", this);
            return;
        }

        foreach (ParticleSystem particle in scanParticles)
        {
            if (particle == null) continue;

            if (restartScanEffectOnScan)
                particle.Clear(true);

            particle.Play(true);

            if (verboseLogging)
            {
                Debug.Log($"[{name}] Played '{particle.name}': isPlaying={particle.isPlaying}, " +
                    $"particleCount={particle.particleCount}, activeInHierarchy={particle.gameObject.activeInHierarchy}",
                    particle);
            }
        }
    }

    void EndScanEffect()
    {
        if (scanParticles != null)
        {
            foreach (ParticleSystem particle in scanParticles)
            {
                if (particle != null)
                    particle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        if (hideScanEffectWhenIdle)
            SetScanEffectActive(false);

        if (verboseLogging)
            Debug.Log($"[{name}] EndScanEffect: 掃描結束", this);
    }

    void LogParentChainActiveState(GameObject go)
    {
        string chain = "";
        Transform t = go.transform;

        while (t != null)
        {
            chain += $"{t.name}(activeSelf={t.gameObject.activeSelf}) -> ";
            t = t.parent;
        }

        Debug.LogWarning($"[{name}] 父物件鏈（由下往上）: {chain}(無父物件)。" +
            $"找出第一個 activeSelf=False 的那一層，那就是卡住的元凶。", go);
    }

    void SetScanEffectActive(bool active)
    {
        if (scanEffectRoot != null)
        {
            scanEffectRoot.SetActive(active);
            return;
        }

        if (scanEffectRenderer != null)
        {
            scanEffectRenderer.gameObject.SetActive(active);
            return;
        }

        if (verboseLogging)
            Debug.LogWarning($"[{name}] SetScanEffectActive({active}): 沒有任何物件可以開關（scanEffectRoot 和 scanEffectRenderer 都是 null）。", this);
    }

    void RevealTargetsInRadius(float radius)
    {
        int count = Physics.OverlapSphereNonAlloc(
            transform.position,
            radius,
            _scanBuffer,
            scanTargetLayer,
            QueryTriggerInteraction.Collide);

        for (int i = 0; i < count; i++)
        {
            Collider hit = _scanBuffer[i];
            if (hit == null) continue;

            EnemyIntelVisibility intel = hit.GetComponentInParent<EnemyIntelVisibility>();
            if (intel == null) continue;
            if (_revealedThisScan.Contains(intel)) continue;

            intel.Confirm(confirmedIntelDuration, staleIntelDuration);
            _revealedThisScan.Add(intel);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, scanRadius);
    }
}