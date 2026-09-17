using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewBoss", menuName = "Game/Boss Data")]
public class BossData : ScriptableObject
{
    [Header("기본 정보")]
    public string bossName;
    public Sprite portrait;
    [TextArea]
    public string description;

    [Header("기초 스탯")]
    public int maxHp;
    public int baseAttack;

    [Header("슬롯 & 행동력")]
    [Range(1, 4)]
    public int bossSlot = 1;        // 전사형=1(전방), 법사형=4(후방)
    public int maxActionPoints = 0; // 보스는 행동력 미사용 (패턴 기반)

    [Header("보드 이동 AI")]
    public BossMoveStyle moveStyle = BossMoveStyle.Collector;  // 이동 성향 (GDD 4장)
    [Range(0f, 1f)]
    public float panicHpThreshold = 0.3f; // HP % 이하 → 패닉: 성향이 일시적으로 선점형으로 뒤집힌다
    // 턴 경과 패닉은 폐기 (v0.6) — 조우지점이 고정돼 장기화가 구조적으로 불가능하다

    [Header("페이즈 전환 HP 임계값 (%)")]
    public float[] phaseThresholds = { 0.6f, 0.3f }; // 예: [0.6, 0.3] → 페이즈 1/2/3

    [Header("행동 패턴")]
    // 비워 두면 기존 동작(최전방 1타)으로 폴백한다.
    // 페이즈·패닉 조건은 패턴 쪽의 minPhase / requirePanic이 담당한다.
    public List<ActionPatternData> patterns = new();

    [Header("소환 가능 하수인 풀")]
    public List<MinionData> minionPool = new(); // 보스가 몬스터 칸 착지 시 여기서 랜덤 선택
}
