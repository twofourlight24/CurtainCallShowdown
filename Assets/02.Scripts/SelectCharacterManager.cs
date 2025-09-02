using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using PhotonHashtable = ExitGames.Client.Photon.Hashtable; // 별칭 추가


public class SelectCharacterManager : MonoBehaviourPunCallbacks
{
    [Header("===== UI - Select Character Panel =====")]
    public GameObject selectCharacterPanel;
    public TMP_Text gameModeText;
    public TMP_Text mapText;
    public TMP_Text timerText;

    [Header("===== UI - Player Borders =====")]
    public GameObject[] playerBorders;
    public TMP_Text[] playerNicknameTexts;
    public TMP_Text[] characterNameTexts;
    public Image[] characterIcons;

    [Header("===== UI - Character Selection =====")]
    public GameObject[] characterButtons;
    public TMP_Text characterGuideText;
    public TMP_Text selectedCharacterNameText;
    public Button selectButton;

    public CharacterData[] allCharacters;

    private Dictionary<string, int> playerIndices = new Dictionary<string, int>();
    private float timer = 30f;
    private bool selectionComplete = false;

    void Start()
    {
        selectCharacterPanel.SetActive(true);
        InitializePlayerPanels();
        InitializeCharacterButtons();
        LoadGameSettings();
    }

    void Update()
    {
        if (!selectionComplete && timer > 0f)
        {
            timer -= Time.deltaTime;
            timerText.text = $"{Mathf.Ceil(timer)}";
            if (timer <= 0f)
            {
                OnTimerEnd();
            }
        }
    }

    private void InitializePlayerPanels()
    {
        for (int i = 0; i < playerBorders.Length; i++)
        {
            playerBorders[i].SetActive(false);
            characterNameTexts[i].gameObject.SetActive(false);
            characterIcons[i].gameObject.SetActive(false);
        }

        int index = 0;
        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (index >= playerBorders.Length) break;

            playerBorders[index].SetActive(true);
            playerNicknameTexts[index].text = player.NickName;
            playerIndices[player.NickName] = index;
            index++;
        }
    }

    private void InitializeCharacterButtons()
    {
        for (int i = 0; i < characterButtons.Length; i++)
        {
            int idx = i;
            characterButtons[idx].GetComponent<Button>().onClick.AddListener(() => OnCharacterSelected(idx));
        }
        selectButton.onClick.AddListener(OnSelectButtonPressed);
    }

    private void LoadGameSettings()
    {
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("GameMode", out object mode))
            gameModeText.text = $"게임 모드: {mode}";
        if (PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue("MapName", out object map))
            mapText.text = $"맵: {map}";
    }

    public void OnCharacterSelected(int characterIndex)
    {
        if (selectionComplete) return;

        string name = allCharacters[characterIndex].data.characterName;
        selectedCharacterNameText.text = $"선택 캐릭터: {name}";
        characterGuideText.text = allCharacters[characterIndex].data.characterDescription;

        PhotonHashtable props = new PhotonHashtable { { "SelectedCharacter", characterIndex } };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
    }

    public void OnSelectButtonPressed()
    {
        if (selectionComplete) return;

        PhotonHashtable props = new PhotonHashtable { { "IsReady", true } };
        PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        selectButton.interactable = false;
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, PhotonHashtable changedProps)
    {
        if (changedProps.ContainsKey("SelectedCharacter"))
        {
            int charIndex = (int)changedProps["SelectedCharacter"];
            if (playerIndices.TryGetValue(targetPlayer.NickName, out int pIndex))
            {
                characterNameTexts[pIndex].gameObject.SetActive(true);
                characterIcons[pIndex].gameObject.SetActive(true);
                characterNameTexts[pIndex].text = allCharacters[charIndex].data.characterName;
                characterIcons[pIndex].sprite = allCharacters[charIndex].data.characterIcon;
            }
        }

        CheckAllPlayersReady();
    }

    private void CheckAllPlayersReady()
    {
        if (selectionComplete || PhotonNetwork.PlayerList.Length < 2) return;

        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (!player.CustomProperties.TryGetValue("IsReady", out object ready) || !(bool)ready)
                return; // 준비 안 된 플레이어가 있으면 게임 시작 안 함
        }

        // 모두 준비 완료
        if (!selectionComplete) // 중복 방지
        {
            selectionComplete = true;
            selectCharacterPanel.SetActive(false);
            PhotonNetwork.CurrentRoom.IsOpen = false;

            if (PhotonNetwork.IsMasterClient)
                StartCoroutine(LoadGameScene(3f));
        }
    }

    private void OnTimerEnd()
    {
        if (selectionComplete) return;

        if (!PhotonNetwork.LocalPlayer.CustomProperties.ContainsKey("IsReady"))
        {
            int randomIndex = Random.Range(0, allCharacters.Length);
            PhotonHashtable props = new PhotonHashtable
            {
                { "SelectedCharacter", randomIndex },
                { "IsReady", true }
            };
            PhotonNetwork.LocalPlayer.SetCustomProperties(props);
        }
    }

    private IEnumerator LoadGameScene(float delay)
    {
        yield return new WaitForSeconds(delay);
        PhotonNetwork.LoadLevel("GameScene");
    }
}
