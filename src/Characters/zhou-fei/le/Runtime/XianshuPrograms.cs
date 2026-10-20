using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum LeZhouFeiXianshuStage { GiftChildren, Drawing, DrawChildren, HealthIssuing, HealthChildren }
public enum XianshuHealthAction { None, Recover, LoseHp }

public sealed record LeZhouFeiXianshuReceipt(CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, int InstructionIndex, int ActualTurn, int TurnOwner,
    int CardId, CardKind PrintedKind, Suit GiftSuit, int TargetSeat, long SequenceBefore)
{
    public LeZhouFeiXianshuStage Stage { get; init; }
    public long GiftBatchId { get; init; }
    public long GiftSequence { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int RequestedDraw { get; init; } = -1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeDrawOutcome? DrawOutcome { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public XianshuHealthAction? HealthAction { get; init; }
    public bool HpLossCaptured { get; init; }
    public bool AppearancePrevented { get; init; }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LeZhouFeiXianshuReceipt? LeZhouFeiXianshu { get; init; }
}

public sealed record XianshuGiftStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    ActualDiscardRecoveryPhaseKey Phase, int ActualTurn, int TurnOwner, int CardId,
    CardKind PrintedKind, Suit GiftSuit, int TargetSeat, long SequenceBefore) : IGameEvent;
public sealed record XianshuGiftPaidEvent(long FrameId, long BatchId, int CardId,
    CardLocation From, CardLocation To, long MovementSequence) : IGameEvent;
public sealed record XianshuDrawRequestedEvent(long FrameId, bool OwnerHasHp, int OwnerHp,
    bool TargetHasHp, int TargetHp, int RequestedCount) : IGameEvent;
public sealed record XianshuDrawReturnedEvent(long FrameId, int RecipientSeat, int RequestedCount,
    int ActualCount, int ReplacementCount, long Before, long After, long? AttemptFrameId) : IGameEvent;
public sealed record XianshuHealthEvaluatedEvent(long FrameId, Suit GiftSuit, bool OwnerHasHp,
    int OwnerHp, bool TargetHasHp, int TargetHp, bool TargetAlive, XianshuHealthAction Action) : IGameEvent;
public sealed record XianshuHpLossAppliedEvent(long FrameId, int TargetSeat, int Amount,
    int RemainingHp, bool PreventedByAppearance) : IGameEvent;
public sealed record XianshuCompletedEvent(long FrameId, int CardId, int TargetSeat,
    XianshuHealthAction HealthAction) : IGameEvent;

internal interface ILeZhouFeiXianshuHost
{ SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal interface ILeZhouFeiXianshuSelectionQuery : IProgramSelectionQuery
{ bool CanGive(int ownerSeat, Card card); }

[ProgramSelectionModule(ProgramSelectionStage.CardAfterDiscard, 28320,
    SkillProgramEffectOp.ResolveXianshuGiftDrawAndHealth)]
internal sealed class LeZhouFeiXianshuCardFilter : IProgramSelectionCardFilter
{
    public bool Allows(ProgramSelectionContext context, Card card, IProgramSelectionQueries queries) =>
        queries.Get<ILeZhouFeiXianshuSelectionQuery>().CanGive(context.OwnerSeat, card);
}

internal sealed class ResolveXianshuGiftDrawAndHealthDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramFactRequirements FactRequirements => new(Activated: ProgramTrackedFact.ActualDiscardRecoveryPhase);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveXianshuGiftDrawAndHealth;
    public override ProgramOperationAiPolicy AiPolicy { get; } =
        new(ProgramOperationAiSemantic.GiveSelected, static (_, context) => context.PublicControlValue(2));
    public override ISkillProgramEffectHandler Handler { get; } = new LeZhouFeiXianshuHandler();
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving),
            new ConsumeSelectedCards(1)];

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}

internal sealed class LeZhouFeiXianshuHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveXianshuGiftDrawAndHealth;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int target, ISkillProgramEffectHost host) => host.GetCapability<ILeZhouFeiXianshuHost>().Resolve(frame);
}

internal static class XianshuComposition
{
    [ProgramCatalogValidation(1320)]
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        var handles = program.Activations.SelectMany(a => a.Effects).Concat(program.Triggers.SelectMany(t => t.Effects))
            .Any(e => e.Op == SkillProgramEffectOp.ResolveXianshuGiftDrawAndHealth);
        if (!handles && program.Id != "le:xianshu") return;
        if (program.Id != "le:xianshu" || program.Triggers.Count != 0 || program.ViewAs.Count != 0 ||
            program.Modifiers.Count != 0 || program.CardPolicies.Count != 0 || program.CardIdentities.Count != 0 ||
            program.DamageModifiers.Count != 0 || program.HpSkillGrants.Count != 0 || program.BooleanStates.Count != 0 ||
            program.Contributions.Count != 0 || program.Activations is not [{ } activation] ||
            activation.Id != "marked-hand-gift" || activation.MinCards != 1 || activation.MaxCards != 1 ||
            activation.MinTargets != 1 || activation.MaxTargets != 1 ||
            activation.TargetKind != SkillProgramTargetKind.OtherLiving ||
            !activation.SourceZones.SequenceEqual([CardZoneKind.Hand]) ||
            activation.UsesPerTurn is not null || activation.UsesPerPhase is not null || activation.UsesPerGame is not null ||
            activation.Condition.Kind != SkillProgramConditionKind.Always || activation.MarkerCost is not null ||
            activation.CategoryTargetLedgerId is not null || activation.TargetPhaseLedgerId is not null ||
            activation.CardCountExpression is not null || activation.SelectedCardsSameSuit || activation.SelectedCardsDistinctSuits ||
            activation.CardKinds.Count != 0 || activation.CardSuits.Count != 0 || activation.CardCategories.Count != 0 ||
            activation.EquipmentSlots.Count != 0 || activation.TargetRequiresEmptyEquipmentSlot ||
            activation.OtherCharactersOnly || activation.ContinueAfterOwnerDeath || activation.UsageGroup != activation.Id ||
            activation.Effects is not [{ Op: SkillProgramEffectOp.ResolveXianshuGiftDrawAndHealth,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException(path + ": Xianshu requires its unlimited real own-Play, one marked Hand entity, one other recipient and complete native gift/draw/health operation.");
    }
}
