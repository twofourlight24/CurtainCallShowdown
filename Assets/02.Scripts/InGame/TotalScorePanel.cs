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

    private bool voteStarted = false;

    public void Bind(Dictionary<int, int> totalPoints, int roundsLeft, string eventsCsv, bool isMaster)
    {
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

        startVoteButtonForMaster.gameObject.SetActive(isMaster);
        guestText.SetActive(!isMaster);

        startVoteButtonForMaster.onClick.RemoveAllListeners();
        if (isMaster)
        {
            voteStarted = false;
            startVoteButtonForMaster.interactable = true;
            startVoteButtonForMaster.onClick.AddListener(OnClickStartVoteAsMaster);
        }
    }

    // TotalScorePanel.cs
    private void OnClickStartVoteAsMaster()
    {
        if (voteStarted) return;
        if (!PhotonNetwork.IsMasterClient) return;
        voteStarted = true;
        startVoteButtonForMaster.interactable = false;

        // 1) 투표 세션 생성 (RoomProps 세팅)
        RoundFlowManager.Instance?.BeginGameModeVote(3, 30);

        // 2) 방장 로컬 즉시 열기 (레이스 방지)
        GameManager.Instance?.uiManager?.OpenGameModeVotePanel();

        // 3) 모두에게 열기 (RPC)
        GameManager.Instance?.OpenGameModeVotePanelForAll();

        // 4) 한 프레임 뒤 Total만 닫기 (상위 캔버스/패널은 건들지 않음)
        StartCoroutine(CloseAfterFrame());
    }

    private System.Collections.IEnumerator CloseAfterFrame()
    {
        yield return null;
        gameObject.SetActive(false);  // ★ 부모/Canvas 끄지 말 것!
    }

}
