using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 호버 툴팁 표시기 — 화면에 하나만 있다.
//
// 대상이 무엇이든(렐릭·포션·상태이상·인텐트·노드·동료·상점 아이템) "제목 / 본문"만 받아 띄운다.
// 붙이는 쪽은 TooltipTrigger를 쓴다. 이 클래스는 직접 부를 일이 거의 없다.
//
// 씬·프리팹을 건드리지 않고 처음 쓰일 때 런타임에 만든다 (프리팹 오버라이드 함정 회피).
// 루트 캔버스 맨 위에 별도 정렬 캔버스로 올려, 어떤 패널보다 앞에 그린다.
public class TooltipView : MonoBehaviour
{
    private static TooltipView instance;

    private const float DefaultWidth = 340f;
    private const float Margin      = 8f;     // 대상과 툴팁 사이 간격
    private const float ScreenPad   = 6f;     // 화면 가장자리 여유
    private const float ShowDelay   = 0.12f;  // 칩 위를 스쳐 지나갈 때 깜빡이지 않게

    private RectTransform   rect;
    private RectTransform   canvasRect;
    private Canvas          rootCanvas;
    private TextMeshProUGUI titleText;
    private TextMeshProUGUI bodyText;

    private RectTransform   pendingTarget;
    private float           pendingSince;
    private Object          owner;          // 지금 툴팁을 띄운 트리거 — 다른 트리거가 실수로 닫지 않게

    // ── 외부 API ────────────────────────────────────────

    public static void Show(Object requester, RectTransform target, string title, string body, float width = 0f)
    {
        if (target == null) return;

        var view = Ensure(target);
        if (view == null) return;

        view.owner = requester;
        view.rect.sizeDelta = new Vector2(width > 0f ? width : DefaultWidth, view.rect.sizeDelta.y);
        view.titleText.text = title ?? "";
        view.titleText.gameObject.SetActive(!string.IsNullOrEmpty(title));
        view.bodyText.text  = body ?? "";
        view.bodyText.gameObject.SetActive(!string.IsNullOrEmpty(body));

        view.pendingTarget = target;
        view.pendingSince  = Time.unscaledTime;
        view.gameObject.SetActive(true);
        view.SetVisible(false);   // 지연이 지나면 위치를 잡고 보인다
    }

    public static void Hide(Object requester)
    {
        if (instance == null || instance.owner != requester) return;
        instance.HideNow();
    }

    // ── 생성 ────────────────────────────────────────────

    private static TooltipView Ensure(RectTransform anyUi)
    {
        if (instance != null) return instance;

        var canvas = anyUi.GetComponentInParent<Canvas>();
        if (canvas == null) return null;
        canvas = canvas.rootCanvas;

        var go = new GameObject("Tooltip", typeof(RectTransform));
        go.transform.SetParent(canvas.transform, false);

        // 별도 정렬 캔버스 — 형제 순서와 무관하게 모든 패널보다 앞에 그린다
        var sub = go.AddComponent<Canvas>();
        sub.overrideSorting = true;
        sub.sortingOrder    = 5000;

        // 툴팁이 마우스를 가로채면 대상에서 포인터가 빠져나간 것으로 처리돼 깜빡인다
        var group = go.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable   = false;

        var view = go.AddComponent<TooltipView>();
        view.rootCanvas = canvas;
        view.canvasRect = canvas.GetComponent<RectTransform>();
        view.Build();

        instance = view;
        go.SetActive(false);
        return view;
    }

    private void Build()
    {
        rect = GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot     = new Vector2(0f, 1f);

        var bg = gameObject.AddComponent<Image>();
        bg.color         = new Color(0.07f, 0.06f, 0.05f, 0.96f);
        bg.raycastTarget = false;

        var outline = gameObject.AddComponent<Outline>();
        outline.effectColor    = new Color(0.72f, 0.62f, 0.38f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);

        var layout = gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding                = new RectOffset(12, 12, 10, 10);
        layout.spacing                = 6f;
        layout.childControlWidth      = true;
        layout.childControlHeight     = true;
        layout.childForceExpandWidth  = true;
        layout.childForceExpandHeight = false;

        var fitter = gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        rect.sizeDelta = new Vector2(DefaultWidth, 0f);

        var font = FindFont();
        titleText = CreateText("Title", font, new Color(1f, 0.86f, 0.50f));
        bodyText  = CreateText("Body",  font, new Color(0.92f, 0.90f, 0.85f));
    }

    private TextMeshProUGUI CreateText(string name, TMP_FontAsset font, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize           = 16;                   // Neo둥근모는 16px 배수만 쓴다
        tmp.fontStyle          = FontStyles.Normal;    // 볼드·이탤릭 금지
        tmp.color              = color;
        tmp.alignment          = TextAlignmentOptions.TopLeft;
        tmp.textWrappingMode   = TextWrappingModes.Normal;
        tmp.enableAutoSizing   = false;
        tmp.raycastTarget      = false;
        if (font != null) { tmp.font = font; tmp.fontSharedMaterial = font.material; }
        return tmp;
    }

    // 게임이 쓰는 한글 폰트(Neo둥근모)를 그대로 빌린다 — 기본 TMP 폰트에는 한글이 없다
    private TMP_FontAsset FindFont()
    {
        foreach (var t in rootCanvas.GetComponentsInChildren<TextMeshProUGUI>(true))
            if (t.font != null) return t.font;
        return TMP_Settings.defaultFontAsset;
    }

    // ── 표시 ────────────────────────────────────────────

    private void Update()
    {
        if (pendingTarget == null || !pendingTarget.gameObject.activeInHierarchy)
        {
            // 대상이 사라졌다(칩 재생성·패널 닫힘) — 떠 있는 툴팁이 남지 않게 닫는다
            HideNow();
            return;
        }

        if (Time.unscaledTime - pendingSince < ShowDelay) return;

        Place(pendingTarget);
        SetVisible(true);
    }

    // 대상 아래에 띄우고, 화면 밖으로 나가면 위로 뒤집는다. 좌우는 화면 안으로 민다.
    private void Place(RectTransform target)
    {
        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

        Camera cam = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;

        var corners = new Vector3[4];   // 0 좌하, 1 좌상, 2 우상, 3 우하
        target.GetWorldCorners(corners);

        Vector2 ToLocal(Vector3 world)
        {
            var screen = RectTransformUtility.WorldToScreenPoint(cam, world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, cam, out var local);
            return local;
        }

        Vector2 bottomLeft = ToLocal(corners[0]);
        Vector2 topLeft    = ToLocal(corners[1]);
        Vector2 topRight   = ToLocal(corners[2]);

        float w = rect.rect.width;
        float h = rect.rect.height;
        var   half = canvasRect.rect.size * 0.5f;

        // 대상 가로 중앙에 맞춘다
        float x = (topLeft.x + topRight.x) * 0.5f - w * 0.5f;
        float y = bottomLeft.y - Margin;                 // 아래쪽 (피벗이 좌상단)

        if (y - h < -half.y + ScreenPad)                  // 아래로 넘친다 → 위로
            y = topLeft.y + Margin + h;

        x = Mathf.Clamp(x, -half.x + ScreenPad, half.x - ScreenPad - w);
        y = Mathf.Clamp(y, -half.y + ScreenPad + h, half.y - ScreenPad);

        rect.anchoredPosition = new Vector2(x, y);
        rect.SetAsLastSibling();
    }

    private void SetVisible(bool visible)
    {
        var group = GetComponent<CanvasGroup>();
        if (group != null) group.alpha = visible ? 1f : 0f;
    }

    private void HideNow()
    {
        pendingTarget = null;
        owner         = null;
        gameObject.SetActive(false);
    }
}
