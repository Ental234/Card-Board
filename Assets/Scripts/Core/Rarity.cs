using System;
using System.Collections.Generic;
using UnityEngine;

// 카드·렐릭 등급 (3단계 6번, 2026-11-20)
//
// ※ Unity는 열거형을 정수로 직렬화한다. 번호가 곧 에셋에 저장된 값이므로 순서를 바꾸지 말 것.
//    Common = 0은 등급 도입 전 에셋이 전부 일반으로 읽히게 하려는 것이다 — 0을 다른 등급에 주면
//    기존 카드가 한꺼번에 그 등급으로 바뀐다. 사이 등급이 생길 자리를 남겨 10씩 띄웠다.
public enum Rarity
{
    Common   = 0,
    Uncommon = 10,
    Rare     = 20,
    Unique   = 30,  // 추첨으로 나오지 않는다 — 시작 렐릭·캐릭터 전용 카드처럼 정해진 경로로만 얻는 것
}

// 추첨 비중과 가격 배수, 이름 색을 한곳에 모은다.
// 상점·보상 풀이 각자 숫자를 들면 같은 등급이 화면마다 다른 확률·가격이 된다.
// 이 클래스는 "어떤 등급을 얼마나"만 정한다 — 풀을 고르는 건(직업 필터·보유 렐릭 제외) 부르는 쪽 몫이다.
//
// 수치는 슬더스를 출발점으로 한 임시값이다. 조정은 6-A 밸런싱에서
public static class RarityTable
{
    // 등급별 비중. Unique는 어떤 표에도 넣지 않는다 (비중 0 = 추첨 제외)
    public readonly struct Weights
    {
        public readonly int common, uncommon, rare;
        public Weights(int common, int uncommon, int rare)
        {
            this.common   = common;
            this.uncommon = uncommon;
            this.rare     = rare;
        }

        public int Of(Rarity r) => r switch
        {
            Rarity.Common   => common,
            Rarity.Uncommon => uncommon,
            Rarity.Rare     => rare,
            _               => 0,
        };
    }

    // ── 추첨 비중 ───────────────────────────────────────

    // 카드 보상 — 강한 적을 이길수록 희귀가 잘 나온다
    public static readonly Weights MonsterCardReward = new(60, 37, 3);
    public static readonly Weights EliteCardReward   = new(50, 40, 10);
    public static readonly Weights BossCardReward    = new(30, 40, 30);

    public static readonly Weights ShopCards = new(55, 37, 8);
    public static readonly Weights Relics    = new(50, 33, 17);  // 보상·상점 공용

    public static Weights CardRewardFor(RewardKind kind) => kind switch
    {
        RewardKind.Boss     => BossCardReward,
        RewardKind.Elite    => EliteCardReward,
        RewardKind.Treasure => EliteCardReward,  // 보물은 정예 대신 얻는 보상이라 같은 표
        _                   => MonsterCardReward,
    };

    // ── 가격 ────────────────────────────────────────────
    //
    // 백분율 정수로 계산한다. float 배수에 RoundToInt를 쓰면 은행가 반올림으로 .5가 아래로 잘린다 —
    // 화면 가격과 차감액은 ShopItem.price 하나를 쓰므로 어긋나진 않지만, 기본가를 바꿀 때 예측이 어렵다
    public static int CardPrice(int basePrice, Rarity r) => basePrice * CardPricePercent(r) / 100;
    public static int RelicPrice(int basePrice, Rarity r) => basePrice * RelicPricePercent(r) / 100;

    // 카드 50 / 75 / 150 (기본가 50 기준, 슬더스와 같은 비율)
    private static int CardPricePercent(Rarity r) => r switch
    {
        Rarity.Uncommon => 150,
        Rarity.Rare     => 300,
        _               => 100,
    };

    // 렐릭은 카드보다 등급 간 차이를 좁힌다 — 기본가가 이미 높아 3배면 한 스테이지 골드로 못 산다
    private static int RelicPricePercent(Rarity r) => r switch
    {
        Rarity.Uncommon => 150,
        Rarity.Rare     => 200,
        _               => 100,
    };

    // ── 이름 색 (DESIGN D-2: 이름 글자색, 2026-11-20 결정) ──
    //
    // 카드 바탕이 어두운 슬레이트라 전부 밝은 톤이다.
    // 금색 계열은 피한다 — 범용 직업색(테두리)과 액센트 골드가 이미 쓰고 있다.
    // 보라도 피한다 — 저주 테두리 색이다
    public static Color NameColor(Rarity r) => r switch
    {
        Rarity.Uncommon => new Color(0.43f, 0.78f, 1.00f),  // 하늘
        Rarity.Rare     => new Color(1.00f, 0.60f, 0.24f),  // 주황
        Rarity.Unique   => new Color(0.31f, 0.85f, 0.69f),  // 청록
        _               => Color.white,
    };

    // 문자열로만 이름을 그리는 곳(상점 버튼·툴팁 제목·렐릭 칩)용
    public static string Colorize(string name, Rarity r)
    {
        if (r == Rarity.Common) return name;   // 일반은 원래 글자색을 그대로 둔다
        return $"<color=#{ColorUtility.ToHtmlStringRGB(NameColor(r))}>{name}</color>";
    }

    // ── 가중 추첨 ───────────────────────────────────────
    //
    // 두 단계로 뽑는다: 등급을 먼저 굴리고, 그 등급 안에서 균등하게.
    // 항목마다 비중을 주는 방식이면 일반 카드가 늘어날수록 희귀 확률이 저절로 떨어진다 —
    // 4단계에서 카드를 채우는 순간 확률표가 무너진다.
    //
    // 후보가 없는 등급은 굴리기 전에 뺀다. 희귀를 굴렸는데 풀에 희귀가 없다고 빈손이면
    // 보상 칸이 비어 버린다. 남은 등급끼리 비중을 다시 나눠 갖는다.
    // 비중이 0인 등급(Unique)은 풀에 들어 있어도 절대 나오지 않는다.
    //
    // 고른 항목을 pool에서 빼서 돌려준다 — 같은 보상·같은 상점 안에서 중복을 막으려는 것.
    // 뽑을 게 없으면 default(null)
    public static T TakeWeighted<T>(List<T> pool, Func<T, Rarity> rarityOf, Weights weights)
    {
        if (pool == null || pool.Count == 0) return default;

        int common   = 0;
        int uncommon = 0;
        int rare     = 0;
        foreach (var item in pool)
        {
            switch (rarityOf(item))
            {
                case Rarity.Common:   common++;   break;
                case Rarity.Uncommon: uncommon++; break;
                case Rarity.Rare:     rare++;     break;
            }
        }

        int wCommon   = common   > 0 ? weights.common   : 0;
        int wUncommon = uncommon > 0 ? weights.uncommon : 0;
        int wRare     = rare     > 0 ? weights.rare     : 0;
        int total     = wCommon + wUncommon + wRare;
        if (total <= 0) return default;

        int roll = UnityEngine.Random.Range(0, total);
        Rarity picked;
        if (roll < wCommon)                  picked = Rarity.Common;
        else if (roll < wCommon + wUncommon) picked = Rarity.Uncommon;
        else                                 picked = Rarity.Rare;

        var candidates = new List<int>();
        for (int i = 0; i < pool.Count; i++)
            if (rarityOf(pool[i]) == picked) candidates.Add(i);

        int index = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        T result  = pool[index];
        pool.RemoveAt(index);
        return result;
    }
}
