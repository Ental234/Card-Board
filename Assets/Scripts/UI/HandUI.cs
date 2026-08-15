using System;
using System.Collections.Generic;
using UnityEngine;

// 카드 손패 팬 레이아웃 — CombatPanel / BoardPanel 양쪽에서 인스턴스 하나씩 사용
// DeckManager 또는 BoardCardManager의 OnHandChanged를 구독해서 RefreshHand() 호출
public class HandUI : MonoBehaviour
{
    [SerializeField] private CardUI       cardPrefab;
    [SerializeField] private RectTransform handRoot;

    [Header("팬 레이아웃")]
    [SerializeField] private float cardSpacing = 120f;  // 카드 간격
    [SerializeField] private float arcHeight   = 30f;   // 중앙이 높고 양끝이 낮은 곡선 높이
    [SerializeField] private float hoverRaise  = 40f;   // 호버 시 올라오는 높이

    // 픽셀 글꼴은 회전시키면 글리프가 임의 각도로 재샘플링되어 뭉개진다.
    // 부채꼴 느낌은 회전 대신 위치 곡선(arcHeight)으로만 낸다.
    [SerializeField] private bool  rotateCards = false;
    [SerializeField] private float fanAngle    = 20f;   // rotateCards가 true일 때만 사용

    public event Action<CardData> OnCardSelected;  // CombatPanel / BoardPanel이 구독

    private readonly List<CardUI>         cardUIs    = new();
    private readonly List<Vector2>        basePositions = new();  // 팬 레이아웃 기본 위치
    private int currentEnergy = 999;

    // ── 손패 갱신 ────────────────────────────────────────

    public void RefreshHand(List<CardData> hand, int energy)
    {
        currentEnergy = energy;
        RebuildCards(hand);
    }

    public void UpdateEnergy(int energy)
    {
        currentEnergy = energy;
        foreach (var ui in cardUIs)
            ui.SetUsable(ui.Data.energyCost <= currentEnergy);
    }

    // ── 카드 생성 & 팬 배치 ──────────────────────────────

    private void RebuildCards(List<CardData> hand)
    {
        foreach (var ui in cardUIs)
        {
            ui.OnClicked    -= HandleClicked;
            ui.OnHoverEnter -= HandleHoverEnter;
            ui.OnHoverExit  -= HandleHoverExit;
            Destroy(ui.gameObject);
        }
        cardUIs.Clear();
        basePositions.Clear();

        foreach (var data in hand)
        {
            var ui = Instantiate(cardPrefab, handRoot);
            ui.Setup(data, data.energyCost <= currentEnergy);
            ui.OnClicked    += HandleClicked;
            ui.OnHoverEnter += HandleHoverEnter;
            ui.OnHoverExit  += HandleHoverExit;
            cardUIs.Add(ui);
        }

        ApplyFanLayout();
    }

    private void ApplyFanLayout()
    {
        basePositions.Clear();
        int count = cardUIs.Count;
        if (count == 0) return;

        float totalWidth = cardSpacing * (count - 1);

        for (int i = 0; i < count; i++)
        {
            float t    = count > 1 ? (float)i / (count - 1) : 0.5f;
            float xPos = -totalWidth / 2f + cardSpacing * i;
            // 양 끝 카드는 내려가고 중앙 카드는 올라오는 포물선
            float yPos = -(Mathf.Abs(t - 0.5f) * 2f) * arcHeight;

            // 정수 좌표로 스냅 — 반픽셀에 걸리면 픽셀 글꼴이 뭉개진다
            var pos = new Vector2(Mathf.Round(xPos), Mathf.Round(yPos));
            basePositions.Add(pos);

            var rt = cardUIs[i].GetComponent<RectTransform>();
            rt.anchoredPosition = pos;
            rt.localRotation    = rotateCards
                                ? Quaternion.Euler(0, 0, (t - 0.5f) * -fanAngle)
                                : Quaternion.identity;
            rt.SetSiblingIndex(i);
        }
    }

    // ── 이벤트 핸들러 ────────────────────────────────────

    private void HandleClicked(CardUI ui) => OnCardSelected?.Invoke(ui.Data);

    private void HandleHoverEnter(CardUI ui)
    {
        int idx = cardUIs.IndexOf(ui);
        if (idx < 0) return;

        var rt = ui.GetComponent<RectTransform>();
        rt.anchoredPosition = basePositions[idx] + new Vector2(0, hoverRaise);
        rt.SetAsLastSibling();

        UIManager.Instance?.ShowCardDetail(ui.Data, ui.transform.position);
    }

    private void HandleHoverExit(CardUI ui)
    {
        int idx = cardUIs.IndexOf(ui);
        if (idx >= 0)
        {
            var rt = ui.GetComponent<RectTransform>();
            rt.anchoredPosition = basePositions[idx];
            rt.SetSiblingIndex(idx);
        }
        UIManager.Instance?.HideCardDetail();
    }
}
