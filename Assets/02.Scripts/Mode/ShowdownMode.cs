using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public class ShowdownMode : MonoBehaviourPunCallbacks, IGameMode
{
    public string ModeName => "Showdown";

    private GameManager gm;
    private bool roundRunning = false;

    private readonly Dictionary<Player, int> lives = new(); // 플레이어별 남은 목숨

    // 무적 상태 추적
    private readonly HashSet<Player> invinciblePlayers = new();

    [Header("Config")]
    public int startingLives = 3;
    public float respawnDelay = 5f;
    public float invincibleTime = 2f;
    public string GetBriefDescription() => "모든 플레이어를 처치하세요!";
    public void Initialize(GameManager gm)
    {
        this.gm = gm;
        Debug.Log("[Showdown] Initialized.");
    }

    public void StartRound()
    {
        Debug.Log("[Showdown] Round Started.");
        roundRunning = true;

        lives.Clear();
        foreach (var p in PhotonNetwork.PlayerList)
        {
            lives[p] = startingLives;
            gm.uiManager.UpdateLifeUI(p, startingLives); // UI 초기화
        }

    }

    public void EndRound()
    {
        if (!roundRunning) return;
        roundRunning = false;
        Debug.Log("[Showdown] Round Ended.");
    }

    /// <summary>
    /// Player가 죽었을 때 호출 (CharacterBase -> GameManager -> ShowdownMode)
    /// </summary>
    public void OnPlayerEliminated(Player player)
    {
        if (!roundRunning || !lives.ContainsKey(player)) return;

        lives[player] -= 1;
        gm.uiManager.UpdateLifeUI(player, lives[player]);

        if (lives[player] > 0)
        {
            if (PhotonNetwork.IsMasterClient)
                gm.OrderRespawn(player, respawnDelay, invincibleTime);
        }
        else
        {
            var obj = gm.GetCharacterObject(player);
            if (obj) obj.SetActive(false);
            CheckForRoundEnd();
        }
    }

    private IEnumerator RespawnAfterDelay(Player player)
    {
        yield return new WaitForSeconds(respawnDelay);
        gm.StartCoroutine(gm.RespawnPlayer(player)); // GameManager의 RespawnPlayer 코루틴 재사용

        // 무적 시간 부여
        StartCoroutine(ApplyInvincibility(player, invincibleTime));
    }

    private IEnumerator ApplyInvincibility(Player player, float duration)
    {
        invinciblePlayers.Add(player);
        var obj = gm.GetCharacterObject(player);
        if (obj != null)
        {
            var renderer = obj.GetComponentInChildren<Renderer>();
            if (renderer) renderer.material.color = Color.cyan; // 간단한 시각효과
        }

        yield return new WaitForSeconds(duration);

        invinciblePlayers.Remove(player);
        if (obj != null)
        {
            var renderer = obj.GetComponentInChildren<Renderer>();
            if (renderer) renderer.material.color = Color.white;
        }
    }

    private void CheckForRoundEnd()
    {
        // 아직 목숨이 남아 있는 플레이어만 필터
        var alive = lives.Where(kv => kv.Value > 0).Select(kv => kv.Key).ToList();

        if (alive.Count <= 1)
        {
            EndRound();

            var ranking = PhotonNetwork.PlayerList.ToList();
            if (alive.Count == 1)
            {
                var winner = alive[0];
                ranking.Remove(winner);
                ranking.Insert(0, winner);
            }

            gm.EndRound(ranking);
        }
    }

    public void OnRoundComplete(List<Player> ranking)
    {
        RoundFlowManager.Instance.HandleRoundComplete(ranking);
    }

    /// <summary>
    /// 공격/데미지 계산 시 무적이면 무시하도록 체크
    /// </summary>
    public bool IsInvincible(Player player) => invinciblePlayers.Contains(player);
}
