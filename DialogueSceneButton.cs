using HarmonyLib;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SilverpineMods.ComfyImageGenerator;

[HarmonyPatch(typeof(DialogBox), "ShowButtons")]
internal static class DialogueSceneButtonPatch
{
    private static readonly FieldInfo PopOut = AccessTools.Field(typeof(DialogBox), "popOutButton");
    private static void Postfix(DialogBox __instance)
    {
        if (__instance.GetComponent<DialogueSceneButton>() != null) return;
        Button? more = PopOut.GetValue(__instance) as Button;
        if (more != null) __instance.gameObject.AddComponent<DialogueSceneButton>().Initialize(__instance, more);
    }
}

internal sealed class DialogueSceneButton : MonoBehaviour
{
    private DialogBox dialog = null!;
    private Button more = null!, button = null!;
    private readonly Vector3[] corners = new Vector3[4];
    internal void Initialize(DialogBox owner, Button anchor)
    {
        dialog = owner;
        more = anchor;
        button = Instantiate(anchor, anchor.transform.parent, false);
        button.name = "ComfyGenerateScene";
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() => { if (NeuralNPC.currentActiveDialogNeuralNPC != null) GeneratorWindow.OpenScene(NeuralNPC.currentActiveDialogNeuralNPC); });
        TextMeshProUGUI text = button.GetComponentInChildren<TextMeshProUGUI>(true);
        text.text = "Generate scene";
        text.enableAutoSizing = true;
        text.fontSizeMin = 13;
        text.fontSizeMax = 21;
        text.color = new Color(0.95f, 0.96f, 0.98f);
        text.margin = new Vector4(7, 0, 7, 0);
        LayoutElement layout = button.GetComponent<LayoutElement>() ?? button.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;
        if (button.TryGetComponent<ContentSizeFitter>(out var fitter)) fitter.enabled = false;
        if (button.TryGetComponent<AspectRatioFitter>(out var aspect)) aspect.enabled = false;
        button.image.color = new Color(0.16f, 0.18f, 0.22f, 1);
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white; colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f);
        colors.pressedColor = new Color(0.7f, 0.8f, 0.9f); colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 1); button.colors = colors;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.gameObject.SetActive(false);
    }
    private void LateUpdate()
    {
        if (button == null || more == null) return;
        bool visible = dialog.isOpen && more.gameObject.activeInHierarchy && NeuralNPC.currentActiveDialogNeuralNPC != null;
        button.gameObject.SetActive(visible);
        if (!visible) return;
        button.interactable = more.interactable && !dialog.isAnimatingText && !GenerationService.Busy;
        // Place directly left of the native ellipsis in the same parent coordinate space,
        // so canvas scaling and resolution changes follow the game's layout.
        RectTransform source = (RectTransform)more.transform;
        RectTransform target = (RectTransform)button.transform;
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMin;
        target.pivot = new Vector2(1, 0.5f);
        target.sizeDelta = new Vector2(164, Mathf.Max(32, source.rect.height));
        source.GetWorldCorners(corners);
        target.position = (corners[0] + corners[1]) * 0.5f - source.right * (8 * source.lossyScale.x);
        target.localScale = source.localScale;
    }
    private void OnDestroy() { if (button != null) Destroy(button.gameObject); }
}

[HarmonyPatch(typeof(DialogBox), "Update")]
internal static class BlockDialogueInputWhileViewing
{
    // Native Update reads keyboard/scroll directly, independently of UI raycasters.
    [HarmonyPriority(Priority.First)]
    private static bool Prefix() => !GeneratorWindow.IsOpen;
}

[HarmonyPatch(typeof(DialogBox), nameof(DialogBox.SendInput))]
internal static class BlockDialogueSendWhileViewing
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix() => !GeneratorWindow.IsOpen;
}
