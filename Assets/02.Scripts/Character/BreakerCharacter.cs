using UnityEngine;

public class BreakerCharacter : CharacterBase
{
    [Header("Breaker Character Properties")]
    public float AttackDamage = 15f;

    public override void Attack()
    {
        Debug.Log("조련사 캐릭터의 공격!");
        // 공격 로직 (예: 데미지 처리) 추가
    }

    // 스킬 행동 재정의 (오버라이드)
    public override void UseSkill()
    {
        Debug.Log("조련사 캐릭터의 스킬 사용!");
    }

    public override void Guard()
    {
        Debug.Log("조련사 캐릭터의 가드!");
    }
}
