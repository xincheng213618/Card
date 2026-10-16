using CardGame.Content.Standard;
using CardGame.Core;

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
