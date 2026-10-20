using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum WuAnGuoLiyongStage { GainChildren, DuelIssued }
public sealed record WuAnGuoLiyongReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    int ActualTurn, int PhaseInstance, int InitialActorSeat, int UsedSuitMask, long Before, long After, int? ObtainedCardId,
    WuAnGuoLiyongStage Stage = WuAnGuoLiyongStage.GainChildren, WuAnGuoLiyongDuelOrigin? Duel = null);
public sealed record WuAnGuoLiyongDuelOrigin(long ProgramFrameId, int InstructionIndex, long CardUseFrameId,
    CardConversionSource Source, string GameplayHash, int ActualTurn, int PhaseInstance, int ActorSeat, int TargetSeat, bool AttackStarted = false);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WuAnGuoLiyongReceipt? WuAnGuoLiyong { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WuAnGuoLiyongDuelOrigin? WuAnGuoLiyong { get; init; }
}
public sealed record WuAnGuoActualSuitUsedEvent(int ActualTurn, int TurnOwnerSeat, int ActorSeat, long ActionId, Suit Suit) : IGameEvent;
public sealed record WuAnGuoLiyongYangConsumedEvent(long UseFrameId, long ActionId, CardConversionSource Source, int ActualTurn, Suit Suit) : IGameEvent;
public sealed record WuAnGuoLiyongYinStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, int ActualTurn, int PhaseInstance, int InitialActorSeat, int UsedSuitMask) : IGameEvent;
public sealed record WuAnGuoLiyongGainPaidEvent(long FrameId, int? CardId, long Before, long After) : IGameEvent;
public sealed record WuAnGuoLiyongDuelIssuedEvent(WuAnGuoLiyongDuelOrigin Origin) : IGameEvent;
public sealed record WuAnGuoLiyongCompletedEvent(long FrameId, bool DuelIssued) : IGameEvent;

public interface IWuAnGuoProgramHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
public sealed class ResolveLiyongUsedSuitDeckAndDuelHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveLiyongUsedSuitDeckAndDuel;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost host) => host.GetCapability<IWuAnGuoProgramHost>().Resolve(f);
}
internal sealed class ResolveLiyongUsedSuitDeckAndDuelDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveLiyongUsedSuitDeckAndDuel;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override ISkillProgramEffectHandler Handler { get; } = new ResolveLiyongUsedSuitDeckAndDuelHandler();
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new ReadSelectedTarget(), new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLiving)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption, static (_, c) => c.PublicControlValue(2));
}
internal static class WuAnGuoComposition
{
    [ProgramCatalogValidation(280)]
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.Activations.Any(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveLiyongUsedSuitDeckAndDuel)) &&
            !p.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveLiyongUsedSuitDeckAndDuel)) &&
            !(p.Id == "ol:liyong" && p.ViewAs.Count != 0)) return;
        if (p.Id != "ol:liyong" || p.Activations is not [var a] || p.Triggers.Count != 0 || p.ViewAs is not [var v] ||
            a.Effects is not [{ Op: SkillProgramEffectOp.ResolveLiyongUsedSuitDeckAndDuel, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            a.Id != "yin-obtain-and-incoming-duel" || a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 1 || a.MaxTargets != 1 || a.TargetKind != SkillProgramTargetKind.OtherLiving ||
            a.UsesPerPhase is not null || a.UsesPerTurn is not null || a.UsesPerGame is not null || a.Condition is not { Kind: SkillProgramConditionKind.BooleanState, StateId: "yin", ExpectedValue: true } ||
            a.ContinueAfterOwnerDeath || a.MarkerCost is not null || a.CardCountExpression is not null || a.CategoryTargetLedgerId is not null || a.TargetPhaseLedgerId is not null ||
            a.SelectedCardsSameSuit || a.SelectedCardsDistinctSuits || a.TargetRequiresEmptyEquipmentSlot || a.EquipmentSlots.Count != 0 || a.CardKinds.Count != 0 || a.CardSuits.Count != 0 || a.CardCategories.Count != 0 ||
            !a.SourceZones.SequenceEqual(new[] { CardZoneKind.Hand }) || v.Id != "yang-unused-suit" || v.OutputKind != CardKind.Duel || v.InputCount != 1 || !v.ForPlay || v.ForResponse || !v.AllowSameKind || !v.SingleCardTrickUse ||
            !v.SourceZones.SequenceEqual(new[] { CardZoneKind.Hand, CardZoneKind.Equipment }) || v.Condition.Kind != SkillProgramConditionKind.Always ||
            v.InputKinds.Count != 0 || v.InputSuits.Count != 0 || v.InputCategories.Count != 0 || v.ConversionStateId is not null || v.UsesPerPhase is not null || v.UsesPerActualTurn is not null ||
            v.AllowChainedInput || v.SameSuit || v.InheritPreviousPlaySuit || v.VariableInputCount || v.AllHandCardsOfSelectedSuit || v.DistanceUnlimited || v.NonBasicUnrespondableSlash ||
            v.UseEffectiveInputSuit is not null || v.ExtendedUse || v.DamageBonus != 0 || v.RecoveryBonus != 0 || v.CostDestination is not null || v.UnusedOutputThisTurn ||
            v.UseOnly || v.NoDying || v.UnusedOutputNameThisGame || v.NameLedgerId is not null || v.ExcludeOwnerEffects || v.MinimumTier != 0 || v.MaximumTier != 2 ||
            v.DeclarationValidation is not null || v.DeclaredEntity || v.ActivationUsageGroup is not null || v.UsageGroup is not null || v.TieredRoundConversion is not null ||
            v.RoundOnceCardlessBasic is not null || v.ChainedStateCost is not null || v.RoundDistinctBasicUse is not null || v.DrawFundedDistinctBasic is not null ||
            v.DisableSkillUntilTurnEndIfNoDamage is not null || v.LastInSourceZone is not null || v.RoundGainedRedHandOnly is not null ||
            p.BooleanStates is not [{ Id: "yin", InitialValue: false, Visibility: SkillProgramStateVisibility.Public, ResetScope: SkillProgramStateResetScope.Game, ReacquirePolicy: SkillProgramStateReacquirePolicy.PreserveUntilGameEnd }] ||
            p.Modifiers.Count != 0 || p.Contributions.Count != 0 || p.CardIdentities.Count != 0 || p.DamageModifiers.Count != 0 || p.CardPolicies.Count != 0 || p.HpSkillGrants.Count != 0 || p.EquipmentSuitSkillGrants.Count != 0 ||
            p.LordSkillProjection || p.CannotChallengeDeclarations)
            throw new InvalidOperationException(path + ": Liyong requires its exact unlimited alternating native HE Duel conversion and one complete zero-card incoming-Duel activation.");
    }
}
[ProgramSelectionModule(ProgramSelectionStage.OrdinaryTarget, 27010, SkillProgramEffectOp.ResolveLiyongUsedSuitDeckAndDuel)]
internal sealed class WuAnGuoLiyongActorFilter : IProgramSelectionTargetFilter
{
    public bool Allows(ProgramSelectionContext context, int targetSeat, IProgramSelectionQueries queries) =>
        queries.Get<ISelectedActorDuelSelectionQuery>().CanSelect(context.OwnerSeat, targetSeat);
}
[ProgramSelectionValidator(ProgramSelectionValidationStage.BeforeDistinctSuits, 27010, SkillProgramEffectOp.ResolveLiyongUsedSuitDeckAndDuel)]
internal sealed class WuAnGuoLiyongActorValidator : IProgramSelectionValidator
{
    public CommandError? Validate(ProgramSelectionValidationRequest request, IProgramSelectionQueries queries) =>
        request.Targets is not [var target] || !queries.Get<ISelectedActorDuelSelectionQuery>().CanSelect(request.Context.OwnerSeat, target)
            ? new(CommandErrorCode.InvalidTarget, "Liyong requires one native legal incoming Duel actor.") : null;
}
