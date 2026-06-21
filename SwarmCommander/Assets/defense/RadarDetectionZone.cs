using UnityEngine;

// 自動掛在 RadarStation2 建立的 DetectionZone 子物件上
// 把 Trigger 事件轉發給父物件的 RadarStation2
// 不需要手動掛，RadarStation2.Awake() 會自動處理
public class RadarDetectionZone : MonoBehaviour
{
    private RadarStation2 radar;

    public void Init(RadarStation2 parent)
    {
        radar = parent;
    }

    void OnTriggerEnter(Collider other)
    {
        Debug.Log($"[RadarDetectionZone] TriggerEnter: {other.name}, radar={radar}");
        if (radar != null) radar.OnDetectionTriggerEnter(other);
       
    }

    void OnTriggerExit(Collider other)
    {
        if (radar != null) radar.OnDetectionTriggerExit(other);
    }
}
