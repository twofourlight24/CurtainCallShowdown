using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class RankRow
{
    public GameObject root;
    public TMP_Text nicknameText;
    public TMP_Text totalPointText;
}

public class TotalScorePanel : MonoBehaviour
{
    [Header("UI")]
    public RankRow[] rows;                 // 인원수만큼 켜짐
    public TMP_Text roundsLeftText;
    public TMP_Text stackedEventsText;
    public Button startVoteButtonForMaster;
    public GameObject guestText;

    // 투표 중복 시작 방지
    private bool voteStarted = false;

    public void Bind(Dictionary<int, int> totalPoints, int roundsLeft, string eventsCsv, bool isMaster)
    {
        // 랭킹 정렬 및 표시
        var ordered = totalPoints.OrderByDescending(kv => kv.Value).ToList();

        for (int i = 0; i < rows.Length; i++)
        {
            bool active = (i < ordered.Count);
            rows[i].root.SetActive(active);
            if (!active) continue;

            var kv = ordered[i];
            var player = PhotonNetwork.CurrentRoom?.GetPlayer(kv.Key);
            rows[i].nicknameText.text = player != null ? player.NickName : $"#{kv.Key}";
            rows[i].totalPointText.text = kv.Value.ToString();
        }

        roundsLeftText.text = $"남은 라운드: {roundsLeft}";
        stackedEventsText.text = string.IsNullOrEmpty(eventsCsv) ? "이벤트 없음" : eventsCsv;

        // 마스터/게스트 분기
        startVoteButtonForMaster.gameObject.SetActive(isMaster);
        guestText.SetActive(!isMaster);

        // 버튼 연결 (매번 새로 바인딩)
        startVoteButtonForMaster.onClick.RemoveAllListeners();
        if (isMaster)
        {
            startVoteButtonForMaster.onClick.AddListener(OnClickStartVoteAsMaster);
            // 혹시 이전 라운드에서 남은 상태가 있으면 초기화
            voteStarted = false;
            startVoteButtonForMaster.interactable = true;
        }
    }

    private void OnClickStartVoteAsMaster()
    {
        if (voteStarted) return; // 중복 방지
        if (!PhotonNetwork.IsMasterClient) return;

        voteStarted = true;
        startVoteButtonForMaster.interactable = false;

        // 1) 라운드 투표 시작 (옵션 3개, 30초 타이머)
        RoundFlowManager.Instance?.BeginGameModeVote(3, 20);

        // 2) 내 화면에서 총점 패널 닫고 투표 패널 열기
        var ui = GameManager.Instance?.uiManager;
        if (ui != null)
        {
            gameObject.SetActive(false);           // TotalScorePanel 닫기
            ui.OpenGameModeVotePanel();            // 내부에서 votePanel.InitializeFromRoom() 호출
        }
    }
}
