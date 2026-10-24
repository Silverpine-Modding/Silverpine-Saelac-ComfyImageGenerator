using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SilverpineMods.ComfyImageGenerator;

internal static class SceneCapture
{
    private static readonly System.Reflection.MethodInfo? ExposureMethod = HarmonyLib.AccessTools.Method(typeof(Player), "IsExposed", new[] { typeof(EquipmentSlotType) });
    internal static List<SceneCharacter> AvailableCharacters()
    {
        var characters = new List<SceneCharacter>();
        if (Player.Instance != null)
        {
            SceneCharacter player = FromPlayer(Player.Instance); player.Name += " (player)"; characters.Add(player);
        }
        foreach (NeuralNPC npc in NeuralNPC.neuralNPCs.Values.Where(n => n != null).OrderBy(n => n.GetFinalName()))
        {
            try { characters.Add(FromNpc(npc)); }
            catch (Exception) { Plugin.Log.LogWarning("Could not read one character's game description; other profiles are available."); }
        }
        return characters.GroupBy(c => c.Key).Select(g => g.First()).ToList();
    }
    private static SceneCharacter FromNpc(NeuralNPC npc) => new()
    {
        Key = CharacterProfileStore.Key(npc.GetFinalName(), false), Name = npc.GetFinalName(),
        Description = npc.GetFinalCharacterDescription(isPrivate: false) ?? "", Species = npc.species ?? "",
        Clothing = new SceneClothing { State = npc.isNude ? "nude" : "clothed", EquipmentKnown = false,
            VisibleDetails = npc.GetNPCVisibleObjectDescription() ?? "" }
    };

    private static SceneCharacter FromPlayer(Player player)
    {
        var character = new SceneCharacter { Key = CharacterProfileStore.Key(player.playerName, true), Name = player.playerName,
            Description = player.characterDescription ?? "" };
        if (player.equippedItems == null) return character; // No live inventory yet: do not infer nudity.
        bool? upperExposed = ReadExposure(player, EquipmentSlotType.Chest), lowerExposed = ReadExposure(player, EquipmentSlotType.Legs);
        character.Clothing = new SceneClothing
        {
            State = !upperExposed.HasValue || !lowerExposed.HasValue ? "unknown" : upperExposed.Value && lowerExposed.Value ? "nude" : upperExposed.Value || lowerExposed.Value ? "partly clothed" : "clothed",
            EquipmentKnown = true, UpperBodyExposed = upperExposed, LowerBodyExposed = lowerExposed,
            Equipped = player.equippedItems.Where(p => p.Value != null && p.Key != EquipmentSlotType.Weapon && p.Key != EquipmentSlotType.NotEquipable)
                .OrderBy(p => (int)p.Key).Select(p => new SceneWornItem { Slot = p.Key.ToString(), Name = p.Value.name ?? "",
                    Description = SceneReferences.PlainText(p.Value.description ?? "") }).ToList(),
            // Calls through Harmony so installed clothing/coverage extensions can
            // add their current visible details; never read unequipped inventory.
            VisibleDetails = SceneReferences.PlainText(player.GetNPCVisibleObjectDescription() ?? "")
        };
        return character;
    }
    private static bool? ReadExposure(Player player, EquipmentSlotType slot)
    {
        try { return ExposureMethod?.Invoke(player, new object[] { slot }) as bool?; }
        catch (Exception) { return null; } // Unsupported game/mod state is unknown, never inferred nude.
    }

    internal static SceneSnapshot Capture(NeuralNPC npc)
    {
        if (npc == null || DialogBox.Instance == null || !DialogBox.Instance.isOpen)
            throw new InvalidOperationException("Open a character conversation before generating a scene.");
        var scene = new SceneSnapshot();
        var participants = new List<NeuralNPC> { npc };
        if (NeuralNPC.multiDialogParticipants != null && NeuralNPC.multiDialogParticipants.Contains(npc))
            participants.AddRange(NeuralNPC.multiDialogParticipants.Where(n => n != null));
        foreach (NeuralNPC participant in participants.Distinct().Take(8)) scene.Characters.Add(FromNpc(participant));
        if (Player.Instance != null)
            scene.Characters.Insert(0, FromPlayer(Player.Instance));
        var history = npc.dialogElements.ToList();
        int marker = history.FindLastIndex(d => d.speakerType == SpeakerType.NewDialogMarker);
        var current = history.Skip(marker + 1).Where(d => !string.IsNullOrWhiteSpace(d.contents)).ToList();
        // Resuming a conversation puts the marker after the last displayed turn.
        if (current.Count == 0) current = history.Where(d => d.speakerType == SpeakerType.NPC || d.speakerType == SpeakerType.Player).TakeLast(4).ToList();
        scene.Dialogue = string.Join("\n", current.TakeLast(10).Select(d => d.GetNamedContents()));
        if (string.IsNullOrWhiteSpace(scene.Dialogue)) throw new InvalidOperationException("Wait for the conversation's first response before generating a scene.");
        var environment = new StringBuilder("Location: " + npc.GetPositionZoneNameAddOutside());
        Vector3 position = npc.transform.position;
        string zone = MapZone.GetPositionZoneName(new Vector2Int(Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.y)));
        if (ZoneDescriptions.Instance != null && ZoneDescriptions.Instance.TryGetDescription(zone, out string description)) environment.Append(". ").Append(description);
        if (WorldInfoManager.Instance != null)
            environment.Append(". Time: ").Append(WorldInfoManager.Instance.GetCurrentHourOfDayAprox()).Append(". Season: ")
                .Append(WorldInfoManager.Instance.GetSeasonString()).Append(". Weather: ").Append(WorldInfoManager.Instance.currentWeatherName);
        environment.Append(". Depict only the current conversation participants. Other people mentioned in the location or dialogue must remain out of frame.");
        scene.Environment = environment.ToString();
        return scene;
    }

    internal static async Task<string> GeneratePrompt(SceneSnapshot scene, ModelFamily family, string guidance,
        Action<string> progress, CancellationToken cancel, GenerationSettings? regionalSettings = null)
    {
        InferenceServerSetupHandler inference = ReadyInference();
        // Cast membership comes only from the active dialogue, never proximity,
        // environment text, mentioned names or the saved character catalog.
        foreach (SceneCharacter character in scene.Characters)
        {
            cancel.ThrowIfCancellationRequested();
            string? saved = Plugin.Profiles.Description(character.Key);
            if (saved == null && Plugin.InitializeProfiles.Value && !string.IsNullOrWhiteSpace(character.Description))
            {
                progress("Initializing visual profile: " + character.Name + "...");
                try
                {
                    saved = await GenerateProfile(character, cancel);
                    cancel.ThrowIfCancellationRequested();
                    // Preserve edits/preset loads made since this request began.
                    if (Plugin.Profiles.Description(character.Key) == null) Plugin.Profiles.Set(character.Key, character.Name, saved, "LLM");
                    saved = Plugin.Profiles.Description(character.Key);
                }
                catch (OperationCanceledException) { throw; }
                catch (TimeoutException) { throw; }
                catch (Exception)
                { progress("Could not initialize " + character.Name + "; using the game's description."); }
            }
            character.Description = saved ?? character.Description;
        }
        progress("Asking the game's LLM for an " + family + " scene prompt...");
        if (regionalSettings?.AttentionCouple == true)
        {
            string regionalResponse = await ScenePrompts.AwaitLlm(inference.GenerateSimpleLowTemp(RegionalPrompts.SystemInstruction,
                new List<string> { RegionalPrompts.Build(scene, regionalSettings.Regions, guidance) }, deterministic: false,
                maxLength: Math.Min(3500, 450 + regionalSettings.Regions.Count * 300)), TimeSpan.FromSeconds(Plugin.LlmTimeoutSeconds.Value), cancel);
            cancel.ThrowIfCancellationRequested();
            return RegionalPrompts.Apply(regionalResponse, regionalSettings.Regions, scene);
        }
        string response = await ScenePrompts.AwaitLlm(inference.GenerateSimpleLowTemp(family == ModelFamily.Anima ? CharacterScenePrompts.AnimaSystemInstruction : CharacterScenePrompts.IllustriousSystemInstruction,
            new List<string> { ScenePrompts.Build(scene, family, guidance) }, deterministic: false,
            maxLength: Math.Min(3500, 500 + scene.Characters.Count * 250)),
            TimeSpan.FromSeconds(Plugin.LlmTimeoutSeconds.Value), cancel);
        cancel.ThrowIfCancellationRequested();
        return CharacterScenePrompts.Apply(response, scene, family);
    }
    internal static async Task<string> GenerateProfile(SceneCharacter character, CancellationToken cancel)
    {
        string response = await ScenePrompts.AwaitLlm(ReadyInference().GenerateSimpleLowTemp(
            "You extract faithful visual character references. Output only the visual description, without reasoning or roleplay.",
            new List<string> { ScenePrompts.ProfileInstruction(character) }, deterministic: false, maxLength: 360),
            TimeSpan.FromSeconds(Plugin.LlmTimeoutSeconds.Value), cancel);
        return ScenePrompts.Clean(response);
    }
    private static InferenceServerSetupHandler ReadyInference()
    {
        var inference = InferenceServerSetupHandler.Instance;
        if (inference == null || !inference.modelLoaded) throw new InvalidOperationException("Configure and load the game's LLM in Silverpine's AI settings first.");
        return inference;
    }
}
