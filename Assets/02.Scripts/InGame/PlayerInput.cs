using UnityEngine;
using Photon.Pun; // Photon.Pun 네임스페이스 추가

// 플레이어 입력을 처리하고 행동을 결정하는 스크립트
public class PlayerInput : MonoBehaviourPun
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
        // 핵심: 로컬 플레이어만 입력을 처리하도록 보장
        if (photonView.IsMine)
        {
            // WASD 또는 방향키 입력
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            Vector3 moveDirection = new Vector3(h, 0f, v).normalized;

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
}
