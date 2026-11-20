using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacter", menuName = "Game/Character Data")]
public class CharacterData : ScriptableObject
{
    [Header("기본 정보")]
    public string   characterName;
    public ClassTag classTag;
    public Sprite   portrait;
    [TextArea]
    public string   description;

    [Header("기초 스탯")]
    public int maxHp;
    public int baseAttack;  // 카드 피해에 더해지는 기본 공격력 보너스 (보통 0, 렐릭으로 증가)

    [Header("포지션 & 행동력")]
    [Range(1, 4)]
    public int initialSlot     = 1;  // 전투 시작 슬롯 (1=최전방, 4=최후방)
    public int maxActionPoints = 2;  // 전투 중 포지션 이동 등에 사용하는 행동력

    [Header("시작 덱")]
    public List<CardData> startingCombatCards = new();
    public List<CardData> startingBoardCards  = new();

    // 캐릭터 고유 렐릭 — 런 시작 때 무조건 받는다 (슬더스의 캐릭터별 시작 렐릭).
    // 상점·보상 풀에 넣지 말 것. 두 개를 갖게 된다.
    // 메타 해금으로 여는 "추가 시작 렐릭 선택"은 이것과 별개다 (3단계 7번)
    [Header("시작 렐릭")]
    public RelicData startingRelic;  // null = 없음
}
