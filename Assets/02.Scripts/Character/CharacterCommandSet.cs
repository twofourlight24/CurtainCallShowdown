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
        public float holdThreshold = 0f;
        public string skillMethod; // CharacterBase 내 함수 이름
        public KeyCode key = KeyCode.J;
        public float cooldown = 2.0f;
    }

    public enum CommandType
    {
        SinglePress,
        DoublePress,
        Hold,
        WhileRunning,
        WhileRunningHold
    }
    public enum ActionKind
    {
        None,
        BasicAttack,     // 기본 공격(탭)
        HoldTier1,       // 홀드 1단계
        HoldTier2,       // 홀드 2단계
        HoldTier3,       // 홀드 3단계
        DashWhileRunning // 달리기+홀드 동안 대시(키업에서 해제)
    }

    public Command[] commands;
}
