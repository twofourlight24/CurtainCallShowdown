using UnityEngine;

// 캐릭터 데이터를 담는 MonoBehaviour 스크립트
// 이 스크립트를 각 캐릭터 프리팹에 추가하여 사용합니다.
public class CharacterData : MonoBehaviour
{
    // 유니티 인스펙터에서 설정 가능하도록 구조체에 [System.Serializable] 속성 부여
    [System.Serializable]
    public struct PlayerCharacterData
    {
        public string characterName;
        public string characterDescription;
        public Sprite characterIcon;
    }

    public PlayerCharacterData data;
}
