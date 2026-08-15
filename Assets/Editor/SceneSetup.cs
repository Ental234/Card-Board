using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 메뉴: Tools > 나의꿈4트 > Setup Scene
public static class SceneSetup
{
    [MenuItem("Tools/나의꿈4트/Setup Scene")]
    public static void SetupScene()
    {
        // ── 매니저 루트 ──────────────────────────────────────
        var managers = GetOrCreate("--- Managers ---");

        var gmGo   = GetOrCreateChild(managers, "GameManager");
        var bpmGo  = GetOrCreateChild(managers, "BoardPhaseManager");
        var cmGo   = GetOrCreateChild(managers, "CombatManager");
        var ssGo   = GetOrCreateChild(managers, "SlotSystem");
        var emGo   = GetOrCreateChild(managers, "EventManager");
        var smGo   = GetOrCreateChild(managers, "ShopManager");
        var rmGo   = GetOrCreateChild(managers, "RelicManager");
        var metaGo = GetOrCreateChild(managers, "MetaProgressionManager");

        EnsureComponent<GameManager>(gmGo);
        EnsureComponent<BoardPhaseManager>(bpmGo);
        EnsureComponent<CombatManager>(cmGo);
        EnsureComponent<SlotSystem>(ssGo);
        EnsureComponent<EventManager>(emGo);
        EnsureComponent<ShopManager>(smGo);
        EnsureComponent<RelicManager>(rmGo);
        EnsureComponent<MetaProgressionManager>(metaGo);

        // ── UI 루트 Canvas ───────────────────────────────────
        var canvasGo = GetOrCreate("Canvas");
        var canvas   = EnsureComponent<Canvas>(canvasGo);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        EnsureComponent<CanvasScaler>(canvasGo);
        EnsureComponent<GraphicRaycaster>(canvasGo);

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;

        // EventSystem
        if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var esGo = new GameObject("EventSystem");
            esGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
            esGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // ── HUD (항상 표시) ──────────────────────────────────
        var hudGo = GetOrCreateChildUI(canvasGo, "HUDPanel");
        EnsureComponent<HUDPanel>(hudGo);
        SetFullStretch(hudGo);

        var hpSliderGo   = GetOrCreateChildUI(hudGo, "HP_Slider");
        EnsureComponent<Slider>(hpSliderGo);
        SetAnchoredPos(hpSliderGo, -700, 500, 250, 30);

        var hpTextGo = GetOrCreateChildUI(hudGo, "HP_Text");
        EnsureComponent<TextMeshProUGUI>(hpTextGo);
        SetAnchoredPos(hpTextGo, -700, 465, 200, 25);
        SetTmpText(hpTextGo, "HP 100/100");

        var goldTextGo = GetOrCreateChildUI(hudGo, "Gold_Text");
        EnsureComponent<TextMeshProUGUI>(goldTextGo);
        SetAnchoredPos(goldTextGo, -500, 500, 150, 30);
        SetTmpText(goldTextGo, "G 0");

        var stageTextGo = GetOrCreateChildUI(hudGo, "Stage_Text");
        EnsureComponent<TextMeshProUGUI>(stageTextGo);
        SetAnchoredPos(stageTextGo, 0, 500, 200, 30);
        SetTmpText(stageTextGo, "Stage 1");

        var relicIconRootGo = GetOrCreateChildUI(hudGo, "RelicIconRoot");
        SetAnchoredPos(relicIconRootGo, 0, 460, 600, 40);
        var relicHLG = EnsureComponent<HorizontalLayoutGroup>(relicIconRootGo);
        relicHLG.spacing = 5;

        // HUDPanel 참조 연결
        var hudPanel = hudGo.GetComponent<HUDPanel>();
        SetSerializedField(hudPanel, "hpSlider",      hpSliderGo.GetComponent<Slider>());
        SetSerializedField(hudPanel, "hpText",        hpTextGo.GetComponent<TextMeshProUGUI>());
        SetSerializedField(hudPanel, "goldText",      goldTextGo.GetComponent<TextMeshProUGUI>());
        SetSerializedField(hudPanel, "stageText",     stageTextGo.GetComponent<TextMeshProUGUI>());
        SetSerializedField(hudPanel, "relicIconRoot", relicIconRootGo.transform);

        // ── Board Panel ──────────────────────────────────────
        var boardGo = GetOrCreateChildUI(canvasGo, "BoardPanel");
        SetFullStretch(boardGo);

        var mapRootGo = GetOrCreateChildUI(boardGo, "MapRoot");
        SetAnchoredPos(mapRootGo, 0, 0, 700, 700);
        EnsureComponent<BoardMapView>(boardGo);

        var playerIconGo = GetOrCreateChildUI(boardGo, "PlayerIcon");
        SetAnchoredPos(playerIconGo, 0, 0, 30, 30);
        var playerImg = EnsureComponent<Image>(playerIconGo);
        playerImg.color = new Color(0.3f, 0.8f, 0.3f);

        var bossIconGo = GetOrCreateChildUI(boardGo, "BossIcon");
        SetAnchoredPos(bossIconGo, 0, 0, 30, 30);
        var bossImg = EnsureComponent<Image>(bossIconGo);
        bossImg.color = new Color(0.9f, 0.2f, 0.2f);

        // BoardMapView 참조 연결
        var bmv = boardGo.GetComponent<BoardMapView>();
        SetSerializedField(bmv, "boardPhaseManager", bpmGo.GetComponent<BoardPhaseManager>());
        SetSerializedField(bmv, "mapRoot",           mapRootGo.GetComponent<RectTransform>());
        SetSerializedField(bmv, "playerIcon",        playerIconGo.GetComponent<RectTransform>());
        SetSerializedField(bmv, "bossIcon",          bossIconGo.GetComponent<RectTransform>());

        boardGo.SetActive(false);

        // ── Combat Panel ─────────────────────────────────────
        var combatGo = GetOrCreateChildUI(canvasGo, "CombatPanel");
        SetFullStretch(combatGo);
        EnsureComponent<CombatPanel>(combatGo);

        // SlotGrid
        var slotGridGo = GetOrCreateChildUI(combatGo, "SlotGrid");
        SetAnchoredPos(slotGridGo, 0, 50, 1100, 300);
        EnsureComponent<CombatSlotGridUI>(slotGridGo);

        // Hand UI
        var handGo = GetOrCreateChildUI(combatGo, "HandUI");
        SetAnchoredPos(handGo, 0, -400, 1200, 200);
        EnsureComponent<HandUI>(handGo);

        // Energy Text
        var energyGo = GetOrCreateChildUI(combatGo, "Energy_Text");
        EnsureComponent<TextMeshProUGUI>(energyGo);
        SetAnchoredPos(energyGo, -550, -350, 100, 40);
        SetTmpText(energyGo, "3/3");

        // AP Text
        var apGo = GetOrCreateChildUI(combatGo, "AP_Text");
        EnsureComponent<TextMeshProUGUI>(apGo);
        SetAnchoredPos(apGo, -650, -350, 120, 40);
        SetTmpText(apGo, "AP 3/3");

        // End Turn Button
        var endTurnGo  = GetOrCreateChildUI(combatGo, "EndTurn_Button");
        SetAnchoredPos(endTurnGo, 700, -420, 160, 50);
        var endTurnBtn = EnsureComponent<Button>(endTurnGo);
        var endTurnImg = EnsureComponent<Image>(endTurnGo);
        endTurnImg.color = new Color(0.8f, 0.4f, 0.2f);
        var endTurnLabelGo = GetOrCreateChildUI(endTurnGo, "Label");
        EnsureComponent<TextMeshProUGUI>(endTurnLabelGo);
        SetTmpText(endTurnLabelGo, "턴 종료");

        // CombatSlotGridUI 참조
        var slotGridUI = slotGridGo.GetComponent<CombatSlotGridUI>();
        SetSerializedField(slotGridUI, "slotSystem", ssGo.GetComponent<SlotSystem>());

        // CombatPanel 참조
        var combatPanel = combatGo.GetComponent<CombatPanel>();
        SetSerializedField(combatPanel, "combatManager",  cmGo.GetComponent<CombatManager>());
        SetSerializedField(combatPanel, "slotSystem",     ssGo.GetComponent<SlotSystem>());
        SetSerializedField(combatPanel, "slotGridUI",     slotGridUI);
        SetSerializedField(combatPanel, "handUI",         handGo.GetComponent<HandUI>());
        SetSerializedField(combatPanel, "energyText",     energyGo.GetComponent<TextMeshProUGUI>());
        SetSerializedField(combatPanel, "actionPointText",apGo.GetComponent<TextMeshProUGUI>());
        SetSerializedField(combatPanel, "endTurnButton",  endTurnBtn);

        combatGo.SetActive(false);

        // ── Event Panel ──────────────────────────────────────
        var eventGo = GetOrCreateChildUI(canvasGo, "EventPanel");
        SetAnchoredPos(eventGo, 0, 0, 600, 500);
        EnsureComponent<Image>(eventGo).color = new Color(0.1f, 0.1f, 0.15f, 0.95f);
        EnsureComponent<EventUI>(eventGo);

        var evTitleGo = GetOrCreateChildUI(eventGo, "Title_Text");
        EnsureComponent<TextMeshProUGUI>(evTitleGo);
        SetAnchoredPos(evTitleGo, 0, 180, 540, 50);
        SetTmpText(evTitleGo, "이벤트 제목");

        var evDescGo = GetOrCreateChildUI(eventGo, "Desc_Text");
        EnsureComponent<TextMeshProUGUI>(evDescGo);
        SetAnchoredPos(evDescGo, 0, 80, 540, 120);
        SetTmpText(evDescGo, "이벤트 설명");

        var choiceRootGo = GetOrCreateChildUI(eventGo, "ChoiceRoot");
        SetAnchoredPos(choiceRootGo, 0, -100, 540, 200);
        var choiceVLG = EnsureComponent<VerticalLayoutGroup>(choiceRootGo);
        choiceVLG.spacing = 10;
        choiceVLG.childControlHeight = true;
        choiceVLG.childForceExpandWidth = true;

        // EventUI 참조
        var eventUI = eventGo.GetComponent<EventUI>();
        SetSerializedField(eventUI, "eventTitleText",   evTitleGo.GetComponent<TextMeshProUGUI>());
        SetSerializedField(eventUI, "eventDescText",    evDescGo.GetComponent<TextMeshProUGUI>());
        SetSerializedField(eventUI, "choiceButtonRoot", choiceRootGo.transform);

        eventGo.SetActive(false);

        // ── Shop Panel ───────────────────────────────────────
        var shopGo = GetOrCreateChildUI(canvasGo, "ShopPanel");
        SetFullStretch(shopGo);
        EnsureComponent<Image>(shopGo).color = new Color(0.08f, 0.08f, 0.12f, 0.97f);
        EnsureComponent<ShopUI>(shopGo);

        var cardSecGo      = GetOrCreateChildUI(shopGo, "CardSection");
        var relicSecGo     = GetOrCreateChildUI(shopGo, "RelicSection");
        var potionSecGo    = GetOrCreateChildUI(shopGo, "PotionSection");
        var companionSecGo = GetOrCreateChildUI(shopGo, "CompanionSection");
        var bossItemSecGo  = GetOrCreateChildUI(shopGo, "BossItemSection");

        SetAnchoredPos(cardSecGo,      -700, 0, 280, 600);
        SetAnchoredPos(relicSecGo,     -350, 0, 280, 600);
        SetAnchoredPos(potionSecGo,        0, 0, 280, 600);
        SetAnchoredPos(companionSecGo,   350, 0, 280, 600);
        SetAnchoredPos(bossItemSecGo,    700, 0, 280, 600);

        foreach (var sec in new[] { cardSecGo, relicSecGo, potionSecGo, companionSecGo, bossItemSecGo })
        {
            var vlg = EnsureComponent<VerticalLayoutGroup>(sec);
            vlg.spacing = 8;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
        }

        var shopCloseGo = GetOrCreateChildUI(shopGo, "Close_Button");
        SetAnchoredPos(shopCloseGo, 850, 480, 80, 40);
        EnsureComponent<Button>(shopCloseGo);
        EnsureComponent<Image>(shopCloseGo).color = new Color(0.7f, 0.2f, 0.2f);
        var shopCloseLbl = GetOrCreateChildUI(shopCloseGo, "Label");
        EnsureComponent<TextMeshProUGUI>(shopCloseLbl);
        SetTmpText(shopCloseLbl, "닫기");

        var removeCardBtnGo = GetOrCreateChildUI(shopGo, "RemoveCard_Button");
        SetAnchoredPos(removeCardBtnGo, 0, -460, 200, 40);
        EnsureComponent<Button>(removeCardBtnGo);
        EnsureComponent<Image>(removeCardBtnGo).color = new Color(0.5f, 0.2f, 0.7f);
        var removeLbl = GetOrCreateChildUI(removeCardBtnGo, "Label");
        EnsureComponent<TextMeshProUGUI>(removeLbl);
        SetTmpText(removeLbl, "카드 제거");

        var removePriceTGo = GetOrCreateChildUI(shopGo, "RemoveCardPrice_Text");
        EnsureComponent<TextMeshProUGUI>(removePriceTGo);
        SetAnchoredPos(removePriceTGo, 110, -460, 80, 40);
        SetTmpText(removePriceTGo, "75G");

        // ShopUI 참조
        var shopUI = shopGo.GetComponent<ShopUI>();
        SetSerializedField(shopUI, "cardSectionRoot",      cardSecGo.transform);
        SetSerializedField(shopUI, "relicSectionRoot",     relicSecGo.transform);
        SetSerializedField(shopUI, "potionSectionRoot",    potionSecGo.transform);
        SetSerializedField(shopUI, "companionSectionRoot", companionSecGo.transform);
        SetSerializedField(shopUI, "bossItemSectionRoot",  bossItemSecGo.transform);
        SetSerializedField(shopUI, "removeCardButton",     removeCardBtnGo.GetComponent<Button>());
        SetSerializedField(shopUI, "removeCardPriceText",  removePriceTGo.GetComponent<TextMeshProUGUI>());
        SetSerializedField(shopUI, "closeButton",          shopCloseGo.GetComponent<Button>());

        shopGo.SetActive(false);

        // ── Run End Panel ────────────────────────────────────
        var runEndGo = GetOrCreateChildUI(canvasGo, "RunEndPanel");
        SetFullStretch(runEndGo);
        EnsureComponent<Image>(runEndGo).color = new Color(0f, 0f, 0f, 0.85f);
        EnsureComponent<RunEndUI>(runEndGo);

        var resultTGo = GetOrCreateChildUI(runEndGo, "Result_Text");
        EnsureComponent<TextMeshProUGUI>(resultTGo);
        SetAnchoredPos(resultTGo, 0, 150, 400, 80);
        SetTmpText(resultTGo, "Victory!");

        var metaTGo = GetOrCreateChildUI(runEndGo, "MetaPoints_Text");
        EnsureComponent<TextMeshProUGUI>(metaTGo);
        SetAnchoredPos(metaTGo, 0, 50, 400, 50);
        SetTmpText(metaTGo, "Meta Points: 0");

        var restartGo = GetOrCreateChildUI(runEndGo, "Restart_Button");
        SetAnchoredPos(restartGo, -120, -100, 200, 60);
        EnsureComponent<Button>(restartGo);
        EnsureComponent<Image>(restartGo).color = new Color(0.2f, 0.7f, 0.3f);
        var restartLbl = GetOrCreateChildUI(restartGo, "Label");
        EnsureComponent<TextMeshProUGUI>(restartLbl);
        SetTmpText(restartLbl, "다시 시작");

        var quitGo = GetOrCreateChildUI(runEndGo, "Quit_Button");
        SetAnchoredPos(quitGo, 120, -100, 200, 60);
        EnsureComponent<Button>(quitGo);
        EnsureComponent<Image>(quitGo).color = new Color(0.7f, 0.2f, 0.2f);
        var quitLbl = GetOrCreateChildUI(quitGo, "Label");
        EnsureComponent<TextMeshProUGUI>(quitLbl);
        SetTmpText(quitLbl, "종료");

        // RunEndUI 참조
        var runEndUI = runEndGo.GetComponent<RunEndUI>();
        SetSerializedField(runEndUI, "resultText",      resultTGo.GetComponent<TextMeshProUGUI>());
        SetSerializedField(runEndUI, "metaPointsText",  metaTGo.GetComponent<TextMeshProUGUI>());
        SetSerializedField(runEndUI, "restartButton",   restartGo.GetComponent<Button>());
        SetSerializedField(runEndUI, "quitButton",      quitGo.GetComponent<Button>());

        runEndGo.SetActive(false);

        // ── Card Detail Popup ────────────────────────────────
        var popupGo = GetOrCreateChildUI(canvasGo, "CardDetailPopup");
        SetAnchoredPos(popupGo, 0, 0, 220, 280);
        EnsureComponent<Image>(popupGo).color = new Color(0.05f, 0.05f, 0.1f, 0.97f);

        var popupNameGo = GetOrCreateChildUI(popupGo, "CardName_Text");
        EnsureComponent<TextMeshProUGUI>(popupNameGo);
        SetAnchoredPos(popupNameGo, 0, 100, 200, 35);
        SetTmpText(popupNameGo, "카드명");

        var popupDescGo = GetOrCreateChildUI(popupGo, "Desc_Text");
        EnsureComponent<TextMeshProUGUI>(popupDescGo);
        SetAnchoredPos(popupDescGo, 0, 0, 200, 120);
        SetTmpText(popupDescGo, "설명");

        var popupCostGo = GetOrCreateChildUI(popupGo, "Cost_Text");
        EnsureComponent<TextMeshProUGUI>(popupCostGo);
        SetAnchoredPos(popupCostGo, 0, -100, 200, 30);
        SetTmpText(popupCostGo, "Cost: 1");

        popupGo.SetActive(false);

        // ── UIManager ────────────────────────────────────────
        var uiManagerGo = GetOrCreateChild(managers, "UIManager");
        EnsureComponent<UIManager>(uiManagerGo);
        var uiManager = uiManagerGo.GetComponent<UIManager>();
        SetSerializedField(uiManager, "boardPanel",       boardGo);
        SetSerializedField(uiManager, "combatPanel",      combatGo);
        SetSerializedField(uiManager, "eventPanel",       eventGo);
        SetSerializedField(uiManager, "shopPanel",        shopGo);
        SetSerializedField(uiManager, "runEndPanel",      runEndGo);
        SetSerializedField(uiManager, "hudPanel",         hudGo);
        SetSerializedField(uiManager, "cardDetailPopup",  popupGo);
        SetSerializedField(uiManager, "popupCardName",    popupNameGo.GetComponent<TextMeshProUGUI>());
        SetSerializedField(uiManager, "popupDescription", popupDescGo.GetComponent<TextMeshProUGUI>());
        SetSerializedField(uiManager, "popupCost",        popupCostGo.GetComponent<TextMeshProUGUI>());
        SetSerializedField(uiManager, "boardMapView",     boardGo.GetComponent<BoardMapView>());

        // ── GameManager 참조 ─────────────────────────────────
        var gm = gmGo.GetComponent<GameManager>();
        SetSerializedField(gm, "boardPhaseManager",      bpmGo.GetComponent<BoardPhaseManager>());
        SetSerializedField(gm, "combatManager",          cmGo.GetComponent<CombatManager>());
        SetSerializedField(gm, "slotSystem",             ssGo.GetComponent<SlotSystem>());
        SetSerializedField(gm, "metaProgressionManager", metaGo.GetComponent<MetaProgressionManager>());

        // ── CombatManager 참조 ───────────────────────────────
        var cm = cmGo.GetComponent<CombatManager>();
        SetSerializedField(cm, "slotSystem", ssGo.GetComponent<SlotSystem>());

        // 씬 저장
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());

        Debug.Log("=== 씬 셋업 완료! Ctrl+S로 저장하세요. ===");
        EditorUtility.DisplayDialog("완료", "씬 셋업이 완료됐습니다!\nCtrl+S로 저장하세요.", "확인");
    }

    // ── 유틸 ─────────────────────────────────────────────────

    static GameObject GetOrCreate(string name)
    {
        var go = GameObject.Find(name);
        if (go == null) go = new GameObject(name);
        return go;
    }

    static GameObject GetOrCreateChild(GameObject parent, string name)
    {
        var t = parent.transform.Find(name);
        if (t != null) return t.gameObject;
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    static GameObject GetOrCreateChildUI(GameObject parent, string name)
    {
        var t = parent.transform.Find(name);
        if (t != null) return t.gameObject;
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    static T EnsureComponent<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null) c = go.AddComponent<T>();
        return c;
    }

    static void SetFullStretch(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void SetAnchoredPos(GameObject go, float x, float y, float w, float h)
    {
        var rt = go.GetComponent<RectTransform>();
        if (rt == null) rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta        = new Vector2(w, h);
    }

    static void SetTmpText(GameObject go, string text)
    {
        var tmp = go.GetComponent<TextMeshProUGUI>();
        if (tmp == null) return;
        tmp.text      = text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize  = 18;
    }

    static void SetSerializedField(Object target, string fieldName, Object value)
    {
        if (target == null || value == null) return;
        var so   = new SerializedObject(target);
        var prop = so.FindProperty(fieldName);
        if (prop != null)
        {
            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }
        else
        {
            Debug.LogWarning($"[SceneSetup] 필드 '{fieldName}' 를 {target.GetType().Name} 에서 찾지 못했습니다.");
        }
    }
}
