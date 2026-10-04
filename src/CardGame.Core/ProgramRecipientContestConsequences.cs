namespace CardGame.Core;

public enum BlackGiftContestStage { GiftPaid, ChoosingSecond, Pindian, ChoosingDiscard, DiscardPaid, LossReady, LossPaid, Complete }
public sealed record BlackGiftContestDiscard(int WinnerSeat, IReadOnlyList<int> CardIds,
    IReadOnlyList<CardLocation> Locations, long Before, long After)
{
    private readonly IReadOnlyList<int> _cardIds = Array.AsReadOnly(CardIds.ToArray());
    public IReadOnlyList<int> CardIds { get => _cardIds; init => _cardIds = Array.AsReadOnly(value.ToArray()); }
    private readonly IReadOnlyList<CardLocation> _locations = Array.AsReadOnly(Locations.ToArray());
    public IReadOnlyList<CardLocation> Locations { get => _locations; init => _locations = Array.AsReadOnly(value.ToArray()); }
}
public sealed record BlackGiftContestLoss(int Seat, int BeforeHp, int AfterHp);
public sealed record BlackGiftContestReceipt(int InstructionIndex, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int TurnOwnerSeat, int RecipientSeat, int GiftCardId, Suit GiftSuit, long GiftBefore, long GiftAfter,
    BlackGiftContestStage Stage = BlackGiftContestStage.GiftPaid, int? SecondSeat = null, long? PindianFrameId = null,
    PindianResult? Result = null, int RequiredDiscards = 0, IReadOnlyList<int>? SelectedDiscardIds = null,
    BlackGiftContestDiscard? Discard = null, int LossIndex = 0, IReadOnlyList<BlackGiftContestLoss>? Losses = null)
{
    private readonly IReadOnlyList<int> _selectedDiscardIds = Array.AsReadOnly((SelectedDiscardIds ?? []).ToArray());
    public IReadOnlyList<int> SelectedDiscardIds { get => _selectedDiscardIds; init => _selectedDiscardIds = Array.AsReadOnly((value ?? []).ToArray()); }
    private readonly IReadOnlyList<BlackGiftContestLoss> _losses = Array.AsReadOnly((Losses ?? []).ToArray());
    public IReadOnlyList<BlackGiftContestLoss> Losses { get => _losses; init => _losses = Array.AsReadOnly((value ?? []).ToArray()); }
}
public enum PrintedLordBenefitStage { ChoosingBeneficiary, MaximumPaid, RecoveryPaid, Qualifying, Complete }
public sealed record PrintedLordBenefitReceipt(int InstructionIndex, CardConversionSource Source, string GameplayHash,
    int ActualTurn, long WindowFrameId, PrintedLordBenefitStage Stage, int BeneficiarySeat = -1,
    int MaximumBefore = 0, int MaximumAfter = 0, int RecoveryRequested = 0,
    IReadOnlyList<PrintedLordSkillQualification>? PrintedQualifications = null)
{
    private readonly IReadOnlyList<PrintedLordSkillQualification> _printedQualifications = Array.AsReadOnly((PrintedQualifications ?? []).ToArray());
    public IReadOnlyList<PrintedLordSkillQualification> PrintedQualifications { get => _printedQualifications; init => _printedQualifications = Array.AsReadOnly((value ?? []).ToArray()); }
}
public sealed record BlackGiftContestGiftPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int ActualTurn, int TurnOwnerSeat, int RecipientSeat, long Before, long After) : IGameEvent;
public sealed record BlackGiftContestStartedEvent(long FrameId, int RecipientSeat, int SecondSeat, long PindianFrameId) : IGameEvent;
public sealed record BlackGiftContestResultCapturedEvent(long FrameId, long PindianFrameId, PindianResult Result) : IGameEvent;
public sealed record BlackGiftContestDiscardPaidEvent(long FrameId, int WinnerSeat, int Count, long Before, long After) : IGameEvent;
public sealed record BlackGiftContestLossPaidEvent(long FrameId, int Index, BlackGiftContestLoss Loss) : IGameEvent;
public sealed record PrintedLordMaximumPaidEvent(long FrameId, int BeneficiarySeat, int Before, int After) : IGameEvent;
public sealed record PrintedLordRecoveryRequestedEvent(long FrameId, int BeneficiarySeat, int Amount) : IGameEvent;
public sealed record PrintedLordQualificationCapturedEvent(long FrameId, int BeneficiarySeat) : IGameEvent;

internal interface IRecipientContestConsequencesHost
{
    SkillProgramStepOutcome BlackGiftContest(ProgramSkillFrame frame, int recipientSeat);
    SkillProgramStepOutcome PrintedLordBenefit(ProgramSkillFrame frame);
}
internal sealed class GiveBlackHandAndResolveRecipientContestDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GiveBlackHandAndResolveRecipientContest;
    public override ISkillProgramEffectHandler Handler { get; } = new GiveBlackHandAndResolveRecipientContestHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Gift, static (_, c) => c.BlackGiftContest());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException($"{r.Path}: black gift requires its first other recipient.");
        var e = new SkillProgramEffect(Op, SkillProgramEffectTarget.SelectedTarget, 0, r.Condition()); RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving), new ConsumeSelectedCards(1)];
}
internal sealed class RaiseMaximumRecoverAndQualifyPrintedLordDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RaiseMaximumRecoverAndQualifyPrintedLord;
    public override ISkillProgramEffectHandler Handler { get; } = new RaiseMaximumRecoverAndQualifyPrintedLordHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Recover, static (_, c) => c.PrintedLordBenefit());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    { r.AllowOnly("op", "target", "condition"); var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition()); RequireAlways(e, r.Path); return e; }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
public sealed class GiveBlackHandAndResolveRecipientContestHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GiveBlackHandAndResolveRecipientContest;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => ((IRecipientContestConsequencesHost)host).BlackGiftContest(f, seat);
}
public sealed class RaiseMaximumRecoverAndQualifyPrintedLordHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RaiseMaximumRecoverAndQualifyPrintedLord;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) => ((IRecipientContestConsequencesHost)host).PrintedLordBenefit(f);
}
internal static class RecipientContestConsequencesComposition
{
    internal static void Activation(string path, SkillProgramActivation a)
    {
        if (!a.Effects.Any(e => e.Op == SkillProgramEffectOp.GiveBlackHandAndResolveRecipientContest)) return;
        if (a.Effects is not [{ Op: SkillProgramEffectOp.GiveBlackHandAndResolveRecipientContest }] || a.MinCards != 1 || a.MaxCards != 1 ||
            a.MinTargets != 1 || a.MaxTargets != 1 || a.TargetKind != SkillProgramTargetKind.OtherLiving ||
            a.UsesPerGame != 1 || a.UsesPerTurn is not null || a.UsesPerPhase is not null ||
            !a.SourceZones.SequenceEqual([CardZoneKind.Hand]) || a.CardKinds.Count != 0 || a.CardSuits.Count != 0 ||
            a.CardCategories.Count != 0 || a.Condition.Kind != SkillProgramConditionKind.Always || a.MarkerCost is not null ||
            a.ContinueAfterOwnerDeath || a.CardCountExpression is not null || a.SelectedCardsSameSuit || a.SelectedCardsDistinctSuits ||
            a.EquipmentSlots.Count != 0 || a.TargetRequiresEmptyEquipmentSlot || a.TargetPhaseLedgerId is not null || a.CategoryTargetLedgerId is not null)
            throw new InvalidOperationException($"{path}: black-hand contest is one unconditional genuine Hand gift, first other target and game-limited use.");
    }
    internal static void Trigger(string path, IReadOnlyList<SkillProgramEffect> e, SkillProgramTriggerWindow w,
        SkillProgramTriggerSubject? subject, bool optional, SkillProgramTurnOwnerScope scope, SkillUsageScope? usage, int? limit)
    {
        if (e.Any(x => x.Op == SkillProgramEffectOp.GiveBlackHandAndResolveRecipientContest))
            throw new InvalidOperationException($"{path}: black-hand contest requires its limited actual-Play activation.");
        if (e.Any(x => x.Op == SkillProgramEffectOp.RaiseMaximumRecoverAndQualifyPrintedLord) &&
            (e is not [{ Op: SkillProgramEffectOp.RaiseMaximumRecoverAndQualifyPrintedLord }] ||
             w != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || subject != SkillProgramTriggerSubject.Owner ||
             !optional || scope != SkillProgramTurnOwnerScope.Own || usage != SkillUsageScope.Game || limit != 1))
            throw new InvalidOperationException($"{path}: printed-lord benefit requires the optional own actual-start game-limited operation.");
    }
}
internal sealed partial class ProgramAiEstimateContext
{
    internal void BlackGiftContest() { _otherAdjustment += 12d; _targetDraw += 1d; _targetHpLoss += 1d; }
    internal void PrintedLordBenefit() { _targetRecovery += 1d; _otherAdjustment += 9d; }
}
