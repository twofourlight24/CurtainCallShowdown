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
    public GameObject lockIcon; // ������̸� ����

    private Action onClickAction;

    /// <summary>
    /// �� ��ư ������ ����
    /// </summary>
    /// <param name="roomName">�� �̸�</param>
    /// <param name="mode">���� ���</param>
    /// <param name="currentPlayers">���� �ο�</param>
    /// <param name="maxPlayers">�ִ� �ο�</param>
    /// <param name="isLocked">��й�ȣ �� ����</param>
    /// <param name="onClick">��ư Ŭ�� �� ������ �׼�</param>
    public void Setup(string roomName, string mode, int currentPlayers, int maxPlayers, bool isLocked, Action onClick)
    {
        roomNameText.text = roomName;
        gameModeText.text = mode;
        playerCountText.text = $"{currentPlayers}/{maxPlayers}";

        lockIcon.SetActive(isLocked);

        onClickAction = onClick;

        // ���� ������ ���� �� �ٽ� ���
        GetComponent<Button>().onClick.RemoveAllListeners();
        GetComponent<Button>().onClick.AddListener(() => onClickAction?.Invoke());
    }
}
