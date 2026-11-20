using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

// 카드 강화 (GDD 9장)
//
// 강화 데이터는 원본 에셋 안에 있다(CardData.upgrade1/2). 강화하는 순간 **그 한 장만 복제**해
// 다음 단계 값을 입힌다 — 같은 카드 4장 중 1장만 강화돼야 하고, 원본 에셋을 고치면 다음 런까지 번진다.
//
//   var up = CardUpgrader.Upgrade(card);         // +1
//   deck.ReplaceCard(card, up);
//
// 복제본은 런 동안만 존재한다. 이어하기를 만들 때는 "원본 + 단계 + 페널티"를 저장할 것.
public static class CardUpgrader
{
    public const int MaxLevel = 2;

    // 보드 카드는 1회용이라 강화하지 않는다
    public static bool CanUpgrade(CardData card)
        => card != null && card.cardType == CardType.Combat && card.upgradeLevel < MaxLevel;

    // 자동 규칙이 손대는 효과 — 수치가 "세기"인 것만. 이동 칸 수·드로우 장수 등은 그대로 둔다
    public static bool IsScalable(EffectType type)
        => type == EffectType.Damage || type == EffectType.Shield || type == EffectType.Heal;

    // levels 단계 강화한 복제본을 돌려준다. 원본(card)은 건드리지 않는다.
    public static CardData Upgrade(CardData card, int levels = 1, CardPenalty penalty = CardPenalty.None)
    {
        if (card == null) return null;

        var original = card.Original;
        var copy     = Object.Instantiate(card);          // 직렬화 복제 — 효과 배열까지 새로 만들어진다
        copy.name         = original.name;
        copy.baseCard     = original;
        copy.upgradeLevel = card.upgradeLevel;
        copy.penalty      = card.penalty;

        for (int i = 0; i < levels && copy.upgradeLevel < MaxLevel; i++)
        {
            ApplyStep(copy, original, copy.upgradeLevel + 1);
            copy.upgradeLevel++;
        }

        if (penalty != CardPenalty.None && copy.penalty == CardPenalty.None)
            ApplyPenalty(copy, penalty);

        copy.cardName = original.cardName + new string('+', copy.upgradeLevel);
        return copy;
    }

    private static void ApplyStep(CardData copy, CardData original, int toLevel)
    {
        var up         = toLevel == 1 ? original.upgrade1 : original.upgrade2;
        var oldEffects = copy.effects ?? new CardEffect[0];

        CardEffect[] newEffects = up != null && up.effects != null && up.effects.Length > 0
                                ? CloneEffects(up.effects)
                                : AutoScale(oldEffects);

        copy.effects    = newEffects;
        copy.energyCost = Mathf.Max(0, copy.energyCost + (up?.costDelta ?? 0));

        // 자동 규칙으로 아무 수치도 안 바뀌는 카드(이동·드로우만 있는 카드 등)는 비용을 1 깎는다
        bool usedAuto = up == null || up.effects == null || up.effects.Length == 0;
        if (usedAuto && (up == null || up.costDelta == 0) && SameEffects(oldEffects, newEffects) && copy.energyCost > 0)
            copy.energyCost -= 1;

        copy.description = up != null && !string.IsNullOrEmpty(up.description)
                         ? up.description
                         : ReplaceNumbers(copy.description, oldEffects, newEffects);
    }

    // 자동 규칙: 피해·방어막·회복 +30% (최소 +2) / 상태이상 지속 +1턴, 누적형(출혈·독·화상)은 스택도 +30%(최소 +1)
    private static CardEffect[] AutoScale(CardEffect[] source)
    {
        var result = CloneEffects(source);
        foreach (var e in result)
        {
            if (e == null) continue;

            if (e.value > 0 && IsScalable(e.type))
                e.value += Mathf.Max(2, Mathf.RoundToInt(e.value * 0.3f));

            if (e.type == EffectType.ApplyStatus)
            {
                if (e.duration > 0) e.duration += 1;
                if (e.value > 0 && IsStacking(e.status))
                    e.value += Mathf.Max(1, Mathf.RoundToInt(e.value * 0.3f));
            }
        }
        return result;
    }

    private static bool IsStacking(StatusEffect s)
        => s == StatusEffect.Bleed || s == StatusEffect.Poison || s == StatusEffect.Burn;

    private static bool SameEffects(CardEffect[] a, CardEffect[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == null || b[i] == null) { if (a[i] != b[i]) return false; continue; }
            if (a[i].type != b[i].type || a[i].value != b[i].value || a[i].duration != b[i].duration) return false;
        }
        return true;
    }

    private static CardEffect[] CloneEffects(CardEffect[] source)
    {
        var result = new CardEffect[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            var s = source[i];
            result[i] = s == null ? null : new CardEffect { type = s.type, value = s.value, status = s.status, duration = s.duration };
        }
        return result;
    }

    // 설명 속 옛 수치를 새 수치로 바꾼다 ("6 피해를 준다" → "9 피해를 준다").
    // 효과 순서대로 맞춰 보고, 바뀐 수치만 첫 등장 한 번 바꾼다. 두 효과가 같은 숫자를 쓸 때
    // 서로 덮어쓰지 않도록 자리표시자를 거친다.
    private static string ReplaceNumbers(string text, CardEffect[] oldEffects, CardEffect[] newEffects)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var pending = new List<(string token, int value)>();
        int n = Mathf.Min(oldEffects.Length, newEffects.Length);

        void Swap(int oldValue, int newValue)
        {
            if (oldValue == newValue) return;
            var m = new Regex($@"(?<!\d){oldValue}(?!\d)").Match(text);
            if (!m.Success) return;

            string token = $"\u0001{pending.Count}\u0001";
            text = text.Substring(0, m.Index) + token + text.Substring(m.Index + m.Length);
            pending.Add((token, newValue));
        }

        for (int i = 0; i < n; i++)
        {
            var o = oldEffects[i];
            var e = newEffects[i];
            if (o == null || e == null) continue;
            Swap(o.value, e.value);         // 수치
            Swap(o.duration, e.duration);   // 지속 턴 ("2턴간")
        }

        foreach (var (token, value) in pending) text = text.Replace(token, value.ToString());
        return text;
    }

    // ── 특별 강화 페널티 ─────────────────────────────────

    private static void ApplyPenalty(CardData copy, CardPenalty penalty)
    {
        copy.penalty = penalty;
        switch (penalty)
        {
            case CardPenalty.CostUp:
                copy.energyCost += 1;
                copy.description += "\n[대가] 비용 +1";
                break;
            case CardPenalty.Exhaust:
                copy.exhaust = true;
                copy.description += "\n[대가] 소모 - 쓰면 이번 전투에서 사라진다";
                break;
            case CardPenalty.FrontOnly:
                copy.useableSlots = SlotMask.Front;
                copy.description += "\n[대가] 1·2번 슬롯에서만 사용";
                break;
            case CardPenalty.AddCurse:
                // 저주 카드를 덱에 넣는 것은 호출한 쪽(GameManager)이 한다 — 카드 자체는 그대로
                copy.description += "\n[대가] 저주 카드 1장이 덱에 들어왔다";
                break;
        }
    }

    // 무작위 페널티. **그 카드에 실제로 손해가 되는 것만** 뽑는다 —
    // 원래 전열 전용인 강타에 "1·2번 슬롯에서만"을 붙이면 공짜 +2가 된다 (실제로 테스트에서 나왔다).
    // 저주 카드가 아직 없으면 저주 동반은 뽑지 않는다.
    public static CardPenalty RandomPenalty(CardData card, bool curseAvailable)
    {
        var pool = new List<CardPenalty> { CardPenalty.CostUp };
        if (card == null || !card.exhaust) pool.Add(CardPenalty.Exhaust);
        if (card == null || (card.EffectiveUseableSlots & SlotMask.Back) != 0) pool.Add(CardPenalty.FrontOnly);
        if (curseAvailable) pool.Add(CardPenalty.AddCurse);
        return pool[Random.Range(0, pool.Count)];
    }

    // ── 미리보기 ─────────────────────────────────────────

    // "지금 → 다음 강화" 비교 문구 (고르는 창·덱 보기 호버용)
    public static string Preview(CardData card, int levels = 1)
    {
        if (card == null) return "";
        if (card.IsCurse)                     return "저주는 강화할 수 없다";
        if (card.cardType != CardType.Combat) return "보드 카드는 강화할 수 없다";
        if (!CanUpgrade(card))               return "최대 강화 (++)";

        var next = Upgrade(card, levels);
        string text = $"{card.cardName} → {next.cardName}";
        if (next.energyCost != card.energyCost) text += $"\n비용 {card.energyCost} → {next.energyCost}";
        text += $"\n\n{next.description}";
        Object.Destroy(next);   // 미리보기용 임시 복제본
        return text;
    }
}
