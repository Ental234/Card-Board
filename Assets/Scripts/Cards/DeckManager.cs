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
    public int                     ExhaustPileCount => exhaustPile.Count;

    // 한 턴에 뽑는 장수. 덱이 이보다 얇아지면 손패가 모자라므로 카드 제거가 여기서 멈춘다.
    // 저주는 세지 않는다 — 저주는 손패를 채우지만 쓸 수 있는 패가 아니라서,
    // 세면 "일반 5장 + 저주 2장" 덱에서 일반 카드를 더 빼 쓸 수 있는 패가 5장 밑으로 내려간다.
    public int DrawPerTurn => drawPerTurn;
    public bool CanRemoveCard
    {
        get
        {
            int playable = GetAllCards().FindAll(c => !c.IsCurse).Count;
            return playable > drawPerTurn;
        }
    }

    // 더미 보기 창용 읽기 전용 목록. 뽑을 더미는 순서가 곧 정보이므로 보여줄 때 정렬할 것
    public IReadOnlyList<CardData> DrawPile    => drawPile;
    public IReadOnlyList<CardData> DiscardPile => discardPile;
    public IReadOnlyList<CardData> ExhaustPile => exhaustPile;

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

    // 전투 시작·끝에 덱을 원상 복구한다.
    //
    // 손패와 버린 더미를 전부 뽑을 더미로 되돌리고 섞는다.
    // 이게 없으면 이전 전투에서 쓴 카드가 버린 더미에 남은 채 다음 전투가 시작되어
    // 반쪽 덱으로 싸우게 된다 (드로우가 완전히 멈추지는 않지만 순서가 뒤틀린다).
    //
    // 소모 더미는 **전투 단위**다 (2026-09-22 결정, 슬더스와 같음) — 전투가 끝나면 소모된 카드도 덱으로 돌아온다.
    // 런 단위로 두면 소모 카드를 쓸수록 덱이 영구히 줄고, 재활용 카드가 이전 전투의 소모분까지 끌어와 후반에 과해진다.
    // 전투 시작과 끝 양쪽에서 부른다 — 끝에서도 부르는 건 전투 사이에 여는 덱 보기·강화·제거 창에
    // 소모된 카드가 빠져 보이지 않게 하기 위해서다.
    public void ResetForCombat()
    {
        drawPile.AddRange(hand);
        drawPile.AddRange(discardPile);
        drawPile.AddRange(exhaustPile);
        hand.Clear();
        discardPile.Clear();
        exhaustPile.Clear();

        Shuffle(drawPile);
        OnHandChanged?.Invoke(hand);
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
        hand.Contains(card) && !card.unplayable && currentEnergy >= card.energyCost;

    // 실제 사용: 에너지 차감 + 손패 → 버리기 파일
    // 반환값 true = 성공, false = 에너지 부족 or 손패에 없음
    public bool TryPlayCard(CardData card)
    {
        if (!CanPlay(card)) return false;

        SpendEnergy(card.energyCost);
        hand.Remove(card);

        // 소모 카드는 버린 더미가 아니라 소모 더미로 — 다음 셔플에 돌아오지 않는다
        if (card.exhaust) exhaustPile.Add(card);
        else              discardPile.Add(card);

        OnHandChanged?.Invoke(hand);
        return true;
    }

    // 소모(Exhaust): 버린 더미로 가지 않고 이번 전투 동안 다시 뽑히지 않는다 (전투가 끝나면 덱으로 복귀)
    public void ExhaustCard(CardData card)
    {
        if (!hand.Remove(card)) return;
        exhaustPile.Add(card);
        OnHandChanged?.Invoke(hand);
    }

    // ── 덱 편집 ────────────────────────────────────────

    // 보상·이벤트로 카드 추가 시 버리기 파일에 넣음 (다음 셔플에 합류)
    public void AddCard(CardData card) => discardPile.Add(card);

    // 전투 단위 카드(scope == Combat)를 덱에서 지운다. 전투가 끝날 때 ResetForCombat **뒤에** 부른다 —
    // 되돌리기 전에 부르면 손패·소모 더미에 있던 것을 놓친다.
    // 전투 **시작**에는 부르지 않는다. 전투 밖에서 얻은 전투 단위 카드는 그다음 전투를 치러야 사라진다.
    public void RemoveCombatScopedCards()
    {
        foreach (var pile in new[] { drawPile, discardPile, hand, exhaustPile })
            pile.RemoveAll(c => c.scope == CardScope.Combat);
        OnHandChanged?.Invoke(hand);
    }

    // 덱 전체 카드 목록 (드로우 + 버리기 파일) — 이벤트·상점의 카드 제거 UI에서 사용
    public List<CardData> GetAllCards()
    {
        var all = new List<CardData>(drawPile);
        all.AddRange(discardPile);
        return all;
    }

    // 강화 — 덱 안의 그 한 장을 강화된 복제본으로 바꿔 끼운다. 어느 더미에 있든 자리를 지킨다
    public bool ReplaceCard(CardData oldCard, CardData newCard)
    {
        foreach (var pile in new[] { drawPile, discardPile, hand, exhaustPile })
        {
            int i = pile.IndexOf(oldCard);
            if (i < 0) continue;
            pile[i] = newCard;
            if (pile == hand) OnHandChanged?.Invoke(hand);
            return true;
        }
        return false;
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

    // SetEnergy의 상한(maxEnergy)을 타면 안 된다 — 에너지 +2로 5/3이 된 뒤 1코스트를 쓰면
    // 4가 아니라 3으로 잘려 에너지 1이 증발했다 (GainEnergy가 최대치를 넘길 수 있게 된 뒤 생긴 버그)
    private void SpendEnergy(int amount)
    {
        currentEnergy = Mathf.Max(0, currentEnergy - amount);
        OnEnergyChanged?.Invoke(currentEnergy, maxEnergy);
    }

    // 에너지 획득은 최대치를 넘길 수 있다 (슬더스 관례) — 턴이 시작되면 최대치로 돌아간다.
    // 예전엔 SetEnergy의 상한에 잘려 "에너지 +1" 카드·기습 준비가 턴 시작 직후엔 아무 효과가 없었다.
    public void GainEnergy(int amount)
    {
        currentEnergy = Mathf.Max(0, currentEnergy + amount);
        OnEnergyChanged?.Invoke(currentEnergy, maxEnergy);
    }

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
