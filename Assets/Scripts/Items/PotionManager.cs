using System;
using System.Collections.Generic;
using UnityEngine;

// 포션 소지품 — 전투 덱과 완전히 분리된 런 단위 자원.
//
// 포션은 CardData로 저작한다. 새 타입을 만들지 않은 이유는 효과 실행기
// (CombatManager.ExecuteEffect)와 타겟 규칙을 카드와 그대로 공유하기 위해서다.
// 카드와 다른 점은 "어디에 담기느냐"뿐이다 — 덱이 아니라 여기에 담긴다.
//
// MonoBehaviour가 아니다. CombatStats와 같은 순수 C# 객체라
// PlayerCharacter가 new로 소유하며, 씬에 컴포넌트를 붙이지 않아도 된다.
public class PotionManager
{
    public event Action OnPotionsChanged;   // UI가 구독 → 소지품 다시 그리기

    private readonly List<CardData> potions = new();
    public IReadOnlyList<CardData> Potions => potions;

    // 상한을 두지 않으면 상점에서 골드로 포션을 무한히 쌓아 전투를 무력화할 수 있다.
    // 렐릭으로 늘릴 수 있도록 상수가 아니라 필드로 둔다.
    public const int DefaultCapacity = 3;

    private int capacity = DefaultCapacity;
    public  int Capacity => capacity;
    public  int Count    => potions.Count;
    public  bool IsFull  => potions.Count >= capacity;

    // 상한을 줄이면 넘치는 만큼 뒤에서 버린다 (렐릭 해제 등)
    public void SetCapacity(int value)
    {
        capacity = Mathf.Max(0, value);

        bool trimmed = false;
        while (potions.Count > capacity)
        {
            potions.RemoveAt(potions.Count - 1);
            trimmed = true;
        }

        if (trimmed) OnPotionsChanged?.Invoke();
    }

    // 가득 차 있으면 실패한다. 호출하는 쪽이 골드를 쓰기 **전에** 확인해야
    // "돈만 나가고 포션은 안 들어오는" 상황이 안 생긴다.
    public bool TryAdd(CardData potion)
    {
        if (potion == null || IsFull) return false;

        potions.Add(potion);
        OnPotionsChanged?.Invoke();
        return true;
    }

    // 같은 종류를 여러 개 들 수 있으므로 첫 하나만 지운다
    public bool Remove(CardData potion)
    {
        if (potion == null || !potions.Remove(potion)) return false;

        OnPotionsChanged?.Invoke();
        return true;
    }

    public bool Has(CardData potion) => potion != null && potions.Contains(potion);

    // 런이 바뀌면 비운다 (PlayerCharacter.Initialize가 호출)
    public void Clear()
    {
        if (potions.Count == 0) return;

        potions.Clear();
        OnPotionsChanged?.Invoke();
    }
}
