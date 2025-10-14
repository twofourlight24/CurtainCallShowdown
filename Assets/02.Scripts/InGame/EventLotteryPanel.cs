using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;

public class EventLotteryPanel : MonoBehaviourPunCallbacks
{
    [Header("Wiring")]
    public GameObject panelRoot;           // 전체 패널 루트
    public RectTransform reelViewport;     // 마스크 영역(중앙 판정선 기준)
    public RectTransform reelContent;      // 아이템들이 들어가는 Content
    public RectTransform itemTemplate;     // 비활성 템플릿(없으면 자동으로 생성/발견)
    public TMP_Text resultText;            // "뽑는 중..." → 결과 텍스트

    [Header("Spin Config")]
    public float preSpinTime = 2f;         // 가속 시간
    public float steadyTime = 2f;          // 유지 시간
    public float decelTime = 5f;           // 감속 시간
    public float ppsBase = 2000f;          // 기본 속도(px/sec)
    public int loopMultiplier = 20;        // 후보 세트 반복 횟수
    public float itemSpacing = 0f;         // 아이템 간격
    public float resultHoldSec = 3f;       // 결과 표시 유지 시간

    // 룸 프로퍼티 키
    private const string PROP_LOT_OPTIONS = "LotteryOptions";
    private const string PROP_LOT_WINNER = "LotteryWinner";
    private const string PROP_LOT_DONE = "LotteryDone";

    // 내부 상태
    private List<string> options = new();
    private string winnerId = "";
    private float itemHeight = 125f;       // 템플릿 기준 높이
    private float totalHeight = 0f;
    private bool isRunning = false;

    void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);

        // 뷰포트 마스크 보장
        if (reelViewport != null && reelViewport.GetComponent<RectMask2D>() == null)
            reelViewport.gameObject.AddComponent<RectMask2D>();

        // 템플릿 자동 보정(있으면 비활성)
        EnsureTemplate();
        if (itemTemplate != null) itemTemplate.gameObject.SetActive(false);
    }
    // === 새 경로: RPC로 바로 받은 데이터로 즉시 오픈 ===
    public void OpenImmediate(string optionsCsv, string winId)
    {
        if (string.IsNullOrEmpty(optionsCsv) || string.IsNullOrEmpty(winId))
        {
            Debug.LogWarning("[Lottery] OpenImmediate invalid data");
            return;
        }

        // 패널 보이기
        if (panelRoot != null && !panelRoot.activeSelf) panelRoot.SetActive(true);

        // 템플릿 자동 보정
        if (!EnsureTemplate()) return;

        options = optionsCsv.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        winnerId = winId;

        BuildReel(options);

        if (resultText) resultText.text = "라운드 이벤트를 추첨 중...";

        StopAllCoroutines();
        StartCoroutine(Co_SpinAndStop(winnerId));
    }

    // --- 인스펙터 누락 대비: 템플릿 자동 탐색/생성 ---
    bool EnsureTemplate()
    {
        if (itemTemplate != null) return true;

        // 1) reelContent 자식 중 첫 번째를 템플릿으로
        if (reelContent != null && reelContent.childCount > 0)
        {
            itemTemplate = reelContent.GetChild(0) as RectTransform;
            itemTemplate.gameObject.SetActive(false);
            Debug.LogWarning("[Lottery] itemTemplate auto-assigned from first child.");
            return true;
        }

        // 2) 완전 없으면 런타임으로 간단 템플릿 생성
        if (reelContent == null)
        {
            Debug.LogError("[Lottery] reelContent missing");
            return false;
        }

        var go = new GameObject("SlotItemTemplate", typeof(RectTransform));
        go.transform.SetParent(reelContent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(600f, 125f);

        // 컬러 박스
        var colorGO = new GameObject("EventColor", typeof(RectTransform), typeof(Image));
        colorGO.transform.SetParent(go.transform, false);
        var crt = colorGO.GetComponent<RectTransform>();
        crt.anchorMin = new Vector2(0, 0);
        crt.anchorMax = new Vector2(0, 1);
        crt.pivot = new Vector2(0, 0.5f);
        crt.sizeDelta = new Vector2(30f, 0f);
        crt.anchoredPosition = new Vector2(0f, 0f);

        // 이름 텍스트
        var textGO = new GameObject("EventNameText", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGO.transform.SetParent(go.transform, false);
        var trt = textGO.GetComponent<RectTransform>();
        trt.anchorMin = new Vector2(0, 0);
        trt.anchorMax = new Vector2(1, 1);
        trt.pivot = new Vector2(0.5f, 0.5f);
        trt.offsetMin = new Vector2(40f, 0f);
        trt.offsetMax = new Vector2(0f, 0f);
        var tmp = textGO.GetComponent<TextMeshProUGUI>();
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.text = "Event";

        itemTemplate = rt;
        itemTemplate.gameObject.SetActive(false);

        // 뷰포트에 RectMask 보정
        if (reelViewport != null && reelViewport.GetComponent<RectMask2D>() == null)
            reelViewport.gameObject.AddComponent<RectMask2D>();

        Debug.LogWarning("[Lottery] itemTemplate created at runtime.");
        return true;
    }

    // --- 룸 프로퍼티 변경 감지 ---
    public override void OnRoomPropertiesUpdate(PhotonHashtable changedProps)
    {
        // 옵션/당첨자 갱신 감지 → 아직 안 돌고 있고, 완료 처리도 안 됐으면 열기
        bool changedOptions = changedProps.ContainsKey(PROP_LOT_OPTIONS);
        bool changedWinner = changedProps.ContainsKey(PROP_LOT_WINNER);

        if (changedOptions || changedWinner)
        {
            var room = PhotonNetwork.CurrentRoom;
            if (room == null) return;

            bool done = room.CustomProperties.TryGetValue(PROP_LOT_DONE, out var d) && d is bool b && b;
            if (done) return;

            if (!isRunning)
                OpenFromRoomProps();
        }
    }

    // --- 외부 호출/자동 호출: RoomProps 기반으로 열기 ---
    public void OpenFromRoomProps()
    {
        if (!EnsureTemplate())
        {
            Debug.LogError("[Lottery] itemTemplate is null. Assign a RectTransform template in inspector.");
            return;
        }

        var room = PhotonNetwork.CurrentRoom;
        if (room == null || room.CustomProperties == null) return;

        string csv = room.CustomProperties.TryGetValue(PROP_LOT_OPTIONS, out var o) ? (o as string) : "";
        string win = room.CustomProperties.TryGetValue(PROP_LOT_WINNER, out var w) ? (w as string) : "";
        bool done = room.CustomProperties.TryGetValue(PROP_LOT_DONE, out var d) && d is bool bd && bd;

        if (string.IsNullOrWhiteSpace(csv) || string.IsNullOrWhiteSpace(win))
        {
            Debug.LogWarning("[Lottery] options or winner empty");
            return;
        }
        if (done) return;

        if (panelRoot != null && !panelRoot.activeSelf) panelRoot.SetActive(true);
        if (!EnsureTemplate()) return;

        options = csv.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        winnerId = win;

        BuildReel(options);

        // 반드시 활성화 후 코루틴 시작
        if (panelRoot != null && !panelRoot.activeSelf) panelRoot.SetActive(true);

        if (resultText != null)
            resultText.text = "라운드 이벤트를 추첨 중...";

        StopAllCoroutines();
        StartCoroutine(Co_SpinAndStop(winnerId));
    }

    // --- 슬롯 리스트 생성 ---
    void BuildReel(List<string> ids)
    {
        // 기존 삭제
        for (int i = reelContent.childCount - 1; i >= 0; i--)
            Destroy(reelContent.GetChild(i).gameObject);

        // 아이템 높이
        if (itemTemplate != null) itemHeight = itemTemplate.rect.height;
        if (itemHeight <= 0) itemHeight = 125f;

        // 후보 반복
        var full = new List<string>();
        int repeat = Mathf.Max(loopMultiplier, 3);
        for (int i = 0; i < repeat; i++) full.AddRange(ids);

        float y = 0f;
        foreach (var id in full)
        {
            var rt = Instantiate(itemTemplate, reelContent);
            rt.gameObject.SetActive(true);
            rt.anchoredPosition = new Vector2(0f, -y);

            var colorBox = rt.Find("EventColor")?.GetComponent<Image>();
            var nameTxt = rt.Find("EventNameText")?.GetComponent<TextMeshProUGUI>();
            if (nameTxt) nameTxt.text = GetDisplayName(id);
            if (colorBox) colorBox.color = GetEventColor(id);

            y += itemHeight + itemSpacing;
        }

        totalHeight = y;
        reelContent.sizeDelta = new Vector2(reelContent.sizeDelta.x, totalHeight);
        reelContent.anchoredPosition = Vector2.zero;

        Debug.Log($"[Lottery] Build items={full.Count}, itemH={itemHeight:F1}, totalH={totalHeight:F1}, vpH={(reelViewport ? reelViewport.rect.height : 0):F1}");
    }
    // --- 회전 코루틴 ---
    IEnumerator Co_SpinAndStop(string targetId)
    {
        if (isRunning) yield break;
        isRunning = true;

        // 가속
        float t = 0f;
        while (t < preSpinTime)
        {
            float speed = Mathf.Lerp(0, ppsBase, t / preSpinTime);
            Move(-speed * Time.deltaTime);
            Wrap();
            t += Time.deltaTime;
            yield return null;
        }

        // 유지
        t = 0f;
        while (t < steadyTime)
        {
            Move(-ppsBase * Time.deltaTime);
            Wrap();
            t += Time.deltaTime;
            yield return null;
        }

        // 감속 + 중앙 정렬
        int targetIndex = FindFinalTargetIndex(targetId);
        float targetTopY = -targetIndex * (itemHeight + itemSpacing);
        float centerCorrection = (reelViewport.rect.height - itemHeight) * 0.5f; // 중앙선 정렬
        float finalY = targetTopY + centerCorrection;

        Vector2 start = reelContent.anchoredPosition;
        t = 0f;
        while (t < decelTime)
        {
            float k = 1f - Mathf.Pow(1f - t / decelTime, 2f); // ease-out
            float y = Mathf.Lerp(start.y, finalY, k);
            SetY(y);
            t += Time.deltaTime;
            yield return null;
        }
        SetY(finalY);

        if (resultText)
            resultText.text = $"선정된 라운드 이벤트: <b>{GetDisplayName(targetId)}</b>";

        yield return new WaitForSeconds(resultHoldSec);
        if (panelRoot) panelRoot.SetActive(false);
        isRunning = false;
        if (PhotonNetwork.IsMasterClient)
        {
            var ht = new PhotonHashtable
            {
                { PROP_LOT_DONE, true },
                { "AddRoundEvent", targetId }
            };
            PhotonNetwork.CurrentRoom.SetCustomProperties(ht);
            RoundFlowManager.Instance?.OnEventLotteryFinished(targetId); // 필요 시 호출
        }
    }

    void Move(float dy) => SetY(reelContent.anchoredPosition.y + dy);
    void SetY(float y) => reelContent.anchoredPosition = new Vector2(reelContent.anchoredPosition.x, y);

    void Wrap()
    {
        float y = reelContent.anchoredPosition.y;
        if (y < -totalHeight) y += totalHeight;
        if (y > 0) y -= totalHeight;
        SetY(y);
    }

    int FindFinalTargetIndex(string targetId)
    {
        string dn = GetDisplayName(targetId);
        int n = reelContent.childCount;
        // 마지막 루프 구간에서 같은 이름 중 아무거나(첫 번째) 채택
        for (int i = n - 1; i >= 0; i--)
        {
            var txt = reelContent.GetChild(i).Find("EventNameText")?.GetComponent<TextMeshProUGUI>();
            if (txt && txt.text == dn) return i;
        }
        return Mathf.Max(0, n - 1);
    }

    Color GetEventColor(string id) => id switch
    {
        "Spotlight" => new Color(1.0f, 0.9f, 0.3f),
        "GoldenAward" => new Color(1.0f, 0.85f, 0.1f),
        "HotOnion" => new Color(1.0f, 0.5f, 0.2f),
        "Paparazzi" => new Color(0.7f, 0.8f, 1.0f),
        "StageMalfunction" => new Color(0.9f, 0.2f, 0.2f),
        _ => Color.white
    };

    string GetDisplayName(string id) => id switch
    {
        "Spotlight" => "스포트라이트",
        "GoldenAward" => "골든 어워드",
        "HotOnion" => "핫 어니언",
        "Paparazzi" => "파파라치",
        "StageMalfunction" => "무대 사고",
        _ => id
    };
}
