using UnityEngine;
using System.Collections.Generic;

public class CameraController : MonoBehaviour
{
    public float minSize = 5f; // 최소 카메라 사이즈
    public float maxSize = 10f; // 최대 카메라 사이즈
    public float sizePadding = 2f; // 플레이어 경계와 카메라 경계 사이 여유
    public float cameraMoveSpeed = 5f;
    public float cameraZoomSpeed = 5f;

    private Camera cam;
    private List<Transform> playerTargets = new List<Transform>();

    void Start()
    {
        cam = GetComponent<Camera>();
        // 플레이어 태그가 "Player"라고 가정
        foreach (var go in GameObject.FindGameObjectsWithTag("Player"))
        {
            playerTargets.Add(go.transform);
        }
    }

    void Update()
    {
        if (playerTargets.Count == 0) return;

        // 플레이어가 추가/제거될 수 있으니 매 프레임 동기화
        SyncPlayerList();

        // 카메라 위치 중앙으로 이동
        Vector3 center = GetCenterPoint();
        transform.position = Vector3.Lerp(transform.position, new Vector3(center.x, center.y, transform.position.z), Time.deltaTime * cameraMoveSpeed);

        // 카메라 사이즈 조절
        float requiredSize = GetRequiredSize(center);
        float targetSize = Mathf.Clamp(requiredSize, minSize, maxSize);
        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetSize, Time.deltaTime * cameraZoomSpeed);

        // 최대 카메라 사이즈일 때 플레이어가 화면 밖으로 못 나가게 제한
        if (Mathf.Approximately(cam.orthographicSize, maxSize))
        {
            ClampPlayersInCamera();
        }
    }

    void SyncPlayerList()
    {
        playerTargets.Clear();
        foreach (var go in GameObject.FindGameObjectsWithTag("Player"))
        {
            playerTargets.Add(go.transform);
        }
    }

    Vector3 GetCenterPoint()
    {
        if (playerTargets.Count == 1)
            return playerTargets[0].position;

        var bounds = new Bounds(playerTargets[0].position, Vector3.zero);
        foreach (var t in playerTargets)
        {
            bounds.Encapsulate(t.position);
        }
        return bounds.center;
    }

    float GetRequiredSize(Vector3 center)
    {
        float size = minSize;
        foreach (var t in playerTargets)
        {
            Vector3 diff = t.position - center;
            float distX = Mathf.Abs(diff.x) / cam.aspect;
            float distY = Mathf.Abs(diff.y);
            size = Mathf.Max(size, distX + sizePadding, distY + sizePadding);
        }
        return size;
    }

    void ClampPlayersInCamera()
    {
        float vertExtent = cam.orthographicSize;
        float horzExtent = vertExtent * cam.aspect;
        Vector3 camPos = transform.position;

        foreach (var t in playerTargets)
        {
            Vector3 pos = t.position;
            float minX = camPos.x - horzExtent;
            float maxX = camPos.x + horzExtent;
            float minY = camPos.y - vertExtent;
            float maxY = camPos.y + vertExtent;

            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            pos.y = Mathf.Clamp(pos.y, minY, maxY);
            t.position = pos;
        }
    }
}