using System.Text.Json.Serialization;
namespace CardGame.Core;

internal interface IAnniversaryLiuYouHost { void Execute(ProgramSkillFrame frame, SkillProgramEffectOp operation); }
internal abstract class AnniversaryLiuYouDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ISkillProgramEffectHandler Handler => new AnniversaryLiuYouHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(2));
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeActionAppearance);
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext c) => c.State.CanContinueAfterOwnerDeath(c.Frame);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => Op == SkillProgramEffectOp.PrepareAnniversaryLiuYouKannan
        ? [new ReadSelectedTarget()] : [new ReadPindianResult(AnniversaryLiuYouComposition.Result)];
}
internal sealed class PrepareAnniversaryLiuYouKannanDescriptor : AnniversaryLiuYouDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.PrepareAnniversaryLiuYouKannan; }
internal sealed class ResolveAnniversaryLiuYouKannanDescriptor : AnniversaryLiuYouDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveAnniversaryLiuYouKannan; }
internal sealed class AnniversaryLiuYouHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h)
    { h.GetCapability<IAnniversaryLiuYouHost>().Execute(f, operation); return SkillProgramStepOutcome.Continue; }
}
internal static class AnniversaryLiuYouComposition
{
    internal const string Skill = "anniversary:kannan", Activation = "phase-contest", Result = "kannan-contest";
    [ProgramCatalogValidation(50000)] internal static void Validate(string path, SkillProgram p)
    {
        var owns = p.Activations.SelectMany(a => a.Effects).Concat(p.Triggers.SelectMany(t => t.Effects))
            .Any(e => e.Op is SkillProgramEffectOp.PrepareAnniversaryLiuYouKannan or SkillProgramEffectOp.ResolveAnniversaryLiuYouKannan);
        if (p.Id != Skill && !owns) return;
        if (p.Id != Skill || p.Triggers.Count != 0 || p.ViewAs.Count != 0 || p.Modifiers.Count != 0 || p.CardPolicies.Count != 0 ||
            p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.Activations is not [{ Id: Activation, MinCards: 0, MaxCards: 0, MinTargets: 1, MaxTargets: 1,
                TargetKind: SkillProgramTargetKind.OtherLiving, UsesPerTurn: null, UsesPerPhase: null, Effects: [
                    { Op: SkillProgramEffectOp.PrepareAnniversaryLiuYouKannan, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always },
                    { Op: SkillProgramEffectOp.StartPindian, Target: SkillProgramEffectTarget.Owner, OpponentReference.Kind: ProgramParticipantRef.SelectedTarget, ResultBind: Result, Visibility: SkillProgramCardSetVisibility.Public },
                    { Op: SkillProgramEffectOp.ResolveAnniversaryLiuYouKannan, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] }])
            throw new InvalidOperationException(path + ": Kannan requires an actual-Play issue, the original native Pindian, then its exact public result.");
    }
}
public sealed record AnniversaryLiuYouReceipt(CardConversionSource Source, string Hash, ActualDiscardRecoveryPhaseKey Phase, int Target)
{
    public long? PindianId { get; init; }
    public PindianResult? Result { get; init; }
    public bool Completed { get; init; }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public AnniversaryLiuYouReceipt? AnniversaryLiuYou { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? AnniversaryLiuYouSlashBonus { get; init; } }
public sealed record AnniversaryLiuYouStartedEvent(long FrameId, CardConversionSource Source, string Hash, ActualDiscardRecoveryPhaseKey Phase, int Target) : IGameEvent;
public sealed record AnniversaryLiuYouResultEvent(long FrameId, long PindianId, int SourceRank, int OpponentRank, int Winner) : IGameEvent;
public sealed record AnniversaryLiuYouBonusIssuedEvent(long FrameId, CardConversionSource Source, string Hash, long PindianId, int Beneficiary) : IGameEvent;
public sealed record AnniversaryLiuYouBonusConsumedEvent(long GrantFrameId, long UseId, long ActionId, int Beneficiary) : IGameEvent;
public sealed record AnniversaryLiuYouSlashCapturedEvent(long UseId, long ActionId, int Actor, int Amount) : IGameEvent;

internal interface IAnniversaryLiuYouQueries : IProgramSelectionQuery
{ bool Target(int owner, int target); int Hp(int owner); }
[ProgramSelectionModule(ProgramSelectionStage.AfterTargets, 50000, SkillProgramEffectOp.PrepareAnniversaryLiuYouKannan)]
internal sealed class AnniversaryLiuYouTargets : IProgramSelectionCandidateFilter
{
    public bool Apply(ProgramSelectionContext c, ProgramSelectionCandidates candidates, IProgramSelectionQueries queries)
    { candidates.Targets = candidates.Targets.Where(s => queries.Get<IAnniversaryLiuYouQueries>().Target(c.OwnerSeat, s)).ToArray(); return true; }
}
[ProgramSelectionModule(ProgramSelectionStage.PhaseLimit, 50000, SkillProgramEffectOp.PrepareAnniversaryLiuYouKannan)]
internal sealed class AnniversaryLiuYouLimit : IProgramSelectionPhaseLimitProvider
{ public int? GetLimit(ProgramSelectionContext c, IProgramSelectionQueries queries) => Math.Max(0, queries.Get<IAnniversaryLiuYouQueries>().Hp(c.OwnerSeat)); }
[ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 50000, SkillProgramEffectOp.PrepareAnniversaryLiuYouKannan)]
internal sealed class AnniversaryLiuYouTargetValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest r, IProgramSelectionQueries queries) => r.Cards.Count == 0 && r.Targets is [var target] &&
        queries.Get<IAnniversaryLiuYouQueries>().Target(r.Context.OwnerSeat, target) ? null : new(CommandErrorCode.InvalidTarget, "戡难须选择本实际出牌阶段尚未以此法拼点且可合法拼点的其他角色。");
}
