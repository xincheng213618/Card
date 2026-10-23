using System.IO;
using System.Reflection;
using System.Text.Json;
using CardGame.Wpf.Persistence;

internal static class ShanheCodexLoadoutPersistenceChecks
{
    private const string LegacyJson = """
        {"FormatVersion":1,"Books":37,"UnlockedRewardIds":["first","second","third"],
         "EquippedTacticIds":["first",null,null,null,null,null],"ClaimedRewards":["earlier-claim"]}
        """;
    private static readonly FieldInfo StoredJson = typeof(MemoryShanheCodexStore).GetField("_json", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static void LegacyJsonAndLoadoutOperationsStayDetached()
    {
        var store = Seed(LegacyJson);
        var legacy = store.Read();
        Program.Assert(legacy.FormatVersion == FileShanheCodexStore.LegacyFormatVersion && legacy.LoadoutPlans is null &&
            legacy.SelectedLoadoutPlanId is null && legacy.EquippedTacticIds[0] == "first" && Raw(store) == LegacyJson,
            "A historical format-one account remains readable with its original six active slots and source JSON unchanged.");
        var first = Detached(legacy, ShanheCodexLoadouts.EnsurePlans);
        Program.Assert(first.LoadoutPlans is [{ Id: "default", Name: "方案1" }] && first.SelectedLoadoutPlanId == "default" &&
            first.FormatVersion == FileShanheCodexStore.CurrentFormatVersion && Raw(store) == LegacyJson &&
            legacy.FormatVersion == FileShanheCodexStore.LegacyFormatVersion && legacy.LoadoutPlans is null,
            "Ensuring plans upgrades only the detached in-memory result; preparing the current format never writes or changes the legacy source.");
        store.Write(legacy);
        using (var json = JsonDocument.Parse(Raw(store)))
            Program.Assert(!json.RootElement.TryGetProperty("LoadoutPlans", out _) && !json.RootElement.TryGetProperty("SelectedLoadoutPlanId", out _) &&
                json.RootElement.GetProperty("FormatVersion").GetInt32() == legacy.FormatVersion,
                "Explicitly rewriting an unmigrated legacy profile keeps both optional fields omitted and its existing format version.");

        var copied = Detached(first, profile => ShanheCodexLoadouts.Create(profile, "  输出组  ", true));
        var copiedId = copied.SelectedLoadoutPlanId!;
        Program.Assert(Guid.TryParseExact(copiedId, "N", out var copiedGuid) && copiedGuid != Guid.Empty &&
            copied.LoadoutPlans is [_, { Name: "输出组" }] && copied.EquippedTacticIds.SequenceEqual(first.EquippedTacticIds),
            "Creating a copied plan gives it a fresh identity, trims its caller-provided name and activates the copied six slots.");
        var empty = Detached(copied, profile => ShanheCodexLoadouts.Create(profile, "空槽", false));
        var emptyId = empty.SelectedLoadoutPlanId!;
        Program.Assert(emptyId != copiedId && Guid.TryParseExact(emptyId, "N", out _) && empty.EquippedTacticIds.All(id => id is null),
            "Creating a blank plan activates six empty slots while retaining the earlier copied and default plans.");
        string?[] slots = ["second", null, "third", null, null, null];
        var edited = Detached(empty, profile => ShanheCodexLoadouts.UpdateActiveSlots(profile, slots));
        slots[0] = "first";
        Program.Assert(edited.EquippedTacticIds[0] == "second" && edited.LoadoutPlans!.Single(plan => plan.Id == emptyId).EquippedTacticIds[0] == "second" &&
            empty.EquippedTacticIds.All(id => id is null) && edited.LoadoutPlans!.Single(plan => plan.Id == copiedId).EquippedTacticIds[0] == "first",
            "Editing active slots freezes the supplied array, updates only the selected plan and preserves all other plans and the original profile.");
        var renamed = Detached(edited, profile => ShanheCodexLoadouts.Rename(profile, emptyId, "  备选  "));
        Program.Assert(renamed.LoadoutPlans!.Single(plan => plan.Id == emptyId).Name == "备选" && renamed.SelectedLoadoutPlanId == emptyId,
            "Renaming retains the plan identity, activation and slot contents.");
        var selected = Detached(renamed, profile => ShanheCodexLoadouts.Select(profile, "default"));
        Program.Assert(selected.EquippedTacticIds[0] == "first" && selected.LoadoutPlans!.Single(plan => plan.Id == emptyId).EquippedTacticIds[0] == "second",
            "Selecting a saved plan changes the active source slots without losing the edited inactive plan.");
        var deletedInactive = Detached(selected, profile => ShanheCodexLoadouts.Delete(profile, copiedId));
        Program.Assert(deletedInactive.SelectedLoadoutPlanId == "default" && deletedInactive.LoadoutPlans!.Length == 2,
            "Deleting an inactive plan retains the selected plan and its slots.");
        var deletedActive = Detached(renamed, profile => ShanheCodexLoadouts.Delete(profile, emptyId));
        Program.Assert(deletedActive.SelectedLoadoutPlanId == "default" && deletedActive.EquippedTacticIds[0] == "first" && deletedActive.LoadoutPlans!.Length == 2,
            "Deleting the selected plan activates the first remaining plan and its exact six slots.");
        _ = Detached(renamed, ShanheCodexLoadouts.EnsurePlans);

        store.Write(renamed);
        using (var json = JsonDocument.Parse(Raw(store)))
            Program.Assert(json.RootElement.GetProperty("FormatVersion").GetInt32() == FileShanheCodexStore.CurrentFormatVersion &&
                json.RootElement.TryGetProperty("LoadoutPlans", out _) && json.RootElement.TryGetProperty("SelectedLoadoutPlanId", out _),
                "Only an explicit save writes the complete plans with the current protected account format.");
        var cold = store.Read();
        Program.Assert(JsonSerializer.Serialize(cold) == JsonSerializer.Serialize(renamed) && cold.Books == 37 && cold.ClaimedRewards.SequenceEqual(["earlier-claim"]),
            "The memory store exercises the file codec and restores all plans, selected active slots and unrelated account progression.");
        cold.EquippedTacticIds[0] = "first";
        cold.LoadoutPlans![0].EquippedTacticIds[0] = "third";
        cold.UnlockedRewardIds[0] = "changed";
        cold.ClaimedRewards[0] = "changed";
        Program.Assert(JsonSerializer.Serialize(store.Read()) == JsonSerializer.Serialize(renamed),
            "Mutating a returned profile cannot alter the persisted account or any independently restored plan.");
    }

    public static void DamagedLoadoutsAndInvalidOperationsAreRejected()
    {
        var single = ShanheCodexLoadouts.Rename(ShanheCodexLoadouts.EnsurePlans(Seed(LegacyJson).Read()), "default", "Alpha");
        var valid = ShanheCodexLoadouts.Create(single, "Beta", true);
        var selected = valid.LoadoutPlans!.Single(plan => plan.Id == valid.SelectedLoadoutPlanId);
        ShanheCodexProfile ReplaceSelected(ShanheCodexLoadoutPlan plan) => valid with
        { LoadoutPlans = valid.LoadoutPlans.Select(item => item.Id == selected.Id ? plan : item).ToArray() };
        var invalid = new[]
        {
            valid with { FormatVersion = FileShanheCodexStore.LegacyFormatVersion },
            Seed(LegacyJson).Read() with { FormatVersion = FileShanheCodexStore.CurrentFormatVersion },
            valid with { FormatVersion = FileShanheCodexStore.CurrentFormatVersion + 1 },
            Seed(LegacyJson).Read() with { FormatVersion = FileShanheCodexStore.CurrentFormatVersion + 1 },
            valid with { SelectedLoadoutPlanId = null },
            valid with { LoadoutPlans = null },
            valid with { LoadoutPlans = [] },
            valid with { SelectedLoadoutPlanId = "missing" },
            valid with { EquippedTacticIds = ["second", null, null, null, null, null] },
            valid with { LoadoutPlans = [valid.LoadoutPlans![0], selected with { Id = "default" }] },
            valid with { LoadoutPlans = [null!] },
            ReplaceSelected(selected with { Id = "../invalid" }),
            ReplaceSelected(selected with { Id = new string('a', 65) }),
            ReplaceSelected(selected with { Name = "alpha" }),
            ReplaceSelected(selected with { Name = " Beta " }),
            ReplaceSelected(selected with { Name = "\tBeta" }),
            ReplaceSelected(selected with { Name = " " }),
            ReplaceSelected(selected with { Name = new string('甲', ShanheCodexLoadouts.MaximumNameLength + 1) }),
            ReplaceSelected(selected with { EquippedTacticIds = null! }),
            ReplaceSelected(selected with { EquippedTacticIds = new string?[5] }),
            ReplaceSelected(selected with { EquippedTacticIds = ["first", "first", null, null, null, null] }),
            ReplaceSelected(selected with { EquippedTacticIds = ["unearned", null, null, null, null, null] }),
            valid with { LoadoutPlans = Enumerable.Range(0, ShanheCodexLoadouts.MaximumPlans + 1)
                .Select(index => new ShanheCodexLoadoutPlan("plan-" + index, "方案" + index, [.. valid.EquippedTacticIds])).ToArray(), SelectedLoadoutPlanId = "plan-0" }
        };
        var store = new MemoryShanheCodexStore();
        store.Write(valid);
        var original = Raw(store);
        foreach (var damaged in invalid)
        {
            Reject(() => store.Write(damaged));
            Program.Assert(Raw(store) == original, "Rejecting a damaged profile must preserve the complete previously persisted account.");
            var json = JsonSerializer.Serialize(damaged);
            var damagedStore = Seed(json);
            Reject(() => _ = damagedStore.Read());
            Program.Assert(Raw(damagedStore) == json, "A damaged saved plan must be rejected without dropping metadata, correcting slots or rewriting its source.");
        }

        var before = JsonSerializer.Serialize(valid);
        foreach (var operation in new Action[]
        {
            () => _ = ShanheCodexLoadouts.Create(valid, " alpha ", false),
            () => _ = ShanheCodexLoadouts.Rename(valid, "default", " beta "),
            () => _ = ShanheCodexLoadouts.Create(valid, "\nTrimmed", false),
            () => _ = ShanheCodexLoadouts.Create(valid, " ", false),
            () => _ = ShanheCodexLoadouts.Rename(valid, selected.Id, new string('a', ShanheCodexLoadouts.MaximumNameLength + 1)),
            () => _ = ShanheCodexLoadouts.Select(valid, "missing"),
            () => _ = ShanheCodexLoadouts.Rename(valid, "missing", "名称"),
            () => _ = ShanheCodexLoadouts.Delete(valid, "missing"),
            () => _ = ShanheCodexLoadouts.Delete(single, "default"),
            () => _ = ShanheCodexLoadouts.UpdateActiveSlots(valid, ["unearned", null, null, null, null, null]),
            () => _ = ShanheCodexLoadouts.UpdateActiveSlots(valid, ["first", "first", null, null, null, null]),
            () => _ = ShanheCodexLoadouts.UpdateActiveSlots(valid, new string?[5]),
            () => _ = ShanheCodexLoadouts.EnsurePlans(valid with { EquippedTacticIds = new string?[6] })
        }) Reject(operation);
        Program.Assert(JsonSerializer.Serialize(valid) == before, "Rejected operations leave the input plans, active slots, balance and unlocks untouched.");
        _ = Detached(valid, profile => ShanheCodexLoadouts.Rename(profile, selected.Id, new string('甲', ShanheCodexLoadouts.MaximumNameLength)));

        var full = single;
        for (var index = 1; index < ShanheCodexLoadouts.MaximumPlans; index++)
            full = Detached(full, profile => ShanheCodexLoadouts.Create(profile, "新增" + index, false));
        var fullBefore = JsonSerializer.Serialize(full);
        Reject(() => _ = ShanheCodexLoadouts.Create(full, "第九套", false));
        Program.Assert(full.LoadoutPlans!.Length == ShanheCodexLoadouts.MaximumPlans && JsonSerializer.Serialize(full) == fullBefore,
            "Eight distinct plans are supported, and a ninth creation cannot evict, rename or modify an existing plan.");
    }

    private static ShanheCodexProfile Detached(ShanheCodexProfile source, Func<ShanheCodexProfile, ShanheCodexProfile> operation)
    {
        var before = JsonSerializer.Serialize(source);
        var next = operation(source);
        var sourceSlots = new[] { source.EquippedTacticIds }.Concat(source.LoadoutPlans?.Select(plan => plan.EquippedTacticIds) ?? []);
        var nextSlots = new[] { next.EquippedTacticIds }.Concat(next.LoadoutPlans!.Select(plan => plan.EquippedTacticIds)).ToArray();
        Program.Assert(!ReferenceEquals(source, next) && !ReferenceEquals(source.UnlockedRewardIds, next.UnlockedRewardIds) &&
            next.FormatVersion == FileShanheCodexStore.CurrentFormatVersion &&
            !ReferenceEquals(source.ClaimedRewards, next.ClaimedRewards) && !ReferenceEquals(source.LoadoutPlans, next.LoadoutPlans) &&
            nextSlots.All(slots => !sourceSlots.Any(old => ReferenceEquals(old, slots))) &&
            nextSlots.SelectMany((slots, index) => nextSlots.Skip(index + 1).Select(other => !ReferenceEquals(slots, other))).All(detached => detached) &&
            next.EquippedTacticIds.SequenceEqual(next.LoadoutPlans!.Single(plan => plan.Id == next.SelectedLoadoutPlanId).EquippedTacticIds) &&
            JsonSerializer.Serialize(source) == before,
            "Every successful operation returns independent active and per-plan slot arrays, retains selected-plan consistency and leaves its complete source untouched.");
        return next;
    }

    private static MemoryShanheCodexStore Seed(string json)
    {
        var store = new MemoryShanheCodexStore();
        StoredJson.SetValue(store, json);
        return store;
    }
    private static string Raw(MemoryShanheCodexStore store) => (string)StoredJson.GetValue(store)!;
    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("The invalid loadout must be rejected with InvalidDataException.");
    }
}
