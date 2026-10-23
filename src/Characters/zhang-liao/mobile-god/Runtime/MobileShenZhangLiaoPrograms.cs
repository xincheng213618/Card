using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum MobileShenZhangLiaoStage { AbolishSlot, AbolishChildren, ChooseSkill, RestoreSlot, RestoreChildren }
public sealed record MobileShenZhangLiaoReceipt(CardConversionSource Source, string GameplayHash, SkillProgramEffectOp Operation,
    long ParentFrameId, int TargetSeat, MobileShenZhangLiaoStage Stage, EquipmentSlot? PaidSlot = null,
    long Before = 0, long After = 0, string? SelectedSkillId = null);
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MobileShenZhangLiaoReceipt? MobileShenZhangLiao { get; init; } }
public sealed record MobileDuoruiIssuedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int TargetSeat,
    string SelectedSkillId, string BorrowGrantId, string BorrowInstanceId, int ActualTurn, EquipmentSlot PaidSlot) : IGameEvent;
public sealed record MobileDuoruiExpiredEvent(long FrameId, int TargetSeat, int ActualTurn, bool Died) : IGameEvent;
public sealed record MobileDuoruiSlotPaidEvent(long FrameId, CardConversionSource Source, EquipmentSlot Slot, long Before, long After) : IGameEvent;
public sealed record MobileZhitiSlotRestoredEvent(long FrameId, CardConversionSource Source, EquipmentSlot Slot) : IGameEvent;

public enum MobileZhitiContestKind { PairPindian, SimultaneousPindian, Duel }
public sealed record MobileZhitiContestReceipt(MobileZhitiContestKind Kind, long HostId, int WinnerSeat, int LoserSeat,
    int SourceCardId = 0, int OpponentCardId = 0, int SourceRank = 0, int OpponentRank = 0, int Occurrence = 0, int Cursor = 0, bool Finished = false)
{
    private IReadOnlyList<ProgramTriggerCandidate> _candidates = Array.AsReadOnly(Array.Empty<ProgramTriggerCandidate>());
    public IReadOnlyList<ProgramTriggerCandidate> Candidates { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
}
public abstract partial record ResolutionFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public MobileZhitiContestReceipt? MobileZhitiContest { get; init; } }
public sealed record MobileZhitiContestIssuedEvent(long HostId, MobileZhitiContestKind Kind, int WinnerSeat, int LoserSeat,
    int SourceCardId, int OpponentCardId, int SourceRank, int OpponentRank, int Occurrence, int Count) : IGameEvent;

internal interface IMobileShenZhangLiaoHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class MobileShenZhangLiaoDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler { get; } = new MobileShenZhangLiaoHandler(op);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => op == SkillProgramEffectOp.ResolveMobileDuoruiBorrowedSkill
        ? [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)]
        : [new RequireTriggerWindows([SkillProgramTriggerWindow.AfterDamageApplied, SkillProgramTriggerWindow.PindianResultDetermined, SkillProgramTriggerWindow.DuelResponseFailed])];
}
internal sealed class ResolveMobileDuoruiBorrowedSkillDescriptor() : MobileShenZhangLiaoDescriptor(SkillProgramEffectOp.ResolveMobileDuoruiBorrowedSkill);
internal sealed class RestoreMobileZhitiEquipmentSlotDescriptor() : MobileShenZhangLiaoDescriptor(SkillProgramEffectOp.RestoreMobileZhitiEquipmentSlot);
internal sealed class MobileShenZhangLiaoHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{ public SkillProgramEffectOp Op => op; public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => h.GetCapability<IMobileShenZhangLiaoHost>().Resolve(f, op); }
internal static class MobileShenZhangLiaoComposition
{
    internal const string Duorui = "mobile:duorui", Zhiti = "mobile:zhiti";
    [ProgramCatalogValidation(39950)] internal static void Validate(string path, SkillProgram p)
    {
        if (!p.Triggers.SelectMany(t => t.Effects).Any(e => e.Op is SkillProgramEffectOp.ResolveMobileDuoruiBorrowedSkill or SkillProgramEffectOp.RestoreMobileZhitiEquipmentSlot)) return;
        var simple = p.Activations.Count == 0 && p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.BooleanStates.Count == 0 && p.CardIdentities.Count == 0 && p.Contributions.Count == 0;
        var valid = simple && (p.Id == Duorui && p.CardPolicies.Count == 0 && p.Triggers is [
            { Id: "borrow-after-damage", Window: SkillProgramTriggerWindow.AfterDamageApplied, Subject: SkillProgramTriggerSubject.DamageSource, Optional: true, DamageOccurrence: SkillProgramDamageOccurrence.PerDamage,
                Effects: [{ Op: SkillProgramEffectOp.ResolveMobileDuoruiBorrowedSkill, Target: SkillProgramEffectTarget.Owner }] }] ||
            p.Id == Zhiti && p.CardPolicies is [{ Id: "wounded-range-penalty", Kind: SkillProgramCardPolicyKind.WoundedInRangeHandLimitPenalty, Condition.Kind: SkillProgramConditionKind.Always }] && p.Triggers is [
                { Id: "restore-after-damage", Window: SkillProgramTriggerWindow.AfterDamageApplied, Subject: SkillProgramTriggerSubject.Owner, Optional: false, DamageOccurrence: SkillProgramDamageOccurrence.PerDamage,
                    Effects: [{ Op: SkillProgramEffectOp.RestoreMobileZhitiEquipmentSlot, Target: SkillProgramEffectTarget.Owner }] },
                { Id: "restore-pindian-win", Window: SkillProgramTriggerWindow.PindianResultDetermined, Subject: SkillProgramTriggerSubject.Owner, Optional: false,
                    Effects: [{ Op: SkillProgramEffectOp.RestoreMobileZhitiEquipmentSlot, Target: SkillProgramEffectTarget.Owner }] },
                { Id: "restore-duel-win", Window: SkillProgramTriggerWindow.DuelResponseFailed, Subject: SkillProgramTriggerSubject.Owner, Optional: false,
                    Effects: [{ Op: SkillProgramEffectOp.RestoreMobileZhitiEquipmentSlot, Target: SkillProgramEffectTarget.Owner }] }]);
        if (!valid) throw new InvalidOperationException(path + ": mobile Shen Zhang Liao requires his exact paid equipment-slot borrowing and all three native restoration boundaries.");
    }
}
