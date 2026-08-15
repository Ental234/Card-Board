using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewMinion", menuName = "Game/Minion Data")]
public class MinionData : ScriptableObject
{
    [Header("기본 정보")]
    public string minionName;
    public Sprite portrait;

    [Header("기초 스탯")]
    public int maxHp;
    public int baseAttack;

    [Header("슬롯")]
    [Range(1, 4)]
    public int preferredSlot = 2;  // 보스 진영에서 선호하는 슬롯 (SlotSystem이 배치)

    // 자리가 고정된 하수인 (고정포대·설치물 등).
    // 전투 행동은 정상이지만 밀려나지 않는다.
    public bool isImmobile;

    [Header("행동 패턴")]
    // 비워 두면 기존 동작(최전방 1타)으로 폴백한다
    public List<ActionPatternData> patterns = new();

    [Header("특수 능력")]
    public int reviveCount;                                  // 쓰러져도 되살아나는 횟수 (0 = 없음)
    [Range(0.1f, 1f)] public float reviveHpPercent = 0.5f;   // 부활 시 회복할 최대 HP 비율
    public bool startsWithTaunt;                             // 전투 시작부터 도발 상태
}
