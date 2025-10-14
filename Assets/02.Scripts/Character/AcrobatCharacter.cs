using UnityEngine;

public class AcrobatCharacter: CharacterBase
{
    [Header("Acrobat Character Properties")]
    public float AttackDamage = 15f;

    public override void Attack()
    {
        Debug.Log("곡예사 캐릭터의 공격!");
        // 공격 로직 (예: 데미지 처리) 추가
    }

    public override void Guard()
    {
        Debug.Log("곡예사 캐릭터의 가드!");
    }
}
