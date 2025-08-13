using UnityEngine;
using UnityEngine.UI;
using Photon.Pun;
using Photon.Realtime;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class ConnectionManager : MonoBehaviourPunCallbacks
{
    [Header("===== UI - �� ���� =====")]
    public InputField roomNameInput;
    public Toggle passwordToggle;
    public InputField passwordInput;
    public Text guideText; // �߸��� �Է� �� ǥ�õǴ� �ؽ�Ʈ
    public Dropdown gameModeDropdown; // ������ ������ ���� ���

    [Header("===== UI - �� ��� =====")]
    public Transform roomListParent;
    public GameObject roomButtonPrefab;

    [Header("===== UI - ���� �ǳ� =====")]
    public GameObject joinPanel;
    public Text joinPanelText;
    public InputField joinPasswordInput;
    public Button joinYesButton;
    public Button joinNoButton;
    public Text warningPasswordText;

    private Dictionary<string, RoomInfo> cachedRoomList = new Dictionary<string, RoomInfo>();
    private string selectedRoomName = "";

    void Start()
    {
        PhotonNetwork.ConnectUsingSettings();
        PhotonNetwork.AutomaticallySyncScene = true;

        passwordToggle.onValueChanged.AddListener(OnPasswordToggleChanged);
        passwordInput.gameObject.SetActive(false);
        joinPanel.SetActive(false);
        warningPasswordText.gameObject.SetActive(false);
    }

    #region ===== ���� ���� �� �κ� =====
    public override void OnConnectedToMaster()
    {
        Debug.Log("Photon ���� ���� ����!");
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        Debug.Log("�κ� ���� �Ϸ�!");
        ClearRoomListUI();
    }
    #endregion

    #region ===== �� ���� =====
    public void CreateRoom()
    {
        string roomName = roomNameInput.text.Trim();
        string password = passwordInput.text.Trim();
        bool isPasswordOn = passwordToggle.isOn;

        // �� �̸� ��ȿ�� �˻�
        if (!IsValidRoomName(roomName))
        {
            StartCoroutine(ShowGuideText("�� �̸��� ��/��/���� 12�� �̳�, Ư������ �Ұ�"));
            return;
        }

        // ��й�ȣ �˻�
        if (isPasswordOn && !IsValidPassword(password))
        {
            StartCoroutine(ShowGuideText("��й�ȣ�� ���ڸ� �Է� �����մϴ�"));
            return;
        }

        RoomOptions options = new RoomOptions();
        options.MaxPlayers = 4;
        options.CustomRoomProperties = new ExitGames.Client.Photon.Hashtable()
        {
            { "Mode", gameModeDropdown.options[gameModeDropdown.value].text },
            { "PW", isPasswordOn ? password : "" }
        };
        options.CustomRoomPropertiesForLobby = new string[] { "Mode", "PW" };

        PhotonNetwork.CreateRoom(roomName, options);
    }
    #endregion

    #region ===== �� ��� ���� =====
    public override void OnRoomListUpdate(List<RoomInfo> roomList)
    {
        foreach (RoomInfo info in roomList)
        {
            if (info.RemovedFromList)
            {
                if (cachedRoomList.ContainsKey(info.Name))
                    cachedRoomList.Remove(info.Name);
            }
            else
            {
                cachedRoomList[info.Name] = info;
            }
        }
        UpdateRoomListUI();
    }

    private void UpdateRoomListUI()
    {
        ClearRoomListUI();
        foreach (RoomInfo info in cachedRoomList.Values)
        {
            GameObject btn = Instantiate(roomButtonPrefab, roomListParent);
            RoomButton rb = btn.GetComponent<RoomButton>();
            string mode = info.CustomProperties.ContainsKey("Mode") ? (string)info.CustomProperties["Mode"] : "";
            string pw = info.CustomProperties.ContainsKey("PW") ? (string)info.CustomProperties["PW"] : "";

            rb.Setup(info.Name, mode, info.PlayerCount, info.MaxPlayers, !string.IsNullOrEmpty(pw), () =>
            {
                OnClickRoomButton(info.Name, pw);
            });
        }
    }

    private void ClearRoomListUI()
    {
        foreach (Transform child in roomListParent)
        {
            Destroy(child.gameObject);
        }
    }
    #endregion

    #region ===== �� ���� =====
    void OnClickRoomButton(string roomName, string pw)
    {
        selectedRoomName = roomName;
        joinPanel.SetActive(true);
        joinPasswordInput.gameObject.SetActive(!string.IsNullOrEmpty(pw));
        joinPanelText.text = $"'{roomName}'�� ���ðڽ��ϱ�?";

        joinYesButton.onClick.RemoveAllListeners();
        joinYesButton.onClick.AddListener(() =>
        {
            if (string.IsNullOrEmpty(pw))
            {
                PhotonNetwork.JoinRoom(selectedRoomName);
            }
            else
            {
                if (joinPasswordInput.text == pw)
                {
                    PhotonNetwork.JoinRoom(selectedRoomName);
                }
                else
                {
                    joinPanel.SetActive(false);
                    StartCoroutine(ShowWarningPassword("��й�ȣ�� Ʋ�Ƚ��ϴ�"));
                }
            }
        });

        joinNoButton.onClick.RemoveAllListeners();
        joinNoButton.onClick.AddListener(() =>
        {
            joinPanel.SetActive(false);
        });
    }
    #endregion

    #region ===== ��ȿ�� �˻� & UI ȿ�� =====
    bool IsValidRoomName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.Length > 12) return false;
        return Regex.IsMatch(name, @"^[��-�Ra-zA-Z0-9]+$");
    }

    bool IsValidPassword(string pw)
    {
        return Regex.IsMatch(pw, @"^[0-9]+$");
    }

    IEnumerator ShowGuideText(string message)
    {
        guideText.text = message;
        guideText.gameObject.SetActive(true);
        yield return new WaitForSeconds(2f);
        guideText.CrossFadeAlpha(0, 1f, false);
        yield return new WaitForSeconds(1f);
        guideText.CrossFadeAlpha(1, 0f, false);
        guideText.gameObject.SetActive(false);
    }

    IEnumerator ShowWarningPassword(string message)
    {
        warningPasswordText.text = message;
        warningPasswordText.gameObject.SetActive(true);
        yield return new WaitForSeconds(2f);
        warningPasswordText.CrossFadeAlpha(0, 1f, false);
        yield return new WaitForSeconds(1f);
        warningPasswordText.CrossFadeAlpha(1, 0f, false);
        warningPasswordText.gameObject.SetActive(false);
    }

    void OnPasswordToggleChanged(bool isOn)
    {
        passwordInput.gameObject.SetActive(isOn);
    }
    #endregion

    #region ===== �� ���� �Ϸ� =====
    public override void OnJoinedRoom()
    {
        Debug.Log($"�� '{PhotonNetwork.CurrentRoom.Name}' ���� �Ϸ�!");
        // �� �̵�: PhotonNetwork.LoadLevel("GameScene");
    }
    #endregion
}
