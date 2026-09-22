using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Reflection;

namespace CardGame.Core;

public enum SkillProgramStepOutcome
{
    Continue,
    AwaitChild,
    AwaitChoice
}

public sealed record SkillProgramActorState(PlayerSkillContext Context, bool IsAlive);

/// <summary>
/// Frozen program and cursor access required by the generic executor. The host
/// retains ownership of resolution-stack mutation and parent continuation.
/// </summary>
public interface ISkillProgramExecutionHost
{
    ProgramSkillFrame? GetActiveFrame(long frameId);
    SkillProgram GetProgram(string skillId);
    SkillProgramActorState GetActor(int seat);
    bool IsGameOver { get; }
    bool OwnsSkillInstance(int ownerSeat, string skillId, string skillInstanceId);
    bool OwnsHandCards(int ownerSeat, IReadOnlyList<int> cardIds);
    void UpdateFrame(ProgramSkillFrame frame);
    void Complete(ProgramSkillFrame frame, bool completed, string? reason = null);
}

/// <summary>Primitive rules operations exposed to reusable effect handlers.</summary>
public interface ISkillProgramEffectHost
{
    void Draw(long frameId, int ownerSeat, int targetSeat, int amount,
        SkillProgramNumberExpression? numberExpression, string? resultBind,
        SkillProgramCardSetVisibility visibility, CardMoveReason reason);
    void Recover(long frameId, int ownerSeat, int targetSeat, int amount,
        SkillProgramNumberExpression? numberExpression, string? sourceBind);
    SkillProgramStepOutcome LoseHp(long frameId, string skillId, int targetSeat, int amount);
    void MoveSelected(
        int ownerSeat,
        int targetSeat,
        IReadOnlyList<int> cardIds,
        bool toDiscard,
        CardMoveReason reason);
    SkillProgramStepOutcome InsertPhase(
        ProgramSkillFrame frame,
        TurnPhase phase,
        SkillProgramPhaseContinuation continuation);
    void RecoverTo(
        long frameId,
        int ownerSeat,
        int targetSeat,
        SkillProgramNumberExpression expression,
        int minimumValue,
        bool clampToMaxHp);
    void DiscardOwnedZoneCards(
        ProgramSkillFrame frame,
        IReadOnlyList<CardZoneKind> zones,
        CardMoveReason reason);
    void SetChainedState(ProgramSkillFrame frame, bool chained);
    void TurnOver(long frameId, int ownerSeat, int targetSeat);
    void SetFaceState(long frameId, int ownerSeat, int targetSeat, bool faceDown);
    SkillProgramStepOutcome StartJudgment(
        ProgramSkillFrame frame,
        int targetSeat,
        string reason,
        string resultBind,
        SkillProgramCardSetVisibility visibility);
    void RevealTopCards(
        long frameId,
        int ownerSeat,
        int amount,
        SkillProgramNumberExpression? numberExpression,
        string resultBind,
        SkillProgramCardSetVisibility visibility);
    void FilterBoundCards(
        long frameId,
        string sourceBind,
        string resultBind,
        IReadOnlyList<Suit> suits);
    SkillProgramStepOutcome SelectCardSubset(
        long frameId,
        int ownerSeat,
        string sourceBind,
        string resultBind,
        int minimumCards,
        int maximumCards,
        int maximumRankSum,
        SkillProgramSubsetAiOrder aiOrder);
    void MoveBoundCards(
        long frameId,
        int ownerSeat,
        string sourceBind,
        string? exceptBind,
        SkillProgramCardDestination destination,
        CardMoveReason reason);
    SkillProgramStepOutcome SelectTarget(
        long frameId,
        int ownerSeat,
        SkillProgramTargetKind targetKind);
    SkillProgramStepOutcome SelectTargets(
        long frameId,
        int ownerSeat,
        SkillProgramTargetKind targetKind,
        int minimumTargets,
        int maximumTargets,
        SkillProgramTargetAiOrder aiOrder);
    SkillProgramStepOutcome SelectSourceCard(
        long frameId,
        int ownerSeat,
        IReadOnlyList<CardZoneKind> zones,
        string resultBind);
    SkillProgramStepOutcome GiveBoundCard(
        long frameId,
        int ownerSeat,
        string sourceBind,
        SkillProgramTargetKind targetKind,
        CardMoveReason reason);
    void ClaimDamageCards(long frameId, int ownerSeat, CardMoveReason reason);
    void TakeRandomHandCardFromSelectedTargets(
        long frameId,
        int ownerSeat,
        int amountPerTarget,
        CardMoveReason reason);
    void AdjustNormalDraw(ProgramSkillFrame frame, int amount);
    void GrantTurnCardDamageModifier(
        ProgramSkillFrame frame,
        IReadOnlyList<CardKind> cardKinds,
        int amount);
    void GrantTurnCardActionProhibition(
        ProgramSkillFrame frame,
        IReadOnlyList<CardKind> cardKinds,
        IReadOnlyList<CardActionType> actionTypes);
    void GrantTurnRuleModifier(
        ProgramSkillFrame frame,
        SkillRuleQuery query,
        SkillRuleOperation operation,
        int amount);
    void GrantTurnCardTargetRestriction(
        ProgramSkillFrame frame,
        SkillProgramCardTargetRestriction restriction);
    void GrantTurnCardConversion(
        ProgramSkillFrame frame,
        string sourceBind,
        SkillProgramCardColorRelation colorRelation,
        CardKind outputKind);
}

/// <summary>One reusable primitive operation, never one character or skill.</summary>
public interface ISkillProgramEffectHandler
{
    SkillProgramEffectOp Op { get; }
    SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host);
}

public sealed class DrawSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.Draw;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.Draw(frame.Id, frame.OwnerSeat, targetSeat, effect.Amount,
            effect.NumberExpression, effect.ResultBind, effect.Visibility, Reason(frame, effect));
        return SkillProgramStepOutcome.Continue;
    }

    private static CardMoveReason Reason(ProgramSkillFrame frame, SkillProgramEffect effect) =>
        new($"skill-program.{frame.SkillId}.{effect.Op}");
}

public sealed class SelectTargetSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SelectTarget;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) =>
        host.SelectTarget(
            frame.Id,
            frame.OwnerSeat,
            effect.TargetKind ?? throw new InvalidOperationException("selectTarget has no target kind."));
}

public sealed class SelectTargetsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SelectTargets;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) =>
        host.SelectTargets(
            frame.Id,
            frame.OwnerSeat,
            effect.TargetKind ?? throw new InvalidOperationException("selectTargets has no target kind."),
            effect.MinimumTargets,
            effect.MaximumTargets,
            effect.TargetAiOrder ?? throw new InvalidOperationException("selectTargets has no AI order."));
}

public sealed class TakeRandomHandCardFromSelectedTargetsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.TakeRandomHandCardFromSelectedTargets;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.TakeRandomHandCardFromSelectedTargets(
            frame.Id,
            frame.OwnerSeat,
            effect.Amount,
            new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class SelectSourceCardSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SelectSourceCard;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) =>
        host.SelectSourceCard(
            frame.Id,
            frame.OwnerSeat,
            effect.Zones,
            effect.ResultBind ?? throw new InvalidOperationException("selectSourceCard has no result bind."));
}

public sealed class GiveBoundCardSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GiveBoundCard;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) =>
        host.GiveBoundCard(
            frame.Id,
            frame.OwnerSeat,
            effect.SourceBind ?? throw new InvalidOperationException("giveBoundCard has no source bind."),
            effect.TargetKind ?? throw new InvalidOperationException("giveBoundCard has no target kind."),
            new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
}

public sealed class ClaimDamageCardsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ClaimDamageCards;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.ClaimDamageCards(
            frame.Id,
            frame.OwnerSeat,
            new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class RecoverSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.Recover;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.Recover(frame.Id, frame.OwnerSeat, targetSeat, effect.Amount,
            effect.NumberExpression, effect.SourceBind);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class LoseHpSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.LoseHp;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) =>
        host.LoseHp(frame.Id, frame.SkillId, targetSeat, effect.Amount);
}

public sealed class GiveSelectedSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GiveSelected;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.MoveSelected(
            frame.OwnerSeat,
            targetSeat,
            frame.SelectedCardIds,
            toDiscard: false,
            Reason(frame, effect));
        return SkillProgramStepOutcome.Continue;
    }

    private static CardMoveReason Reason(ProgramSkillFrame frame, SkillProgramEffect effect) =>
        new($"skill-program.{frame.SkillId}.{effect.Op}");
}

public sealed class DiscardSelectedSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardSelected;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.MoveSelected(
            frame.OwnerSeat,
            targetSeat,
            frame.SelectedCardIds,
            toDiscard: true,
            Reason(frame, effect));
        return SkillProgramStepOutcome.Continue;
    }

    private static CardMoveReason Reason(ProgramSkillFrame frame, SkillProgramEffect effect) =>
        new($"skill-program.{frame.SkillId}.{effect.Op}");
}

public sealed class InsertPhaseSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.InsertPhase;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        host.InsertPhase(
            frame,
            effect.Phase ?? throw new InvalidOperationException("insertPhase has no phase."),
            effect.PhaseContinuation ??
            throw new InvalidOperationException("insertPhase has no continuation."));
}

public sealed class RecoverToSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RecoverTo;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.RecoverTo(frame.Id, frame.OwnerSeat, targetSeat,
            effect.NumberExpression ??
            throw new InvalidOperationException("recoverTo has no numeric expression."),
            effect.MinimumValue, effect.ClampToMaxHp);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class DiscardOwnedZoneCardsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardOwnedZoneCards;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.DiscardOwnedZoneCards(
            frame,
            effect.Zones,
            new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class SetChainedStateSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SetChainedState;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.SetChainedState(
            frame,
            effect.Chained ?? throw new InvalidOperationException("setChainedState has no chained value."));
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class TurnOverSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.TurnOver;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.TurnOver(frame.Id, frame.OwnerSeat, targetSeat);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class SetFaceStateSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SetFaceState;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.SetFaceState(
            frame.Id,
            frame.OwnerSeat,
            targetSeat,
            effect.FaceDown ?? throw new InvalidOperationException("setFaceState has no faceDown value."));
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class StartJudgmentSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.StartJudgment;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host) =>
        host.StartJudgment(
            frame,
            targetSeat,
            effect.JudgmentReason ??
            throw new InvalidOperationException("startJudgment has no stable reason."),
            effect.ResultBind ??
            throw new InvalidOperationException("startJudgment has no result bind."),
            effect.Visibility);
}

public sealed class RevealTopCardsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RevealTopCards;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.RevealTopCards(frame.Id, frame.OwnerSeat, effect.Amount, effect.NumberExpression,
            effect.ResultBind ?? throw new InvalidOperationException("revealTopCards has no result bind."),
            effect.Visibility);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class FilterBoundCardsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.FilterBoundCards;

    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.FilterBoundCards(
            frame.Id,
            effect.SourceBind ?? throw new InvalidOperationException("filterBoundCards has no source bind."),
            effect.ResultBind ?? throw new InvalidOperationException("filterBoundCards has no result bind."),
            effect.Suits);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class SelectCardSubsetSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.SelectCardSubset;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host) =>
        host.SelectCardSubset(frame.Id, frame.OwnerSeat,
            effect.SourceBind ?? throw new InvalidOperationException("selectCardSubset has no source bind."),
            effect.ResultBind ?? throw new InvalidOperationException("selectCardSubset has no result bind."),
            effect.MinimumCards, effect.MaximumCards, effect.MaximumRankSum,
            effect.AiOrder ?? throw new InvalidOperationException("selectCardSubset has no AI order."));
}

public sealed class MoveBoundCardsSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.MoveBoundCards;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int targetSeat, ISkillProgramEffectHost host)
    {
        host.MoveBoundCards(frame.Id, frame.OwnerSeat,
            effect.SourceBind ?? throw new InvalidOperationException("moveBoundCards has no source bind."),
            effect.ExceptBind,
            effect.Destination ?? throw new InvalidOperationException("moveBoundCards has no destination."),
            new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}"));
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class AdjustNormalDrawSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.AdjustNormalDraw;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.AdjustNormalDraw(frame, effect.Amount);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class GrantTurnCardDamageModifierSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnCardDamageModifier;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.GrantTurnCardDamageModifier(frame, effect.CardKinds, effect.Amount);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class GrantTurnCardActionProhibitionSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnCardActionProhibition;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.GrantTurnCardActionProhibition(frame, effect.CardKinds, effect.ActionTypes);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class GrantTurnRuleModifierSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnRuleModifier;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.GrantTurnRuleModifier(
            frame,
            effect.RuleQuery ?? throw new InvalidOperationException("A turn rule modifier lost its query."),
            effect.RuleOperation ?? throw new InvalidOperationException("A turn rule modifier lost its operation."),
            effect.Amount);
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class GrantTurnCardTargetRestrictionSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnCardTargetRestriction;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.GrantTurnCardTargetRestriction(
            frame,
            effect.TargetRestriction ??
            throw new InvalidOperationException("A turn card-target restriction lost its policy."));
        return SkillProgramStepOutcome.Continue;
    }
}

public sealed class GrantTurnCardConversionSkillProgramEffectHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantTurnCardConversion;

    public SkillProgramStepOutcome Execute(
        SkillProgramEffect effect,
        ProgramSkillFrame frame,
        int targetSeat,
        ISkillProgramEffectHost host)
    {
        host.GrantTurnCardConversion(
            frame,
            effect.SourceBind ?? throw new InvalidOperationException("A turn conversion lost its source bind."),
            effect.ColorRelation ?? throw new InvalidOperationException("A turn conversion lost its color relation."),
            effect.OutputKind ?? throw new InvalidOperationException("A turn conversion lost its output kind."));
        return SkillProgramStepOutcome.Continue;
    }
}

/// <summary>
/// A deterministic catalog of primitive handlers. Reflection is scoped to one
/// explicit assembly and never scans the AppDomain or filesystem.
/// </summary>
public sealed class SkillProgramEffectCatalog
{
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyList<Type>> DiscoveredTypes = new();
    private static readonly Lazy<SkillProgramEffectCatalog> BuiltIn = new(
        () => Discover(typeof(SkillProgramEffectCatalog).Assembly),
        LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly IReadOnlyDictionary<SkillProgramEffectOp, ISkillProgramEffectHandler> _byOp;

    public SkillProgramEffectCatalog(IEnumerable<ISkillProgramEffectHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        var ordered = handlers
            .Select(handler => handler ?? throw new InvalidOperationException("A skill-program effect handler is null."))
            .OrderBy(handler => handler.GetType().FullName, StringComparer.Ordinal)
            .ToArray();
        var byOp = new Dictionary<SkillProgramEffectOp, ISkillProgramEffectHandler>();
        foreach (var handler in ordered)
        {
            SkillProgramEffectOp op;
            try { op = handler.Op; }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Skill-program effect handler '{handler.GetType().FullName}' failed to declare its operation.",
                    exception);
            }
            if (!Enum.IsDefined(op))
            {
                throw new InvalidOperationException(
                    $"Skill-program effect handler '{handler.GetType().FullName}' declares invalid operation '{op}'.");
            }
            if (byOp.TryGetValue(op, out var duplicate))
            {
                throw new InvalidOperationException(
                    $"Skill-program effect operation '{op}' is handled by both " +
                    $"'{duplicate.GetType().FullName}' and '{handler.GetType().FullName}'.");
            }
            byOp.Add(op, handler);
        }
        _byOp = new ReadOnlyDictionary<SkillProgramEffectOp, ISkillProgramEffectHandler>(byOp);
        Handlers = Array.AsReadOnly(ordered);
    }

    public static SkillProgramEffectCatalog Default => BuiltIn.Value;

    public IReadOnlyList<ISkillProgramEffectHandler> Handlers { get; }

    public static SkillProgramEffectCatalog Discover(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var types = DiscoveredTypes.GetOrAdd(assembly, static source => source.GetTypes()
            .Where(type => type.IsVisible && type.IsClass && !type.IsAbstract &&
                           !type.ContainsGenericParameters &&
                           typeof(ISkillProgramEffectHandler).IsAssignableFrom(type))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray());
        var handlers = types.Select(Create).ToArray();
        return new SkillProgramEffectCatalog(handlers);
    }

    public ISkillProgramEffectHandler Resolve(SkillProgramEffectOp op)
    {
        if (!Enum.IsDefined(op) || !_byOp.TryGetValue(op, out var handler))
            throw new InvalidOperationException($"No skill-program effect handler is registered for operation '{op}'.");
        return handler;
    }

    private static ISkillProgramEffectHandler Create(Type type)
    {
        var constructor = type.GetConstructor(Type.EmptyTypes);
        if (constructor is null || !constructor.IsPublic)
        {
            throw new InvalidOperationException(
                $"Skill-program effect handler type '{type.FullName}' requires a public parameterless constructor.");
        }
        try
        {
            return (ISkillProgramEffectHandler)constructor.Invoke(null);
        }
        catch (TargetInvocationException exception)
        {
            throw new InvalidOperationException(
                $"Failed to construct skill-program effect handler type '{type.FullName}'.",
                exception.InnerException ?? exception);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Failed to construct skill-program effect handler type '{type.FullName}'.",
                exception);
        }
    }
}

/// <summary>
/// Stateless instruction runner. It owns validation and cursor semantics while
/// the narrow hosts own rules state, primitive mutations and parent resumption.
/// </summary>
public sealed class SkillProgramExecutor
{
    private readonly SkillProgramEffectCatalog _effects;

    public SkillProgramExecutor(SkillProgramEffectCatalog? effects = null) =>
        _effects = effects ?? SkillProgramEffectCatalog.Default;

    public void Run(
        long frameId,
        ISkillProgramExecutionHost state,
        ISkillProgramEffectHost effects)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(effects);
        while (state.GetActiveFrame(frameId) is { } frame)
        {
            if (frame.Id != frameId)
                throw new InvalidOperationException(
                    $"Active skill-program frame '{frame.Id}' does not match requested frame '{frameId}'.");
            var actor = state.GetActor(frame.OwnerSeat);
            var program = state.GetProgram(frame.SkillId);
            if (!string.Equals(program.GameplayHash, frame.GameplayHash, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Running skill program '{frame.SkillId}' changed its gameplay hash.");
            if (!state.OwnsSkillInstance(frame.OwnerSeat, frame.SkillId, frame.SkillInstanceId))
            {
                state.Complete(frame, completed: false, "技能实例在结算前已失效，剩余步骤取消。");
                return;
            }
            var plan = ProgramInstructionResolver.Default.Resolve(frame, program);
            var instructions = plan.Instructions;
            if (frame.InstructionIndex < 0 || frame.InstructionIndex > instructions.Count)
                throw new InvalidOperationException(
                    $"Running skill program '{frame.SkillId}' has an invalid instruction cursor.");

            if (!actor.IsAlive || state.IsGameOver || frame.InstructionIndex >= instructions.Count)
            {
                state.Complete(
                    frame,
                    completed: actor.IsAlive && frame.InstructionIndex >= instructions.Count);
                return;
            }

            var effect = plan.GetInstruction(frame.InstructionIndex).Effect;
            var handler = _effects.Resolve(effect.Op);
            // Commit the cursor before any primitive can suspend into a child.
            frame = frame with { InstructionIndex = frame.InstructionIndex + 1 };
            state.UpdateFrame(frame);
            if (!effect.Condition.Evaluate(actor.Context)) continue;

            var targetSeat = effect.Op is SkillProgramEffectOp.SelectTarget or SkillProgramEffectOp.SelectTargets
                ? frame.OwnerSeat
                : effect.Target switch
            {
                SkillProgramEffectTarget.Owner => frame.OwnerSeat,
                SkillProgramEffectTarget.SelectedTarget => frame.SelectedTargetSeats.Single(),
                _ => throw new InvalidOperationException(
                    $"Skill program '{frame.SkillId}' uses unsupported target '{effect.Target}'.")
            };
            var target = state.GetActor(targetSeat);
            if (effect.Op is SkillProgramEffectOp.GiveSelected or SkillProgramEffectOp.DiscardSelected &&
                (!target.IsAlive || !state.OwnsHandCards(frame.OwnerSeat, frame.SelectedCardIds)))
            {
                state.Complete(
                    frame,
                    completed: false,
                    "所选牌或接收者在结算中已失效，技能剩余步骤取消。");
                return;
            }
            if (!target.IsAlive) continue;

            var outcome = handler.Execute(effect, frame, targetSeat, effects);
            if (outcome is SkillProgramStepOutcome.AwaitChild or SkillProgramStepOutcome.AwaitChoice) return;
            if (outcome != SkillProgramStepOutcome.Continue)
                throw new InvalidOperationException(
                    $"Skill-program effect '{effect.Op}' returned unsupported outcome '{outcome}'.");
        }
    }
}
