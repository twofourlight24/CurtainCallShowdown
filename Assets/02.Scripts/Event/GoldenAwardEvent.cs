using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>
/// 라운드 중 무작위 플레이어를 10초간 황금 동상으로 만드는 이벤트.
/// 황금 상태: 이동/공격 불가, 체력 풀, HP바 황금색.
/// 다른 플레이어가 공격 시, 준 데미지만큼 라운드 점수에 합산.
/// 30초 이전엔 발동 X, 그 후 30~60초 랜덤 주기로 발생.
/// </summary>
public class GoldenAwardEvent : MonoBehaviourPunCallbacks, IRoundEvent
{
    public string Id => "GoldenAward";
    public string DisplayName => "골든 어워드";

    private RoundEventContext ctx;
    private bool active = false;
    private Coroutine loopCo;

    [Header("Settings")]
    public float minStartDelay = 30f;        // 첫 발동 최소 시간
    public Vector2 intervalRange = new Vector2(30f, 60f); // 발동 간격
    public float statueDuration = 10f;       // 황금상 지속시간
    public Color goldColor = new Color(1f, 0.84f, 0.1f);

    private readonly Dictionary<int, float> goldenDamage = new(); // 공격 데미지 누적 (actorNum -> dmg)

    public void EnableEvent(RoundEventContext ctx)
    {
        if (active) return;
        this.ctx = ctx;
        active = true;
        loopCo = StartCoroutine(EventLoop());
        Debug.Log($"[GoldenAward] Enabled");
    }

    public void DisableEvent(RoundEventContext ctx)
    {
        active = false;
        if (loopCo != null) StopCoroutine(loopCo);
        loopCo = null;
        goldenDamage.Clear();
        Debug.Log($"[GoldenAward] Disabled");
    }

    private IEnumerator EventLoop()
    {
        yield return new WaitForSeconds(minStartDelay);

        while (active && PhotonNetwork.InRoom)
        {
            if (PhotonNetwork.IsMasterClient)
                TriggerGoldenAward();

            float wait = Random.Range(intervalRange.x, intervalRange.y);
            yield return new WaitForSeconds(wait);
        }
    }

    private void TriggerGoldenAward()
    {
        var players = PhotonNetwork.PlayerList
            .Where(p => ctx.gm?.GetCharacterObject(p) != null)
            .ToList();

        if (players.Count == 0) return;

        var target = players[Random.Range(0, players.Count)];
        photonView.RPC(nameof(RPC_ActivateGoldenAward), RpcTarget.All, target.ActorNumber, statueDuration);
    }

    [PunRPC]
    private void RPC_ActivateGoldenAward(int actorNumber, float duration)
    {
        var player = PhotonNetwork.CurrentRoom?.GetPlayer(actorNumber);
        if (player == null) return;

        var go = ctx.gm?.GetCharacterObject(player);
        if (go == null) return;

        var ch = go.GetComponent<CharacterBase>();
        if (ch == null) return;

        ch.CurHp = ch.MaxHp;
        ch.ApplyGoldenStatue(duration, true);
        ctx.ui?.SetHpBarColor(player, goldColor);

        // 지속시간 끝나면 HP바 색 복구
        StartCoroutine(RestoreHpBarAfter(player, duration));
    }

    private IEnumerator RestoreHpBarAfter(Player p, float dur)
    {
        yield return new WaitForSeconds(dur);
        ctx.ui?.ResetHpBarColor(p);
    }

    /// <summary>Projectile이 호출할 수 있도록 공개: 황금 동상에게 데미지 기록</summary>
    public void RegisterGoldenDamage(int attackerActor, float dmg)
    {
        if (attackerActor <= 0 || dmg <= 0f) return;
        if (!goldenDamage.ContainsKey(attackerActor)) goldenDamage[attackerActor] = 0f;
        goldenDamage[attackerActor] += dmg;
    }

    /// <summary>라운드 종료 시 점수에 반영하도록 RoundFlowManager에서 불러줌</summary>
    public void ApplyGoldenScores(Dictionary<int, int> totalPoints)
    {
        foreach (var kv in goldenDamage)
        {
            int actor = kv.Key;
            int bonus = Mathf.RoundToInt(kv.Value);
            if (!totalPoints.ContainsKey(actor))
                totalPoints[actor] = 0;
            totalPoints[actor] += bonus;
        }
        goldenDamage.Clear();
    }
}
