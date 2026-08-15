using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 항상 표시되는 HUD — HP, 골드, 렐릭 아이콘, 스테이지 표시
public class HUDPanel : MonoBehaviour
{
    [Header("플레이어 HP")]
    [SerializeField] private Slider          hpSlider;
    [SerializeField] private TextMeshProUGUI hpText;

    [Header("골드 & 스테이지")]
    [SerializeField] private TextMeshProUGUI goldText;
    [SerializeField] private TextMeshProUGUI stageText;

    [Header("렐릭 아이콘")]
    [SerializeField] private Transform relicIconRoot;
    [SerializeField] private Image     relicIconPrefab;  // 렐릭 아이콘 한 칸 프리팹

    private void OnEnable()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        gm.Player.Stats.OnHpChanged           += RefreshHp;
        gm.BoardPhaseManagerRef.OnPlayerGoldChanged += RefreshGold;
        gm.OnStageChanged                     += RefreshStage;

        // RelicManager는 Awake 순서에 따라 아직 없을 수 있다
        if (RelicManager.Instance != null)
            RelicManager.Instance.OnRelicAdded += OnRelicAdded;

        // 초기값
        RefreshHp(gm.Player.Stats.CurrentHp, gm.Player.Stats.MaxHp);
        RefreshGold(gm.BoardPhaseManagerRef.PlayerGold);
        RefreshStage(gm.CurrentStage + 1);
    }

    private void OnDisable()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        gm.Player.Stats.OnHpChanged               -= RefreshHp;
        gm.BoardPhaseManagerRef.OnPlayerGoldChanged -= RefreshGold;
        gm.OnStageChanged                         -= RefreshStage;

        if (RelicManager.Instance != null)
            RelicManager.Instance.OnRelicAdded -= OnRelicAdded;
    }

    // ── 갱신 ────────────────────────────────────────────

    private void RefreshHp(int current, int max)
    {
        if (hpSlider != null) hpSlider.value = max > 0 ? (float)current / max : 0f;
        if (hpText   != null) hpText.text    = $"{current}/{max}";
    }

    private void RefreshGold(int gold)
    {
        if (goldText != null) goldText.text = $"G {gold}";
    }

    private void RefreshStage(int stage)
    {
        if (stageText != null) stageText.text = $"Stage {stage}";
    }

    private void OnRelicAdded(RelicData relic, bool isPlayer)
    {
        if (!isPlayer || relicIconPrefab == null || relicIconRoot == null) return;

        var icon = Instantiate(relicIconPrefab, relicIconRoot);
        if (relic.icon != null) icon.sprite = relic.icon;
    }
}
