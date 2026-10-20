namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_RecoveryReplacementDamageRides
{
    internal static bool PaidCardUseHpFrameRidesOn(ResolutionFrame ride, ResolutionFrame beneath) =>
        beneath is CardUseFrame { RecoveryPaidContinuation.Kind: RecoveryPaidCardUseKind.DodgeCompletion or RecoveryPaidCardUseKind.CommittedSlash } use &&
        ride is HpChangedTriggerWindowFrame hp && hp.Change.ParentFrameId == use.Id &&
        hp.ResumeFrameId == use.Id && hp.Continuation == PostEventContinuation.RecoveryPaidCardUse;
}
