using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 전투 중 포션 소지품 표시 + 사용 입력.
//
// 인텐트 배지·상태이상 칩과 같은 방식으로 **런타임에 만든다** —
// 프리팹을 건드리지 않아 오버라이드 함정을 피한다.
// 아트가 나오면 PotionLabel / 색 상수만 교체하면 된다.
//
// CombatPanel이 자식 오브젝트로 생성하고 PotionManager를 물려준다.
public class PotionBarUI : MonoBehaviour
{
    public event Action<CardData> OnPotionSelected;   // CombatPanel이 구독 → 타겟팅 시작

    // ── 임시 아트 (에셋 나오면 교체) ─────────────────────
    private static readonly Color SlotEmpty  = new(0.16f, 0.16f, 0.18f, 0.85f);
    private static readonly Color SlotFilled = new(0.30f, 0.42f, 0.34f, 0.95f);
    private static readonly Color TextEmpty  = new(0.45f, 0.45f, 0.48f, 1f);
    private static readonly Color TextFilled = new(0.92f, 0.94f, 0.90f, 1f);

    private const float SlotWidth  = 96f;
    private const float SlotHeight = 40f;
    private const float Spacing    = 8f;
    private const float MarginX    = 16f;
    private const float MarginY    = 16f;
    private const float LabelSize  = 16f;   // Neo둥근모는 16px 배수만

    private PotionManager  potions;
    private TMP_FontAsset  font;
    private readonly List<GameObject> slotObjects = new();

    // ── 수명 ────────────────────────────────────────────

    public void Bind(PotionManager manager, TMP_FontAsset labelFont)
    {
        Unbind();

        potions = manager;
        font    = labelFont;

        if (potions != null) potions.OnPotionsChanged += Rebuild;
        Rebuild();
    }

    public void Unbind()
    {
        if (potions != null) potions.OnPotionsChanged -= Rebuild;
        potions = null;
        ClearSlots();
    }

    private void OnDestroy() => Unbind();

    // ── 그리기 ──────────────────────────────────────────

    private void Rebuild()
    {
        ClearSlots();
        if (potions == null) return;

        for (int i = 0; i < potions.Capacity; i++)
        {
            CardData potion = i < potions.Count ? potions.Potions[i] : null;
            BuildSlot(i, potion);
        }
    }

    private void BuildSlot(int index, CardData potion)
    {
        var go = new GameObject(potion == null ? $"PotionSlot_{index}_빈칸"
                                               : $"PotionSlot_{index}_{potion.cardName}",
                                typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);   // 좌하단 기준
        rt.pivot     = new Vector2(0f, 0f);
        rt.sizeDelta = new Vector2(SlotWidth, SlotHeight);
        rt.anchoredPosition = new Vector2(
            MarginX + index * (SlotWidth + Spacing),
            MarginY);

        var img = go.AddComponent<Image>();
        img.color = potion == null ? SlotEmpty : SlotFilled;

        BuildLabel(go, potion);

        if (potion != null)
        {
            var btn = go.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => OnPotionSelected?.Invoke(potion));

            AddHover(go, potion);
        }

        slotObjects.Add(go);
    }

    private void BuildLabel(GameObject parent, CardData potion)
    {
        var labelGo = new GameObject("Text", typeof(RectTransform));
        labelGo.transform.SetParent(parent.transform, false);

        var lrt = labelGo.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero;
        lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;

        var label = labelGo.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;

        label.text             = PotionLabel(potion);
        label.color            = potion == null ? TextEmpty : TextFilled;
        label.fontSize         = LabelSize;
        label.alignment        = TextAlignmentOptions.Center;
        label.enableAutoSizing = false;
        label.raycastTarget    = false;                  // 클릭은 슬롯이 받는다
        label.fontStyle        = FontStyles.Normal;      // Neo둥근모는 볼드·이탤릭 금지
    }

    // 임시 표기 — 아트가 나오면 아이콘으로 교체한다.
    // 폰트에 없는 글자(※★→ 등)를 쓰지 말 것.
    private static string PotionLabel(CardData potion)
    {
        if (potion == null) return "빈 칸";

        // "치유 물약" → "치유". 이름이 길어도 슬롯을 넘지 않게 자른다
        string name = potion.cardName ?? "물약";
        int space = name.IndexOf(' ');
        if (space > 0) name = name.Substring(0, space);
        return name.Length > 4 ? name.Substring(0, 4) : name;
    }

    // 포션은 카드가 아니라 소지품이라 카드 팝업이 아닌 공용 툴팁을 쓴다
    private void AddHover(GameObject go, CardData potion)
    {
        TooltipTrigger.Attach(go, potion.cardName,
            $"{potion.description}\n\n에너지를 쓰지 않는다. 클릭해서 사용");
    }

    private void ClearSlots()
    {
        foreach (var go in slotObjects)
            if (go != null) Destroy(go);

        slotObjects.Clear();
        UIManager.Instance?.HideCardDetail();
    }
}
