using CardGame.Content.Standard;
using CardGame.Core;
using System.Text.Json.Nodes;

internal static class ProgramCommittedUseContractChecks
{
    public static void RegistrationRejectsAmbiguousAndMutableContracts()
    {
        var first = new ValidContract();
        var catalog = ProgramCommittedUseContracts.Build([first]);
        Require(ReferenceEquals(catalog["fixture:committed-use"], first) &&
            ((IDictionary<string, IProgramCommittedUseContract>)catalog).IsReadOnly,
            "A contract is shared as immutable metadata and cannot be replaced by a caller.");
        Reject([first, new ValidContract()]);
        Reject([new MutableContract()]);
        Reject([new MissingIdentityContract()]);
        var trigger = StandardContentRegistry.CreateWithClassicGenerals().GetSkill("ol:taohuai").Program!.Triggers[0];
        Require(!ProgramCommittedUseContracts.TryValidate("fixture:unregistered", trigger),
            "An unregistered response-use contract must retain the normal validation boundary.");
        Require(ProgramCommittedUseContracts.TryValidate("ol:taohuai", trigger),
            "A discovered character contract must validate its actual registered trigger.");
        ResponseOnlyConversionsRequireExplicitUseAdmission();
        SingleActionAdmissionKeepsExactContractValidation();
    }

    private static void ResponseOnlyConversionsRequireExplicitUseAdmission()
    {
        var rules = JsonNode.Parse(ReadResource("mobile-shen-zhao-yun.rules.json"))!;
        var presentation = ReadResource("mobile-shen-zhao-yun.presentation.json");
        var admitted = SkillProgramCatalog.Load(rules.ToJsonString(), presentation).Programs["mobile:longhun"];
        Require(admitted.ViewAs.Any(rule => rule.Id == "2-club" && rule.ForResponse && !rule.ForPlay) &&
            admitted.Triggers.Any(t => t.SourceViewAsId == "2-club" && t.Window == SkillProgramTriggerWindow.CardUseCommitted && t.IncludeResponseUses),
            "An admitted committed-use contract must support a real response-only conversion without pretending it is playable.");
        var noOptIn = rules.DeepClone();
        var skill = noOptIn["skills"]!.AsArray().Single(skill => skill!["id"]!.GetValue<string>() == "mobile:longhun")!;
        foreach (var candidate in skill["triggers"]!.AsArray()) candidate!.AsObject().Remove("includeResponseUses");
        try { _ = SkillProgramCatalog.Load(noOptIn.ToJsonString(), presentation); }
        catch (InvalidOperationException e) when (e.Message.Contains("source viewAs does not support window", StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("A response-only conversion must still reject a committed-use observer without explicit response-use admission.");
    }

    private static void SingleActionAdmissionKeepsExactContractValidation()
    {
        Require(!ProgramCommittedUseContracts.AllowsSingleActionInstance("fixture:unregistered") &&
            !ProgramCommittedUseContracts.AllowsSingleActionInstance("mobile:longhun") &&
            ProgramCommittedUseContracts.AllowsSingleActionInstance("happy:longhun"),
            "Single-action admission is immutable registered metadata; existing contracts retain their default restriction.");
        var rules = JsonNode.Parse(ReadResource("happy-shen-zhao-yun.rules.json"))!;
        var presentation = ReadResource("happy-shen-zhao-yun.presentation.json");
        var admitted = SkillProgramCatalog.Load(rules.ToJsonString(), presentation).Programs["happy:longhun"];
        Require(admitted.Triggers.Count(t => t.SingleActionInstance && t.IncludeResponseUses &&
            t.Window == SkillProgramTriggerWindow.CardUseCommitted && t.Effects is [{ Op: SkillProgramEffectOp.Draw, Amount: 1 }]) == 2,
            "Both exact red conversions issue one draw per original use, including multi-target uses.");
        var changed = rules["skills"]!.AsArray().Single(skill => skill!["id"]!.GetValue<string>() == "happy:longhun")!["triggers"]!
            .AsArray().First(trigger => trigger!["singleActionInstance"]?.GetValue<bool>() == true)!;
        changed["effects"]![0]!["amount"] = 2;
        try { _ = SkillProgramCatalog.Load(rules.ToJsonString(), presentation); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Registered single-action metadata must still validate the complete exact trigger shape.");
    }

    private static string ReadResource(string suffix)
    {
        var assembly = typeof(StandardClassicGeneralPackage).Assembly;
        var name = assembly.GetManifestResourceNames().Single(name => name.EndsWith(suffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void Reject(IProgramCommittedUseContract[] contracts)
    {
        try { _ = ProgramCommittedUseContracts.Build(contracts); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Ambiguous, mutable or anonymous contracts must fail before content registration.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class ValidContract : IProgramCommittedUseContract
    {
        public string SkillId => "fixture:committed-use";
        public void ValidateTrigger(SkillProgramTrigger trigger) { }
    }
    private sealed class MutableContract : IProgramCommittedUseContract
    {
        public int Calls;
        public string SkillId => "fixture:mutable";
        public void ValidateTrigger(SkillProgramTrigger trigger) => Calls++;
    }
    private sealed class MissingIdentityContract : IProgramCommittedUseContract
    {
        public string SkillId => "";
        public void ValidateTrigger(SkillProgramTrigger trigger) { }
    }
}
