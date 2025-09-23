using UnityEngine;
using Photon.Pun;
using System.Collections;
public class DancerCharacter : CharacterBase
{
    [Header("Character Properties")]
    public float AttackDamage = 15f;

    [Header("Dancer Guard Properties")]
    public float shieldScaleDecrease = 0.3f; // 가드 시 쉴드 크기 감소량
    public float minShieldScale = 0.1f; // 쉴드가 파괴되는 최소 크기
    public float shieldRegenTime = 2.0f; // 쉴드 재생성 시간
    private Vector3 initialShieldScale;

    private bool isGuarding = false;

    new void Start()
    {
        // CharacterBase의 Start() 함수를 먼저 호출
        base.Start();

        // 쉴드 오브젝트의 초기 크기 저장
        if (ShieldObject != null)
        {
            initialShieldScale = ShieldObject.transform.localScale;
        }
    }

    void Update()
    {
        // 로컬 플레이어만 입력을 받습니다.
        if (photonView.IsMine)
        {
            // 가드 (K 키) - 키를 누르는 동안만 작동
            if (Input.GetKey(KeyCode.K))
            {
                if (!isGuarding)
                {
                    isGuarding = true;
                    // 가드 시작 로직
                    StartGuard();
                }
                UpdateGuard();
            }
            else if (isGuarding)
            {
                // 가드 종료 로직
                isGuarding = false;
                EndGuard();
            }
        }
    }

    private void StartGuard()
    {
        Debug.Log("마술사 캐릭터의 가드 시작!");
        if (ShieldObject != null)
        {
            // 가드 시작 시 쉴드 활성화
            ShieldObject.SetActive(true);
        }
    }

    private void UpdateGuard()
    {
        if (ShieldObject != null)
        {
            // 쉴드 크기 감소
            Vector3 newScale = ShieldObject.transform.localScale - new Vector3(shieldScaleDecrease, shieldScaleDecrease, 0) * Time.deltaTime;
            ShieldObject.transform.localScale = newScale;

            // 쉴드 파괴 조건
            if (newScale.x <= minShieldScale)
            {
                BreakShield();
            }
        }
    }

    private void EndGuard()
    {
        Debug.Log("마술사 캐릭터의 가드 종료!");
        if (ShieldObject != null)
        {
            ShieldObject.SetActive(false);
        }
    }

    private void BreakShield()
    {
        Debug.Log("쉴드 파괴! 2초간 이동 불가!");
        // 쉴드 파괴 시 비활성화 및 이동 불가 상태로 전환
        ShieldObject.SetActive(false);
        isGuarding = false; // 가드 상태를 즉시 종료
        StartCoroutine(ImmobilizeCharacter());

        // 2초 뒤 쉴드 재생성 코루틴 시작
        StartCoroutine(RegenerateShield());
    }

    private IEnumerator RegenerateShield()
    {
        yield return new WaitForSeconds(shieldRegenTime);
        Debug.Log("쉴드 재생성!");
        if (ShieldObject != null)
        {
            ShieldObject.transform.localScale = initialShieldScale;
        }
    }

    public override void Attack()
    {
        Debug.Log("댄서 캐릭터의 강한 공격!");
        // 공격 로직 (예: 데미지 처리) 추가
    }

    // 스킬 행동 재정의 (오버라이드)
    public override void UseSkill()
    {
        Debug.Log("댄서 캐릭터의 스킬 사용!");
    }

    public override void Guard()
    {
        Debug.Log("댄서 캐릭터의 가드!");
    }
}
