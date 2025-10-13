using UnityEngine;

[CreateAssetMenu(fileName = "CharacterCommandSet", menuName = "Game/Character Command Set")]
public class CharacterCommandSet : ScriptableObject
{
    [System.Serializable]
    public class Command
    {
        public string commandName;
        public CommandType type;
        public float timeWindow = 0.2f; // 더블탭 등 시간 간격
        public string skillMethod; // CharacterBase 내 함수 이름
        public KeyCode key = KeyCode.J;
        public float cooldown = 2.0f;
    }

    public enum CommandType
    {
        SinglePress,
        DoublePress,
        Hold,
        WhileRunning
    }

    public Command[] commands;
}
