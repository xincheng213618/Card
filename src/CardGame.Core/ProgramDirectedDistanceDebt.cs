using System.Text.Json.Serialization;
namespace CardGame.Core;

public sealed record FixedDistanceOneTurnGrant(long ProgramFrameId, int InstructionIndex,
    CardConversionSource Source, string GameplayHash, int ActualTurnNumber, int ActualTurnOwnerSeat, int TargetSeat,
    string EndingBindingId);
public sealed record FixedDistanceOneTurnGrantedEvent(FixedDistanceOneTurnGrant Grant) : IGameEvent;
public sealed record FixedDistanceOneDebtConsumedEvent(FixedDistanceOneTurnGrant Grant, bool RequiredDiscard) : IGameEvent;
public sealed record FixedDistanceOneDebtPaidEvent(long ProgramFrameId, FixedDistanceOneTurnGrant Grant,
    int CardId, CardLocation From, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record FixedDistanceOneDebtPayment(FixedDistanceOneTurnGrant Grant, int InstructionIndex,
    int? CardId = null, CardLocation? From = null, long SequenceBefore = 0, long SequenceAfter = 0);

public sealed record ShortRangeSlashTargetReceipt(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    long CardUseFrameId, long? ActionId, int ActorSeat, int AddedTargetSeat, IReadOnlyList<int> OriginalTargets)
{
    private IReadOnlyList<int> _targets = Array.AsReadOnly(OriginalTargets.ToArray());
    public IReadOnlyList<int> OriginalTargets
    { get => _targets; init => _targets = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray()); }
}
public sealed record ShortRangeSlashTargetResolvedEvent(long CardUseFrameId, long? ActionId,
    CardConversionSource Source, bool Added, ShortRangeSlashTargetReceipt? Receipt) : IGameEvent;
// Scalar proof for native redirect of this batch's issued short-range Slash only.
public sealed record ProgramActualSlashTargetRedirectedEvent(long ProgramFrameId, long CardUseFrameId, long? ActionId,
    CardConversionSource Source, string GameplayHash, int InstructionIndex, int TargetIndex,
    int OriginalTargetSeat, int NewTargetSeat) : IGameEvent;
public sealed record ShortRangeSlashTargetDraft(long CardUseFrameId, long? ActionId, int InstructionIndex);
public sealed record ShortRangeSlashResponseRequirement(int ActorSeat, int TargetSeat, int RequiredDodgeResponses);

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FixedDistanceOneDebtPayment? FixedDistanceDebtPayment { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public ShortRangeSlashTargetDraft? ShortRangeSlashTargetDraft { get; init; }
}
public sealed partial record ProgramSkillWindowContext
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public FixedDistanceOneTurnGrant? FixedDistanceDebt { get; init; }
}
internal interface IDirectedDistanceDebtHost
{
    SkillProgramStepOutcome OfferShortRangeSlashTarget(ProgramSkillFrame frame);
    void GrantFixedDistanceOneTurnPolicy(ProgramSkillFrame frame, int targetSeat);
    SkillProgramStepOutcome SettleFixedDistanceOneEndingDebt(ProgramSkillFrame frame);
}
internal abstract class DirectedDistanceDebtDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new DirectedDistanceDebtHandler(Op);
    public override ProgramOperationInteraction Interaction => Op == SkillProgramEffectOp.GrantFixedDistanceOneTurnPolicy
        ? ProgramOperationInteraction.Automatic : ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnCardTargetRestriction,
        static (_, context) => context.PublicControlValue(6));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var target = reader.RequiredEnum<SkillProgramEffectTarget>("target");
        if (target != (Op == SkillProgramEffectOp.GrantFixedDistanceOneTurnPolicy ? SkillProgramEffectTarget.SelectedTarget : SkillProgramEffectTarget.Owner))
            throw new InvalidOperationException("A fixed-distance or short-range operation has an exact owner/selected-target contract.");
        var effect = new SkillProgramEffect(Op, target, 0, reader.Condition());
        RequireAlways(effect, reader.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => Op switch
    {
        SkillProgramEffectOp.GrantFixedDistanceOneTurnPolicy => [new ReadSelectedTarget()],
        SkillProgramEffectOp.OfferShortRangeSlashTarget => [new RequireTriggerWindows([
            SkillProgramTriggerWindow.CardUseTargetsFinalized, SkillProgramTriggerWindow.OtherActualUseTargeted])],
        _ => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)]
    };
}
internal sealed class OfferShortRangeSlashTargetDescriptor : DirectedDistanceDebtDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferShortRangeSlashTarget; }
internal sealed class GrantFixedDistanceOneTurnPolicyDescriptor : DirectedDistanceDebtDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantFixedDistanceOneTurnPolicy; }
internal sealed class SettleFixedDistanceOneEndingDebtDescriptor : DirectedDistanceDebtDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.SettleFixedDistanceOneEndingDebt; }
internal sealed class DirectedDistanceDebtHandler(SkillProgramEffectOp operation) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => operation;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int target, ISkillProgramEffectHost host)
    {
        var typed = (IDirectedDistanceDebtHost)host;
        if (Op == SkillProgramEffectOp.OfferShortRangeSlashTarget) return typed.OfferShortRangeSlashTarget(frame);
        if (Op == SkillProgramEffectOp.SettleFixedDistanceOneEndingDebt) return typed.SettleFixedDistanceOneEndingDebt(frame);
        typed.GrantFixedDistanceOneTurnPolicy(frame, target); return SkillProgramStepOutcome.Continue;
    }
}
internal static class DirectedDistanceDebtComposition
{
    internal static bool IsOperation(SkillProgramEffectOp op) => op is SkillProgramEffectOp.OfferShortRangeSlashTarget or
        SkillProgramEffectOp.GrantFixedDistanceOneTurnPolicy or SkillProgramEffectOp.SettleFixedDistanceOneEndingDebt;
    internal static void ValidateActivation(string path, SkillProgramActivation activation)
    {
        if (!activation.Effects.Any(e => IsOperation(e.Op))) return;
        if (activation.Effects is not [{ Op: SkillProgramEffectOp.GrantFixedDistanceOneTurnPolicy }] ||
            activation.MinCards != 0 || activation.MaxCards != 0 || activation.MinTargets != 1 || activation.MaxTargets != 1 ||
            activation.TargetKind != SkillProgramTargetKind.OtherLiving || activation.UsesPerPhase != 1 || activation.UsesPerTurn is not null)
            throw new InvalidOperationException(path + ": fixed-distance issuance is a zero-card once-per-actual-Play selected-other activation.");
    }
    internal static void ValidateTrigger(string path, IReadOnlyList<SkillProgramEffect> effects,
        SkillProgramTriggerWindow window, SkillProgramTriggerSubject? subject, SkillProgramTurnOwnerScope scope,
        bool optional, SkillProgramCardActionOwnerRelation? relation, IReadOnlyList<CardKind> kinds)
    {
        if (!effects.Any(e => IsOperation(e.Op)) && scope != SkillProgramTurnOwnerScope.IssuedFixedDistanceEnding) return;
        if (effects is not [var effect]) throw new InvalidOperationException(path + ": directed-distance operations require one exact instruction.");
        var valid = effect.Op switch
        {
            SkillProgramEffectOp.OfferShortRangeSlashTarget => window == SkillProgramTriggerWindow.CardUseTargetsFinalized &&
                relation == SkillProgramCardActionOwnerRelation.Actor && !optional && kinds.Count == 3 &&
                kinds.ToHashSet().SetEquals([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) ||
                window == SkillProgramTriggerWindow.OtherActualUseTargeted && subject == SkillProgramTriggerSubject.Owner && optional,
            SkillProgramEffectOp.SettleFixedDistanceOneEndingDebt => window == SkillProgramTriggerWindow.TurnEnding &&
                subject == SkillProgramTriggerSubject.Owner && scope == SkillProgramTurnOwnerScope.IssuedFixedDistanceEnding && !optional,
            _ => false
        };
        if (!valid) throw new InvalidOperationException(path + ": short-range or distance debt lost its exact real-use/actual-Ending entry.");
    }
    internal static void ValidatePrograms(IReadOnlyDictionary<string, SkillProgram> programs)
    {
        foreach (var program in programs.Values)
        {
            var grants = program.Activations.Where(a => a.Effects.Any(e => e.Op == SkillProgramEffectOp.GrantFixedDistanceOneTurnPolicy)).ToArray();
            var debts = program.Triggers.Where(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.SettleFixedDistanceOneEndingDebt)).ToArray();
            if (grants.Length == 0 && debts.Length == 0) continue;
            if (grants.Length != 1 || debts.Length != 1) throw new InvalidOperationException(program.Id + ": a directed-distance producer requires its one exact frozen-source debt resolver.");
        }
    }
}
