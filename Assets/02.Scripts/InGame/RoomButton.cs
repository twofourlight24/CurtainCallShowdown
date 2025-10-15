using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class RoomButton : MonoBehaviour
{
    [Header("UI")]
    public TextMeshProUGUI roomNameText;
    public TextMeshProUGUI gameModeText;
    public TextMeshProUGUI playerCountText;
    public GameObject lockIcon; // 비번방이면 켜짐

    private Action onClickAction;

    /// <summary>
    /// 방 버튼 데이터 세팅
    /// </summary>
    /// <param name="roomName">방 이름</param>
    /// <param name="mode">게임 모드</param>
    /// <param name="currentPlayers">현재 인원</param>
    /// <param name="maxPlayers">최대 인원</param>
    /// <param name="isLocked">비밀번호 방 여부</param>
    /// <param name="onClick">버튼 클릭 시 실행할 액션</param>
    public void Setup(string roomName, string mode, int currentPlayers, int maxPlayers, bool isLocked, Action onClick)
    {
        roomNameText.text = roomName;
        gameModeText.text = mode;
        playerCountText.text = $"{currentPlayers}/{maxPlayers}";

        lockIcon.SetActive(isLocked);

        onClickAction = onClick;

        // 기존 리스너 제거 후 다시 등록
        GetComponent<Button>().onClick.RemoveAllListeners();
        GetComponent<Button>().onClick.AddListener(() => onClickAction?.Invoke());
    }
}
