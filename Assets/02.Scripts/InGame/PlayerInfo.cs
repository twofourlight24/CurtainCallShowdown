using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Photon.Pun;

/// <summary>
/// 각 플레이어 정보 패널을 관리하는 스크립트.
/// 플레이어 닉네임, 방장 여부, 준비 상태 UI를 설정합니다.
/// </summary>
public class PlayerInfo : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text nicknameText;
    public GameObject hostImage;
    public GameObject readyCheckImage;

    // 플레이어의 Photon.Realtime.Player 객체
    private Photon.Realtime.Player player;

    /// <summary>
    /// 플레이어 정보를 초기화하고 UI를 업데이트합니다.
    /// </summary>
    /// <param name="player">Photon 플레이어 객체</param>
    public void Setup(Photon.Realtime.Player player)
    {
        this.player = player;
        nicknameText.text = player.NickName;

        // 방장 여부에 따라 UI 설정
        if (player.IsMasterClient)
        {
            hostImage.gameObject.SetActive(true);
            readyCheckImage.gameObject.SetActive(false); // 방장은 준비 체크 필요 없음
        }
        else
        {
            hostImage.gameObject.SetActive(false);
            readyCheckImage.gameObject.SetActive(false); // 초기에는 비활성화
        }
    }

    /// <summary>
    /// 플레이어의 준비 상태 UI를 업데이트합니다.
    /// </summary>
    /// <param name="isReady">준비 상태 여부</param>
    public void SetReadyState(bool isReady)
    {
        if (readyCheckImage != null)
        {
            readyCheckImage.gameObject.SetActive(isReady);
        }
    }

    /// <summary>
    /// 현재 PlayerInfo에 연결된 Photon 플레이어 객체를 반환합니다.
    /// </summary>
    public Photon.Realtime.Player GetPlayer()
    {
        return this.player;
    }
}
