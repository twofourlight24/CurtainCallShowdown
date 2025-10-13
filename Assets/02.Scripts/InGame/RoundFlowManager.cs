using Photon.Pun;
using Photon.Realtime;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

    [Header("Modes/Events")]
    public List<string> allGameModes = new() { "Showdown", "King of the Hill", "FateRoullet", "ItemRush", "DodgeRain" };
    public List<string> allRoundEvents = new() { "Spotlight", "GoldenAward", "HotOnion", "Paparazzi", "StageMalfunction" };

    // Room Prop Keys (Persistent)
    private const string PROP_TOTAL_POINTS = "TotalPointsCsv";
    private const string PROP_CURRENT_ROUND = "CurrentRoundIndex";
    private const string PROP_STACKED_EVENTS = "StackedRoundEventsCsv";

    // 룸 프로퍼티 키(투표)
    private const string PROP_VOTE_ACTIVE = "VoteActive";
    private const string PROP_VOTE_OPTIONS = "VoteOptions";
    private const string PROP_VOTE_START_TS = "VoteStartTS";
    private const string PROP_VOTE_LAST = "VoteLastActor";
    private const string PROP_VOTE_DONE = "VoteDone";
    private const string PROP_VOTE_WIN_MODE = "VoteWinnerMode";
    private const string PROP_VOTE_WIN_ACT = "VoteWinnerActor";
    private const string PROP_LOT_DONE = "LotteryDone";


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
        SavePersistentStateToRoom();
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
                SetRoomProp(PROP_VOTE_ACTIVE, false);
                SetRoomProp(PROP_VOTE_DONE, false);
                SetRoomProp(PROP_LOT_DONE, false);
            }
        }
    }

    // ===== 투표 시작(마스터 버튼에서 호출) =====
    public void BeginGameModeVote(int optionCount = 3, int durationSec = 20)
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
            { PROP_VOTE_ACTIVE, true },
            { PROP_VOTE_OPTIONS, string.Join(",", options) },
            { PROP_VOTE_START_TS, PhotonNetwork.Time },
            { PROP_VOTE_LAST, lastActor },
            { PROP_VOTE_DONE, false },
            { PROP_VOTE_WIN_MODE, "" },
            { PROP_VOTE_WIN_ACT, -1 }
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

        BeginEventLottery(allRoundEvents);
    }

    // =========================
    //  이벤트 뽑기 시작(전 플레이어 패널 오픈)
    // =========================
    public void BeginEventLottery(List<string> candidateIds)
    {
        if (!PhotonNetwork.IsMasterClient) return;
        if (candidateIds == null || candidateIds.Count == 0) return;

        // 1) 옵션과 승자를 먼저 RoomProps에 기록
        var optionsCsv = string.Join(",", candidateIds);
        var winnerId = candidateIds[UnityEngine.Random.Range(0, candidateIds.Count)];

        var ht = new PhotonHashtable
    {
        { "LotteryOptions", optionsCsv },
        { "LotteryWinner",  winnerId },
        { "LotteryDone",    false }
    };
        PhotonNetwork.CurrentRoom.SetCustomProperties(ht);

        // 2) 아주 짧은 한 프레임 뒤에 모두에게 “이제 열어” 신호
        StartCoroutine(Co_OpenLotteryPanelForAll());
    }

    private IEnumerator Co_OpenLotteryPanelForAll()
    {
        yield return null; // 한 프레임 대기(Props 전파 보장)
        photonView.RPC(nameof(RPC_OpenEventLotteryAll), RpcTarget.All);
    }

    [PunRPC]
    private void RPC_OpenEventLotteryAll()
    {
        GameManager.Instance?.uiManager?.OpenEventLotteryPanel();
    }

    /// <summary>
    /// EventLotteryPanel 연출이 끝났을 때(마스터에서) 호출.
    /// </summary>
    public void OnEventLotteryFinished(string selectedEventId)
    {
        if (!PhotonNetwork.IsMasterClient) return;

        // 스택에 반영
        if (!string.IsNullOrEmpty(selectedEventId))
        {
            stackedRoundEventIds.Add(selectedEventId);
            SetRoomProp("StackedRoundEventsCsv", string.Join(",", stackedRoundEventIds));
        }

        // 상태 저장
        SavePersistentStateToRoom();

        // 다음 라운드 시작(씬 재로딩 또는 재활용은 GameManager에 위임)
        GameManager.Instance?.BeginNextRoundAfterVote();
    }
    public void SavePersistentStateToRoom()
    {
        if (!PhotonNetwork.IsMasterClient) return;

        string pointsCsv = string.Join(";", playerPoints.Select(kv => $"{kv.Key}:{kv.Value}"));
        string eventsCsv = (stackedRoundEventIds == null || stackedRoundEventIds.Count == 0)
            ? "" : string.Join(",", stackedRoundEventIds);

        var ht = new PhotonHashtable
        {
            { PROP_TOTAL_POINTS, pointsCsv },
            { PROP_CURRENT_ROUND, currentRoundIndex },
            { PROP_STACKED_EVENTS, eventsCsv }
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(ht);
    }
    public void RestorePersistentStateFromRoom()
    {
        var room = PhotonNetwork.CurrentRoom;
        if (room == null) return;

        // 점수
        playerPoints.Clear();
        if (room.CustomProperties.TryGetValue(PROP_TOTAL_POINTS, out var p) && p is string pointsCsv && !string.IsNullOrEmpty(pointsCsv))
        {
            foreach (var token in pointsCsv.Split(';'))
            {
                var t = token.Split(':');
                if (t.Length == 2 && int.TryParse(t[0], out int actor) && int.TryParse(t[1], out int val))
                    playerPoints[actor] = val;
            }
        }
        else
        {
            foreach (var pl in PhotonNetwork.PlayerList) playerPoints[pl.ActorNumber] = 0;
        }

        // 라운드 인덱스
        if (room.CustomProperties.TryGetValue(PROP_CURRENT_ROUND, out var r) && r is int cr)
            currentRoundIndex = cr;

        // 누적 이벤트
        stackedRoundEventIds.Clear();
        if (room.CustomProperties.TryGetValue(PROP_STACKED_EVENTS, out var s) && s is string eventsCsv && !string.IsNullOrEmpty(eventsCsv))
            stackedRoundEventIds.AddRange(eventsCsv.Split(',').Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)));
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
