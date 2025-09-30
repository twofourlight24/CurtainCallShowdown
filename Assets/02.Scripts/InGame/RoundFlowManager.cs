using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;

public class RoundFlowManager : MonoBehaviourPunCallbacks
{
    public static RoundFlowManager Instance { get; private set; }

    [Header("Config")]
    public int totalRounds = 3;                         // Room 설정과 동기화
    public int currentRoundIndex = 0;                   // 0-based
    public List<string> stackedRoundEventIds = new();   // 라운드 이벤트 누적 (ID 문자열)

    [Header("Runtime / Score")]
    public Dictionary<int, int> playerPoints = new();   // ActorNumber -> 누적 포인트
    public Dictionary<int, int> killCounts = new();     // ActorNumber -> 이번 라운드 킬 수
    public List<Player> lastRanking = new();            // 직전 라운드 순위

    // 순위별 점수 (기존 4,3,2,1 → 기획 반영 50,40,30,20)
    private readonly int[] rankPoints = { 50, 40, 30, 20 };
    private readonly int killPoint = 5;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // 누적 포인트 초기화
        foreach (var p in PhotonNetwork.PlayerList)
            if (!playerPoints.ContainsKey(p.ActorNumber)) playerPoints[p.ActorNumber] = 0;

        // Room CustomProperties에서 RoundCount 동기화
        if (PhotonNetwork.CurrentRoom?.CustomProperties != null &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("RoundCount", out object rc) &&
            rc is int rr)
        {
            totalRounds = rr;
        }

        ResetRoundData();
    }

    /// <summary>
    /// 라운드 시작 시 킬카운트 초기화
    /// </summary>
    public void ResetRoundData()
    {
        killCounts.Clear();
        foreach (var p in PhotonNetwork.PlayerList)
            killCounts[p.ActorNumber] = 0;
    }

    /// <summary>
    /// Projectile에서 호출: 킬 카운트 기록
    /// </summary>
    public void RegisterKill(int killerActor)
    {
        if (!killCounts.ContainsKey(killerActor))
            killCounts[killerActor] = 0;
        killCounts[killerActor] += 1;

        Debug.Log($"[RoundFlowManager] Actor {killerActor} 킬 기록 {killCounts[killerActor]}");
    }

    /// <summary>
    /// GameManager가 라운드 종료 시 호출
    /// </summary>
    public void HandleRoundComplete(List<Player> ranking)
    {
        if (ranking == null || ranking.Count == 0) return;
        lastRanking = ranking.ToList();

        // --------------------------
        // 1) 점수 지급 (순위 + 킬)
        // --------------------------
        var roundPoints = new Dictionary<int, int>();

        for (int i = 0; i < ranking.Count; i++)
        {
            var p = ranking[i];
            int actor = p.ActorNumber;

            int baseScore = (i < rankPoints.Length) ? rankPoints[i] : 0;
            int killBonus = killCounts.TryGetValue(actor, out var kills) ? kills * killPoint : 0;
            int score = baseScore + killBonus;

            if (!playerPoints.ContainsKey(actor)) playerPoints[actor] = 0;
            playerPoints[actor] += score;

            roundPoints[actor] = score;
            Debug.Log($"[RoundFlow] {p.NickName}({actor}) → RoundScore {score}");

            Debug.Log($"[RoundFlow] {p.NickName} : 순위 {baseScore} + 킬 {killBonus} = {score}, 누적 {playerPoints[actor]}");
        }

        // --------------------------
        // 2) UI에 라운드 결과 전달
        // --------------------------
        GameManager.Instance.uiManager.ShowRoundResultUI(ranking, roundPoints);

        // --------------------------
        // 3) 기존 권한 배분/RoomProp 로직 유지
        // --------------------------
        if (currentRoundIndex < totalRounds - 1)
        {
            var first = ranking[0];
            var last = ranking[ranking.Count - 1];

            if (PhotonNetwork.IsMasterClient)
            {
                string suggestedNextMode = SuggestNextModeFallback();
                SetRoomProp("NextGameMode", suggestedNextMode);

                string newEventId = SuggestNextEventFallback();
                SetRoomProp("AddRoundEvent", newEventId);
            }
        }

        currentRoundIndex++;
        if (currentRoundIndex >= totalRounds)
        {
            var final = playerPoints.OrderByDescending(kv => kv.Value).ToList();
            var top = final.First();
            Player winner = PhotonNetwork.PlayerList.First(p => p.ActorNumber == top.Key);

            Debug.Log($"[RoundFlow] Final Winner: {winner.NickName} ({top.Value} pts)");
            if (PhotonNetwork.IsMasterClient)
            {
                SetRoomProp("FinalWinnerNick", winner.NickName);
                SetRoomProp("FinalWinnerScore", top.Value);
                SetRoomProp("GameEnded", true);
            }
        }
        else
        {
            if (PhotonNetwork.IsMasterClient)
            {
                if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("AddRoundEvent", out object ev) && ev is string evId && !string.IsNullOrEmpty(evId))
                {
                    stackedRoundEventIds.Add(evId);
                    SetRoomProp("StackedRoundEventsCsv", string.Join(",", stackedRoundEventIds));
                    SetRoomProp("AddRoundEvent", "");
                }

                if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("NextGameMode", out object nm) && nm is string nextMode && !string.IsNullOrEmpty(nextMode))
                {
                    SetRoomProp("GameMode", nextMode);
                    SetRoomProp("NextGameMode", "");
                }

                
            }
        }
    }
    // 누적 점수 스냅샷 반환 (외부에서 마음대로 바꾸지 못하도록 복사본)
    public Dictionary<int, int> GetTotalPoints()
    {
        return new Dictionary<int, int>(playerPoints);
    }

    // 남은 라운드 수 (음수 방지)
    public int GetRoundsLeft()
    {
        int left = totalRounds - currentRoundIndex;
        return left < 0 ? 0 : left;
    }

    // 누적 이벤트 CSV
    public string GetStackedEventsCsv()
    {
        return (stackedRoundEventIds == null || stackedRoundEventIds.Count == 0)
            ? ""
            : string.Join(",", stackedRoundEventIds);
    }

    // --- 간단 대체 로직 ---
    private string SuggestNextModeFallback()
    {
        var modes = new[] { "Showdown", "King of the Hill" };
        string current = (string)PhotonNetwork.CurrentRoom.CustomProperties["GameMode"];
        return (current == modes[0]) ? modes[1] : modes[0];
    }

    private string SuggestNextEventFallback()
    {
        var events = new[] { "Spotlight", "GoldenAward", "HotOnion", "Paparazzi", "StageMalfunction" };
        int idx = UnityEngine.Random.Range(0, events.Length);
        return events[idx];
    }

    private void SetRoomProp(string key, object val)
    {
        PhotonHashtable p = new PhotonHashtable { { key, val } };
        PhotonNetwork.CurrentRoom.SetCustomProperties(p);
    }
}
