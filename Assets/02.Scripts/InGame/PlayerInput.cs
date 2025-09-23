using UnityEngine;
using Photon.Pun; // Photon.Pun 네임스페이스 추가


[DisallowMultipleComponent]
public class PlayerInput : MonoBehaviourPun
{
    // 입력에 따라 호출할 델리게이트 (이벤트)
    public delegate void OnMove(Vector2 direction);
    public event OnMove onMove;

    public delegate void OnAction();
    public event OnAction onAttack;
    public event OnAction onGuard;
    public event OnAction onSkill;

    public CharacterBase controlledCharacter;

    void Update()
    {
        if (photonView.IsMine)
        {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            Vector3 moveDirection = new Vector3(h, v, 0f).normalized;

            if (controlledCharacter != null)
            {
                controlledCharacter.SetMoveDirection(moveDirection);
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

            // 스킬 (L 키)
            if (Input.GetKeyDown(KeyCode.L))
            {
                if (controlledCharacter != null)
                {
                    controlledCharacter.UseSkill();
                }
            }

            // 점프 (스페이스바)
            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (controlledCharacter != null)
                {
                    controlledCharacter.Jump();
                }
            }

            // 아래 방향키로 발판 통과
            if (Input.GetKeyDown(KeyCode.DownArrow)||Input.GetKeyDown(KeyCode.S))
            {
                if (controlledCharacter != null)
                {
                    controlledCharacter.DropThroughPlatform();
                }
            }
        }
    }
}
