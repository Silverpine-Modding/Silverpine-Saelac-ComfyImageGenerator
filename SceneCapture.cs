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
    private static readonly System.Reflection.MethodInfo? NpcSexMethod = HarmonyLib.AccessTools.Method(typeof(NeuralNPC), "GetFinalSex", Type.EmptyTypes);
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
        Sex = NpcSexHint(npc), Pronouns = PronounHint(npc.Pronouns),
        Clothing = new SceneClothing { State = npc.isNude ? "nude" : "clothed", EquipmentKnown = false,
            VisibleDetails = npc.GetNPCVisibleObjectDescription() ?? "" }
    };

    private static SceneCharacter FromPlayer(Player player)
    {
        var character = new SceneCharacter { Key = CharacterProfileStore.Key(player.playerName, true), Name = player.playerName,
            Description = player.characterDescription ?? "", Sex = player.sex.ToString().ToLowerInvariant(), Pronouns = PronounHint(player.Pronouns) };
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

    private static string PronounHint(Pronouns pronouns) => pronouns switch
    {
        Pronouns.Male => "he/him",
        Pronouns.Female => "she/her",
        _ => "unknown"
    };

    private static string NpcSexHint(NeuralNPC npc)
    {
        // The game's private method applies settings such as noIntersexNPCs,
        // and invoking it also respects installed Harmony patches.
        try { return (NpcSexMethod?.Invoke(npc, null) is Sex sex ? sex : npc.sex).ToString().ToLowerInvariant(); }
        catch (Exception) { return ""; } // Missing state must not prevent generation.
    }

    internal static SceneSnapshot Capture(NeuralNPC npc)
    {
        if (npc == null || DialogBox.Instance == null || !DialogBox.Instance.isOpen)
            throw new InvalidOperationException("Open a character conversation before generating a scene.");
        var scene = new SceneSnapshot();
        var participants = new List<NeuralNPC> { npc };
        if (NeuralNPC.multiDialogParticipants != null && NeuralNPC.multiDialogParticipants.Contains(npc))
            participants.AddRange(NeuralNPC.multiDialogParticipants.Where(n => n != null));
        foreach (NeuralNPC participant in participants.Distinct()) scene.Characters.Add(FromNpc(participant));
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
        scene.Environment = environment.ToString();
        return scene;
    }

    internal static async Task<string> GeneratePrompt(SceneSnapshot scene, ModelFamily family, string guidance,
        Action<string> progress, CancellationToken cancel, Action<string>? reportPromptNotice = null)
    {
        SceneSelection.Validate(scene, family);
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
        string response = await ScenePrompts.AwaitLlm(inference.GenerateSimpleLowTemp(family == ModelFamily.Anima ? CharacterScenePrompts.AnimaSystemInstruction : CharacterScenePrompts.IllustriousSystemInstruction,
            new List<string> { ScenePrompts.Build(scene, family, guidance) }, deterministic: false,
            maxLength: Math.Min(3500, 650 + scene.Characters.Count * 300)),
            TimeSpan.FromSeconds(Plugin.LlmTimeoutSeconds.Value), cancel);
        cancel.ThrowIfCancellationRequested();
        string prompt = CharacterScenePrompts.Apply(response, scene, family, out string notice);
        reportPromptNotice?.Invoke(notice);
        return prompt;
    }
    internal static async Task<string> GenerateProfile(SceneCharacter character, CancellationToken cancel)
    {
        string response = await ScenePrompts.AwaitLlm(ReadyInference().GenerateSimpleLowTemp(
            "You extract faithful visual character references. Output only the visual description, without reasoning or roleplay.",
            new List<string> { ScenePrompts.ProfileInstruction(character) }, deterministic: false, maxLength: 360),
            TimeSpan.FromSeconds(Plugin.LlmTimeoutSeconds.Value), cancel);
        return ScenePrompts.Clean(response);
    }
    internal static async Task<string> GenerateVisionPrompt(SceneSnapshot scene, GenerationSettings settings, string guidance,
        Action<string> progress, CancellationToken cancel)
    {
        // Capture saved profile text on the main thread, independently of game-LLM availability.
        foreach (SceneCharacter character in scene.Characters)
            character.Description = Plugin.Profiles.Description(character.Key) ?? character.Description;
        using var client = new VisionClient(settings.Vision);
        return await VisionPrompts.GenerateAsync(client, settings.Vision, scene, settings.Family, guidance, settings.Portraits, progress, cancel,
            (character, draft) =>
            {
                if (Plugin.InitializeProfiles.Value && Plugin.Profiles.Description(character.Key) == null && !string.IsNullOrWhiteSpace(draft))
                {
                    try { Plugin.Profiles.Set(character.Key, character.Name, draft, "Vision LLM beta"); }
                    catch (Exception) { settings.PortraitNotice += " Could not save the new visual profile; portrait observations still guide this scene."; }
                }
            }, notice => settings.PromptNotice = notice);
    }
    private static InferenceServerSetupHandler ReadyInference()
    {
        var inference = InferenceServerSetupHandler.Instance;
        if (inference == null || !inference.modelLoaded) throw new InvalidOperationException("Configure and load the game's LLM in Silverpine's AI settings first.");
        return inference;
    }
}
