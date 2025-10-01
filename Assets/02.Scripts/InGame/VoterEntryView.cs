using Photon.Realtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class VoterEntryView : MonoBehaviour
{
    public Image icon;
    public TMP_Text nick;

    private void Awake()
    {
        if (icon == null) icon = transform.Find("Icon")?.GetComponent<Image>();
        if (nick == null) nick = transform.Find("PlayerNickName")?.GetComponent<TMP_Text>();
    }

    public void Set(Player p, Sprite characterIcon, Color nameColor)
    {
        if (nick != null)
        {
            nick.text = p?.NickName ?? "Player";
            nick.color = nameColor;
        }
        if (icon != null && characterIcon != null)
        {
            icon.sprite = characterIcon;
            icon.enabled = true;
        }
    }
}
