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
    public int totalRounds = 3;
    public int currentRoundIndex = 0;
    public List<string> stackedRoundEventIds = new();

    [Header("Runtime / Score")]
    public Dictionary<int, int> playerPoints = new();
    public Dictionary<int, int> killCounts = new();
    public List<Player> lastRanking = new();

    // 점수 테이블
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
        foreach (var p in PhotonNetwork.PlayerList)
            if (!playerPoints.ContainsKey(p.ActorNumber)) playerPoints[p.ActorNumber] = 0;

        if (PhotonNetwork.CurrentRoom?.CustomProperties != null &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("RoundCount", out object rc) &&
            rc is int rr)
        {
            totalRounds = rr;
        }

        ResetRoundData();
    }

    public void ResetRoundData()
    {
        killCounts.Clear();
        foreach (var p in PhotonNetwork.PlayerList)
            killCounts[p.ActorNumber] = 0;
    }

    public void RegisterKill(int killerActor)
    {
        if (!killCounts.ContainsKey(killerActor)) killCounts[killerActor] = 0;
        killCounts[killerActor] += 1;
        Debug.Log($"[RoundFlowManager] Actor {killerActor} 킬 기록 {killCounts[killerActor]}");
    }

    public void HandleRoundComplete(List<Player> ranking)
    {
        if (ranking == null || ranking.Count == 0) return;
        lastRanking = ranking.ToList();

        // 1) 라운드 점수 계산
        var roundPoints = new Dictionary<int, int>();
        for (int i = 0; i < ranking.Count; i++)
        {
            var p = ranking[i];
            int actor = p.ActorNumber;

            int baseScore = (i < rankPoints.Length) ? rankPoints[i] : 0;
            int killBonus = killCounts.TryGetValue(actor, out var k) ? k * killPoint : 0;
            int score = baseScore + killBonus;

            if (!playerPoints.ContainsKey(actor)) playerPoints[actor] = 0;
            playerPoints[actor] += score;
            roundPoints[actor] = score;
        }

        // 2) 라운드 결과 UI
        GameManager.Instance?.uiManager?.ShowRoundResultUI(ranking, roundPoints);

        // 3) 라운드 카운트 증가
        currentRoundIndex++;

        // 4) 종료/진행
        if (currentRoundIndex >= totalRounds)
        {
            // 최종 승자
            var final = playerPoints.OrderByDescending(kv => kv.Value).ToList();
            var top = final.First();
            Player winner = PhotonNetwork.PlayerList.FirstOrDefault(p => p.ActorNumber == top.Key);
            Debug.Log($"[RoundFlow] Final Winner: {winner?.NickName} ({top.Value} pts)");
            if (PhotonNetwork.IsMasterClient)
            {
                SetRoomProp("FinalWinnerNick", winner?.NickName ?? "???");
                SetRoomProp("FinalWinnerScore", top.Value);
                SetRoomProp("GameEnded", true);
            }
        }
        else
        {
            // 누적 이벤트만 반영(투표 자동 시작은 하지 않음)
            if (PhotonNetwork.IsMasterClient)
            {
                if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("AddRoundEvent", out object ev) &&
                    ev is string evId && !string.IsNullOrEmpty(evId))
                {
                    stackedRoundEventIds.Add(evId);
                    SetRoomProp("StackedRoundEventsCsv", string.Join(",", stackedRoundEventIds));
                    SetRoomProp("AddRoundEvent", "");
                }

                // 투표 관련 플래그 초기화
                SetRoomProp("VoteActive", false);
                SetRoomProp("VoteDone", false);
            }
        }
    }

    // ===== 투표 시작(마스터 버튼에서 호출) =====
    public void BeginGameModeVote(int optionCount = 3, int durationSec = 30)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        var options = allGameModes.OrderBy(_ => UnityEngine.Random.value).Take(optionCount).ToList();

        int lastActor = -1;
        if (lastRanking != null && lastRanking.Count > 0)
            lastActor = lastRanking[lastRanking.Count - 1].ActorNumber;

        var room = PhotonNetwork.CurrentRoom;
        if (room == null) return;

        // 이전 표 지우기
        var clear = new PhotonHashtable();
        foreach (var p in PhotonNetwork.PlayerList)
            clear[$"VOTE_{p.ActorNumber}"] = "";
        room.SetCustomProperties(clear);

        // 투표 속성 설정
        var p2 = new PhotonHashtable
        {
            { "VoteActive", true },
            { "VoteOptions", string.Join(",", options) },
            { "VoteStartTS", PhotonNetwork.Time },
            { "VoteLastActor", lastActor },
            { "VoteDone", false },
            { "VoteWinnerActor", -1 },
            { "VoteWinnerMode", "" }
        };
        room.SetCustomProperties(p2);
    }

    // ===== 투표 끝 → 다음 라운드 시작(마스터만) =====
    public void OnVoteFinishedAndReadyToStartNextRound()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("NextGameMode", out var nm) &&
            nm is string nextMode && !string.IsNullOrEmpty(nextMode))
        {
            SetRoomProp("GameMode", nextMode);
            SetRoomProp("NextGameMode", "");
        }

        GameManager.Instance?.BeginNextRoundAfterVote();
    }

    // ===== 유틸 =====
    public Dictionary<int, int> GetTotalPoints() => new Dictionary<int, int>(playerPoints);

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

    private void SetRoomProp(string key, object val)
    {
        PhotonHashtable p = new PhotonHashtable { { key, val } };
        PhotonNetwork.CurrentRoom?.SetCustomProperties(p);
    }
}
