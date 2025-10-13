using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>특정 구역 점령으로 점수를 쌓아 제한시간 내 최다 득점자가 승리</summary>
public class KingOfTheHillMode : MonoBehaviourPunCallbacks, IGameMode
{
    public string ModeName => "King of the Hill";

    private GameManager gm;
    private float roundTime = 120f; // 2분
    private float timer;

    // 간단 점수표 (ActorNumber → 점수)
    private Dictionary<int, int> scores = new();

    // 힐 존(간단 구현: 월드 중심 반경)
    private Vector3 hillCenter = Vector3.zero;
    private float hillRadius = 5f;

    public string GetBriefDescription() => "특정 구역에 머물러 점수를 획득하세요! ";

    public void Initialize(GameManager gm)
    {
        this.gm = gm;
        Debug.Log("[KOTH] Initialized.");
    }

    public void StartRound()
    {
        Debug.Log("[KOTH] Round Started.");
        timer = roundTime;
        scores.Clear();
        foreach (var p in PhotonNetwork.PlayerList) scores[p.ActorNumber] = 0;

        // 라운드 시작 시 누적된 이벤트 적용
        var csv = PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("StackedRoundEventsCsv", out object v) ? (v as string) : "";
        var list = string.IsNullOrEmpty(csv) ? new List<string>() : new List<string>(csv.Split(','));
        RoundEventManager.Instance?.EnableStackedEvents(list);
    }

    public void EndRound()
    {
        Debug.Log("[KOTH] Round Ended.");
        RoundEventManager.Instance?.DisableAll();

        // 점수 → 순위 산출(내림차순)
        var ranking = new List<Player>(PhotonNetwork.PlayerList);
        ranking.Sort((a, b) => scores[b.ActorNumber].CompareTo(scores[a.ActorNumber]));
        gm.EndRound(ranking);
    }

    public void OnPlayerEliminated(Player player)
    {
        // 점령전은 탈락 개념이 필수는 아님. 필요 시 비활성 처리
    }

    public void OnRoundComplete(List<Player> ranking)
    {
        // 포인트/권한은 RoundFlowManager가 처리
    }

    private void Update()
    {
        if (!PhotonNetwork.InRoom) return;

        // 간단한 점수 틱 (모두 같은 프레임에서 돌아도 큰 문제 없음/정교화는 Master 전용 틱 권장)
        if (timer > 0f)
        {
            timer -= Time.deltaTime;

            foreach (var p in PhotonNetwork.PlayerList)
            {
                var charObj = gm.GetCharacterObject(p);
                if (charObj == null) continue;

                if (Vector3.Distance(charObj.transform.position, hillCenter) <= hillRadius)
                {
                    scores[p.ActorNumber] = scores.TryGetValue(p.ActorNumber, out int s) ? s + 1 : 1;
                }
            }

            if (timer <= 0f)
            {
                EndRound();
            }
        }
    }
}
