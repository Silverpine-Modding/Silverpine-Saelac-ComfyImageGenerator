using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SilverpineMods.ComfyImageGenerator;

internal sealed partial class GeneratorWindow
{
    private List<SceneCharacter> sceneCandidates = new();
    private readonly HashSet<string> selectedSceneKeys = new(StringComparer.Ordinal);
    private List<string> promptCharacterKeys = new();
    private ModelFamily? selectionFamily;
    private string selectionConversation = "";
    private Vector2 castScroll;

    private void OpenCastPicker(NeuralNPC npc) => Try(() =>
    {
        if (GenerationService.Busy || profileBusy) throw new InvalidOperationException("Wait for the current request to finish.");
        SceneSnapshot scene = SceneCapture.Capture(npc);
        string conversation = CharacterProfileStore.Key(npc.GetFinalName(), false);
        if (selectionFamily != family || selectionConversation != conversation) selectedSceneKeys.Clear();
        selectedSceneKeys.RemoveWhere(key => !scene.Characters.Any(c => c.Key == key));
        if (selectedSceneKeys.Count == 0)
        {
            selectedSceneKeys.Add(conversation);
            if (family == ModelFamily.Anima)
            {
                SceneCharacter? player = scene.Characters.FirstOrDefault(c => c.Key.StartsWith("player:", StringComparison.Ordinal));
                if (player != null) selectedSceneKeys.Add(player.Key);
            }
        }
        sceneCandidates = scene.Characters.GroupBy(c => c.Key).Select(g => g.First()).ToList();
        selectionFamily = family; selectionConversation = conversation;
        choose = null; page = 7; castScroll = Vector2.zero;
        status = "Choose the characters who should appear. Unselected participants remain off-screen.";
    });

    private SceneSnapshot SelectedLiveScene()
    {
        NeuralNPC npc = NeuralNPC.currentActiveDialogNeuralNPC;
        SceneSnapshot current = SceneCapture.Capture(npc);
        if (selectionFamily != family || selectionConversation != CharacterProfileStore.Key(npc.GetFinalName(), false))
            throw new InvalidOperationException("The conversation or model family changed. Refresh the character list before generating.");
        // Recapture clothing and dialogue on confirmation, not when the picker opened.
        return SceneSelection.Select(current, selectedSceneKeys, family);
    }

    private void DrawCastPicker()
    {
        int maximum = SceneSelection.Maximum(family);
        GUILayout.BeginArea(new Rect(32, 80, 1536, 708));
        GUI.enabled = !GenerationService.Busy && !profileBusy;
        GUILayout.Label("Choose characters  /  " + family, title);
        GUILayout.Label(family == ModelFamily.Anima
            ? "Select one or two people from this conversation. The player is optional; you can choose two NPCs."
            : "Select exactly one person for solo character art. Clicking another name replaces the selection.", label);
        GUILayout.Label("Selected: " + selectedSceneKeys.Count + " / " + maximum + ". Only these characters' profiles and current clothing will be sent to the LLM.", label);
        castScroll = GUILayout.BeginScrollView(castScroll);
        foreach (SceneCharacter character in sceneCandidates)
        {
            bool selected = selectedSceneKeys.Contains(character.Key);
            GUI.enabled = !GenerationService.Busy && !profileBusy && (maximum == 1 || selected || selectedSceneKeys.Count < maximum);
            string name = character.Name + (character.Key.StartsWith("player:", StringComparison.Ordinal) ? " (player)" : " (NPC)");
            if (GUILayout.Button((selected ? "[Selected]  " : "[Choose]  ") + name, button, GUILayout.Height(48)))
            {
                if (maximum == 1) { selectedSceneKeys.Clear(); selectedSceneKeys.Add(character.Key); }
                else if (selected) selectedSceneKeys.Remove(character.Key);
                else selectedSceneKeys.Add(character.Key);
            }
            GUILayout.Space(8);
        }
        GUILayout.EndScrollView();
        GUI.enabled = !GenerationService.Busy && !profileBusy;
        GUILayout.Label("The dialogue and location still provide context. Everyone you leave unselected is explicitly kept out of the picture.", label);
        if (GUILayout.Button("Refresh conversation participants", button, GUILayout.Height(40)))
            OpenCastPicker(NeuralNPC.currentActiveDialogNeuralNPC);
        GUI.enabled &= selectionFamily == family && selectedSceneKeys.Count >= 1 && selectedSceneKeys.Count <= maximum;
        if (GUILayout.Button("Generate selected characters", button, GUILayout.Height(48)))
        {
            BeginScene();
        }
        GUI.enabled = true;
        GUILayout.EndArea();
        DrawStatus(new Rect(32, 800, 1536, 74));
    }
}
