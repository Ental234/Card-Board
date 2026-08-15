using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 보드 페이즈 편성창 — 전투 밖에서 진형을 짠다.
//
// 전투 중 이동(CombatManager.TryMoveFriendly)은 행동력을 쓰고 한 칸씩만 움직이지만,
// 여기서는 비용 없이 임의의 두 칸을 맞바꾼다. 전투 전에 진형을 결정하는 것이 목적이다.
//
// 슬롯 위젯은 런타임에 만든다 — 이동 화살표·의도 배지와 같은 방식이라
// 프리팹 오버라이드가 되돌아가는 함정을 피한다.
public class FormationPanel : MonoBehaviour
{
    [SerializeField] private Transform       slotRoot;
    [SerializeField] private Button          closeButton;
    [SerializeField] private TextMeshProUGUI hintText;

    [Header("슬롯 위젯")]
    [SerializeField] private Vector2 slotSize = new(190f, 240f);
    [SerializeField] private float   slotGap  = 28f;
    [SerializeField] private int     fontSize = 16;   // Neo둥근모는 16px 배수만 쓴다

    // 0 = 아무것도 고르지 않음
    private int selectedSlot;

    private readonly List<GameObject> slotWidgets = new();

    private void Awake()
    {
        // 리스너는 여기서 한 번만 건다. OnEnable에 걸면 창을 여닫을 때마다 쌓인다.
        closeButton?.onClick.AddListener(Close);
    }

    private void OnEnable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnFormationChanged += Rebuild;

        selectedSlot = 0;
        Rebuild();
    }

    private void OnDisable()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnFormationChanged -= Rebuild;

        ClearWidgets();
    }

    public void Close() => UIManager.Instance?.HideFormation();

    // ── 그리기 ──────────────────────────────────────────

    // 전투 화면과 같은 좌우 배치를 쓴다 — 왼쪽이 최후방(슬롯4), 오른쪽이 최전방(슬롯1).
    // 순서를 뒤집으면 편성창에서 짠 그림과 전투 화면이 좌우로 어긋나 헷갈린다.
    private static int SlotForIndex(int index) => 4 - index;

    private void Rebuild()
    {
        ClearWidgets();

        var gm = GameManager.Instance;
        if (gm == null || slotRoot == null) return;

        float totalWidth = 4 * slotSize.x + 3 * slotGap;
        float startX     = -totalWidth * 0.5f + slotSize.x * 0.5f;

        for (int i = 0; i < 4; i++)
        {
            int slot = SlotForIndex(i);
            float x  = startX + i * (slotSize.x + slotGap);

            BuildSlotWidget(slot, new Vector2(x, 0f), gm.GetFormationOccupant(slot));
        }

        RefreshHint();
    }

    private void BuildSlotWidget(int slot, Vector2 position, CombatEntity occupant)
    {
        var go = new GameObject($"FormationSlot_{slot}", typeof(RectTransform));
        go.transform.SetParent(slotRoot, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = position;
        rt.sizeDelta        = slotSize;

        bool isSelected = selectedSlot == slot;

        var img = go.AddComponent<Image>();
        img.color = isSelected
                  ? new Color(0.42f, 0.34f, 0.14f, 0.98f)   // 고른 칸은 밝게
                  : new Color(0.14f, 0.13f, 0.16f, 0.96f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = img;
        int captured = slot;                                  // 클로저가 마지막 값을 잡지 않도록 복사
        button.onClick.AddListener(() => HandleSlotClicked(captured));

        // 슬롯 번호 + 전후방 표기
        AddLabel(go, $"슬롯 {slot}  {PositionName(slot)}",
                 new Vector2(0f, slotSize.y * 0.5f - 22f), new Color(0.75f, 0.72f, 0.62f));

        // 점유자
        string body = occupant == null
                    ? "(빈 칸)"
                    : $"{occupant.EntityName}\nHP {occupant.Stats.CurrentHp}/{occupant.Stats.MaxHp}";

        AddLabel(go, body, Vector2.zero,
                 occupant == null ? new Color(0.45f, 0.45f, 0.45f) : Color.white);

        slotWidgets.Add(go);
    }

    private void AddLabel(GameObject parent, string text, Vector2 offset, Color color)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = offset;
        rt.sizeDelta        = new Vector2(slotSize.x - 16f, 80f);

        var label = go.AddComponent<TextMeshProUGUI>();
        label.text             = text;
        label.color            = color;
        label.fontSize         = fontSize;
        label.alignment        = TextAlignmentOptions.Center;
        label.enableAutoSizing = false;
        label.raycastTarget    = false;      // 슬롯 클릭을 가로채면 안 된다
        label.fontStyle        = FontStyles.Normal;   // 볼드·이탤릭 금지
    }

    private static string PositionName(int slot)
    {
        switch (slot)
        {
            case 1:  return "최전방";
            case 4:  return "최후방";
            default: return "중열";
        }
    }

    // ── 조작 ────────────────────────────────────────────

    private void HandleSlotClicked(int slot)
    {
        // 아무것도 안 고른 상태 → 이 칸을 고른다
        if (selectedSlot == 0)
        {
            // 빈 칸부터 고르면 옮길 대상이 없다
            if (GameManager.Instance.GetFormationOccupant(slot) == null)
            {
                ShowHint("빈 칸입니다. 옮길 유닛을 먼저 고르세요.");
                return;
            }

            selectedSlot = slot;
            Rebuild();
            return;
        }

        // 같은 칸을 다시 누르면 선택 해제
        if (selectedSlot == slot)
        {
            selectedSlot = 0;
            Rebuild();
            return;
        }

        int from = selectedSlot;
        selectedSlot = 0;

        // 성공하면 OnFormationChanged가 Rebuild를 부른다
        if (!GameManager.Instance.TrySwapFormation(from, slot))
        {
            ShowHint("자리를 바꿀 수 없습니다.");
            Rebuild();
        }
    }

    private void RefreshHint()
    {
        ShowHint(selectedSlot == 0
               ? "자리를 바꿀 두 칸을 차례로 누르세요. 행동력은 들지 않습니다."
               : $"슬롯 {selectedSlot} 선택됨 — 옮길 자리를 누르세요.");
    }

    private void ShowHint(string text)
    {
        if (hintText != null) hintText.text = text;
    }

    private void ClearWidgets()
    {
        foreach (var go in slotWidgets)
        {
            if (go == null) continue;

            // Destroy는 프레임 끝에 처리된다. 그냥 두면 새로 만든 위젯과 한 프레임 겹쳐
            // 옛 글자가 비쳐 보이므로, 먼저 꺼서 그리기부터 멈춘다.
            go.SetActive(false);
            Destroy(go);
        }
        slotWidgets.Clear();
    }
}
