using Photon.Pun;
using Photon.Realtime;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public class GamemodeVotePanel : MonoBehaviourPunCallbacks
{
    [Header("Wiring")]
    public Transform modeButtonParent;     // 모드 버튼들이 들어갈 부모
    public GameObject modeButtonTemplate;  // 비활성 템플릿(ModeButtonView 포함)
    public TMP_Text timerText;
    public GameObject waitingVotePanel;
    public TMP_Text voteResultText;

    [Header("Colors")]
    public Color lastPlaceNickColor = Color.yellow;            // 꼴등 표시 색(닉네임)
    public Color chosenPlayerNickColor = new Color(0.5f, 0.8f, 1f); // 당첨자 닉 하이라이트

    private readonly List<ModeButtonView> buttons = new();
    private bool voteActive = false;
    private double voteStartTime;
    private int voteDuration = 30;
    private bool resultShown = false;
    private int lastPlaceActorNumber = -1;
    private string localSelectedMode = "";

    // RoomProp 키
    private const string PROP_VOTE_ACTIVE = "VoteActive";
    private const string PROP_VOTE_OPTIONS = "VoteOptions";
    private const string PROP_VOTE_START_TS = "VoteStartTS";
    private const string PROP_VOTE_LAST = "VoteLastActor";
    private const string PROP_VOTE_DONE = "VoteDone";
    private const string PROP_VOTE_WIN_MODE = "VoteWinnerMode";
    private const string PROP_VOTE_WIN_ACT = "VoteWinnerActor";

    private static string VoteKeyFor(int actor) => $"VOTE_{actor}";

    private void Awake()
    {
        EnsureTemplate();
        if (modeButtonTemplate != null && modeButtonTemplate.activeSelf)
            modeButtonTemplate.SetActive(false);

        if (waitingVotePanel != null) waitingVotePanel.SetActive(false);
        gameObject.SetActive(false); // RPC/버튼에서만 엶
    }

    /// 템플릿 자동 보정(인스펙터 누락 방지)
    private bool EnsureTemplate()
    {
        if (modeButtonTemplate != null) return true;

        if (modeButtonParent != null && modeButtonParent.childCount > 0)
        {
            var first = modeButtonParent.GetChild(0).gameObject;
            modeButtonTemplate = first;
            if (modeButtonTemplate.activeSelf) modeButtonTemplate.SetActive(false);
            Debug.LogWarning($"[Vote] modeButtonTemplate auto-assigned: {first.name}");
            return true;
        }

        Debug.LogError("[Vote] modeButtonTemplate NOT assigned and parent has no child!");
        return false;
    }

    /// UI 열 때 호출: RoomProps를 읽어 구성
    public void InitializeFromRoom()
    {
        var room = PhotonNetwork.CurrentRoom;
        if (room == null || room.CustomProperties == null) return;
        if (!EnsureTemplate()) return;

        string optionsCsv = room.CustomProperties.TryGetValue(PROP_VOTE_OPTIONS, out var opt) ? opt as string : "";
        if (string.IsNullOrEmpty(optionsCsv)) { Debug.LogWarning("[Vote] VoteOptions empty."); return; }

        if (room.CustomProperties.TryGetValue(PROP_VOTE_START_TS, out var ts) && ts is double t) voteStartTime = t;
        else voteStartTime = PhotonNetwork.Time;

        lastPlaceActorNumber = room.CustomProperties.TryGetValue(PROP_VOTE_LAST, out var la) ? (int)la : -1;

        // 버튼 재구성
        foreach (Transform c in modeButtonParent)
            if (c != modeButtonTemplate.transform) Destroy(c.gameObject);
        buttons.Clear();

        var modes = optionsCsv.Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToList();
        foreach (var m in modes)
        {
            var go = Instantiate(modeButtonTemplate, modeButtonParent);
            go.SetActive(true);
            var view = go.GetComponent<ModeButtonView>();
            view.Bind(m);
            view.SetOnClick(OnClickVote);
            buttons.Add(view);
        }

        // 이미 제출된 표 UI 복원(재진입/늦게 합류 대비)
        foreach (var p in PhotonNetwork.PlayerList)
        {
            string key = VoteKeyFor(p.ActorNumber);
            if (room.CustomProperties.TryGetValue(key, out var v) && v is string votedMode && !string.IsNullOrEmpty(votedMode))
            {
                PlaceOrMoveVoterUI(p, votedMode);
            }
        }

        voteDuration = 30;
        resultShown = false;
        voteActive = true;

        gameObject.SetActive(true);
        StopAllCoroutines();
        StartCoroutine(VoteTimerRoutine());
    }

    /// 투표(변경 가능): 기존 표 UI 제거 → 새 버튼에 표시 → RoomProps 갱신
    private void OnClickVote(string mode)
    {
        if (!voteActive) return;

        var me = PhotonNetwork.LocalPlayer;
        string key = VoteKeyFor(me.ActorNumber);

        // 로컬 즉시 반영 + 기억
        localSelectedMode = mode;
        RemoveVoterUIFromAllButtons(me.ActorNumber);
        PlaceOrMoveVoterUI(me, mode);

        // UI에서 기존 표시 제거 후 새 버튼에 배치
        RemoveVoterUIFromAllButtons(me.ActorNumber);
        PlaceOrMoveVoterUI(me, mode);

        // RoomProps 갱신(덮어쓰기)
        var ht = new ExitGames.Client.Photon.Hashtable { { key, mode } };
        PhotonNetwork.CurrentRoom.SetCustomProperties(ht);
    }

    /// 모든 버튼에서 특정 배우의 표 UI 제거
    private void RemoveVoterUIFromAllButtons(int actorNumber)
    {
        foreach (var b in buttons)
            b.RemoveVoter(actorNumber);
    }

    /// 특정 모드 버튼 아래에 표 UI 배치(중복 방지 + 꼴등 색 + x2 배지)
    private void PlaceOrMoveVoterUI(Player p, string mode)
    {
        var btn = buttons.FirstOrDefault(b => b.modeName == mode);
        if (btn == null) return;

        // 캐릭터 아이콘
        Sprite icon = null;
        var ch = GameManager.Instance?.GetCharacterObject(p);
        var data = ch ? ch.GetComponent<CharacterData>() : null;
        if (data == null)
        {
            if (p.CustomProperties != null &&
                p.CustomProperties.TryGetValue("SelectedCharacterName", out var n) &&
                n is string prefabName && !string.IsNullOrEmpty(prefabName))
            {
                var prefab = Resources.Load<GameObject>("Characters/" + prefabName);
                if (prefab != null)
                {
                    var cd = prefab.GetComponent<CharacterData>();
                    if (cd.data.characterIcon != null) data = cd;
                }
            }
        }
        if (data != null && data.data.characterIcon != null)
            icon = data.data.characterIcon;

        // 꼴등 색
        var nameColor = (p.ActorNumber == lastPlaceActorNumber) ? lastPlaceNickColor : Color.white;

        // x2 배지: UI에만 “x2” 띄우고, 실제 집계는 EndVote에서 가중치로 처리
        bool showX2 = (p.ActorNumber == lastPlaceActorNumber);

        btn.AddOrUpdateVoter(p, icon, nameColor, p.ActorNumber, showX2);
    }

    private IEnumerator VoteTimerRoutine()
    {
        while (voteActive)
        {
            double left = voteDuration - (PhotonNetwork.Time - voteStartTime);
            int sec = Mathf.Max(0, Mathf.CeilToInt((float)left));
            if (timerText != null) timerText.text = $"{sec}";

            if (left <= 0)
            {
                voteActive = false;
                if (PhotonNetwork.IsMasterClient) ComputeAndBroadcastResult();
                break;
            }
            yield return null;
        }
    }

    /// 집계: 꼴등 가중치(2배)는 여기서만 적용. UI는 배지로만.
    private void ComputeAndBroadcastResult()
    {
        if (resultShown) return;

        var room = PhotonNetwork.CurrentRoom;
        if (room == null) return;

        var votes = new Dictionary<string, List<Player>>(); // mode -> voters
        foreach (var kv in room.CustomProperties)
        {
            if (kv.Key is string k && k.StartsWith("VOTE_"))
            {
                if (!int.TryParse(k.Replace("VOTE_", ""), out int actor)) continue;
                string mode = kv.Value as string;
                if (string.IsNullOrEmpty(mode)) continue;

                var p = room.GetPlayer(actor);
                if (p == null) continue;

                if (!votes.ContainsKey(mode)) votes[mode] = new List<Player>();
                votes[mode].Add(p);
            }
        }

        string winnerMode;
        int winnerActor;

        if (votes.Count == 0)
        {
            var allModes = buttons.Select(b => b.modeName).ToList();
            winnerMode = allModes[Random.Range(0, allModes.Count)];
            winnerActor = PhotonNetwork.PlayerList[Random.Range(0, PhotonNetwork.PlayerList.Length)].ActorNumber;
        }
        else
        {
            var bag = new List<(Player voter, string mode)>();
            foreach (var kv in votes)
            {
                foreach (var voter in kv.Value)
                {
                    bag.Add((voter, kv.Key));
                    if (voter.ActorNumber == lastPlaceActorNumber) bag.Add((voter, kv.Key)); // 가중치 2배
                }
            }

            var pick = bag[Random.Range(0, bag.Count)];
            winnerActor = pick.voter.ActorNumber;
            winnerMode = pick.mode;
        }

        var ht = new ExitGames.Client.Photon.Hashtable
        {
            { PROP_VOTE_DONE, true },
            { PROP_VOTE_WIN_MODE, winnerMode },
            { PROP_VOTE_WIN_ACT,  winnerActor },
            { "NextGameMode",     winnerMode }
        };
        room.SetCustomProperties(ht);

        StartCoroutine(ShowResultRoutine(winnerActor, winnerMode));
    }

    private IEnumerator ShowResultRoutine(int winnerActor, string winnerMode)
    {
        if (waitingVotePanel != null) waitingVotePanel.SetActive(true);
        if (voteResultText != null) voteResultText.text = "투표 결과를 집계 중...";

        yield return new WaitForSeconds(3f);

        var winnerPlayer = PhotonNetwork.CurrentRoom?.GetPlayer(winnerActor);
        string nick = winnerPlayer != null ? winnerPlayer.NickName : "???";
        if (voteResultText != null)
            voteResultText.text = $"{nick} 님이 선택한 [{winnerMode}] 모드가 선정되었습니다!";

        foreach (var b in buttons)
        {
            bool selected = b.modeName == winnerMode;
            b.SetSelectedVisual(selected);
            b.AccentWinnerNick(winnerActor, chosenPlayerNickColor);
        }

        yield return new WaitForSeconds(5f);

        if (waitingVotePanel != null) waitingVotePanel.SetActive(false);
        gameObject.SetActive(false);

        if (PhotonNetwork.IsMasterClient)
            RoundFlowManager.Instance?.OnVoteFinishedAndReadyToStartNextRound();
    }

    /// 다른 플레이어가 표를 바꿨을 때도 UI 이동 반영
    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable changedProps)
    {
        // 표 변경 반영
        foreach (var p in PhotonNetwork.PlayerList)
        {
            string key = VoteKeyFor(p.ActorNumber);
            if (changedProps.TryGetValue(key, out var v))
            {
                string votedMode = v as string;

                //    들어온 값이 빈표("")면 무시해, UI 지우지 않음.
                if (p.IsLocal && string.IsNullOrEmpty(votedMode) && !string.IsNullOrEmpty(localSelectedMode))
                    continue;

                // 일반 처리: 전 버튼에서 제거 후, 값이 있으면 재배치
                RemoveVoterUIFromAllButtons(p.ActorNumber);
                if (!string.IsNullOrEmpty(votedMode))
                    PlaceOrMoveVoterUI(p, votedMode);

                // 내 표가 네트워크로 확정된 경우, 로컬 기억도 동기화
                if (p.IsLocal) localSelectedMode = votedMode ?? "";
            }
        }

        // 결과 나왔을 때 보조
        if (changedProps.TryGetValue(PROP_VOTE_DONE, out var done) && done is bool b && b)
        {
            string wm = PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(PROP_VOTE_WIN_MODE, out var m) ? (m as string) : "";
            int wa = PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(PROP_VOTE_WIN_ACT, out var a) ? (int)a : -1;
            if (!resultShown && !string.IsNullOrEmpty(wm) && wa != -1)
            {
                resultShown = true;
                StartCoroutine(ShowResultRoutine(wa, wm));
            }
        }
    }
}
