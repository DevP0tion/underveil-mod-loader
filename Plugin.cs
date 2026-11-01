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
        private static RectTransform descriptionContent;
        private static TMP_Text descriptionText;
        private static ModEntry selected;
        private static Sprite circleSprite;

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
            panel.sizeDelta = new Vector2(1400f, 650f);
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

            BuildDescription(panel);
            BuildList(panel);

            RefreshLabels();
        }

        private static void BuildDescription(RectTransform panel)
        {
            RectTransform box = NewRect("Description", panel);
            box.anchorMin = Vector2.zero;
            box.anchorMax = new Vector2(0f, 1f);
            box.offsetMin = new Vector2(40f, 120f);
            box.offsetMax = new Vector2(440f, -100f);
            box.gameObject.AddComponent<Image>().color = new Color(0.14f, 0.13f, 0.17f);

            RectTransform area = NewRect("Area", box);
            area.anchorMin = Vector2.zero;
            area.anchorMax = Vector2.one;
            area.offsetMin = new Vector2(24f, 24f);
            area.offsetMax = new Vector2(-24f, -24f);
            descriptionContent = AddScroll(area);
            descriptionText = AddText(descriptionContent, "");
        }

        private static void BuildList(RectTransform panel)
        {
            RectTransform scrollRect = NewRect("List", panel);
            scrollRect.anchorMin = Vector2.zero;
            scrollRect.anchorMax = Vector2.one;
            scrollRect.offsetMin = new Vector2(480f, 120f);
            scrollRect.offsetMax = new Vector2(-50f, -100f);
            listContent = AddScroll(scrollRect);
        }

        private static RectTransform AddScroll(RectTransform area)
        {
            // Transparent, so the mouse wheel also scrolls over the empty part of the list.
            area.gameObject.AddComponent<Image>().color = Color.clear;
            area.gameObject.AddComponent<RectMask2D>();
            ScrollRect scroll = area.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.scrollSensitivity = 40f;
            scroll.movementType = ScrollRect.MovementType.Clamped;

            RectTransform content = NewRect("Content", area);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 18f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = area;
            scroll.content = content;
            return content;
        }

        private static void RefreshDescription()
        {
            if (selected == null)
            {
                descriptionText.text = "";
                return;
            }
            string description = string.IsNullOrWhiteSpace(selected.Description)
                ? (IsKorean ? "설명 없음" : "No description")
                : selected.Description;
            string text = $"<b><size=90%><noparse>{selected.Name}</noparse></size></b>\n{FormatEntry(selected)}\n";
            if (!string.IsNullOrEmpty(selected.Error)) text += $"<size=75%><color=#E08080><noparse>{selected.Error}</noparse></color></size>\n";
            descriptionText.text = text + $"\n<size=85%><noparse>{description}</noparse></size>";
        }

        private static void RefreshList()
        {
            RefreshDescription();
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
            AddInfoButton(row, entry);
            AddText(row, $"<noparse>{entry.Name}</noparse>").gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            if (entry.CanToggle)
            {
                AddPendingNote(row, entry);
                AddToggle(row, entry);
            }
        }

        private static void AddInfoButton(Transform row, ModEntry entry)
        {
            RectTransform rect = NewRect("Info", row);
            LayoutElement size = rect.gameObject.AddComponent<LayoutElement>();
            size.minWidth = 36f;
            size.minHeight = 36f;
            size.preferredWidth = 36f;
            size.preferredHeight = 36f;
            Image background = rect.gameObject.AddComponent<Image>();
            background.sprite = GetCircleSprite();
            background.color = entry == selected ? new Color(0.88f, 0.75f, 0.44f) : new Color(0.25f, 0.23f, 0.3f);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() =>
            {
                selected = entry == selected ? null : entry;
                descriptionContent.anchoredPosition = Vector2.zero;
                SoundManager.ins.PlaySFX(SFXType.Click);
                RefreshList();
            });

            TextMeshProUGUI label = AddText(rect, "i");
            label.alignment = TextAlignmentOptions.Center;
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            if (entry == selected) label.color = new Color(0.09f, 0.08f, 0.11f);
        }

        private static Sprite GetCircleSprite()
        {
            if (circleSprite != null)
            {
                return circleSprite;
            }
            const int size = 64;
            float radius = size / 2f;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(radius - distance)));
                }
            }
            texture.Apply();
            circleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
            return circleSprite;
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
            string text = "";
            if (!string.IsNullOrEmpty(entry.Version)) text += $"<size=75%><color=#AAAAAA><noparse>v{entry.Version}</noparse></color></size>  ";
            if (!string.IsNullOrEmpty(entry.Author)) text += $"<size=75%><color=#AAAAAA><noparse>{entry.Author}</noparse></color></size>  ";
            return text + FormatStatus(entry.Status);
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
