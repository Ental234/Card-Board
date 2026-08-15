using System;
using UnityEngine;

// 보스 유닛 — 카드 없음, 패턴 기반 전투
// HP 임계값에 따라 페이즈 전환 이벤트 발생
// OnKnockedOut → 스테이지 클리어 이벤트
public class BossEntity : CombatEntity
{
    public event Action<int>    OnPhaseChanged;   // (새 페이즈 번호 1-based)
    public event Action<BossEntity> OnStageClear; // CombatManager가 구독 → 스테이지 클리어 처리

    public int  CurrentPhase    { get; private set; } = 1;
    public bool IsPanicMode     { get; private set; }

    private float[] phaseThresholds;  // HP % 임계값 (내림차순 보장 필요)
    private int      turnCount;

    // 보드 이동 AI용 데이터 (BoardPhaseManager가 읽음)
    public int   BaseMoveDistance  { get; private set; }
    public float PanicHpThreshold  { get; private set; }
    public int   PanicTurnThreshold{ get; private set; }

    // ── 초기화 ──────────────────────────────────────────

    public void Initialize(BossData data)
    {
        IsPlayerSide = false;

        InitializeBase(
            maxHp:           data.maxHp,
            baseAttack:      data.baseAttack,
            maxActionPoints: data.maxActionPoints,
            entityName:      data.bossName,
            portrait:        data.portrait
        );

        SetSlot(data.bossSlot);

        phaseThresholds  = data.phaseThresholds;
        BaseMoveDistance  = data.baseMoveDistance;
        PanicHpThreshold  = data.panicHpThreshold;
        PanicTurnThreshold= data.panicTurnThreshold;

        SetPatterns(data.patterns);

        CurrentPhase = 1;
        IsPanicMode  = false;
        turnCount    = 0;
    }

    // ── 턴 흐름 ────────────────────────────────────────

    public override void OnTurnStart()
    {
        base.OnTurnStart();
        turnCount++;

        CheckPanic();
        CheckPhase();
    }

    // 보드 페이즈 턴 틱 — BoardPhaseManager가 매 보드 턴 호출.
    // 전투용 OnTurnStart와 달리 방어막·상태이상 처리는 하지 않고
    // 경과 턴만 세어 패닉 전환을 판정한다.
    public void OnBoardTurn()
    {
        turnCount++;
        CheckPanic();
    }

    // ── 패닉 & 페이즈 ───────────────────────────────────

    private void CheckPanic()
    {
        if (IsPanicMode) return;

        float hpRatio = (float)stats.CurrentHp / stats.MaxHp;
        if (hpRatio <= PanicHpThreshold || turnCount >= PanicTurnThreshold)
            IsPanicMode = true;
    }

    private void CheckPhase()
    {
        if (phaseThresholds == null || phaseThresholds.Length == 0) return;

        float hpRatio    = (float)stats.CurrentHp / stats.MaxHp;
        int   newPhase   = 1;

        // threshold 배열은 내림차순 (0.6, 0.3)
        // HP가 임계값 이하로 내려갈 때마다 페이즈 +1
        for (int i = 0; i < phaseThresholds.Length; i++)
        {
            if (hpRatio <= phaseThresholds[i])
                newPhase = i + 2;  // phase 1 기준, threshold 1개 통과 = phase 2
        }

        if (newPhase != CurrentPhase)
        {
            CurrentPhase = newPhase;
            OnPhaseChanged?.Invoke(CurrentPhase);
        }
    }

    // ── KnockOut ────────────────────────────────────────

    protected override void OnKnockedOut()
    {
        OnStageClear?.Invoke(this);
    }
}
