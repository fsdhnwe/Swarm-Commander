using UnityEngine;

// 測試用：掛在「玩家無人機」的方塊/球體上
// 用方向鍵移動它，靠近雷達站看看 Console 有沒有印出偵測訊息
// 記得：這個物件的 Tag 要設成 "PlayerDrone" (在 Inspector 左上角 Tag 下拉選單新增)
public class PlayerDroneMarker : MonoBehaviour
{
    public float moveSpeed = 5f;

    void Update()
    {
        float h = Input.GetAxis("Horizontal"); // 左右鍵 / A D
        float v = Input.GetAxis("Vertical");   // 上下鍵 / W S

        transform.Translate(new Vector3(h, 0, v) * moveSpeed * Time.deltaTime);
    }
}
