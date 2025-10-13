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
    public GameObject panelRoot;                 // 전체 패널 루트
    public RectTransform reelViewport;           // 마스크 영역 (중앙선 기준)
    public RectTransform reelContent;            // 아이템들이 들어가는 Content (Pivot.y = 1)
    public RectTransform itemTemplate;           // 비활성 템플릿 (Pivot.y = 1, Top-Center)
    public TMP_Text resultText;                  // 결과 텍스트
    public RectTransform selectEdge;             // 중앙선 데코(옵션)

    [Header("Spin Config (요청값 반영)")]
    public float preSpinTime = 2.0f;           // 가속
    public float steadyTime = 2.0f;           // 유지
    public float decelTime = 5.0f;           // 감속
    public float ppsBase = 2000f;          // 기본 속도 (px/s)
    public int loopMultiplier = 20;             // 후보 세트 반복 횟수
    public float itemSpacing = 0f;             // 간격(템플릿 간)
    public float resultHoldSec = 3.0f;           // 결과 유지

    // Room Props
    private const string PROP_LOT_OPTIONS = "LotteryOptions";
    private const string PROP_LOT_WINNER = "LotteryWinner";
    private const string PROP_LOT_DONE = "LotteryDone";

    // state
    private  List<string> options = new();
    private string winnerId = "";
    private float itemHeight = 120f;
    private float totalHeight = 0f;
    private bool isRunning = false;

    void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        if (itemTemplate != null) itemTemplate.gameObject.SetActive(false);

        // 템플릿 높이 캐시
        if (itemTemplate != null) itemHeight = itemTemplate.rect.height;
    }

    // Room Props가 세팅되면(옵션/위너) 연출 시작. 마스터 포함 전원 동일 로직.
    public override void OnRoomPropertiesUpdate(PhotonHashtable changedProps)
    {
        var room = PhotonNetwork.CurrentRoom;
        if (room == null) return;

        // Done이면 아무 것도 하지 않음
        if (room.CustomProperties.TryGetValue(PROP_LOT_DONE, out var d) && d is bool done && done) return;

        bool hasOptions = (room.CustomProperties.TryGetValue(PROP_LOT_OPTIONS, out var o) && o is string so && !string.IsNullOrEmpty(so));
        bool hasWinner = (room.CustomProperties.TryGetValue(PROP_LOT_WINNER, out var w) && w is string sw && !string.IsNullOrEmpty(sw));

        if (hasOptions && hasWinner && !isRunning)
        {
            OpenFromRoomProps();
        }
    }

    // UIManager에서 열어줄 때 호출됨
    static void NormalizeRT(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot)
    {
        if (!rt) return;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.localScale = Vector3.one;
        rt.anchoredPosition3D = new Vector3(rt.anchoredPosition3D.x, rt.anchoredPosition3D.y, 0f);
    }

    void EnsureHierarchyLayout()
    {
        // 뷰포트: 부모 크기 고정, 피벗은 상관없지만 보편적으로 중앙 피벗을 씁니다.
        if (reelViewport) NormalizeRT(reelViewport, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f));

        // 컨텐츠: 반드시 "위쪽 앵커·피벗"이어야 아래로 내려가는 Y(-) 계산이 맞습니다.
        if (reelContent) NormalizeRT(reelContent, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));

        // 템플릿: 위쪽 피벗, 컨텐츠에 자식으로 들어갈 전제
        if (itemTemplate)
        {
            NormalizeRT(itemTemplate, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
            // 비활성 상태여도 rect.height는 정상적으로 읽힙니다.
            itemHeight = itemTemplate.rect.height;
        }
    }

    private void CommitLotteryWinnerIfNeeded()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        var room = PhotonNetwork.CurrentRoom;
        if (room == null) return;

        // 이미 커밋됐으면 스킵
        if (room.CustomProperties.TryGetValue("LotteryDone", out var d) && d is bool done && done) return;

        if (room.CustomProperties.TryGetValue("LotteryWinner", out var w) && w is string winner && !string.IsNullOrEmpty(winner))
        {
            var ht = new ExitGames.Client.Photon.Hashtable {
            { "LotteryDone", true },
            { "AddRoundEvent", winner }
        };
            room.SetCustomProperties(ht);

            RoundFlowManager.Instance?.OnEventLotteryFinished(winner);
        }
    }

    // --- OpenFromRoomProps() 맨 위/BuildReel() 호출 전에 보장
    public void OpenFromRoomProps()
    {
        if (isRunning) return;
        var room = PhotonNetwork.CurrentRoom;
        if (room == null || room.CustomProperties == null) return;

        string csv = room.CustomProperties.TryGetValue(PROP_LOT_OPTIONS, out var o) ? (o as string) : "";
        string win = room.CustomProperties.TryGetValue(PROP_LOT_WINNER, out var w) ? (w as string) : "";

        // 둘 중 하나라도 없으면 폴백 커밋 시도(마스터만) 후 종료
        if (string.IsNullOrEmpty(csv) || string.IsNullOrEmpty(win))
        {
            CommitLotteryWinnerIfNeeded(); // 연출 없이 결과만 적용
            return;
        }

        // 이미 완료된 상태면 그냥 닫고 끝
        if (room.CustomProperties.TryGetValue(PROP_LOT_DONE, out var dd) && dd is bool done && done)
        {
            panelRoot?.SetActive(false);
            return;
        }

        // ★ 패널/컴포넌트 활성화 보장
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (!enabled) enabled = true;
        if (panelRoot && !panelRoot.activeSelf) panelRoot.SetActive(true);

        // ★ 앵커/피벗 정규화
        EnsureHierarchyLayout();

        options = csv.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        winnerId = win;

        BuildReel(options);

        if (resultText) resultText.text = "라운드 이벤트를 추첨 중...";
        Canvas.ForceUpdateCanvases();

        StopAllCoroutines();
        StartCoroutine(Co_SpinAndStop(winnerId));
    }

    // --- 아이템 생성 (피벗/앵커 강제 + 위치 = -currentY)
    private void BuildReel(List<string> ids)
    {
        for (int i = reelContent.childCount - 1; i >= 0; i--)
            Destroy(reelContent.GetChild(i).gameObject);

        if (loopMultiplier < 2) loopMultiplier = 2;

        float spacing = itemSpacing;
        float currentY = 0f;

        for (int loop = 0; loop < loopMultiplier; loop++)
        {
            foreach (var id in ids)
            {
                var inst = Instantiate(itemTemplate, reelContent);
                NormalizeRT(inst, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f));
                inst.gameObject.SetActive(true);

                inst.anchoredPosition = new Vector2(0f, -currentY);

                var colorBox = inst.Find("EventColor")?.GetComponent<Image>();
                var nameTxt = inst.Find("EventNameText")?.GetComponent<TMP_Text>();
                if (nameTxt) nameTxt.text = GetDisplayName(id);
                if (colorBox) colorBox.color = GetEventColor(id);

                currentY += itemHeight + spacing;
            }
        }

        totalHeight = currentY;
        // 컨텐츠 사이즈는 전체 높이로, X는 유지
        reelContent.sizeDelta = new Vector2(reelContent.sizeDelta.x, totalHeight);
        // 시작 위치 꼭 0으로
        reelContent.anchoredPosition = Vector2.zero;

        // 디버그 찍기 (지금처럼)
        Debug.Log($"[Lottery] Build items={loopMultiplier * ids.Count}, itemH={itemHeight}, totalH={totalHeight}, vpH={reelViewport.rect.height}");
    }

    // --- 감속부 떨림 제거 + 중앙 정렬 확실히
    private IEnumerator Co_SpinAndStop(string targetId)
    {
        if (isRunning) yield break;
        isRunning = true;

        // 가속
        float t = 0f;
        while (t < preSpinTime)
        {
            float speed = Mathf.Lerp(0f, ppsBase, t / preSpinTime);
            SetY(reelContent.anchoredPosition.y - speed * Time.deltaTime);
            WrapReel();
            t += Time.deltaTime;
            yield return null;
        }

        // 유지
        t = 0f;
        while (t < steadyTime)
        {
            SetY(reelContent.anchoredPosition.y - ppsBase * Time.deltaTime);
            WrapReel();
            t += Time.deltaTime;
            yield return null;
        }

        // 목표 인덱스(마지막 루프 중 하나) 산출
        int targetIndex = FindFinalTargetIndex(targetId);

        // 중앙 정렬될 최종 Y 계산
        float viewCenter = reelViewport.rect.height * 0.5f;
        float itemCenter = itemHeight * 0.5f;
        float targetTopY = IndexToContentYTop(targetIndex);
        float finalY = targetTopY + (viewCenter - itemCenter);

        // 감속 (부드러운 ease-out)
        Vector2 start = reelContent.anchoredPosition;
        t = 0f;
        while (t < decelTime)
        {
            float k = 1f - Mathf.Pow(1f - (t / decelTime), 3f);  // cubic ease-out
            float y = Mathf.Lerp(start.y, finalY, k);
            SetY(y);
            t += Time.deltaTime;
            yield return null;
        }
        SetY(finalY);

        if (resultText)
            resultText.text = $"선정된 라운드 이벤트: <b>{GetDisplayName(targetId)}</b>";

        // 마스터 커밋은 현 로직 유지
        if (PhotonNetwork.IsMasterClient)
        {
            var ht = new PhotonHashtable
        {
            { PROP_LOT_DONE, true },
            { "AddRoundEvent", targetId },
        };
            PhotonNetwork.CurrentRoom.SetCustomProperties(ht);
            RoundFlowManager.Instance?.OnEventLotteryFinished(targetId);
        }

        yield return new WaitForSeconds(resultHoldSec);
        if (panelRoot) panelRoot.SetActive(false);
        isRunning = false;

        if (PhotonNetwork.IsMasterClient)
        {
            CommitLotteryWinnerIfNeeded();
        }
    }

    // --- 좌표 유틸 (위쪽 피벗 전제)
    private float IndexToContentYTop(int index) => -(index * (itemHeight + itemSpacing));
    private void SetY(float y) => reelContent.anchoredPosition = new Vector2(reelContent.anchoredPosition.x, y);

    // --- 래핑: 위쪽 피벗 기준, y가 (-totalHeight, 0] 영역을 벗어나지 않게
    private void WrapReel()
    {
        float y = reelContent.anchoredPosition.y;

        if (y <= -totalHeight) y += totalHeight;
        else if (y > 0f) y -= totalHeight;

        SetY(y);
    }

    // 마지막 루프 구간에서 가장 가까운 target 인덱스 반환
    private int FindFinalTargetIndex(string targetId)
    {
        string targetName = GetDisplayName(targetId);
        if (options.Count == 0) return 0;

        // reelContent의 모든 자식 중에서 타겟 이름 인덱스 수집
        var indices = new List<int>();
        for (int i = 0; i < reelContent.childCount; i++)
        {
            var txt = reelContent.GetChild(i).Find("EventNameText")?.GetComponent<TMP_Text>();
            if (txt != null && txt.text == targetName)
                indices.Add(i);
        }
        if (indices.Count == 0) return 0;

        int lastLoopStart = (loopMultiplier - 1) * options.Count;
        // 마지막 루프에 있는 후보들
        var lastLoop = indices.Where(i => i >= lastLoopStart).ToList();
        if (lastLoop.Count == 0) return indices.Last();

        // 현재 y 기준으로 "아직 아래쪽(더 내려가야 도달)" 중 가장 먼저 만나는 것을 사용
        float y = reelContent.anchoredPosition.y;
        float viewportH = reelViewport.rect.height;
        float centerOffset = (viewportH - itemHeight) * 0.5f;
        float currentCenterY = y - centerOffset; // 현재 중앙선 기준 상단 y

        foreach (var idx in lastLoop)
        {
            float topY = -(idx * (itemHeight + itemSpacing));
            if (topY < currentCenterY) return idx;
        }
        // 못 찾으면 마지막 루프의 첫 번째 것
        return lastLoop[0];
    }

    private Color GetEventColor(string id)
    {
        return id switch
        {
            "Spotlight" => new Color(1.0f, 0.9f, 0.3f),
            "GoldenAward" => new Color(1.0f, 0.85f, 0.1f),
            "HotOnion" => new Color(1.0f, 0.5f, 0.2f),
            "Paparazzi" => new Color(0.7f, 0.8f, 1.0f),
            "StageMalfunction" => new Color(0.9f, 0.2f, 0.2f),
            _ => Color.white
        };
    }

    private string GetDisplayName(string id) => id switch
    {
        "Spotlight" => "스포트라이트",
        "GoldenAward" => "골든 어워드",
        "HotOnion" => "핫 어니언",
        "Paparazzi" => "파파라치",
        "StageMalfunction" => "무대 사고",
        _ => id
    };
}
