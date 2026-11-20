using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 타이틀 화면 — 런 시작 대기 상태
// 누적 메타 포인트를 보여줘 이전 런의 성과가 이어진다는 걸 알린다.
//
// 시작 버튼 뒤의 선택 창(캐릭터 → 추가 시작 렐릭)도 여기서 띄운다 (3단계 7번).
// 고를 게 하나뿐인 단계는 창을 건너뛴다 — 해금 전에는 지금처럼 버튼 한 번에 런이 시작된다.
// 무엇이 열려 있는지는 GameManager.AvailableCharacters / AvailableStartingRelics가 정한다. 이 클래스는 묻고 고르기만 한다
// 창은 런타임 코드로 만든다 (씬 프리팹을 건드리지 않는다 — 오버라이드 함정)
public class MainMenuUI : MonoBehaviour
{
    [Header("텍스트")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI metaPointsText;

    [Header("버튼")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button quitButton;

    private void OnEnable()
    {
        startButton?.onClick.AddListener(OnStartClicked);
        quitButton ?.onClick.AddListener(OnQuitClicked);

        Refresh();
    }

    private void OnDisable()
    {
        startButton?.onClick.RemoveListener(OnStartClicked);
        quitButton ?.onClick.RemoveListener(OnQuitClicked);
        CloseChoiceMenu();
    }

    private void Refresh()
    {
        if (titleText != null) titleText.text = "나의 꿈 4트";

        if (metaPointsText == null) return;

        var meta = MetaProgressionManager.Instance;
        metaPointsText.text = meta != null
                            ? $"누적 메타 포인트 {meta.TotalMetaPoints}"
                            : "누적 메타 포인트 0";
    }

    // ── 버튼 ────────────────────────────────────────────

    private void OnStartClicked()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        var characters = gm.AvailableCharacters;
        if (characters.Count == 0) { gm.StartRun(); return; }   // 기본 캐릭터 미지정 — StartRun이 에러를 낸다
        if (characters.Count == 1) { ChooseStartingRelic(characters[0]); return; }

        var menu = OpenChoiceMenu("캐릭터를 고른다");
        foreach (var c in characters)
        {
            var captured = c;
            var btn = AddChoiceButton(menu, c.characterName, () => ChooseStartingRelic(captured));
            TooltipTrigger.Attach(btn, c.characterName, c.description);
        }
    }

    // 해금된 시작 렐릭이 하나뿐이면 묻지 않고 준다 — 고를 수 없는 선택 창은 클릭만 하나 늘린다
    private void ChooseStartingRelic(CharacterData character)
    {
        CloseChoiceMenu();
        var gm     = GameManager.Instance;
        var relics = gm.AvailableStartingRelics;

        if (relics.Count == 0) { gm.StartRun(character);            return; }
        if (relics.Count == 1) { gm.StartRun(character, relics[0]); return; }

        var menu = OpenChoiceMenu("시작 렐릭을 하나 고른다");
        foreach (var r in relics)
        {
            var captured = r;
            var btn = AddChoiceButton(menu, RarityTable.Colorize(r.relicName, r.rarity), () =>
            {
                CloseChoiceMenu();
                gm.StartRun(character, captured);
            });
            TooltipTrigger.Attach(btn, RarityTable.Colorize(r.relicName, r.rarity), r.description);
        }
    }

    // ── 선택 창 ─────────────────────────────────────────

    [NonSerialized] private GameObject choiceMenu;

    private GameObject OpenChoiceMenu(string title)
    {
        CloseChoiceMenu();

        // 화면 전체를 덮는 막 — 없으면 뒤의 시작·종료 버튼이 창 밖으로 비치고 그대로 눌린다
        choiceMenu = new GameObject("RunSetupMenu", typeof(RectTransform));
        choiceMenu.transform.SetParent(transform, false);
        choiceMenu.transform.SetAsLastSibling();

        var blockerRt = choiceMenu.GetComponent<RectTransform>();
        blockerRt.anchorMin = Vector2.zero;
        blockerRt.anchorMax = Vector2.one;
        blockerRt.offsetMin = blockerRt.offsetMax = Vector2.zero;
        choiceMenu.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.75f);

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(choiceMenu.transform, false);

        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);

        var bg = panel.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.07f, 0.06f, 0.96f);

        var layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding                = new RectOffset(24, 24, 16, 16);
        layout.spacing                = 8f;
        layout.childAlignment         = TextAnchor.UpperCenter;
        layout.childControlWidth      = true;
        layout.childControlHeight     = true;
        layout.childForceExpandWidth  = true;
        layout.childForceExpandHeight = false;

        // 선택지 수에 맞춰 창 높이가 늘어난다
        var fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        rt.sizeDelta = new Vector2(400f, 0f);

        var titleGo = new GameObject("Title", typeof(RectTransform));
        titleGo.transform.SetParent(panel.transform, false);
        titleGo.AddComponent<LayoutElement>().preferredHeight = 40f;
        CreateTmp(titleGo, title).color = new Color(1f, 0.9f, 0.6f);

        // 뒤로 — 시작 버튼을 잘못 눌렀을 때 메뉴로 돌아갈 길
        AddChoiceButton(panel, "돌아가기", CloseChoiceMenu, last: true);
        return panel;
    }

    private GameObject AddChoiceButton(GameObject menu, string label, Action onClick, bool last = false)
    {
        var go = new GameObject(last ? "BackButton" : "ChoiceButton", typeof(RectTransform));
        go.transform.SetParent(menu.transform, false);
        go.AddComponent<LayoutElement>().preferredHeight = 44f;

        // 돌아가기는 항상 맨 아래 — 선택지는 그 앞에 끼워 넣는다
        var back = menu.transform.Find("BackButton");
        if (!last && back != null) go.transform.SetSiblingIndex(back.GetSiblingIndex());

        var img = go.AddComponent<Image>();
        img.color = last ? new Color(0.20f, 0.18f, 0.16f) : new Color(0.30f, 0.25f, 0.18f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());

        var textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(go.transform, false);
        var trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        CreateTmp(textGo, label);
        return go;
    }

    private void CloseChoiceMenu()
    {
        if (choiceMenu != null) Destroy(choiceMenu);
        choiceMenu = null;
    }

    // Neo둥근모는 16px 배수만 — 메뉴의 기존 텍스트와 같은 폰트를 빌려 쓴다
    private TextMeshProUGUI CreateTmp(GameObject go, string text)
    {
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text          = text;
        tmp.fontSize      = 16;
        tmp.fontStyle     = FontStyles.Normal;
        tmp.alignment     = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        tmp.color         = Color.white;
        var font = metaPointsText != null ? metaPointsText.font : null;
        if (font != null) { tmp.font = font; tmp.fontSharedMaterial = font.material; }
        return tmp;
    }

    private void OnQuitClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
