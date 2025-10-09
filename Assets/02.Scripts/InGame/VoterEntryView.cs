using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Photon.Realtime;

public class VoterEntryView : MonoBehaviour
{
    public Image iconImage;
    public TMP_Text nicknameText;

    private string baseNick = "";   // X2 붙이기 전 원본 닉 보관

    public void Set(Player p, Sprite icon, Color nameColor)
    {
        if (iconImage) iconImage.sprite = icon;

        baseNick = (p != null ? p.NickName : "Unknown");
        if (nicknameText)
        {
            nicknameText.text = baseNick;
            nicknameText.color = nameColor;
        }
    }

    public void SetX2Suffix(bool on)
    {
        if (!nicknameText) return;

        // 혹시 이전에 X2가 붙어있다면 제거 후 다시 구성
        string current = nicknameText.text;
        if (current.EndsWith(" X2"))
            current = current.Substring(0, current.Length - 3);

        // baseNick이 비어있다면 현재(접미사 제거된) 텍스트를 기준으로
        if (string.IsNullOrEmpty(baseNick)) baseNick = current;

        nicknameText.text = on ? (baseNick + " X2") : baseNick;
    }
}
