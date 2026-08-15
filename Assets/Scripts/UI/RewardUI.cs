using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 전투 승리 보상 화면
// 골드·렐릭은 이미 지급된 상태로 표시만 하고, 카드 3장 중 1장을 고르게 한다.
public class RewardUI : MonoBehaviour
{
    [Header("텍스트")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI goldText;
    [SerializeField] private TextMeshProUGUI relicText;

    [Header("카드 선택")]
    [SerializeField] private CardUI        cardPrefab;
    [SerializeField] private RectTransform cardRoot;
    [SerializeField] private float         cardSpacing = 180f;

    [Header("버튼")]
    [SerializeField] private Button skipButton;

    private readonly List<CardUI> spawned = new();

    private void OnEnable()
    {
        skipButton?.onClick.AddListener(OnSkipClicked);

        // 패널이 이벤트보다 늦게 켜지는 경우를 대비해 현재 보상으로 한 번 그린다.
        Render(RewardManager.Instance?.Current);
    }

    private void OnDisable()
    {
        skipButton?.onClick.RemoveListener(OnSkipClicked);
        ClearCards();
    }

    // ── 표시 ────────────────────────────────────────────

    public void Render(CombatReward reward)
    {
        ClearCards();
        if (reward == null) return;

        if (titleText != null)
            titleText.text = reward.kind switch
            {
                RewardKind.Elite    => "정예 처치 보상",
                RewardKind.Boss     => "보스 처치 보상",
                RewardKind.Treasure => "보물 발견",
                _                   => "전투 보상",
            };

        if (goldText != null)
        {
            goldText.gameObject.SetActive(reward.gold > 0);
            goldText.text = $"골드 +{reward.gold}";
        }

        if (relicText != null)
        {
            bool has = reward.relic != null;
            relicText.gameObject.SetActive(has);
            if (has) relicText.text = $"렐릭 획득  {reward.relic.relicName}";
        }

        BuildCards(reward.cardChoices);
    }

    private void BuildCards(List<CardData> choices)
    {
        if (cardPrefab == null || cardRoot == null || choices == null) return;

        float totalWidth = cardSpacing * (choices.Count - 1);

        for (int i = 0; i < choices.Count; i++)
        {
            var ui = Instantiate(cardPrefab, cardRoot);
            ui.Setup(choices[i], usable: true);   // 보상 카드는 항상 선택 가능
            ui.OnClicked += HandleCardClicked;

            var rt = ui.GetComponent<RectTransform>();
            rt.anchoredPosition = new Vector2(Mathf.Round(-totalWidth / 2f + cardSpacing * i), 0f);

            spawned.Add(ui);
        }
    }

    private void ClearCards()
    {
        foreach (var ui in spawned)
        {
            if (ui == null) continue;
            ui.OnClicked -= HandleCardClicked;
            Destroy(ui.gameObject);
        }
        spawned.Clear();
    }

    // ── 입력 ────────────────────────────────────────────

    private void HandleCardClicked(CardUI ui) => RewardManager.Instance?.SelectCard(ui.Data);

    private void OnSkipClicked() => RewardManager.Instance?.Skip();
}
