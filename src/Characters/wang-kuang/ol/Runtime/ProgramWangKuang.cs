using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum WangKuangRenxiaBranch { Discard, Draw }
public enum WangKuangRenxiaStage { BranchChoice, DiscardChoice, MovementChildren }
public sealed record WangKuangRenxiaPhaseKey(int ActualTurn, int OwnerSeat, int PhaseInstance);
public sealed record WangKuangRenxiaCard(int CardId, CardLocation From);
public sealed record WangKuangRenxiaReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    WangKuangRenxiaPhaseKey Phase, bool Ending, long? ObligationId, WangKuangRenxiaStage Stage,
    WangKuangRenxiaBranch? Branch = null, int RequiredCount = 0, int Cursor = 0, long Before = 0, long After = 0, int ActualCount = 0)
{
    private readonly IReadOnlyList<WangKuangRenxiaCard> _candidates = Array.AsReadOnly(Array.Empty<WangKuangRenxiaCard>());
    private readonly IReadOnlyList<WangKuangRenxiaCard> _selected = Array.AsReadOnly(Array.Empty<WangKuangRenxiaCard>());
    private readonly IReadOnlyList<int> _grain = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<WangKuangRenxiaCard> Candidates { get => _candidates; init => _candidates = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<WangKuangRenxiaCard> Selected { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> WoodenOxGrainIds { get => _grain; init => _grain = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WangKuangRenxiaReceipt? WangKuangRenxia { get; init; } }
public sealed partial record ProgramLifecycleTriggerWindowFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WangKuangRenxiaPhaseKey? WangKuangRenxiaEndingPhase { get; init; } }
public sealed record WangKuangRenxiaStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    WangKuangRenxiaPhaseKey Phase, bool Ending, long? ObligationId) : IGameEvent;
public sealed record WangKuangRenxiaBranchChosenEvent(long FrameId, WangKuangRenxiaBranch Branch) : IGameEvent;
public sealed record WangKuangRenxiaIterationPaidEvent(long FrameId, WangKuangRenxiaBranch Branch, int Cursor, long Before, long After, int ActualCount) : IGameEvent;
public sealed record WangKuangRenxiaDiscardEntityEvent(long FrameId, int Cursor, int CardId, CardLocation From, long Sequence) : IGameEvent;
public sealed record WangKuangRenxiaTailClosedEvent(long FrameId, int Cursor, long After) : IGameEvent;
public sealed record WangKuangRenxiaObligationIssuedEvent(long ActiveFrameId, CardConversionSource Source, string GameplayHash,
    WangKuangRenxiaPhaseKey Phase, WangKuangRenxiaBranch FirstBranch) : IGameEvent;
public sealed record WangKuangRenxiaEndingConsumedEvent(long ActiveFrameId, long EndingFrameId, long WindowId, WangKuangRenxiaPhaseKey Phase) : IGameEvent;
public sealed record WangKuangRenxiaComparedEvent(long FrameId, int Cursor, bool HandHasDamage, bool Repeat) : IGameEvent;
public sealed record WangKuangRenxiaCompletedEvent(long FrameId, bool Ending, WangKuangRenxiaBranch? Branch, int Iterations, string Reason) : IGameEvent;

internal interface IWangKuangProgramHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, bool ending); }
internal sealed class WangKuangHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<IWangKuangProgramHost>().Resolve(frame, op == SkillProgramEffectOp.ResolveRenxiaOppositeAtPlayEnding);
}
internal abstract class WangKuangDescriptor(SkillProgramEffectOp op) : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => op;
    public override ISkillProgramEffectHandler Handler { get; } = new WangKuangHandler(op);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(3));
}
internal sealed class ResolveRenxiaRepeatedBranchDescriptor() : WangKuangDescriptor(SkillProgramEffectOp.ResolveRenxiaRepeatedBranch)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => []; }
internal sealed class ResolveRenxiaOppositeAtPlayEndingDescriptor() : WangKuangDescriptor(SkillProgramEffectOp.ResolveRenxiaOppositeAtPlayEnding)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding)]; }

internal static class WangKuangComposition
{
    private static bool Own(SkillProgramEffectOp op) => op is SkillProgramEffectOp.ResolveRenxiaRepeatedBranch or SkillProgramEffectOp.ResolveRenxiaOppositeAtPlayEnding;
    internal static void ValidateProgram(string path, SkillProgram p)
    {
        if (!p.Activations.Any(a => a.Effects.Any(e => Own(e.Op))) && !p.Triggers.Any(t => t.Effects.Any(e => Own(e.Op)))) return;
        if (p.Id != "ol:renxia" || p.Activations.Count != 1 || p.Triggers.Count != 1 || p.Modifiers.Count != 0 || p.ViewAs.Count != 0 || p.CardPolicies.Count != 0 ||
            p.Contributions.Count != 0 || p.CardIdentities.Count != 0 || p.BooleanStates.Count != 0 || p.DamageModifiers.Count != 0 || p.HpSkillGrants.Count != 0 ||
            p.EquipmentSuitSkillGrants.Count != 0 || p.LordSkillProjection || p.CannotChallengeDeclarations)
            throw new InvalidOperationException(path + ": Renxia requires its unique paired original activation and issued phase-ending trigger.");
        var a = p.Activations[0]; var t = p.Triggers[0];
        if (a.Id != "choose-first-branch" || a.Effects is not [{ Op: SkillProgramEffectOp.ResolveRenxiaRepeatedBranch, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            a.MinCards != 0 || a.MaxCards != 0 || a.MinTargets != 0 || a.MaxTargets != 0 || a.TargetKind != SkillProgramTargetKind.AnyLiving || a.UsesPerPhase != 1 ||
            a.UsesPerTurn is not null || a.UsesPerGame is not null || a.UsageGroup != a.Id || a.Condition.Kind != SkillProgramConditionKind.Always || a.MarkerCost is not null ||
            a.CardCountExpression is not null || a.ContinueAfterOwnerDeath || a.CategoryTargetLedgerId is not null || a.TargetPhaseLedgerId is not null || a.SelectedCardsSameSuit ||
            a.SelectedCardsDistinctSuits || a.TargetRequiresEmptyEquipmentSlot || !a.SourceZones.SequenceEqual([CardZoneKind.Hand]) || a.CardKinds.Count != 0 ||
            a.CardSuits.Count != 0 || a.CardCategories.Count != 0 || a.EquipmentSlots.Count != 0 ||
            t.Id != "issued-opposite-at-play-end" || t.Effects is not [{ Op: SkillProgramEffectOp.ResolveRenxiaOppositeAtPlayEnding, Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }] ||
            t.Window != SkillProgramTriggerWindow.PlayEnding || t.Subject != SkillProgramTriggerSubject.Owner || t.Optional || t.OwnerRelation is not null ||
            t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own || t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.UsageLimit is not null || t.UsageScope is not null ||
            t.ChoiceGroup is not null || t.NamedUsageGroup is not null || t.SourceSkillId is not null || t.SourceViewAsId is not null || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 ||
            t.SourceZones.Count != 0 || t.DestinationZones.Count != 0 || t.Suits.Count != 0 || t.DamageCardKinds.Count != 0 || t.MovementReasons.Count != 0 || t.ExcludedMovementReasons.Count != 0 ||
            t.ExcludedReasons.Count != 0 || t.JudgmentReasons.Count != 0 || t.JudgmentSource is not null || t.MovementOccurrence is not null || t.DamageOccurrence is not null ||
            t.RequireDamageSource is not null || t.RequireNoCardConversion is not null || t.MarkerCost is not null || t.DynamicUsageLimit is not null || t.GainPhaseQualification is not null ||
            t.AllowNoEventTarget || t.IncludeResponseUses || t.SingleActionInstance || t.NoDyingAtActivation || t.DeferredTurnEndOnly || t.OnlyDesignatedCardTargets ||
            t.AllowOwnDiscardPhaseEnded || t.EvaluateConditionAtResolution || t.IgnoreOwnSkillMovements || t.MovementDiscardOnly)
            throw new InvalidOperationException(path + ": Renxia requires an exact once-per-Play zero-input choice and unconditional issued own PlayEnding operation.");
    }
}
