using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 카드 한 장 프리팹 컴포넌트 — HandUI / ShopUI 양쪽에서 사용
public class CardUI : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private TextMeshProUGUI cardNameText;
    [SerializeField] private TextMeshProUGUI costText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private Image           artworkImage;
    [SerializeField] private Image           frameImage;     // 직업별 테두리 색
    [SerializeField] private CanvasGroup     canvasGroup;    // 사용 불가 시 반투명

    public event Action<CardUI> OnClicked;
    public event Action<CardUI> OnHoverEnter;
    public event Action<CardUI> OnHoverExit;

    public CardData Data      { get; private set; }
    public bool     IsUsable  { get; private set; }

    // 직업 → 테두리 색
    private static readonly Color[] ClassColors =
    {
        new(0.80f, 0.32f, 0.32f), // Warrior   — 붉은
        new(0.32f, 0.48f, 0.80f), // Mage      — 푸른
        new(0.32f, 0.72f, 0.40f), // Rogue     — 초록
        new(0.72f, 0.64f, 0.40f), // Universal — 금색
    };

    // 저주는 직업과 무관하게 보라 — 옛 저주 노드 색과 같은 계열
    private static readonly Color CurseColor = new(0.50f, 0.20f, 0.60f);

    public void Setup(CardData data, bool usable)
    {
        Data     = data;
        IsUsable = usable;

        if (cardNameText    != null)
        {
            cardNameText.text = data.cardName;
            // 등급은 이름 글자색으로 (DESIGN D-2). 테두리는 직업색이 쓰고 있다.
            // 저주는 등급이 뜻이 없어 흰색 — 필드 기본값(일반)과 같지만 저주에 등급을 매겨도 색이 새지 않게 막는다
            cardNameText.color = data.IsCurse ? Color.white : RarityTable.NameColor(data.rarity);
        }
        // 사용 불가 카드는 비용이 뜻이 없다. 0으로 보이면 "공짜로 쓸 수 있다"로 읽힌다
        if (costText        != null) costText.text        = data.unplayable ? "-" : data.energyCost.ToString();
        if (descriptionText != null) descriptionText.text = data.description;
        if (artworkImage    != null && data.artwork != null) artworkImage.sprite = data.artwork;
        if (frameImage      != null) frameImage.color = data.IsCurse ? CurseColor : ClassColors[(int)data.classTag];

        RefreshAlpha();
    }

    public void SetUsable(bool usable)
    {
        IsUsable = usable;
        RefreshAlpha();
    }

    private void RefreshAlpha()
    {
        if (canvasGroup != null) canvasGroup.alpha = IsUsable ? 1f : 0.5f;
    }

    public void OnPointerClick(PointerEventData _)
    {
        if (IsUsable) OnClicked?.Invoke(this);
    }

    public void OnPointerEnter(PointerEventData _) => OnHoverEnter?.Invoke(this);
    public void OnPointerExit(PointerEventData _)  => OnHoverExit?.Invoke(this);
}
