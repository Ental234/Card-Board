using System;
using UnityEngine;

// CombatEntity 상속 — 플레이어 캐릭터 전용
// DeckManager(전투 덱) + BoardCardManager(보드 덱) 연결
// OnKnockedOut → 런 종료 이벤트 발생
public class PlayerCharacter : CombatEntity
{
    // ── 이벤트 ──────────────────────────────────────────

    public event Action OnRunEnd;  // HP = 0 → GameManager가 구독해 런 종료 처리

    public ClassTag ClassTag { get; private set; }

    // ── 덱 매니저 참조 ──────────────────────────────────

    [SerializeField] private DeckManager      deckManager;
    [SerializeField] private BoardCardManager boardCardManager;

    // 포션은 덱이 아니라 소지품에 담긴다. CombatStats처럼 순수 C# 객체라
    // 씬에 컴포넌트를 붙이지 않는다.
    private readonly PotionManager potionManager = new();

    public DeckManager      DeckManager      => deckManager;
    public BoardCardManager BoardCardManager => boardCardManager;
    public PotionManager    PotionManager    => potionManager;

    // ── 초기화 ──────────────────────────────────────────

    public void Initialize(CharacterData data)
    {
        IsPlayerSide = true;
        ClassTag     = data.classTag;

        InitializeBase(
            maxHp:           data.maxHp,
            baseAttack:      data.baseAttack,
            maxActionPoints: data.maxActionPoints,
            entityName:      data.characterName,
            portrait:        data.portrait
        );

        SetSlot(data.initialSlot);

        deckManager.InitDeck(data.startingCombatCards);
        boardCardManager.InitDeck(data.startingBoardCards);
        potionManager.Clear();   // 포션도 런 단위 — 이전 런의 소지품이 넘어오지 않는다
    }

    // ── 보드 페이즈 ─────────────────────────────────────

    public void StartBoardPhase() => boardCardManager.StartBoardPhase();
    public void EndBoardPhase()   => boardCardManager.EndBoardPhase();

    // 보드 카드 사용 — 효과 실행은 BoardPhaseManager가 담당
    public bool TryUseBoardCard(CardData card) => boardCardManager.TryUseCard(card);

    // ── 전투 턴 흐름 ────────────────────────────────────

    public override void OnTurnStart()
    {
        base.OnTurnStart();       // 방어막 초기화·상태이상 처리·행동력 회복
        deckManager.StartTurn();  // 에너지 초기화·드로우
    }

    public override void OnTurnEnd()
    {
        base.OnTurnEnd();
        deckManager.EndTurn();    // 손패 버리기
    }

    // 전투 카드 사용 — 덱 상태 처리 (효과 실행은 CombatManager가 담당)
    public bool TryPlayCombatCard(CardData card) => deckManager.TryPlayCard(card);

    // 포지션 이동은 CombatManager.TryMoveFriendly()가 담당한다.
    // (행동력 검사·이동 가능 판정·자리 교체를 한곳에서 처리하기 위해 이쪽으로 옮김)

    // ── KnockOut ────────────────────────────────────────

    protected override void OnKnockedOut()
    {
        OnRunEnd?.Invoke();
    }
}
