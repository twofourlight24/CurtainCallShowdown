using UnityEngine;

// 플레이어 입력을 처리하고 행동을 결정하는 스크립트
public class PlayerInput : MonoBehaviour
{
    // 입력에 따라 호출할 델리게이트 (이벤트)
    public delegate void OnMove(Vector2 direction);
    public event OnMove onMove;

    public delegate void OnAction();
    public event OnAction onAttack;
    public event OnAction onGuard;
    public event OnAction onSkill;

    // 현재 제어할 캐릭터
    public CharacterBase controlledCharacter;

    void Update()
    {
        // WASD 또는 방향키 입력
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        Vector2 moveDirection = new Vector2(h, v).normalized;
        if (moveDirection != Vector2.zero)
        {
            if (controlledCharacter != null)
            {
                controlledCharacter.Move(moveDirection);
            }
        }

        // 공격 (J 키)
        if (Input.GetKeyDown(KeyCode.J))
        {
            if (controlledCharacter != null)
            {
                controlledCharacter.Attack();
            }
        }

        // 가드 (K 키)
        if (Input.GetKeyDown(KeyCode.K))
        {
            if (controlledCharacter != null)
            {
                controlledCharacter.Guard();
            }
        }

        // 스킬 (L 키, 예시)
        if (Input.GetKeyDown(KeyCode.L))
        {
            if (controlledCharacter != null)
            {
                controlledCharacter.UseSkill();
            }
        }
    }
}
