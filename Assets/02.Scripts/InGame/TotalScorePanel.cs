using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

[System.Serializable]
public class RankRow
{
    public GameObject root;
    public TMP_Text nicknameText;
    public TMP_Text totalPointText;
}

public class TotalScorePanel : MonoBehaviour
{
    public RankRow[] rows; // 인원수만큼 켜짐
    public TMP_Text roundsLeftText;
    public TMP_Text stackedEventsText;
    public UnityEngine.UI.Button startVoteButtonForMaster;
    public GameObject guestText;

    public void Bind(Dictionary<int, int> totalPoints, int roundsLeft, string eventsCsv, bool isMaster)
    {
        var ordered = totalPoints.OrderByDescending(kv => kv.Value).ToList();

        for (int i = 0; i < rows.Length; i++)
        {
            bool active = (i < ordered.Count);
            rows[i].root.SetActive(active);
            if (!active) continue;

            var kv = ordered[i];
            var player = PhotonNetwork.CurrentRoom.GetPlayer(kv.Key);
            rows[i].nicknameText.text = player != null ? player.NickName : $"#{kv.Key}";
            rows[i].totalPointText.text = kv.Value.ToString();
        }

        roundsLeftText.text = $"남은 라운드: {roundsLeft}";
        stackedEventsText.text = string.IsNullOrEmpty(eventsCsv) ? "이벤트 없음" : eventsCsv;

        startVoteButtonForMaster.gameObject.SetActive(isMaster);
        guestText.SetActive(!isMaster);
    }
}
