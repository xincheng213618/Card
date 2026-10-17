namespace CardGame.Core;

internal interface ITeamActivityProgramHost
{
    SkillProgramStepOutcome ExecuteTeamActivity(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal sealed class SelectAllPublicTeamTargetsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectAllPublicTeamTargets;
    public override ISkillProgramEffectHandler Handler { get; } = new TeamActivityEffectHandler(SkillProgramEffectOp.SelectAllPublicTeamTargets);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SelectTargets,
        static (effect, context) => context.SelectTargets(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "targetKind", "allowEmpty", "condition");
        var kind = r.RequiredEnum<SkillProgramTargetKind>("targetKind");
        if (kind is not (SkillProgramTargetKind.PublicTeamEnemies or SkillProgramTargetKind.PublicTeamWoundedAllies or
            SkillProgramTargetKind.OtherPublicTeamWoundedAllies or SkillProgramTargetKind.PublicTeamUnchainedEnemies or
            SkillProgramTargetKind.OtherPublicTeamAllies))
            throw new InvalidOperationException($"{r.Path}: a public-team target set is required.");
        var minimum = r.Has("allowEmpty") && r.RequiredBool("allowEmpty") ? 0 : 1;
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(), targetKind: kind,
            minimumTargets: minimum, maximumTargets: 8);
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new SelectTargetSet(effect.MinimumTargets, 8)];
}

internal sealed class SelectRandomPublicTeamTargetDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.SelectRandomPublicTeamTarget;
    public override ISkillProgramEffectHandler Handler { get; } = new TeamActivityEffectHandler(SkillProgramEffectOp.SelectRandomPublicTeamTarget);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.SelectTargets,
        static (effect, context) => context.SelectTargets(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "targetKind", "allowEmpty", "condition");
        var kind = r.RequiredEnum<SkillProgramTargetKind>("targetKind");
        if (kind is not (SkillProgramTargetKind.PublicTeamEnemies or SkillProgramTargetKind.PublicTeamWoundedAllies or
            SkillProgramTargetKind.OtherPublicTeamWoundedAllies or SkillProgramTargetKind.PublicTeamUnchainedEnemies or
            SkillProgramTargetKind.OtherPublicTeamAllies))
            throw new InvalidOperationException($"{r.Path}: a public-team target set is required.");
        var minimum = r.Has("allowEmpty") && r.RequiredBool("allowEmpty") ? 0 : 1;
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            targetKind: kind, minimumTargets: minimum, maximumTargets: 1);
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new SelectTargetSet(effect.MinimumTargets, 1)];
}

internal sealed class RepeatCurrentRedTrickDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RepeatCurrentRedTrick;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.UseSelectedCardsAs,
        static (_, _) => { });
    public override ISkillProgramEffectHandler Handler { get; } = new TeamActivityEffectHandler(SkillProgramEffectOp.RepeatCurrentRedTrick);
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseCommitted)];
}

internal sealed class TeamActivityEffectHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        host.GetCapability<ITeamActivityProgramHost>().ExecuteTeamActivity(frame, effect);
}

public sealed record PublicTeamTargetsBoundEvent(long ProgramFrameId, int OwnerSeat, SkillProgramTargetKind Kind, int Count) : IGameEvent
{
    public int InstructionIndex { get; init; }
    public SkillProgramEffectOp SelectionOp { get; init; } = SkillProgramEffectOp.SelectAllPublicTeamTargets;
    // Scalar provenance keeps the issued target order immutable even after a
    // target dies or changes teams while an earlier damage child resolves.
    public string TargetSeatsKey { get; init; } = "";
}
public sealed record AutomaticExtraCardEffectIssuedEvent(long ProgramFrameId, CardConversionSource Source,
    long CardUseFrameId, long ActionId, string GameplayHash) : IGameEvent;

public sealed partial class GameEngine
{
    private bool PublicTeamEnemy(int owner, CharacterState target) => _players[owner].TeamId is { } team &&
        target.TeamId is { } other && team != other;

    private bool OtherRevealedPublicTeamAlly(CharacterState owner, CharacterState target) =>
        owner.IsAlive && owner.Seat != target.Seat && owner.TeamRevealed && target.TeamRevealed && SamePublicTeam(owner, target);

    [ProgramCapability]
    private sealed class TeamActivityProgramHost(GameEngine engine) : ITeamActivityProgramHost
    {
        public SkillProgramStepOutcome ExecuteTeamActivity(ProgramSkillFrame f, SkillProgramEffect e) => engine.ExecuteTeamActivity(f, e);
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 27400)]
    private bool CanOfferTeamActivity(ProgramTriggerCandidate c, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects.FirstOrDefault(e => e.Op is SkillProgramEffectOp.SelectAllPublicTeamTargets or
                SkillProgramEffectOp.SelectRandomPublicTeamTarget) is { TargetKind: { } kind } selection &&
            GetProgramTargetSeats(c.OwnerSeat, kind, context).Count < selection.MinimumTargets) return false;
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.RepeatCurrentRedTrick))
            return TryGetExtraEffectUse(c.OwnerSeat, context, out var use) && IsOrdinaryTrick(use.CardKind) &&
                context.Facts?.CardActionCardIsRed == true;
        return true;
    }

    private SkillProgramStepOutcome ExecuteTeamActivity(ProgramSkillFrame input, SkillProgramEffect effect)
    {
        var frame = GetActiveProgramFrame(input.Id);
        if (effect.Op is SkillProgramEffectOp.SelectAllPublicTeamTargets or SkillProgramEffectOp.SelectRandomPublicTeamTarget)
        {
            var targets = GetProgramTargetSeats(frame.OwnerSeat, effect.TargetKind!.Value, frame.WindowContext);
            if (targets.Count < effect.MinimumTargets) throw new InvalidOperationException("The accepted public-team target set became empty.");
            // Select once before any child can pause, die or change sides. The
            // selected seat is owned by this frame; resumption never rerolls it.
            var frozenTargets = Array.AsReadOnly(effect.Op == SkillProgramEffectOp.SelectRandomPublicTeamTarget && targets.Count > 1
                ? new[] { targets[_random.Next(targets.Count)] } : targets.ToArray());
            ReplaceRuntimeTop(frame with { SelectedTargetSeats = frozenTargets });
            AdvanceEventRulesAndQueueFact(new PublicTeamTargetsBoundEvent(frame.Id, frame.OwnerSeat, effect.TargetKind.Value, frozenTargets.Count)
            { InstructionIndex = frame.InstructionIndex, SelectionOp = effect.Op, TargetSeatsKey = PublicTeamTargetSeatsKey(frozenTargets) });
            return SkillProgramStepOutcome.Continue;
        }
        if (effect.Op != SkillProgramEffectOp.RepeatCurrentRedTrick || !TryGetExtraEffectUse(frame.OwnerSeat, frame.WindowContext, out var use) ||
            !IsOrdinaryTrick(use.CardKind) || frame.WindowContext?.Facts?.CardActionCardIsRed != true)
            throw new InvalidOperationException("A red-trick repeat requires its original accepted ordinary-trick use.");
        var window = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.Id == frame.WindowContext.ParentFrameId);
        var source = new CardConversionSource(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
        ReplaceRuntimeFrame(use.Id, use with { ExtraCardEffect = new()
        {
            Source = source, ProgramFrameId = frame.Id, ActionId = use.Action!.ActionId,
            AutomaticGameplayHash = frame.GameplayHash, Simple = window.SimpleContinuation, Trick = window.TrickContinuation
        } });
        AdvanceEventRulesAndQueueFact(new AutomaticExtraCardEffectIssuedEvent(frame.Id, source, use.Id, use.Action!.ActionId, frame.GameplayHash));
        return SkillProgramStepOutcome.Continue;
    }

    private static string PublicTeamTargetSeatsKey(IReadOnlyList<int> seats) =>
        string.Join(",", seats.Select(seat => seat.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    private bool IsExactPublicTeamTargetSelection(ProgramSkillFrame frame, SkillProgramEffect selection, ProgramExecutionPlan plan)
    {
        if (selection.Op is not (SkillProgramEffectOp.SelectAllPublicTeamTargets or SkillProgramEffectOp.SelectRandomPublicTeamTarget) || selection.TargetKind is not { } kind ||
            frame.SelectedTargetSeats.Count < selection.MinimumTargets || frame.SelectedTargetSeats.Count > selection.MaximumTargets ||
            frame.SelectedTargetSeats.Distinct().Count() != frame.SelectedTargetSeats.Count) return false;
        var producerIndex = 0;
        for (var index = 0; index < frame.InstructionIndex; index++)
            if (ReferenceEquals(plan.Instructions[index], selection)) producerIndex = index + 1;
        if (producerIndex == 0) return false;
        var issued = ProgramEventHistory<PublicTeamTargetsBoundEvent>().Where(e => e.ProgramFrameId == frame.Id).ToArray();
        return issued is [var receipt] && receipt.OwnerSeat == frame.OwnerSeat && receipt.Kind == kind &&
            receipt.InstructionIndex == producerIndex && receipt.SelectionOp == selection.Op && receipt.Count == frame.SelectedTargetSeats.Count &&
            receipt.TargetSeatsKey == PublicTeamTargetSeatsKey(frame.SelectedTargetSeats);
    }
}
