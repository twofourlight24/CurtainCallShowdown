using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using System.Collections;
using Hashtable = ExitGames.Client.Photon.Hashtable;

// This script requires a PhotonView component on the same GameObject.
[RequireComponent(typeof(PhotonView))]
public class RoomManager : MonoBehaviourPunCallbacks, IOnEventCallback
{
    [Header("===== UI - Room Info =====")]
    public TMP_Text roomNameText;
    public Button leaveButton; // Leave Room Button

    public TMP_Text gameModeInfoText;
    public TMP_Text gameRoundInfoText;
    public TMP_Text characterDuplicationInfoText;

    [Header("===== UI - Player List =====")]
    public Transform playerPanel;
    public GameObject playerInfoPrefab;
    private Dictionary<int, GameObject> playerInfoObjects = new Dictionary<int, GameObject>();

    [Header("===== UI - Control Buttons =====")]
    public Button startButton;
    public Button readyButton;
    public Button gameSetupButton;
    public TextMeshProUGUI warningMessageText;

    [Header("===== UI - Game Setup Panel =====")]
    public GameObject gameSetupPanel;
    public Button gameSetupConfirmButton;
    public Button gameSetupCancelButton;

    [Header("===== UI - Game Setup Options =====")]
    public TMP_Text gameModeText;
    public TMP_Text gameModeGuideText;
    public Button prevModeButton;
    public Button nextModeButton;
    public TMP_Text gameRoundText;
    public Button round3Button;
    public Button round4Button;
    public Button round5Button;
    public TMP_Text characterDuplicationText;
    public Toggle characterDuplicationToggle;
    public TMP_Text mapText;
    public Image mapImage;
    public Button prevMapButton;
    public Button nextMapButton;

    [Header("===== UI - Chat =====")]
    public TMP_InputField chatInput;
    public ScrollRect chatScrollRect;
    public Transform chatContent;
    public GameObject chatMessagePrefab;
    public Button chatSendButton;

    // --- Chat constants ---
    const byte ChatEvent = 101;
    const int MaxCachedMessages = 30;
    int cachedCount = 0;

    // --- Private Variables ---
    private string[] gameModes = { "Showdown", "King of the Hill" };
    private string[] gameModeDescriptions =
    {
        "각 플레이어당 3개의 목숨이 주어지며, 최후의 생존자가 승리합니다.",
        "특정 영역에 오랫동안 머물러 점수가 가장 높은 플레이어가 승리합니다."
    };
    private string[] mapNames = { "BasicMap", "Desert Map", "Snow Map" };
    private int selectedModeIndex = 0;
    private int selectedRoundCount = 3;
    private int selectedMapIndex = 0;

    void Start()
    {
        roomNameText.text = PhotonNetwork.CurrentRoom.Name;

        startButton.onClick.AddListener(OnStartButtonClicked);
        readyButton.onClick.AddListener(OnReadyButtonClicked);
        gameSetupButton.onClick.AddListener(OnGameSetupButtonClicked);
        gameSetupConfirmButton.onClick.AddListener(OnGameSetupConfirmButtonClicked);
        gameSetupCancelButton.onClick.AddListener(OnGameSetupCancelButtonClicked);
        leaveButton.onClick.AddListener(OnLeaveButtonClicked);
        prevModeButton.onClick.AddListener(() => OnGameModeChanged(-1));
        nextModeButton.onClick.AddListener(() => OnGameModeChanged(1));
        round3Button.onClick.AddListener(() => OnRoundCountChanged(3));
        round4Button.onClick.AddListener(() => OnRoundCountChanged(4));
        round5Button.onClick.AddListener(() => OnRoundCountChanged(5));
        prevMapButton.onClick.AddListener(() => OnMapChanged(-1));
        nextMapButton.onClick.AddListener(() => OnMapChanged(1));
        if (chatSendButton) chatSendButton.onClick.AddListener(OnClickSendChat); // 버튼 바인딩
        chatInput.lineType = TMP_InputField.LineType.SingleLine; // 엔터 전송 모드
        chatInput.onEndEdit.AddListener(OnChatInputEndEdit);

        chatInput.onEndEdit.RemoveAllListeners();
        chatInput.onSubmit.RemoveAllListeners();
        chatInput.onSubmit.AddListener(_ => OnClickSendChat());
        gameSetupPanel.SetActive(false);
        if (warningMessageText != null) warningMessageText.gameObject.SetActive(false);

        // 전송 버튼 → 전송
        if (chatSendButton) chatSendButton.onClick.AddListener(OnClickSendChat);
        SetNavigationNone(startButton, readyButton, gameSetupButton, leaveButton, chatSendButton);
        SetNavigationNone(chatInput);

        chatInput.onSelect.AddListener(_ => { if (EventSystem.current) EventSystem.current.sendNavigationEvents = false; });
        chatInput.onDeselect.AddListener(_ => { if (EventSystem.current) EventSystem.current.sendNavigationEvents = true; });


        CheckMasterClientStatus();
        RefreshPlayerList();

        if (PhotonNetwork.IsMasterClient) InitializeGameSettings();
        UpdateGameSetupUI(PhotonNetwork.CurrentRoom.CustomProperties);
    }

    // ===== 채팅 구현 (RaiseEvent + RoomCache) =====
    public void OnEvent(EventData photonEvent)
    {
        if (photonEvent.Code != ChatEvent) return;

        // 캐시 초기화 신호
        if (photonEvent.CustomData == null)
        {
            ClearChatUI();
            cachedCount = 0;
            return;
        }

        var data = (object[])photonEvent.CustomData;
        string sender = (string)data[0];
        string msg = (string)data[1];
        long ticks = (long)data[2];

        var local = new System.DateTime(ticks, System.DateTimeKind.Utc).ToLocalTime();
        AddChatLine($"[{local:HH:mm}] {sender}: {msg}");
        StartCoroutine(ScrollToBottom());
    }
    public void OnClickSendChat()
    {
        if (!chatInput) return;
        var text = chatInput.text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        SubmitChat(text);                   // 기존 전송 로직 호출
        chatInput.text = "";
        if (EventSystem.current) EventSystem.current.SetSelectedGameObject(chatInput.gameObject);
        chatInput.ActivateInputField();
    }
    public void SubmitChat(string text)
    {
        var msg = text?.Trim();
        if (string.IsNullOrEmpty(msg)) return;
        if (msg.Length > 200) msg = msg.Substring(0, 200);

        var payload = new object[] { PhotonNetwork.NickName, msg, System.DateTime.UtcNow.Ticks };
        var opt = new RaiseEventOptions { Receivers = ReceiverGroup.All, CachingOption = EventCaching.AddToRoomCache };
        PhotonNetwork.RaiseEvent(ChatEvent, payload, opt, SendOptions.SendReliable);

        cachedCount++;
        if (cachedCount > MaxCachedMessages)
        {
            // 모든 클라이언트 캐시 제거 후 카운트 리셋
            ClearChatCacheNetwork();
            cachedCount = 1; // 방금 보낸 한 건부터 다시 카운트
        }
    }

    void ClearChatCacheNetwork()
    {
        var opt = new RaiseEventOptions { Receivers = ReceiverGroup.All, CachingOption = EventCaching.RemoveFromRoomCache };
        PhotonNetwork.RaiseEvent(ChatEvent, null, opt, SendOptions.SendReliable);
    }

    void AddChatLine(string text)
    {
        var go = Instantiate(chatMessagePrefab, chatContent);
        var tmp = go.GetComponent<TMP_Text>();
        tmp.enableAutoSizing = false;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Ellipsis;
        tmp.text = text.Replace('\n', ' '); // 줄바꿈 차단

        var rt = (RectTransform)go.transform;
        float w = ((RectTransform)go.transform.parent).rect.width;
        float h = tmp.GetPreferredValues(tmp.text, w, Mathf.Infinity).y;
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, h);

        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)chatContent);
        StartCoroutine(ScrollToBottom());
    }

    void ClearChatUI()
    {
        if (!chatContent) return;
        for (int i = chatContent.childCount - 1; i >= 0; i--)
            Destroy(chatContent.GetChild(i).gameObject);
    }

    IEnumerator ScrollToBottom()
    {
        yield return new WaitForEndOfFrame();
        if (chatScrollRect) chatScrollRect.verticalNormalizedPosition = 0f;
    }

    public void OnChatInputEndEdit(string text)
    {
        // 엔터키에서만 전송
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            OnClickSendChat();
    }

    // ===== Photon 콜백 =====
    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Debug.Log($"플레이어 입장: {newPlayer.NickName}");
        if (PhotonNetwork.IsMasterClient) CheckForDuplicateNicknames(newPlayer);
        RefreshPlayerList();

        // 시스템 메시지
        AddChatLine($"[시스템] {newPlayer.NickName} 입장");
        StartCoroutine(ScrollToBottom());
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        Debug.Log($"플레이어 퇴장: {otherPlayer.NickName}");
        RefreshPlayerList();

        if (otherPlayer.IsMasterClient)
        {
            Debug.Log("이전 방장이 나갔습니다. 새로운 방장이 설정됩니다.");
            CheckMasterClientStatus();
        }

        // 시스템 메시지
        AddChatLine($"[시스템] {otherPlayer.NickName} 퇴장");
        StartCoroutine(ScrollToBottom());
    }

    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        UpdateGameSetupUI(propertiesThatChanged);
    }

    public override void OnLeftRoom()
    {
        PhotonNetwork.LoadLevel("LobbyScene");
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (changedProps.ContainsKey("IsReady") && playerInfoObjects.ContainsKey(targetPlayer.ActorNumber))
            playerInfoObjects[targetPlayer.ActorNumber].GetComponent<PlayerInfo>().SetReadyState((bool)changedProps["IsReady"]);

        if (PhotonNetwork.IsMasterClient) CheckAllPlayersReady();
    }

    public override void OnMasterClientSwitched(Player newMaster)
    {
        Debug.Log($"마스터 교체: {newMaster.NickName}");
        CheckMasterClientStatus();
        EnsureGameSettingsExist();
        AddChatLine($"[시스템] 방장이 {newMaster.NickName} 님으로 변경");
        StartCoroutine(ScrollToBottom());
    }

    // ===== 닉네임 중복 검사 =====
    private void CheckForDuplicateNicknames(Player newPlayer)
    {
        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (player.ActorNumber != newPlayer.ActorNumber && player.NickName == newPlayer.NickName)
            {
                Debug.LogWarning($"닉네임 '{newPlayer.NickName}' 중복. 추방 안내.");
                if (photonView != null) photonView.RPC("ShowDuplicateNicknameWarning", newPlayer);
                return;
            }
        }
    }

    [PunRPC]
    public void ShowDuplicateNicknameWarning()
    {
        Debug.Log("닉네임 중복 경고 메시지 수신");
        StartCoroutine(ShowWarningAndLeaveRoom("이미 존재하는 닉네임입니다. 다른 닉네임으로 다시 시도해주세요."));
    }

    private IEnumerator ShowWarningAndLeaveRoom(string message)
    {
        if (warningMessageText != null)
        {
            warningMessageText.text = message;
            warningMessageText.gameObject.SetActive(true);
            warningMessageText.CrossFadeAlpha(1, 0, false);
            yield return new WaitForSeconds(2f);
            warningMessageText.CrossFadeAlpha(0, 1f, false);
        }
        else yield return new WaitForSeconds(2f);

        PhotonNetwork.LeaveRoom();
    }

    private void RefreshPlayerList()
    {
        foreach (var infoObject in playerInfoObjects.Values) Destroy(infoObject);
        playerInfoObjects.Clear();

        foreach (var player in PhotonNetwork.PlayerList)
        {
            GameObject playerObj = Instantiate(playerInfoPrefab, playerPanel);
            PlayerInfo info = playerObj.GetComponent<PlayerInfo>();
            info.Setup(player);
            playerInfoObjects[player.ActorNumber] = playerObj;
        }

        CheckAllPlayersReady();
    }

    private void CheckMasterClientStatus()
    {
        if (PhotonNetwork.IsMasterClient)
        {
            startButton.gameObject.SetActive(true);
            readyButton.gameObject.SetActive(false);
            gameSetupButton.gameObject.SetActive(true);
            UpdateGameSetupUI(PhotonNetwork.CurrentRoom.CustomProperties);
            CheckAllPlayersReady();
        }
        else
        {
            startButton.gameObject.SetActive(false);
            readyButton.gameObject.SetActive(true);
            gameSetupButton.gameObject.SetActive(false);
            UpdateGameSetupUI(PhotonNetwork.CurrentRoom.CustomProperties);
        }
    }

    public void OnReadyButtonClicked()
    {
        if (PhotonNetwork.LocalPlayer == null)
        {
            Debug.LogError("로컬 플레이어 정보 미로딩");
            return;
        }

        var props = new Hashtable();
        bool isReady = PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey("IsReady")
            ? !(bool)PhotonNetwork.LocalPlayer.CustomProperties["IsReady"]
            : true;
        props.Add("IsReady", isReady);
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    public void OnStartButtonClicked()
    {
        if (!CheckAllPlayersReady())
        {
            Debug.Log("모두 준비 아님");
            StartCoroutine(ShowWarningMessage("모든 플레이어(최소 2명)가 준비되어야 합니다."));
            return;
        }

        if (PhotonNetwork.IsMasterClient)
        {
            PhotonNetwork.CurrentRoom.IsOpen = false;
            PhotonNetwork.CurrentRoom.IsVisible = false;
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { "RoomState", "Playing" } });

            // 룸 시작 전 채팅 캐시 비우기
            ClearChatCacheNetwork();
        }

        PhotonNetwork.LoadLevel("SelectCharacterScene");
    }

    private bool CheckAllPlayersReady()
    {
        if (PhotonNetwork.PlayerList.Length < 2)
        {
            startButton.interactable = false;
            return false;
        }

        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (player.IsMasterClient) continue;
            if (!player.CustomProperties.TryGetValue("IsReady", out object isReadyObj) || !(bool)isReadyObj)
            {
                startButton.interactable = false;
                return false;
            }
        }
        startButton.interactable = true;
        return true;
    }

    public void OnLeaveButtonClicked() { PhotonNetwork.LeaveRoom(); }
    public void OnGameSetupButtonClicked() { gameSetupPanel.SetActive(true); }

    private void InitializeGameSettings()
    {
        if (PhotonNetwork.CurrentRoom == null) return;

        var props = new Hashtable
        {
            {"GameMode", gameModes[selectedModeIndex]},
            {"RoundCount", selectedRoundCount},
            {"AllowDuplication", characterDuplicationToggle.isOn},
            {"MapName", mapNames[selectedMapIndex]}
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
    }

    public void OnGameSetupConfirmButtonClicked()
    {
        var props = new Hashtable
        {
            {"GameMode", gameModes[selectedModeIndex]},
            {"RoundCount", selectedRoundCount},
            {"AllowDuplication", characterDuplicationToggle.isOn},
            {"MapName", mapNames[selectedMapIndex]}
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        gameSetupPanel.SetActive(false);
    }

    private void EnsureGameSettingsExist()
    {
        var room = PhotonNetwork.CurrentRoom;
        if (room == null) return;
        var props = room.CustomProperties;

        var ht = new Hashtable();
        bool dirty = false;

        if (!props.ContainsKey("GameMode")) { ht["GameMode"] = gameModes[selectedModeIndex]; dirty = true; }
        if (!props.ContainsKey("RoundCount")) { ht["RoundCount"] = selectedRoundCount; dirty = true; }
        if (!props.ContainsKey("AllowDuplication")) { ht["AllowDuplication"] = characterDuplicationToggle.isOn; dirty = true; }
        if (!props.ContainsKey("MapName")) { ht["MapName"] = mapNames[selectedMapIndex]; dirty = true; }

        if (dirty) room.SetCustomProperties(ht);
    }

    public void OnGameSetupCancelButtonClicked()
    {
        gameSetupPanel.SetActive(false);
        UpdateGameSetupUI(PhotonNetwork.CurrentRoom.CustomProperties);
    }

    private void UpdateGameSetupUI(Hashtable roomProps)
    {
        if (roomProps.ContainsKey("GameMode"))
        {
            string mode = (string)roomProps["GameMode"];
            gameModeText.text = mode;
            gameModeInfoText.text = mode;
            selectedModeIndex = System.Array.IndexOf(gameModes, mode);
            gameModeGuideText.text = gameModeDescriptions[selectedModeIndex];
        }

        if (roomProps.ContainsKey("RoundCount"))
        {
            selectedRoundCount = (int)roomProps["RoundCount"];
            gameRoundText.text = $"{selectedRoundCount} 라운드";
            gameRoundInfoText.text = $"{selectedRoundCount} 라운드";
        }

        if (roomProps.ContainsKey("AllowDuplication"))
        {
            bool allow = (bool)roomProps["AllowDuplication"];
            characterDuplicationText.text = $"캐릭터 중복 선택 {(allow ? "허용" : "제한")}";
            characterDuplicationInfoText.text = $"단원 중복 {(allow ? "허용" : "제한")}";
            characterDuplicationToggle.isOn = allow;
            characterDuplicationToggle.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        }

        if (roomProps.ContainsKey("MapName"))
        {
            string map = (string)roomProps["MapName"];
            mapText.text = map;
            selectedMapIndex = System.Array.IndexOf(mapNames, map);
        }

        bool isMaster = PhotonNetwork.IsMasterClient;
        prevModeButton.gameObject.SetActive(isMaster);
        nextModeButton.gameObject.SetActive(isMaster);
        prevMapButton.gameObject.SetActive(isMaster);
        nextMapButton.gameObject.SetActive(isMaster);
        round3Button.gameObject.SetActive(isMaster);
        round4Button.gameObject.SetActive(isMaster);
        round5Button.gameObject.SetActive(isMaster);
    }
    public void OnGameModeChanged(int change)
    {
        selectedModeIndex = (selectedModeIndex + change + gameModes.Length) % gameModes.Length;
        gameModeText.text = gameModes[selectedModeIndex];
        gameModeGuideText.text = gameModeDescriptions[selectedModeIndex];
    }

    public void OnRoundCountChanged(int round)
    {
        selectedRoundCount = round;
        gameRoundText.text = $"{selectedRoundCount} 라운드";
    }

    public void OnMapChanged(int change)
    {
        selectedMapIndex = (selectedMapIndex + change + mapNames.Length) % mapNames.Length;
        mapText.text = mapNames[selectedMapIndex];
    }
    private IEnumerator ShowWarningMessage(string message)
    {
        if (warningMessageText != null)
        {
            warningMessageText.text = message;
            warningMessageText.gameObject.SetActive(true);
            warningMessageText.CrossFadeAlpha(1, 0, false);
            yield return new WaitForSeconds(2f);
            warningMessageText.CrossFadeAlpha(0, 1f, false);
        }
    }
    void SetNavigationNone(params Selectable[] xs) { foreach (var x in xs) { if (!x) continue; var n = x.navigation; n.mode = Navigation.Mode.None; x.navigation = n; } }
    void SetNavigationNone(TMP_InputField ip) { var s = ip?.GetComponent<Selectable>(); if (!s) return; var n = s.navigation; n.mode = Navigation.Mode.None; s.navigation = n; }
}
