using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable;

public class GamemodeVotePanel : MonoBehaviourPunCallbacks
{
    [Header("Refs")]
    public Transform modeButtonParent;           // 모드 버튼들이 들어갈 부모 (Grid/Vertical)
    public GameObject modeButtonPrefab;          // "게임모드 버튼" 프리팹 (안에 SelectImage, PlayerGroup 포함)
    public GameObject playerInfoPrefab;          // 버튼 안 PlayerGroup에 넣는 "플레이어 인포" 프리팹(아이콘+닉네임)
    public TMP_Text timerText;                   // 30초 카운트다운 텍스트
    public GameObject waitingVotePanel;          // 결과 기다리는 패널
    public TMP_Text waitingVoteResultText;       // "투표 결과..." → "OOO이 선택한 XXX가 선정되었습니다!"로 변경
    public CanvasGroup waitingVotePanelCanvas;   // 페이드 아웃용 (없으면 null)

    [Header("Colors")]
    public Color lastPlaceNickColor = Color.yellow;      // 꼴등 노란색
    public Color chosenPlayerNickColor = new Color(0.5f, 0.8f, 1f); // 하늘색 계열

    [Header("State (debug view)")]
    public List<string> activeOptions = new();  // 현재 투표 옵션(3개)
    public Dictionary<string, ModeButtonEntry> buttonByMode = new();

    // 내부 구조: 버튼에 대한 참조/컨테이너
    public class ModeButtonEntry
    {
        public string modeName;
        public GameObject go;
        public Button btn;
        public Image selectImage;
        public Transform playerGroup;
    }

    private bool isMaster => PhotonNetwork.IsMasterClient;
    private bool initialized = false;
    private double voteStartServerTime = 0; // 룸에 저장된 투표 시작(PhotonNetwork.Time 기반)
    private int lastPlaceActorNumber = -1;
    private int voteDurationSec = 30;
    private bool voteClosed = false;
    private HashSet<int> alreadySpawnedVoterUI = new(); // "VOTE_{Actor}"가 생길 때 한 번만 UI 생성

    // --- RoomProp 키 상수 ---
    const string PROP_VOTE_ACTIVE = "VoteActive";        // bool
    const string PROP_VOTE_OPTIONS = "VoteOptions";       // csv (e.g. "Showdown,King of the Hill,Something")
    const string PROP_VOTE_START_TS = "VoteStartTS";       // double (PhotonNetwork.Time)
    const string PROP_VOTE_LAST = "VoteLastActor";     // int
    const string PROP_VOTE_DONE = "VoteDone";          // bool
    const string PROP_VOTE_WIN_ACT = "VoteWinnerActor";   // int
    const string PROP_VOTE_WIN_MODE = "VoteWinnerMode";    // string

    // 플레이어별 표 저장 키: "VOTE_{actorNumber}" = "ModeName"
    static string VoteKeyFor(int actor) => $"VOTE_{actor}";

    // 외부에서 RoundFlowManager가 호출
    public void InitializeFromRoom()
    {
        initialized = false;
        voteClosed = false;
        ClearButtons();

        var room = PhotonNetwork.CurrentRoom;
        if (room == null || room.CustomProperties == null)
        {
            gameObject.SetActive(false);
            return;
        }

        // 필수 RoomProps 읽기
        if (!TryRead(room.CustomProperties, PROP_VOTE_OPTIONS, out string optionsCsv) || string.IsNullOrEmpty(optionsCsv)) { gameObject.SetActive(false); return; }
        if (!TryRead(room.CustomProperties, PROP_VOTE_START_TS, out double startTs)) { gameObject.SetActive(false); return; }
        TryRead(room.CustomProperties, PROP_VOTE_LAST, out lastPlaceActorNumber);
        TryRead(room.CustomProperties, PROP_VOTE_DONE, out bool doneFlag);

        voteStartServerTime = startTs;
        activeOptions = optionsCsv.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        if (activeOptions.Count == 0) { gameObject.SetActive(false); return; }

        // 버튼 생성
        foreach (var mode in activeOptions)
        {
            var go = Instantiate(modeButtonPrefab, modeButtonParent);
            var btn = go.GetComponentInChildren<Button>(true);
            var selImg = go.transform.Find("SelectImage")?.GetComponent<Image>();
            var group = go.transform.Find("PlayerSelectModeGroup");
            var entry = new ModeButtonEntry
            {
                modeName = mode,
                go = go,
                btn = btn,
                selectImage = selImg,
                playerGroup = group != null ? group : go.transform
            };
            buttonByMode[mode] = entry;

            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnClickVote(mode));
            }
            if (selImg != null) selImg.gameObject.SetActive(false);
        }

        // 시작 시 이미 제출된 표 UI 반영(재진입, Late-join 대비)
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (room.CustomProperties.TryGetValue(VoteKeyFor(p.ActorNumber), out var v) && v is string votedMode && !string.IsNullOrEmpty(votedMode))
            {
                SpawnVoterUI(p, votedMode);
            }
        }

        // 타이머 시작
        StopAllCoroutines();
        StartCoroutine(TimerRoutine());

        // 투표 완료 상태였다면 결과 UI 보여주기
        if (doneFlag)
        {
            OnVoteDoneRoomProp();
        }

        initialized = true;
        gameObject.SetActive(true);
    }

    void ClearButtons()
    {
        foreach (Transform c in modeButtonParent) Destroy(c.gameObject);
        buttonByMode.Clear();
        alreadySpawnedVoterUI.Clear();
        waitingVotePanel.SetActive(false);
        if (waitingVotePanelCanvas != null) waitingVotePanelCanvas.alpha = 1f;
        if (timerText != null) timerText.text = "30";
    }

    void OnEnable()
    {
        // 재활성화 시 동기화 보정
        if (PhotonNetwork.InRoom) InitializeFromRoom();
    }

    // === 투표 클릭 ===
    void OnClickVote(string mode)
    {
        if (voteClosed) return;

        var me = PhotonNetwork.LocalPlayer;
        // 이미 표가 있으면 무시 (원하면 '변경 허용' 로직으로 바꿀 수 있음)
        if (PhotonNetwork.CurrentRoom.CustomProperties.ContainsKey(VoteKeyFor(me.ActorNumber))) return;

        SetRoomProp(VoteKeyFor(me.ActorNumber), mode);
        // UI는 OnRoomPropertiesUpdate에서 SpawnVoterUI로 그려짐 (네트워크 일관)
    }

    // === 타이머 ===
    IEnumerator TimerRoutine()
    {
        while (true)
        {
            // 남은 시간 계산 (PhotonNetwork.Time 기준)
            double now = PhotonNetwork.Time;
            double endTs = voteStartServerTime + voteDurationSec;
            double remain = endTs - now;
            int seconds = Mathf.Max(0, (int)Math.Floor(remain));

            if (timerText) timerText.text = seconds.ToString();

            if (remain <= 0)
            {
                // 마스터만 집계/종료
                if (!voteClosed && isMaster)
                {
                    voteClosed = true;
                    ComputeAndBroadcastResult();
                }
                break;
            }
            yield return null;
        }
    }

    // === 결과 집계(마스터) ===
    void ComputeAndBroadcastResult()
    {
        var room = PhotonNetwork.CurrentRoom;
        if (room == null) return;

        // 수집: 각 플레이어 → 선택 모드
        var votes = new Dictionary<int, string>(); // actor -> mode
        foreach (var p in PhotonNetwork.PlayerList)
        {
            if (room.CustomProperties.TryGetValue(VoteKeyFor(p.ActorNumber), out var v) && v is string m && !string.IsNullOrEmpty(m))
            {
                votes[p.ActorNumber] = m;
            }
        }

        // 아무도 투표 안 했으면 옵션 중 랜덤
        if (votes.Count == 0)
        {
            string m = activeOptions[UnityEngine.Random.Range(0, activeOptions.Count)];
            // winner는 null로 둘 수 없으니 랜덤으로 한 명도 고름 (모드만 중요)
            int dummyActor = PhotonNetwork.MasterClient.ActorNumber;
            SetRoomProps(new PhotonHashtable {
                { PROP_VOTE_DONE, true },
                { PROP_VOTE_WIN_ACT, dummyActor },
                { PROP_VOTE_WIN_MODE, m }
            });
            // NextGameMode 설정은 RoundFlowManager가 훗단에서 가져다 씀
            return;
        }

        // 가중 랜덤 풀 구성 (꼴등이면 weight 2, 아니면 1)
        var bag = new List<int>(); // actorNumber만 넣었다가 나중에 그 사람의 모드로 매핑
        foreach (var kv in votes)
        {
            int weight = (kv.Key == lastPlaceActorNumber) ? 2 : 1;
            for (int i = 0; i < weight; i++) bag.Add(kv.Key);
        }
        int winnerActor = bag[UnityEngine.Random.Range(0, bag.Count)];
        string winnerMode = votes[winnerActor];

        // 결과 브로드캐스트
        SetRoomProps(new PhotonHashtable {
            { PROP_VOTE_DONE, true },
            { PROP_VOTE_WIN_ACT, winnerActor },
            { PROP_VOTE_WIN_MODE, winnerMode }
        });
    }

    // === RoomProp 업데이트 수신 ===
    public override void OnRoomPropertiesUpdate(PhotonHashtable propertiesThatChanged)
    {
        if (!initialized) return;

        // 표 생성 반영
        foreach (var p in PhotonNetwork.PlayerList)
        {
            string key = VoteKeyFor(p.ActorNumber);
            if (propertiesThatChanged.TryGetValue(key, out var v) && v is string votedMode && !string.IsNullOrEmpty(votedMode))
            {
                SpawnVoterUI(p, votedMode);
            }
        }

        // 완료 신호
        if (propertiesThatChanged.TryGetValue(PROP_VOTE_DONE, out var done) && done is bool b && b)
        {
            OnVoteDoneRoomProp();
        }
    }

    // === 투표자 UI 스폰 ===
    void SpawnVoterUI(Player voter, string mode)
    {
        if (!buttonByMode.TryGetValue(mode, out var entry)) return;

        // 중복 방지: "VOTE_{actor}"가 찍힌 이후 한 번만
        if (!alreadySpawnedVoterUI.Add(voter.ActorNumber)) return;

        var go = Instantiate(playerInfoPrefab, entry.playerGroup);
        var icon = go.transform.Find("Icon")?.GetComponent<Image>();
        var nameText = go.transform.Find("Nick")?.GetComponent<TMP_Text>();

        // 캐릭터 아이콘
        var chObj = GameManager.Instance?.GetCharacterObject(voter);
        var data = chObj ? chObj.GetComponent<CharacterData>() : null;
        if (icon && data && data.data.characterIcon != null)
            icon.sprite = data.data.characterIcon;

        // 닉네임 + 꼴등 색
        if (nameText)
        {
            nameText.text = voter.NickName;
            if (voter.ActorNumber == lastPlaceActorNumber) nameText.color = lastPlaceNickColor;
        }
    }

    // === 투표 완료 연출 ===
    void OnVoteDoneRoomProp()
    {
        var room = PhotonNetwork.CurrentRoom;
        if (room == null) return;

        string winnerMode = room.CustomProperties.TryGetValue(PROP_VOTE_WIN_MODE, out var wm) ? (wm as string) : "";
        int winnerActor = room.CustomProperties.TryGetValue(PROP_VOTE_WIN_ACT, out var wa) ? (int)wa : -1;

        // 선정된 버튼 하이라이트 + 선정자 닉네임 하늘색
        if (!string.IsNullOrEmpty(winnerMode) && buttonByMode.TryGetValue(winnerMode, out var entry))
        {
            if (entry.selectImage) entry.selectImage.gameObject.SetActive(true);

            // 플레이어 인포 그룹에서 winnerActor에 해당하는 텍스트를 찾아 색 변경
            foreach (Transform child in entry.playerGroup)
            {
                var nameText = child.Find("Nick")?.GetComponent<TMP_Text>();
                var tagActor = child.GetComponent<VoterActorTag>(); // 선택사항: 프리팹에 부착해서 ActorNumber 저장하면 탐색 쉬움
                if (nameText != null)
                {
                    if (tagActor != null && tagActor.actorNumber == winnerActor)
                        nameText.color = chosenPlayerNickColor;
                    else if (tagActor == null)
                    {
                        // 태그를 안 쓴다면, Nick으로 비교(동명이인 위험은 낮다고 가정)
                        if (nameText.text == PhotonNetwork.CurrentRoom?.GetPlayer(winnerActor)?.NickName)
                            nameText.color = chosenPlayerNickColor;
                    }
                }
            }
        }

        // Waiting 패널 연출
        StartCoroutine(WaitingSequence(winnerActor, winnerMode));
    }

    IEnumerator WaitingSequence(int winnerActor, string winnerMode)
    {
        waitingVotePanel.SetActive(true);
        if (waitingVoteResultText) waitingVoteResultText.text = "투표 결과...";

        // 3초 기다렸다가 메시지 변경
        yield return new WaitForSeconds(3f);

        var winnerPlayer = PhotonNetwork.CurrentRoom?.GetPlayer(winnerActor);
        string nick = winnerPlayer != null ? winnerPlayer.NickName : "누군가";
        if (waitingVoteResultText) waitingVoteResultText.text = $"{nick}이(가) 선택한 <color=#7FD0FF>{winnerMode}</color> 가 선정되었습니다!";

        // 페이드 아웃(선택)
        if (waitingVotePanelCanvas != null)
        {
            float t = 0f;
            float dur = 1.2f;
            while (t < dur)
            {
                t += Time.deltaTime;
                waitingVotePanelCanvas.alpha = Mathf.Lerp(1f, 0f, t / dur);
                yield return null;
            }
        }

        waitingVotePanel.SetActive(false);

        // 5초 후 다음 라운드 진행(마스터만)
        if (isMaster && !string.IsNullOrEmpty(winnerMode))
        {
            // NextGameMode 설정 → RoundFlowManager가 이 값을 읽어 다음 라운드 시작
            SetRoomProps(new PhotonHashtable {
                { "NextGameMode", winnerMode }
            });

            yield return new WaitForSeconds(5f);

            // 다음 라운드 시작 트리거
            RoundFlowManager.Instance?.OnVoteFinishedAndReadyToStartNextRound();
        }
    }

    // === 유틸 ===
    static bool TryRead<T>(PhotonHashtable ht, string key, out T value)
    {
        if (ht != null && ht.TryGetValue(key, out var v) && v is T cast)
        {
            value = cast; return true;
        }
        value = default; return false;
    }

    void SetRoomProp(string key, object val)
    {
        SetRoomProps(new PhotonHashtable { { key, val } });
    }

    void SetRoomProps(PhotonHashtable p)
    {
        PhotonNetwork.CurrentRoom?.SetCustomProperties(p);
    }
}

// 선택사항: Voter UI 프리팹에 붙여서 ActorNumber 기억
public class VoterActorTag : MonoBehaviour
{
    public int actorNumber;
}
