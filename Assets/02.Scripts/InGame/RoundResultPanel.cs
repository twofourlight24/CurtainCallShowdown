using Photon.Realtime;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class ResultBorder
{
    public GameObject root;
    public Image characterImage;
    public TMP_Text nickname;
    public TMP_Text roundPointText;
}

public class RoundResultPanel : MonoBehaviour
{
    public ResultBorder[] borders; // 인스펙터에 1등/2등/3등/4등 순서대로 할당
    public UnityEngine.UI.Button nextButtonForMaster;
    public GameObject guestText;

    public void Bind(List<Player> ranking, Dictionary<int, int> roundPoints, bool isMaster)
    {
        for (int i = 0; i < borders.Length; i++)
        {
            bool active = (i < ranking.Count);
            borders[i].root.SetActive(active);
            if (!active) continue;

            var player = ranking[i];
            borders[i].nickname.text = player.NickName;

            int score = 0;
            if (roundPoints != null && roundPoints.TryGetValue(player.ActorNumber, out var val))
                score = val;
            borders[i].roundPointText.text = $"+{score}";

            var img = TryGetCharacterIconOf(player);
            if (img != null) borders[i].characterImage.sprite = img;

            if (isMaster)
            {
                nextButtonForMaster.onClick.RemoveAllListeners();
                nextButtonForMaster.onClick.AddListener(() =>
                {
                    var rf = RoundFlowManager.Instance;
                    var totals = rf.GetTotalPoints();
                    int roundsLeft = rf.totalRounds - rf.currentRoundIndex;
                    string eventsCsv = string.Join(",", rf.stackedRoundEventIds);

                    GameManager.Instance.uiManager.ShowTotalScoreUI(totals, roundsLeft, eventsCsv);
                });
            }
        }

        nextButtonForMaster.gameObject.SetActive(isMaster);
        guestText.SetActive(!isMaster);
    }
    private Sprite TryGetCharacterIconOf(Player p)
    {
        // GameManager에서 플레이어 캐릭터 오브젝트를 찾아 아이콘을 뽑아옵니다.
        var go = GameManager.Instance?.GetCharacterObject(p);
        if (go == null) return null;

        var data = go.GetComponent<CharacterData>();
        return data != null ? data.data.characterImg : null;
    }
}
