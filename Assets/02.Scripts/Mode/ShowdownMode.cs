using UnityEngine;
using TMPro;
using Photon.Pun;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using System.Linq;
using System.Collections;

public class ShowdownMode : MonoBehaviourPunCallbacks
{
    private GameManager gameManager;
    private int lifeCount = 3;
    private Dictionary<string, int> playerLives = new Dictionary<string, int>();

    /// <summary>
    /// 게임 모드를 초기화하고 필요한 참조를 설정합니다.
    /// </summary>
    public void Initialize(GameManager manager)
    {
        gameManager = manager;
        InitializeShowdownMode();
    }

    /// <summary>
    /// 쇼다운 모드를 초기화하고 플레이어 목숨을 설정합니다.
    /// </summary>
    private void InitializeShowdownMode()
    {
        // 모든 플레이어의 목숨 초기화
        foreach (var player in PhotonNetwork.PlayerList)
        {
            playerLives[player.NickName] = lifeCount;
        }

        // 로컬 플레이어만 자신의 캐릭터를 스폰
        if (PhotonNetwork.LocalPlayer != null)
        {
            // GameManager에서 캐릭터 스폰 코루틴 호출
            gameManager.StartCoroutine(gameManager.WaitForCharacterSelectionAndSpawn());
        }
    }

    /// <summary>
    /// 캐릭터의 체력이 0이 되었을 때 호출됩니다.
    /// </summary>
    /// <param name="player">체력을 잃은 플레이어</param>
    public void OnPlayerDied(Photon.Realtime.Player player)
    {
        if (!playerLives.ContainsKey(player.NickName)) return;

        // 목숨 감소
        playerLives[player.NickName]--;

        // GameManager를 통해 UI 업데이트
        gameManager.UpdateLifeUI(player, playerLives[player.NickName]);

        if (playerLives[player.NickName] <= 0)
        {
            // 목숨이 0이면 게임에서 탈락
            player.SetCustomProperties(new ExitGames.Client.Photon.Hashtable { { "IsEliminated", true } });
            CheckForWinner();
        }
        else
        {
            // 목숨이 남아있으면 부활
            gameManager.StartCoroutine(gameManager.RespawnPlayer(player));
        }
    }

    /// <summary>
    /// 최후의 1인이 남았는지 확인하고 게임을 종료합니다.
    /// </summary>
    private void CheckForWinner()
    {
        var activePlayers = PhotonNetwork.PlayerList
            .Where(p => !(p.CustomProperties.ContainsKey("IsEliminated") && (bool)p.CustomProperties["IsEliminated"]))
            .ToList();

        if (activePlayers.Count <= 1)
        {
            // GameManager를 통해 게임 종료
            gameManager.EndGame(activePlayers.FirstOrDefault());
        }
    }
}
