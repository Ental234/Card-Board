using System.Collections.Generic;
using UnityEngine;

// 보드 카드 덱 관리 (Board 카드 전용)
// 전투 덱과 동일한 드로우→사용→버리기 사이클
// 보드 페이즈 전용 에너지(boardEnergy)로 코스트 처리
// 카드 효과 실행은 BoardPhaseManager가 담당
public class BoardCardManager : MonoBehaviour
{
    public event System.Action<List<CardData>> OnHandChanged;
    public event System.Action<int, int>       OnBoardEnergyChanged;  // (current, max)

    [Header("보드 페이즈 설정")]
    [SerializeField] private int drawPerPhase  = 3;
    [SerializeField] private int maxBoardEnergy = 2;

    private readonly List<CardData> drawPile    = new();
    private readonly List<CardData> discardPile = new();
    private readonly List<CardData> hand        = new();

    private int currentBoardEnergy;

    public IReadOnlyList<CardData> Hand               => hand;
    public int                     CurrentBoardEnergy => currentBoardEnergy;
    public int                     MaxBoardEnergy     => maxBoardEnergy;
    public int                     DrawPileCount      => drawPile.Count;
    public int                     DiscardPileCount   => discardPile.Count;

    // ── 런 시작 ────────────────────────────────────────

    public void InitDeck(IEnumerable<CardData> startingDeck)
    {
        drawPile.Clear();
        discardPile.Clear();
        hand.Clear();

        drawPile.AddRange(startingDeck);
        Shuffle(drawPile);
    }

    // ── 보드 페이즈 시작 ───────────────────────────────

    public void StartBoardPhase()
    {
        SetEnergy(maxBoardEnergy);
        DrawCards(drawPerPhase);
    }

    // 보드 페이즈 종료 시 손패 버리기
    public void EndBoardPhase()
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

    public bool CanUse(CardData card) =>
        hand.Contains(card) && currentBoardEnergy >= card.energyCost;

    public bool TryUseCard(CardData card)
    {
        if (!CanUse(card)) return false;

        SpendEnergy(card.energyCost);
        hand.Remove(card);
        discardPile.Add(card);
        OnHandChanged?.Invoke(hand);
        return true;
    }

    // ── 덱 편집 ────────────────────────────────────────

    public void AddCard(CardData card)
    {
        if (card.cardType != CardType.Board)
        {
            Debug.LogWarning($"[BoardCardManager] {card.cardName}은 Board 카드가 아닙니다.");
            return;
        }
        discardPile.Add(card);
    }

    public bool RemoveCard(CardData card)
    {
        if (drawPile.Remove(card))    return true;
        if (discardPile.Remove(card)) return true;
        if (hand.Remove(card))
        {
            OnHandChanged?.Invoke(hand);
            return true;
        }
        return false;
    }

    // ── 내부 유틸 ──────────────────────────────────────

    private void SetEnergy(int value)
    {
        currentBoardEnergy = Mathf.Clamp(value, 0, maxBoardEnergy);
        OnBoardEnergyChanged?.Invoke(currentBoardEnergy, maxBoardEnergy);
    }

    private void SpendEnergy(int amount) => SetEnergy(currentBoardEnergy - amount);

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
