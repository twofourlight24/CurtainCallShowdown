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
    public int totalRounds = 3;                          // 전체 라운드 수
    public int currentRoundIndex = 0;                    // 현재 라운드 인덱스 (0-based)
    public List<string> stackedRoundEventIds = new();    // 라운드 이벤트 누적 (ID 문자열)

    [Header("Runtime / Score")]
    public Dictionary<int, int> playerPoints = new();    // ActorNumber -> 누적 점수
    public Dictionary<int, int> killCounts = new();      // ActorNumber -> 이번 라운드 킬 수
    public List<Player> lastRanking = new();             // 직전 라운드 순위

    // 순위별 점수 (기획 반영: 1등 50, 2등 40, 3등 30, 4등 20)
    private readonly int[] rankPoints = { 50, 40, 30, 20 };
    private readonly int killPoint = 5;

    [Header("Modes")]
    public List<string> allGameModes = new() { "Showdown", "King of the Hill", "TagHunt", "ItemRush", "DodgeRain" };

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // 플레이어별 누적 포인트 초기화
        foreach (var p in PhotonNetwork.PlayerList)
            if (!playerPoints.ContainsKey(p.ActorNumber))
                playerPoints[p.ActorNumber] = 0;

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
    /// 라운드 시작 시 킬 카운트 초기화
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

        // 1) 점수 계산 (순위 점수 + 킬 보너스)
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
            Debug.Log($"[RoundFlow] {p.NickName}({actor}) : 순위 {baseScore} + 킬 {killBonus} = {score}, 누적 {playerPoints[actor]}");
        }

        // 2) UI에 라운드 결과 전달
        GameManager.Instance.uiManager.ShowRoundResultUI(ranking, roundPoints);

        // 3) 라운드 카운트 증가
        currentRoundIndex++;

        // 4) 라운드 종료 처리
        if (currentRoundIndex >= totalRounds)
        {
            // 최종 결과 발표
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
            // 이벤트 누적 처리
            if (PhotonNetwork.IsMasterClient)
            {
                if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("AddRoundEvent", out object ev) &&
                    ev is string evId && !string.IsNullOrEmpty(evId))
                {
                    stackedRoundEventIds.Add(evId);
                    SetRoomProp("StackedRoundEventsCsv", string.Join(",", stackedRoundEventIds));
                    SetRoomProp("AddRoundEvent", "");
                }
            }

            // -----> 여기서 투표 시작 (마스터만)
            if (PhotonNetwork.IsMasterClient)
                BeginGameModeVote(3, 30);
        }
    }

    // ===== 투표 관련 =====

    public void BeginGameModeVote(int optionCount = 3, int durationSec = 30)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // 옵션 3개 랜덤
        var options = allGameModes.OrderBy(_ => UnityEngine.Random.value).Take(optionCount).ToList();

        // 이번 라운드 꼴등
        int lastActor = -1;
        if (lastRanking != null && lastRanking.Count > 0)
            lastActor = lastRanking[lastRanking.Count - 1].ActorNumber;

        var room = PhotonNetwork.CurrentRoom;
        if (room == null) return;

        // 이전 잔여 표 지우기
        var clear = new PhotonHashtable();
        foreach (var p in PhotonNetwork.PlayerList)
            clear[VoteKeyFor(p.ActorNumber)] = "";
        room.SetCustomProperties(clear);

        // 투표 RoomProps 설정
        var p2 = new PhotonHashtable
        {
            { "VoteActive", true },
            { "VoteOptions", string.Join(",", options) },
            { "VoteStartTS", PhotonNetwork.Time }, // 서버 시간
            { "VoteLastActor", lastActor },
            { "VoteDone", false },
            { "VoteWinnerActor", -1 },
            { "VoteWinnerMode", "" }
        };
        room.SetCustomProperties(p2);

        // UI 열기
        GameManager.Instance?.uiManager?.OpenGameModeVotePanel();
    }

    public void OnVoteFinishedAndReadyToStartNextRound()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // NextGameMode 읽기 → GameMode로 채택
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("NextGameMode", out var nm) &&
            nm is string nextMode && !string.IsNullOrEmpty(nextMode))
        {
            SetRoomProp("GameMode", nextMode);
            SetRoomProp("NextGameMode", "");
        }

        // 다음 라운드 시작 (GameManager가 처리)
        GameManager.Instance?.BeginNextRoundAfterVote();
    }

    // ===== 유틸 =====

    public Dictionary<int, int> GetTotalPoints()
    {
        return new Dictionary<int, int>(playerPoints);
    }

    public int GetRoundsLeft()
    {
        int left = totalRounds - currentRoundIndex;
        return left < 0 ? 0 : left;
    }

    public string GetStackedEventsCsv()
    {
        return (stackedRoundEventIds == null || stackedRoundEventIds.Count == 0)
            ? ""
            : string.Join(",", stackedRoundEventIds);
    }

    static string VoteKeyFor(int actor) => $"VOTE_{actor}";

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
