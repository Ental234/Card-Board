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

    public void Setup(CardData data, bool usable)
    {
        Data     = data;
        IsUsable = usable;

        if (cardNameText    != null) cardNameText.text    = data.cardName;
        if (costText        != null) costText.text        = data.energyCost.ToString();
        if (descriptionText != null) descriptionText.text = data.description;
        if (artworkImage    != null && data.artwork != null) artworkImage.sprite = data.artwork;
        if (frameImage      != null) frameImage.color = ClassColors[(int)data.classTag];

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
