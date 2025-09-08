using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Photon.Pun;
using Photon.Realtime;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using System.Collections;

// This script requires a PhotonView component on the same GameObject.
// It is essential for sending messages over the network using RPCs.
[RequireComponent(typeof(PhotonView))]
public class RoomManager : MonoBehaviourPunCallbacks
{
    [Header("===== UI - Room Info =====")]
    public TMP_Text roomNameText;
    public Button leaveButton; // Leave Room Button

    // UI elements to display game settings to other players
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
    // UI for displaying nickname duplication and other warnings
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

    // --- Private Variables ---
    private string[] gameModes = { "Showdown", "King of the Hill" };
    private string[] gameModeDescriptions =
    {
        "각 플레이어당 3개의 목숨이 주어지며, 최후의 생존자가 승리합니다.",
        "특정 영역에 오랫동안 머물러 점수가 가장 높은 플레이어가 승리합니다."
    };
    private string[] mapNames = { "BasicMap", "Desert Map", "Snow Map" }; // Example map names
    private int selectedModeIndex = 0;
    private int selectedRoundCount = 3;
    private int selectedMapIndex = 0;

    void Start()
    {
        // Set room name
        roomNameText.text = PhotonNetwork.CurrentRoom.Name;

        // Connect button events
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
        chatInput.onEndEdit.AddListener(OnChatInputEndEdit);

        // Initialize UI
        gameSetupPanel.SetActive(false);
        if (warningMessageText != null)
        {
            warningMessageText.gameObject.SetActive(false);
        }

        // Enable/disable UI based on Master Client status
        CheckMasterClientStatus();

        // Refresh the list of all players
        RefreshPlayerList();

        // If this client is the Master Client, initialize game settings on room creation.
        if (PhotonNetwork.IsMasterClient)
        {
            InitializeGameSettings();
        }

        UpdateGameSetupUI(PhotonNetwork.CurrentRoom.CustomProperties);
    }

    // Photon Callback Functions
    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        Debug.Log($"플레이어 입장: {newPlayer.NickName}");

        // Check for duplicate nicknames when a new player enters (Master Client only)
        if (PhotonNetwork.IsMasterClient)
        {
            CheckForDuplicateNicknames(newPlayer);
        }

        RefreshPlayerList();
    }

    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        Debug.Log($"플레이어 퇴장: {otherPlayer.NickName}");
        RefreshPlayerList();

        // If the Master Client leaves, a new one is designated
        if (otherPlayer.IsMasterClient)
        {
            Debug.Log("이전 방장이 나갔습니다. 새로운 방장이 설정됩니다.");
            CheckMasterClientStatus();
        }
    }

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
    {
        // Update UI when room settings change
        UpdateGameSetupUI(propertiesThatChanged);
    }

    public override void OnLeftRoom()
    {
        // Load the LobbyScene when leaving the room
        PhotonNetwork.LoadLevel("LobbyScene");
    }

    public override void OnPlayerPropertiesUpdate(Photon.Realtime.Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps)
    {
        // Update UI if the player's ready state changes
        if (changedProps.ContainsKey("IsReady"))
        {
            if (playerInfoObjects.ContainsKey(targetPlayer.ActorNumber))
            {
                playerInfoObjects[targetPlayer.ActorNumber].GetComponent<PlayerInfo>().SetReadyState((bool)changedProps["IsReady"]);
            }
        }

        // Check if all players are ready (Master Client only)
        if (PhotonNetwork.IsMasterClient)
        {
            CheckAllPlayersReady();
        }
    }

    // Check for duplicate nicknames
    private void CheckForDuplicateNicknames(Photon.Realtime.Player newPlayer)
    {
        // Check other players except the newly joined player
        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (player.ActorNumber != newPlayer.ActorNumber && player.NickName == newPlayer.NickName)
            {
                Debug.LogWarning($"닉네임 '{newPlayer.NickName}'이 이미 존재합니다. 새로 들어온 플레이어를 추방합니다.");

                // Use RPC to send a warning message to the kicked player
                if (photonView != null)
                {
                    photonView.RPC("ShowDuplicateNicknameWarning", newPlayer);
                }

                return; // Exit the function after calling the RPC
            }
        }
    }

    [PunRPC]
    public void ShowDuplicateNicknameWarning()
    {
        Debug.Log("닉네임 중복 경고 메시지를 받았습니다.");
        // The client who received the RPC leaves the room
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
        else
        {
            yield return new WaitForSeconds(2f);
        }

        PhotonNetwork.LeaveRoom(); // Client leaves the room directly
    }

    private void RefreshPlayerList()
    {
        // Delete existing player UI
        foreach (var infoObject in playerInfoObjects.Values)
        {
            Destroy(infoObject);
        }
        playerInfoObjects.Clear();

        // Create new player UI
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
            // Load existing room settings into the UI after becoming the new Master Client
            UpdateGameSetupUI(PhotonNetwork.CurrentRoom.CustomProperties);
            CheckAllPlayersReady(); // Re-check ready status when the Master Client changes
        }
        else
        {
            startButton.gameObject.SetActive(false);
            readyButton.gameObject.SetActive(true);
            gameSetupButton.gameObject.SetActive(false);
            // Non-master clients just view the settings set by the master client
            UpdateGameSetupUI(PhotonNetwork.CurrentRoom.CustomProperties);
        }
    }

    public void OnReadyButtonClicked()
    {
        if (PhotonNetwork.LocalPlayer != null)
        {
            ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable();
            bool isReady = PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey("IsReady") ? !(bool)PhotonNetwork.LocalPlayer.CustomProperties["IsReady"] : true;
            props.Add("IsReady", isReady);
            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        }
        else
        {
            Debug.LogError("로컬 플레이어 정보가 아직 로드되지 않았습니다.");
        }
    }

    public void OnStartButtonClicked()
    {
        if (CheckAllPlayersReady())
        {
            PhotonNetwork.LoadLevel("SelectCharacterScene");
        }
        else
        {
            Debug.Log("모든 플레이어가 준비되지 않았거나 플레이어 수가 부족합니다!");
            StartCoroutine(ShowWarningMessage("모든 플레이어(최소 2명)가 준비되어야 게임을 시작할 수 있습니다."));
        }
    }

    private bool CheckAllPlayersReady()
    {
        // Check for minimum number of players (2)
        if (PhotonNetwork.PlayerList.Length < 2)
        {
            startButton.interactable = false;
            return false;
        }

        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (!player.IsMasterClient)
            {
                object isReadyObj;
                if (!player.CustomProperties.TryGetValue("IsReady", out isReadyObj) || !(bool)isReadyObj)
                {
                    startButton.interactable = false;
                    return false;
                }
            }
        }
        startButton.interactable = true;
        return true;
    }

    public void OnLeaveButtonClicked()
    {
        PhotonNetwork.LeaveRoom();
    }

    public void OnGameSetupButtonClicked()
    {
        gameSetupPanel.SetActive(true);
    }

    private void InitializeGameSettings()
    {
        if (PhotonNetwork.CurrentRoom == null) return;

        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
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
        ExitGames.Client.Photon.Hashtable props = new ExitGames.Client.Photon.Hashtable
        {
            {"GameMode", gameModes[selectedModeIndex]},
            {"RoundCount", selectedRoundCount},
            {"AllowDuplication", characterDuplicationToggle.isOn},
            {"MapName", mapNames[selectedMapIndex]}
        };
        PhotonNetwork.CurrentRoom.SetCustomProperties(props);
        gameSetupPanel.SetActive(false);
    }

    public void OnGameSetupCancelButtonClicked()
    {
        gameSetupPanel.SetActive(false);
        UpdateGameSetupUI(PhotonNetwork.CurrentRoom.CustomProperties);
    }

    private void UpdateGameSetupUI(ExitGames.Client.Photon.Hashtable roomProps)
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
            if (PhotonNetwork.IsMasterClient)
            {
                characterDuplicationToggle.gameObject.SetActive(true);
            }
            else
            {
                characterDuplicationToggle.gameObject.SetActive(false);
            }
        }

        if (roomProps.ContainsKey("MapName"))
        {
            string map = (string)roomProps["MapName"];
            mapText.text = map;
            selectedMapIndex = System.Array.IndexOf(mapNames, map);
        }

        prevModeButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        nextModeButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        prevMapButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        nextMapButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        round3Button.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        round4Button.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        round5Button.gameObject.SetActive(PhotonNetwork.IsMasterClient);
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

    public void OnChatInputEndEdit(string text)
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            if (!string.IsNullOrEmpty(text))
            {
                photonView.RPC("ReceiveChatMessage", RpcTarget.All, PhotonNetwork.LocalPlayer.NickName, text);
                chatInput.text = "";
                chatInput.ActivateInputField();
            }
        }
    }

    [PunRPC]
    public void ReceiveChatMessage(string senderName, string message)
    {
        GameObject chatMsgObj = Instantiate(chatMessagePrefab, chatContent);
        TMP_Text chatText = chatMsgObj.GetComponent<TMP_Text>();
        chatText.text = $"<color=yellow>{senderName}</color>: {message}";

        LayoutRebuilder.ForceRebuildLayoutImmediate(chatContent.GetComponent<RectTransform>());
        StartCoroutine(ScrollToBottom());
    }

    private IEnumerator ScrollToBottom()
    {
        yield return new WaitForEndOfFrame();
        chatScrollRect.verticalNormalizedPosition = 0f;
    }

    // This coroutine is added again as 'ShowWarningMessage'
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
}
