using System.Collections.Generic;
using UnityEngine;

/// <summary>라운드 이벤트가 구현해야 하는 최소 인터페이스</summary>
public interface IRoundEvent
{
    string Id { get; }                    // "Spotlight", "GoldenAward" 등
    string DisplayName { get; }           // UI 노출 이름
    void EnableEvent(RoundEventContext ctx);
    void DisableEvent(RoundEventContext ctx);
}

/// <summary>이벤트들이 공통으로 참조할 컨텍스트 (맵, 영역, 플레이어/점수 접근 등)</summary>
public class RoundEventContext
{
    public GameManager gm;        // 캐릭터/플레이어 접근, UI 갱신 등  :contentReference[oaicite:5]{index=5}
    public UIManager ui;          // 이벤트 안내/효과 UI  :contentReference[oaicite:6]{index=6}
    // 필요하면: 맵 기믹, 스포트라이트 라이트, 타이머 핸들 등 추가
}

/// <summary>누적 이벤트를 관리하고, 각 라운드 시작/종료 시 Enable/Disable 수행</summary>
public class RoundEventManager : MonoBehaviour
{

    public static RoundEventManager Instance { get; private set; }

    private Dictionary<string, IRoundEvent> registry = new();
    private readonly List<IRoundEvent> activeEvents = new();
    private RoundEventContext context;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 컨텍스트 구성
        context = new RoundEventContext
        {
            gm = GameManager.Instance,     // GameManager는 DontDestroy로 유지중  :contentReference[oaicite:7]{index=7}
            ui = GameManager.Instance?.uiManager
        };

        // 이벤트 레지스트리 등록 (필요시 실제 구현으로 교체)
        Register(new SpotlightEvent());
        Register(new GoldenAwardEvent());
        Register(new HotOnionEvent());
        Register(new PaparazziEvent());
        Register(new StageMalfunctionEvent());
    }

    public void Register(IRoundEvent ev)
    {
        if (!registry.ContainsKey(ev.Id)) registry.Add(ev.Id, ev);
    }

    public void EnableStackedEvents(IEnumerable<string> eventIds)
    {
        DisableAll(); // 안전하게 초기화
        if (eventIds == null) return;

        foreach (var id in eventIds)
        {
            if (registry.TryGetValue(id, out var ev))
            {
                ev.EnableEvent(context);
                activeEvents.Add(ev);
                Debug.Log($"[RoundEvent] Enabled: {ev.Id}");
            }
        }
    }
    public void RefreshContext()
    {
        // RefreshContext 메서드의 구현 내용을 여기에 추가하세요.
        Debug.Log("RoundEventManager: Context refreshed.");
    }
    public void DisableAll()
    {
        foreach (var ev in activeEvents)
        {
            ev.DisableEvent(context);
            Debug.Log($"[RoundEvent] Disabled: {ev.Id}");
        }
        activeEvents.Clear();
    }
}

/* =========================
 *  이벤트 샘플 구현(스텁)
 *  ========================= */

// 무대가 어두워지고 특정 단원만 빛 ? 패널티/타깃팅 유도  :contentReference[oaicite:8]{index=8}
public class SpotlightEvent : IRoundEvent
{
    public string Id => "Spotlight";
    public string DisplayName => "스포트라이트";

    public void EnableEvent(RoundEventContext ctx)
    {
        // TODO: 랜덤 플레이어를 타깃으로 스포트라이트 조명 On, 비타깃은 시야축소(포스트프로세싱/컬러그레이딩)
    }

    public void DisableEvent(RoundEventContext ctx)
    {
        // 조명/후처리 원복
    }
}

// 특정 단원 타격시 추가 점수 ? 점수 소진시 해제  :contentReference[oaicite:9]{index=9}
public class GoldenAwardEvent : IRoundEvent
{
    public string Id => "GoldenAward";
    public string DisplayName => "골든어워드";
    public void EnableEvent(RoundEventContext ctx) { /* TODO */ }
    public void DisableEvent(RoundEventContext ctx) { /* TODO */ }
}

// 15초마다 5번 폭발, 맞을수록 데미지 증가 ? 마지막으로 친 사람에게 귀속  :contentReference[oaicite:10]{index=10}
public class HotOnionEvent : IRoundEvent
{
    public string Id => "HotOnion";
    public string DisplayName => "핫어니언";
    public void EnableEvent(RoundEventContext ctx) { /* TODO */ }
    public void DisableEvent(RoundEventContext ctx) { /* TODO */ }
}

// 랜덤 타이밍 포토존 등장, 셔터 전 사진 찍히면 추가 점수  :contentReference[oaicite:11]{index=11}
public class PaparazziEvent : IRoundEvent
{
    public string Id => "Paparazzi";
    public string DisplayName => "파파라치";
    public void EnableEvent(RoundEventContext ctx) { /* TODO */ }
    public void DisableEvent(RoundEventContext ctx) { /* TODO */ }
}

// 무대가 시소처럼 기울어짐(최대 35도)  :contentReference[oaicite:12]{index=12}
public class StageMalfunctionEvent : IRoundEvent
{
    public string Id => "StageMalfunction";
    public string DisplayName => "무대고장";
    public void EnableEvent(RoundEventContext ctx) { /* TODO: 맵의 루트 트랜스폼을 주기적으로 회전시키는 기믹 */ }
    public void DisableEvent(RoundEventContext ctx) { /* TODO: 각도 0으로 복귀 */ }
}
