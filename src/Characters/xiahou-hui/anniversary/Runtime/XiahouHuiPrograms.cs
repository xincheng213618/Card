using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryXiahouHuiHost { SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class XiahouHuiOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new XiahouHuiOperationHandler(Op);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.BindAnniversaryXiahouHuiDujun ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy => new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(3));
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeDrawInvocation);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        if (Op != SkillProgramEffectOp.GiveAnniversaryXiahouHuiDujunDrawnCards) return ParseOwnerAlways(r);
        r.AllowOnly("op", "target", "sourceBind", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => Op switch
    {
        SkillProgramEffectOp.BindAnniversaryXiahouHuiDujun => [new RequireTriggerWindow(SkillProgramTriggerWindow.GameStarting), new ReadSelectedTarget()],
        SkillProgramEffectOp.GiveAnniversaryXiahouHuiDujunDrawnCards => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied), new GiftCardSet(e.SourceBind!)],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)]
    };
}
internal sealed class BindAnniversaryXiahouHuiDujunDescriptor : XiahouHuiOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.BindAnniversaryXiahouHuiDujun; }
internal sealed class GiveAnniversaryXiahouHuiDujunDrawnCardsDescriptor : XiahouHuiOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiveAnniversaryXiahouHuiDujunDrawnCards; }
internal sealed class ResolveAnniversaryXiahouHuiJikunDescriptor : XiahouHuiOperationDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryXiahouHuiJikun; }
internal sealed class XiahouHuiOperationHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IAnniversaryXiahouHuiHost>().Execute(f, operation);
}
public enum XiahouHuiStage { Recipient, MovementChildren }
public sealed record XiahouHuiCandidateCard(int CardId, CardLocation From);
public sealed record XiahouHuiReceipt(CardConversionSource Source, string Hash, long ParentId, int Turn, SkillProgramEffectOp Operation,
    XiahouHuiStage Stage = XiahouHuiStage.Recipient, int Recipient = -1, int Donor = -1, int ThresholdSequence = 0,
    int Before = 0, int After = 0, long BatchId = 0, uint RandomBefore = 0, uint RandomAfter = 0, long DrawInvocationId = 0)
{
    private IReadOnlyList<int> _drawn = Array.AsReadOnly(Array.Empty<int>());
    private IReadOnlyList<XiahouHuiCandidateCard> _candidates = Array.AsReadOnly(Array.Empty<XiahouHuiCandidateCard>());
    private IReadOnlyList<CardMovementRecord> _moves = Array.AsReadOnly(Array.Empty<CardMovementRecord>());
    public IReadOnlyList<int> Drawn { get => _drawn; init => _drawn = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<XiahouHuiCandidateCard> Candidates { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardMovementRecord> Moves { get => _moves; init => _moves = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public XiahouHuiReceipt? XiahouHui { get; init; }
}
public sealed record XiahouHuiBoundEvent(long FrameId, CardConversionSource Source, string Hash, long WindowId, int Partner) : IGameEvent;
public sealed record XiahouHuiDamageRecordedEvent(long DamageId, int Turn, int? Source, int Target) : IGameEvent;
public sealed record XiahouHuiLossRecordedEvent(int Owner, string Instance, string Hash, int Sequence, int Count) : IGameEvent;
public sealed record XiahouHuiStartedEvent(long FrameId, CardConversionSource Source, string Hash, long ParentId, int Turn, SkillProgramEffectOp Operation, int ThresholdSequence, int DrawnCount) : IGameEvent;
public sealed record XiahouHuiMovedEvent(long FrameId, int Recipient, int Donor, int Count, long BatchId, int Before, int After) : IGameEvent;
public sealed record XiahouHuiCommittedEvent(long FrameId, int Recipient, int Donor, int Count, int CandidateCount, int Before) : IGameEvent;

internal static class XiahouHuiComposition
{
    internal const string Dujun = "anniversary:dujun", Jikun = "anniversary:jikun", Drawn = "dujun-drawn";
    internal static readonly string[] DamageBindings = ["self-first-caused", "self-first-received", "partner-first-caused", "partner-first-received"];
    [ProgramCatalogValidation(46200)] internal static void Validate(string path, SkillProgram p)
    {
        var local = p.Triggers.SelectMany(t => t.Effects).Concat(p.Activations.SelectMany(a => a.Effects)).Any(e => e.Op is SkillProgramEffectOp.BindAnniversaryXiahouHuiDujun or SkillProgramEffectOp.GiveAnniversaryXiahouHuiDujunDrawnCards or SkillProgramEffectOp.ResolveAnniversaryXiahouHuiJikun);
        if (!local) return;
        bool valid = p.Activations.Count == 0 && p.ViewAs.Count == 0 && p.Modifiers.Count == 0 && p.Contributions.Count == 0 && p.CardPolicies.Count == 0 && p.CardIdentities.Count == 0 && p.BooleanStates.Count == 0;
        if (p.Id == Dujun)
        {
            valid &= p.Triggers.Count == 5 && p.Triggers[0] is { Id: "choose-partner", Window: SkillProgramTriggerWindow.GameStarting, Subject: SkillProgramTriggerSubject.Owner, Optional: false,
                Effects: [{ Op: SkillProgramEffectOp.SelectTarget, Target: SkillProgramEffectTarget.Owner, TargetKind: SkillProgramTargetKind.OtherLiving }, { Op: SkillProgramEffectOp.BindAnniversaryXiahouHuiDujun, Target: SkillProgramEffectTarget.Owner }] };
            for (var i = 0; i < 4 && valid; i++)
            {
                var t = p.Triggers[i + 1];
                valid &= t.Id == DamageBindings[i] && t.Window == SkillProgramTriggerWindow.AfterDamageApplied && t.Subject == (i == 0 ? SkillProgramTriggerSubject.DamageSource : i == 1 ? SkillProgramTriggerSubject.Owner : SkillProgramTriggerSubject.Any) && t.Optional &&
                    t.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage && t.Effects is [
                        { Op: SkillProgramEffectOp.Draw, Target: SkillProgramEffectTarget.Owner, Amount: 2, ResultBind: Drawn },
                        { Op: SkillProgramEffectOp.GiveAnniversaryXiahouHuiDujunDrawnCards, Target: SkillProgramEffectTarget.Owner, SourceBind: Drawn }];
            }
        }
        else if (p.Id == Jikun)
        {
            valid &= p.Triggers.Count == 2;
            for (var i = 0; i < p.Triggers.Count && valid; i++)
            {
                var t = p.Triggers[i];
                valid &= t.Id == (i == 0 ? "five-hand-loss" : "five-equipment-loss") && t.Window == SkillProgramTriggerWindow.CardsMoved && t.Subject == SkillProgramTriggerSubject.Owner && t.Optional &&
                    t.MovementOccurrence == SkillProgramMovementOccurrence.PerCard && t.SourceZones.SequenceEqual(new[] { i == 0 ? CardZoneKind.Hand : CardZoneKind.Equipment }) &&
                    t.Effects is [{ Op: SkillProgramEffectOp.ResolveAnniversaryXiahouHuiJikun, Target: SkillProgramEffectTarget.Owner }];
            }
        }
        else valid = false;
        if (!valid || p.Triggers.Any(t => t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.Effects.Any(e => e.Condition.Kind != SkillProgramConditionKind.Always)))
            throw new InvalidOperationException(path + ": Xiahou Hui requires her exact original partner, four per-turn damage roles and per-card HE loss thresholds.");
    }
}
