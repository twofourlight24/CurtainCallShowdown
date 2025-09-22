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
    public List<Player> lastRanking = new();            // 직전 라운드 순위

    // 포인트 테이블(인원수 4명 가정: 1등 4점, 2등 3점, 3등 2점, 4등 1점)
    private readonly int[] rankPoints = { 4, 3, 2, 1 };

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        // 룸의 라운드 수/초기 모드/맵 등은 RoomManager & GameManager에서 세팅함
        // 여기선 누적 포인트 테이블 초기화만 담당
        foreach (var p in PhotonNetwork.PlayerList)
            if (!playerPoints.ContainsKey(p.ActorNumber)) playerPoints[p.ActorNumber] = 0;

        // Room CustomProperties에서 RoundCount 있으면 동기화
        if (PhotonNetwork.CurrentRoom?.CustomProperties != null &&
            PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("RoundCount", out object rc) &&
            rc is int rr)
        {
            totalRounds = rr;
        }
    }

    /// <summary>
    /// GameManager가 라운드 종료 시 호출: 최종 순위 리스트를 넘겨주면 포인트 계산/권한 배분/다음 라운드 준비를 진행
    /// </summary>
    public void HandleRoundComplete(List<Player> ranking)
    {
        if (ranking == null || ranking.Count == 0) return;
        lastRanking = ranking.ToList();

        // 1) 포인트 지급
        for (int i = 0; i < ranking.Count; i++)
        {
            var p = ranking[i];
            int point = (i < rankPoints.Length) ? rankPoints[i] : 0; // 4위 이후 0점 처리
            if (!playerPoints.ContainsKey(p.ActorNumber)) playerPoints[p.ActorNumber] = 0;
            playerPoints[p.ActorNumber] += point;
        }

        // 2) 다음 라운드 권한(1등 → 모드 선택, 4등 → 라운드 이벤트 추가) 및 이벤트 누적
        //    UI/투표 구현 전까진 "룸 속성 + 랜덤 대체" 로직 제공
        if (currentRoundIndex < totalRounds - 1)
        {
            var first = ranking[0];
            var last = ranking[ranking.Count - 1];

            if (PhotonNetwork.IsMasterClient)
            {
                // (A) 1등이 다음 라운드 모드 선택 ? 우선 Room CustomProperties "NextGameMode"로 반영
                string suggestedNextMode = SuggestNextModeFallback(); // 간단 랜덤/라운드로빈
                SetRoomProp("NextGameMode", suggestedNextMode);

                // (B) 4등이 라운드 이벤트 추가 ? Room CustomProperties "AddRoundEvent"에 ID 기록
                string newEventId = SuggestNextEventFallback(); // 간단 랜덤
                SetRoomProp("AddRoundEvent", newEventId);
            }
        }

        // 3) 라운드 종료 → 다음 라운드로 넘어갈지, 최종 결과 낼지 결정
        currentRoundIndex++;
        if (currentRoundIndex >= totalRounds)
        {
            // 최종 우승자 결정: 최고 포인트
            var final = playerPoints.OrderByDescending(kv => kv.Value).ToList();
            var top = final.First();
            Player winner = PhotonNetwork.PlayerList.First(p => p.ActorNumber == top.Key);

            // UI 표시는 GameManager/UIManager에서 처리
            Debug.Log($"[RoundFlow] Final Winner: {winner.NickName} ({top.Value} pts)");
            // RoomProp로 발표자/점수 방송
            if (PhotonNetwork.IsMasterClient)
            {
                SetRoomProp("FinalWinnerNick", winner.NickName);
                SetRoomProp("FinalWinnerScore", top.Value);
                SetRoomProp("GameEnded", true);
            }
        }
        else
        {
            // 다음 라운드 준비 ? RoomProps에 누적 이벤트 반영
            if (PhotonNetwork.IsMasterClient)
            {
                // 만약 AddRoundEvent가 올라왔다면 누적
                if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("AddRoundEvent", out object ev) && ev is string evId && !string.IsNullOrEmpty(evId))
                {
                    stackedRoundEventIds.Add(evId);
                    // 저장형식: "id1,id2,id3"
                    SetRoomProp("StackedRoundEventsCsv", string.Join(",", stackedRoundEventIds));
                    // 소모(다음 라운드로 넘겼으니 비움)
                    SetRoomProp("AddRoundEvent", "");
                }

                // “NextGameMode” → “GameMode”로 채택
                if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("NextGameMode", out object nm) && nm is string nextMode && !string.IsNullOrEmpty(nextMode))
                {
                    SetRoomProp("GameMode", nextMode);
                    SetRoomProp("NextGameMode", "");
                }

                // 다음 라운드로 씬 재시작(캐릭터 재선택 or 그대로 진행 중 하나 선택)
                // 여기서는 기존 플로우 존중: SelectCharacterScene으로 재이동 → GameScene
                PhotonNetwork.LoadLevel("SelectCharacterScene");
            }
        }
    }

    // --- 간단 대체 로직(투표 UI 붙이기 전까지 사용) ---
    private string SuggestNextModeFallback()
    {
        // Room에 있는 모드 목록/현재 모드 참고
        var modes = new[] { "Showdown", "King of the Hill" }; // RoomManager에 이미 2개 사용중  :contentReference[oaicite:0]{index=0}
        string current = (string)PhotonNetwork.CurrentRoom.CustomProperties["GameMode"];
        // current가 Showdown이면 King of the Hill, 아니면 Showdown
        return (current == modes[0]) ? modes[1] : modes[0];
    }

    private string SuggestNextEventFallback()
    {
        // 기획서에 명시된 5개 이벤트 중 랜덤 하나  :contentReference[oaicite:1]{index=1}
        var events = new[] { "Spotlight", "GoldenAward", "HotOnion", "Paparazzi", "StageMalfunction" };
        // 이미 쌓인 이벤트는 중복 허용(“중첩되는 재미”) ? 중복 허용!  :contentReference[oaicite:2]{index=2}
        int idx = UnityEngine.Random.Range(0, events.Length);
        return events[idx];
    }

    private void SetRoomProp(string key, object val)
    {
        PhotonHashtable p = new PhotonHashtable { { key, val } };
        PhotonNetwork.CurrentRoom.SetCustomProperties(p);
    }
}
