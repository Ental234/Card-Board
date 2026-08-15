using System.Collections.Generic;
using UnityEngine;

// 전투 덱 관리 (Combat 카드 전용)
// 드로우 파일 → 손패 → 버리기 파일 → (셔플) → 드로우 파일 순환
// 카드 효과 실행은 CombatManager가 담당 — 이 클래스는 덱 상태만 관리
public class DeckManager : MonoBehaviour
{
    public event System.Action<List<CardData>> OnHandChanged;
    public event System.Action<int, int>       OnEnergyChanged;  // (current, max)

    [Header("전투 설정")]
    [SerializeField] private int drawPerTurn = 5;
    [SerializeField] private int maxEnergy   = 3;

    private readonly List<CardData> drawPile    = new();
    private readonly List<CardData> discardPile = new();
    private readonly List<CardData> exhaustPile = new();
    private readonly List<CardData> hand        = new();

    private int currentEnergy;

    public IReadOnlyList<CardData> Hand         => hand;
    public int                     CurrentEnergy => currentEnergy;
    public int                     MaxEnergy     => maxEnergy;
    public int                     DrawPileCount    => drawPile.Count;
    public int                     DiscardPileCount => discardPile.Count;

    // ── 런 시작 ────────────────────────────────────────

    public void InitDeck(IEnumerable<CardData> startingDeck)
    {
        drawPile.Clear();
        discardPile.Clear();
        exhaustPile.Clear();
        hand.Clear();

        drawPile.AddRange(startingDeck);
        Shuffle(drawPile);
    }

    // ── 턴 흐름 ────────────────────────────────────────

    public void StartTurn()
    {
        SetEnergy(maxEnergy);
        DrawCards(drawPerTurn);
    }

    public void EndTurn()
    {
        discardPile.AddRange(hand);
        hand.Clear();
        OnHandChanged?.Invoke(hand);
    }

    // ── 드로우 ─────────────────────────────────────────

    public void DrawCards(int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (drawPile.Count == 0)
            {
                if (discardPile.Count == 0) break;
                ShuffleDiscardIntoDraw();
            }

            int last = drawPile.Count - 1;
            hand.Add(drawPile[last]);
            drawPile.RemoveAt(last);
        }
        OnHandChanged?.Invoke(hand);
    }

    // ── 카드 사용 ──────────────────────────────────────

    // 사용 가능 여부만 확인 (UI 등에서 활성화 판단용)
    public bool CanPlay(CardData card) =>
        hand.Contains(card) && currentEnergy >= card.energyCost;

    // 실제 사용: 에너지 차감 + 손패 → 버리기 파일
    // 반환값 true = 성공, false = 에너지 부족 or 손패에 없음
    public bool TryPlayCard(CardData card)
    {
        if (!CanPlay(card)) return false;

        SpendEnergy(card.energyCost);
        hand.Remove(card);
        discardPile.Add(card);
        OnHandChanged?.Invoke(hand);
        return true;
    }

    // 소모(Exhaust): 버리기 파일로 가지 않고 이번 런 재사용 불가
    public void ExhaustCard(CardData card)
    {
        if (!hand.Remove(card)) return;
        exhaustPile.Add(card);
        OnHandChanged?.Invoke(hand);
    }

    // ── 덱 편집 ────────────────────────────────────────

    // 보상·이벤트로 카드 추가 시 버리기 파일에 넣음 (다음 셔플에 합류)
    public void AddCard(CardData card) => discardPile.Add(card);

    // 덱 전체 카드 목록 (드로우 + 버리기 파일) — 이벤트·상점의 카드 제거 UI에서 사용
    public List<CardData> GetAllCards()
    {
        var all = new List<CardData>(drawPile);
        all.AddRange(discardPile);
        return all;
    }

    // 덱 슬리밍 (상점·이벤트에서 카드 제거)
    public bool RemoveCard(CardData card)
    {
        if (drawPile.Remove(card))    return true;
        if (discardPile.Remove(card)) return true;
        return false;
    }

    // ── 내부 유틸 ──────────────────────────────────────

    private void SetEnergy(int value)
    {
        currentEnergy = Mathf.Clamp(value, 0, maxEnergy);
        OnEnergyChanged?.Invoke(currentEnergy, maxEnergy);
    }

    private void SpendEnergy(int amount) => SetEnergy(currentEnergy - amount);

    public void GainEnergy(int amount)   => SetEnergy(currentEnergy + amount);

    private void ShuffleDiscardIntoDraw()
    {
        drawPile.AddRange(discardPile);
        discardPile.Clear();
        Shuffle(drawPile);
    }

    private static void Shuffle(List<CardData> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
