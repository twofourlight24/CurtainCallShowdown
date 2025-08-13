using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;

public class ConnectionManager : MonoBehaviourPunCallbacks
{
    [Header("===== UI - ¹æ »ý¼º =====")]
    public TMP_InputField roomNameInput;
    public Toggle passwordToggle;
    public TMP_InputField passwordInput;
    public TextMeshProUGUI guideText; // Àß¸øµÈ ÀÔ·Â ½Ã Ç¥½ÃµÇ´Â ÅØ½ºÆ®
    public Button createRoomButton;

    [Header("===== UI - ¹æ ¸ñ·Ï =====")]
    public Transform roomListParent;
    public GameObject roomButtonPrefab;
    public Button refreshButton;  // ¹æ °»½Å ¹öÆ°
    public Button backButton;     // µÇµ¹¾Æ°¡±â ¹öÆ°

    [Header("===== UI - ÀÔÀå ÆÇ³Ú =====")]
    public GameObject joinPanel;
    public TextMeshProUGUI joinPanelText;
    public TMP_InputField joinPasswordInput;
    public Button joinYesButton;
    public Button joinNoButton;
    public TextMeshProUGUI warningPasswordText;

    private Dictionary<string, RoomInfo> cachedRoomList = new Dictionary<string, RoomInfo>();
    private string selectedRoomName = "";
    private string selectedRoomPassword = "";

    void Start()
    {
        PhotonNetwork.ConnectUsingSettings();
        PhotonNetwork.AutomaticallySyncScene = true;

        passwordToggle.onValueChanged.AddListener(OnPasswordToggleChanged);
        createRoomButton.onClick.AddListener(CreateRoom); // ¹öÆ° Å¬¸¯ ¿¬°á
        passwordInput.gameObject.SetActive(false);
        joinPanel.SetActive(false);
        warningPasswordText.gameObject.SetActive(false);

        // ¹öÆ° ÀÌº¥Æ® ¿¬°á
        refreshButton.onClick.AddListener(RefreshRoomList);
        backButton.onClick.AddListener(ReturnToTitle);
    }

    #region ===== ¼­¹ö ¿¬°á ¹× ·Îºñ =====
    public override void OnConnectedToMaster()
    {
        Debug.Log("Photon ¼­¹ö ¿¬°á ¼º°ø!");
        PhotonNetwork.JoinLobby();
    }

    public override void OnJoinedLobby()
    {
        Debug.Log("·Îºñ ÀÔÀå ¿Ï·á!");
        ClearRoomListUI();
    }
    #endregion

    #region ===== ¹æ »ý¼º =====
    public void CreateRoom()
    {
        string roomName = roomNameInput.text.Trim();
        string password = passwordInput.text.Trim();
        bool isPasswordOn = passwordToggle.isOn;

        if (!IsValidRoomName(roomName))
        {
            StartCoroutine(ShowGuideText("¹æ ÀÌ¸§Àº ÇÑ/¿µ/¼ýÀÚ 12ÀÚ ÀÌ³», Æ¯¼ö¹®ÀÚ ºÒ°¡"));
            return;
        }

        if (isPasswordOn && !IsValidPassword(password))
        {
            StartCoroutine(ShowGuideText("ºñ¹Ð¹øÈ£´Â ¼ýÀÚ¸¸ ÀÔ·Â °¡´ÉÇÕ´Ï´Ù"));
            return;
        }

        RoomOptions options = new RoomOptions();
        options.MaxPlayers = 4;
        options.CustomRoomProperties = new ExitGames.Client.Photon.Hashtable()
        {
            { "Mode", "Showdown" }, // ±âº» ¸ðµå °íÁ¤
            { "PW", isPasswordOn ? password : "" }
        };
        options.CustomRoomPropertiesForLobby = new string[] { "Mode", "PW" };

        PhotonNetwork.CreateRoom(roomName, options);
    }
    #endregion

    #region ===== ¹æ ¸ñ·Ï °»½Å =====
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

    public void RefreshRoomList()
    {
        ClearRoomListUI();
        PhotonNetwork.LeaveLobby();
        PhotonNetwork.JoinLobby();
        Debug.Log("¹æ ¸ñ·Ï °»½Å ¿äÃ»");
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

    #region ===== ¹æ ÀÔÀå =====
    void OnClickRoomButton(string roomName, string pw)
    {
        selectedRoomName = roomName;
        selectedRoomPassword = pw;

        joinPanel.SetActive(true);
        joinPasswordInput.gameObject.SetActive(!string.IsNullOrEmpty(pw));
        joinPasswordInput.text = "";
        joinPanelText.text = $"'{roomName}'¿¡ µé¾î°¡½Ã°Ú½À´Ï±î?";

        joinYesButton.onClick.RemoveAllListeners();
        joinYesButton.onClick.AddListener(TryJoinSelectedRoom);

        joinNoButton.onClick.RemoveAllListeners();
        joinNoButton.onClick.AddListener(() =>
        {
            joinPanel.SetActive(false);
        });
    }

    private void TryJoinSelectedRoom()
    {
        if (string.IsNullOrEmpty(selectedRoomPassword))
        {
            PhotonNetwork.JoinRoom(selectedRoomName);
        }
        else
        {
            if (joinPasswordInput.text == selectedRoomPassword)
            {
                PhotonNetwork.JoinRoom(selectedRoomName);
            }
            else
            {
                joinPanel.SetActive(false);
                StartCoroutine(ShowWarningPassword("ºñ¹Ð¹øÈ£°¡ Æ²·È½À´Ï´Ù"));
            }
        }
    }
    #endregion

    #region ===== À¯È¿¼º °Ë»ç & UI È¿°ú =====
    bool IsValidRoomName(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.Length > 12) return false;
        return Regex.IsMatch(name, @"^[°¡-ÆRa-zA-Z0-9]+$");
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

    #region ===== µÇµ¹¾Æ°¡±â =====
    public void ReturnToTitle()
    {
        PhotonNetwork.LeaveLobby();
        PhotonNetwork.Disconnect();
        SceneManager.LoadScene("TitleScene");
    }
    #endregion

    #region ===== ¹æ ÀÔÀå ¿Ï·á =====
    public override void OnJoinedRoom()
    {
        Debug.Log($"¹æ '{PhotonNetwork.CurrentRoom.Name}' ÀÔÀå ¿Ï·á!");
        PhotonNetwork.LoadLevel("RoomScene");
    }
    #endregion
}
