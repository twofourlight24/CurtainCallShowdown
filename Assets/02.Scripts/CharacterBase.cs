using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;

// 모든 플레이어 캐릭터의 기본이 되는 추상 클래스
// 모든 공통적인 속성 (체력, 이동 속도, 목숨 등)을 정의합니다.
public abstract class CharacterBase : MonoBehaviourPun
{
    // 캐릭터의 공통적인 속성
    public float MaxHp = 100f;
    public float CurHp = 100f;
    public float MoveSpeed = 5f;
    public int LifeCount = 3;

    // 이동 함수 (모든 캐릭터에 공통적으로 적용)
    public void Move(Vector3 direction)
    {
        if (photonView.IsMine)
        {
            transform.Translate(direction * MoveSpeed * Time.deltaTime);
        }
    }

    // 공격 함수 (각 캐릭터마다 다르게 구현)
    public abstract void Attack();

    // 스킬 사용 함수 (각 캐릭터마다 다르게 구현)
    public abstract void UseSkill();
    public abstract void Guard();

    // 데미지를 받는 함수
    public void TakeDamage(float damage)
    {
        if (photonView.IsMine)
        {
            CurHp -= damage;
            if (CurHp <= 0)
            {
                // 목숨 차감 및 리스폰 로직 (GameManager에서 처리)
            }
        }
    }
}