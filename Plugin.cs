using System.Linq;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace UnderveilModLoader
{
    [BepInPlugin("underveil.modloader", "Underveil Mod Loader", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            LocalizationHelper.OnLocaleChanged += TitleModsButton.RefreshLabels;
            new Harmony("underveil.modloader").PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("Loaded");
            ModLoader.LoadAll(Log);
        }
    }

    [HarmonyPatch(typeof(GameManager), "Start")]
    internal static class GameManagerStartPatch
    {
        private static void Postfix(GameManager __instance) => TitleModsButton.Inject(__instance);
    }

    // GameManager.Update opens the pause menu on Esc and the deck/map on D/M, so it must not run behind the window.
    [HarmonyPatch(typeof(GameManager), "Update")]
    internal static class GameManagerUpdatePatch
    {
        private static bool Prefix()
        {
            if (!TitleModsButton.IsWindowOpen)
            {
                return true;
            }
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                TitleModsButton.CloseWindow();
            }
            return false;
        }
    }

    internal static class TitleModsButton
    {
        private const string OpenOptionMethod = "OpenTitleOption";

        private static Button optionButton;
        private static TMP_Text buttonLabel;
        private static TMP_Text windowTitle;
        private static TMP_Text closeLabel;
        private static GameObject window;
        private static RectTransform listContent;

        internal static bool IsWindowOpen => window != null && window.activeSelf;

        private static bool IsKorean => LocalizationHelper.CurrentLocaleCode.StartsWith("ko");
        private static string ModsText => IsKorean ? "모드" : "Mods";
        private static string CloseText => IsKorean ? "닫기" : "Close";

        internal static void Inject(GameManager game)
        {
            optionButton = game.titleGo.GetComponentsInChildren<Button>(true).FirstOrDefault(IsOptionButton);
            if (optionButton == null)
            {
                Plugin.Log.LogError($"Title button bound to {OpenOptionMethod} not found");
                return;
            }
            LogLayout(optionButton.transform);

            Button modsButton = CloneButton(optionButton, optionButton.transform.parent, "ModsButton", out buttonLabel);
            modsButton.onClick.AddListener(OpenWindow);
            modsButton.transform.SetSiblingIndex(optionButton.transform.GetSiblingIndex());
            RefreshLabels();
        }

        internal static void RefreshLabels()
        {
            if (buttonLabel != null) buttonLabel.text = ModsText;
            if (windowTitle != null) windowTitle.text = ModsText;
            if (closeLabel != null) closeLabel.text = CloseText;
        }

        internal static void CloseWindow()
        {
            window.SetActive(false);
            SoundManager.ins.PlaySFX(SFXType.Click);
        }

        private static void OpenWindow()
        {
            Plugin.Log.LogInfo("Mods button clicked");
            if (window == null)
            {
                BuildWindow();
            }
            RefreshList();
            window.SetActive(true);
            SoundManager.ins.PlaySFX(SFXType.OpenPopup);
        }

        private static bool IsOptionButton(Button button)
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentMethodName(i) == OpenOptionMethod)
                {
                    return true;
                }
            }
            return false;
        }

        private static Button CloneButton(Button source, Transform parent, string name, out TMP_Text label)
        {
            GameObject clone = Object.Instantiate(source.gameObject, parent);
            clone.name = name;
            foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
            {
                string typeName = behaviour.GetType().Name;
                if (typeName == "LocalizeStringEvent" || typeName == "DisplayText" || typeName == "LocalizedKeywordText")
                {
                    Object.DestroyImmediate(behaviour);
                }
            }
            Button button = clone.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            label = clone.GetComponentInChildren<TMP_Text>(true);
            return button;
        }

        private static void BuildWindow()
        {
            window = new GameObject("ModsWindow", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = window.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 30000;
            CanvasScaler scaler = window.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform dim = NewRect("Dim", window.transform);
            dim.anchorMin = Vector2.zero;
            dim.anchorMax = Vector2.one;
            dim.offsetMin = Vector2.zero;
            dim.offsetMax = Vector2.zero;
            dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.7f);

            RectTransform panel = NewRect("Panel", dim);
            panel.sizeDelta = new Vector2(1000f, 650f);
            panel.gameObject.AddComponent<Image>().color = new Color(0.09f, 0.08f, 0.11f, 0.98f);

            RectTransform titleRect = NewRect("Title", panel);
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(1f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -55f);
            titleRect.sizeDelta = new Vector2(0f, 70f);
            windowTitle = titleRect.gameObject.AddComponent<TextMeshProUGUI>();
            windowTitle.font = buttonLabel.font;
            windowTitle.fontSize = 44f;
            windowTitle.alignment = TextAlignmentOptions.Center;

            Button close = CloneButton(optionButton, panel, "CloseButton", out closeLabel);
            close.onClick.AddListener(CloseWindow);
            RectTransform closeRect = (RectTransform)close.transform;
            closeRect.anchorMin = closeRect.anchorMax = new Vector2(0.5f, 0f);
            closeRect.pivot = new Vector2(0.5f, 0.5f);
            closeRect.anchoredPosition = new Vector2(0f, 70f);
            RectTransform closeLabelRect = closeLabel.rectTransform;
            closeLabelRect.anchorMin = Vector2.zero;
            closeLabelRect.anchorMax = Vector2.one;
            closeLabelRect.offsetMin = Vector2.zero;
            closeLabelRect.offsetMax = Vector2.zero;
            closeLabel.alignment = TextAlignmentOptions.Center;

            BuildList(panel);

            RefreshLabels();
        }

        private static void BuildList(RectTransform panel)
        {
            RectTransform scrollRect = NewRect("List", panel);
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(50f, 120f);
            scrollRect.offsetMax = new Vector2(-50f, -100f);
            // Transparent, so the mouse wheel also scrolls over the empty part of the list.
            scrollRect.gameObject.AddComponent<Image>().color = Color.clear;
            scrollRect.gameObject.AddComponent<RectMask2D>();
            ScrollRect scroll = scrollRect.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 40f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            listContent = NewRect("Content", scrollRect);
            listContent.anchorMin = new Vector2(0f, 1f);
            listContent.anchorMax = new Vector2(1f, 1f);
            listContent.pivot = new Vector2(0.5f, 1f);
            listContent.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = listContent.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 18f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            listContent.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = scrollRect;
            scroll.content = listContent;
        }

        private static void RefreshList()
        {
            foreach (Transform row in listContent)
            {
                Object.Destroy(row.gameObject);
            }
            if (ModLoader.SaveError != null)
            {
                AddText(listContent, $"<color=#E08080><noparse>{ModLoader.SaveError}</noparse></color>");
            }
            if (ModLoader.Entries.Count == 0)
            {
                AddText(listContent, IsKorean ? "설치된 모드가 없습니다." : "No mods installed.");
            }
            foreach (ModEntry entry in ModLoader.Entries)
            {
                AddModRow(entry);
            }
        }

        private static void AddModRow(ModEntry entry)
        {
            RectTransform row = NewRect("Row", listContent);
            HorizontalLayoutGroup layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 20f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            AddText(row, FormatEntry(entry)).gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            if (entry.CanToggle)
            {
                AddPendingNote(row, entry);
                AddToggle(row, entry);
            }
        }

        // Always takes the same width, so a toggle never rewraps the row and moves the buttons below it.
        private static void AddPendingNote(Transform row, ModEntry entry)
        {
            bool enabled = ModLoader.IsEnabled(entry);
            string text = "";
            if (enabled != (entry.Status != ModStatus.Disabled))
            {
                text = enabled ? (IsKorean ? "재시작 후 켜짐" : "Restart: on") : (IsKorean ? "재시작 후 꺼짐" : "Restart: off");
            }
            TextMeshProUGUI note = AddText(row, text);
            note.fontSize = 21f;
            note.color = new Color(0.88f, 0.75f, 0.44f);
            note.alignment = TextAlignmentOptions.MidlineRight;
            LayoutElement size = note.gameObject.AddComponent<LayoutElement>();
            size.minWidth = 140f;
            size.preferredWidth = 140f;
        }

        private static TextMeshProUGUI AddText(Transform parent, string text)
        {
            TextMeshProUGUI label = NewRect("Text", parent).gameObject.AddComponent<TextMeshProUGUI>();
            label.font = buttonLabel.font;
            label.fontSize = 28f;
            label.raycastTarget = false;
            label.text = text;
            return label;
        }

        private static void AddToggle(Transform row, ModEntry entry)
        {
            bool enabled = ModLoader.IsEnabled(entry);
            RectTransform rect = NewRect("Toggle", row);
            LayoutElement size = rect.gameObject.AddComponent<LayoutElement>();
            size.minWidth = 120f;
            size.minHeight = 50f;
            Image background = rect.gameObject.AddComponent<Image>();
            background.color = new Color(0.25f, 0.23f, 0.3f);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() =>
            {
                ModLoader.SetEnabled(entry, !enabled);
                SoundManager.ins.PlaySFX(SFXType.Click);
                RefreshList();
            });

            TextMeshProUGUI label = AddText(rect, enabled ? (IsKorean ? "끄기" : "Disable") : (IsKorean ? "켜기" : "Enable"));
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
        }

        private static string FormatEntry(ModEntry entry)
        {
            string text = $"<noparse>{entry.Name}</noparse>";
            if (!string.IsNullOrEmpty(entry.Version)) text += $"  <size=75%><color=#AAAAAA><noparse>v{entry.Version}</noparse></color></size>";
            if (!string.IsNullOrEmpty(entry.Author)) text += $"  <size=75%><color=#AAAAAA><noparse>{entry.Author}</noparse></color></size>";
            text += "  " + FormatStatus(entry.Status);
            if (!string.IsNullOrEmpty(entry.Error)) text += $"\n<size=75%><color=#E08080><noparse>{entry.Error}</noparse></color></size>";
            return text;
        }

        private static string FormatStatus(ModStatus status)
        {
            switch (status)
            {
                case ModStatus.Loaded: return $"<color=#80D080>[{(IsKorean ? "로드됨" : "Loaded")}]</color>";
                case ModStatus.Disabled: return $"<color=#AAAAAA>[{(IsKorean ? "꺼짐" : "Disabled")}]</color>";
                default: return $"<color=#E08080>[{(IsKorean ? "실패" : "Failed")}]</color>";
            }
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static void LogLayout(Transform option)
        {
            StringBuilder log = new StringBuilder("Title layout:\n");
            for (Transform t = option; t != null; t = t.parent)
            {
                log.Append("  path: ").Append(t.name).Append(" [")
                    .Append(string.Join(", ", t.GetComponents<Component>().Select(c => c.GetType().Name))).Append("]\n");
            }
            foreach (Transform sibling in option.parent)
            {
                RectTransform rect = sibling as RectTransform;
                log.Append("  sibling: ").Append(sibling.name)
                    .Append(" active=").Append(sibling.gameObject.activeSelf)
                    .Append(" pos=").Append(rect != null ? rect.anchoredPosition.ToString() : "-")
                    .Append(" size=").Append(rect != null ? rect.sizeDelta.ToString() : "-").Append('\n');
            }
            Plugin.Log.LogInfo(log.ToString());
        }
    }
}
