namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int GetCardUseTargetIndex(long id) => _resolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == id).TargetIndex;
    private Card GetAttackCard(int id) => _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(card => card.Id == id);

    private CardAttackState GetCardAttackState(long id) => _resolutionStack.FirstOrDefault(frame => frame.Id == id) switch
    {
        CardUseFrame { CardAttack: { } state } => state,
        ProgramSkillFrame { CardAttack: { } state } => state,
        JudgmentFrame { CardAttack: { } state } => state,
        _ => throw new InvalidOperationException($"Card attack owner {id} lost its attempt.")
    };

    private void InitializeCardAttackState(long id, CardAttackState state)
    {
        var owner = _resolutionStack.FirstOrDefault(frame => frame.Id == id);
        if (owner is not (CardUseFrame or ProgramSkillFrame or JudgmentFrame))
            throw new InvalidOperationException($"Card attack owner {id} is unavailable.");
        UpdateCardAttackState(id, _ => state);
    }

    private void UpdateCardAttackState(long id, Func<CardAttackState?, CardAttackState> update)
    {
        var index = _resolutionStack.FindIndex(frame => frame.Id == id);
        if (index < 0) throw new InvalidOperationException($"Card attack owner {id} is unavailable.");
        ReplaceRuntimeFrame(_resolutionStack[index].Id, _resolutionStack[index] switch
        {
            CardUseFrame use => use with { CardAttack = update(use.CardAttack) },
            ProgramSkillFrame program => program with { CardAttack = update(program.CardAttack) },
            JudgmentFrame judgment => judgment with { CardAttack = update(judgment.CardAttack) },
            _ => throw new InvalidOperationException($"Frame {id} cannot own a card attack.")
        });
    }

    private sealed record AttackCompletionReceipt(long ResolutionId, bool DelayedTurn,
        long? ProgramFrameId, int? DelayedTurnSeat,
        bool RequestedSlashChild = false, bool DamageWasApplied = false, int FinalTargetSeat = 0, ProgramDamageTargetDuelOrigin? DamageTargetDuelReturn = null, ProgramDualColorDuelOrigin? DualColorDuelReturn = null,
        SharedSlashBenefitReturn? SharedSlashBenefit = null, bool SharedSlashCausedDamage = false,
        ForeignTurnContestSlashReturn? ForeignTurnContestSlashReturn = null, PindianWinnerSlashReturn? PindianWinnerSlashReturn = null);
    private AttackCompletionReceipt CaptureAttackCompletion(CardAttackHandle attack) =>
        new(attack.ResolutionId, attack.IsDelayedJudgmentDamage,
            attack.ProgramSkillFrameId ?? attack.ProgramSkillCardUseFrameId ?? NextActualUseAdjustedSlashParent(attack),
            attack.IsDelayedJudgmentDamage ? attack.DelayedJudgmentSeat ?? attack.SourceSeat : null,
            attack.ProgramSkillFrameId is null && attack.ProgramSkillCardUseFrameId is not null,
            attack.DamageWasApplied, attack.TargetSeat,
            LifecycleCardUse(attack.ResolutionId)?.DamageTargetDuelOrigin,
            LifecycleCardUse(attack.ResolutionId)?.DualColorDuelOrigin,
            LifecycleCardUse(attack.ResolutionId)?.SharedSlashBenefit,
            LifecycleCardUse(attack.ResolutionId)?.SharedSlashBenefit is not null && attack.CardUseCausedDamage,
            LifecycleCardUse(attack.ResolutionId)?.ForeignTurnContestSlashReturn,
            LifecycleCardUse(attack.ResolutionId)?.PindianWinnerSlashReturn);
    private Card ReadCardAppearance(CardAppearanceReference appearance) => GetAttackCard(appearance.Id) with
        { Kind = appearance.Kind, Suit = appearance.Suit, Rank = appearance.Rank };
    private CardResolutionContinuations GetCardContinuations(long id) =>
        _resolutionStack.FirstOrDefault(frame => frame.Id == id) switch
        {
            CardUseFrame use => use.Continuations,
            ProgramSkillFrame program => program.Continuations,
            JudgmentFrame judgment => judgment.Continuations,
            _ => throw new InvalidOperationException($"Continuation owner {id} is unavailable.")
        };
    private void UpdateCardContinuations(long id, Func<CardResolutionContinuations, CardResolutionContinuations> update)
    {
        var index = _resolutionStack.FindIndex(frame => frame.Id == id);
        if (index < 0) throw new InvalidOperationException($"Continuation owner {id} is unavailable.");
        ReplaceRuntimeFrame(_resolutionStack[index].Id, _resolutionStack[index] switch
        {
            CardUseFrame use => use with { Continuations = update(use.Continuations) },
            ProgramSkillFrame program => program with { Continuations = update(program.Continuations) },
            JudgmentFrame judgment => judgment with { Continuations = update(judgment.Continuations) },
            _ => throw new InvalidOperationException($"Frame {id} cannot own a card continuation.")
        });
    }

    private interface ICardContinuationHandle { long OwnerFrameId { get; } }
    private long? FindCardContinuationOwner(Func<CardResolutionContinuations, bool> matches) =>
        _resolutionStack.LastOrDefault(frame => frame switch
        {
            CardUseFrame use => matches(use.Continuations),
            ProgramSkillFrame program => matches(program.Continuations),
            JudgmentFrame judgment => matches(judgment.Continuations),
            _ => false
        })?.Id;

    private static SuspendedDecisionState CaptureSuspendedDecision(PendingDecision prompt) =>
        new(prompt.Kind, prompt.PlayerSeat, prompt.Prompt, Array.AsReadOnly(prompt.ValidCardIds.ToArray()), Array.AsReadOnly(prompt.ValidTargetSeats.ToArray()),
            prompt.SourceSeat, prompt.IncomingCard, prompt.SkillPrompt, prompt.PromptId, prompt.Revision,
            prompt.RequiredCardCount, Array.AsReadOnly(prompt.Choices.ToArray()), prompt.IsPrivate, Array.AsReadOnly(prompt.ValidContentIds.ToArray()),
            prompt.TargetSeat, prompt.RequiredCardKind);
    private static PendingDecision RestoreSuspendedDecision(SuspendedDecisionState state) =>
        new(state.Kind, state.PlayerSeat, state.Prompt, state.ValidCardIds, state.ValidTargetSeats,
            state.SourceSeat, state.IncomingCard)
        { SkillPrompt = state.SkillPrompt, PromptId = state.PromptId, Revision = state.Revision,
            RequiredCardCount = state.RequiredCardCount, Choices = state.Choices, IsPrivate = state.IsPrivate,
            ValidContentIds = state.ValidContentIds, TargetSeat = state.TargetSeat, RequiredCardKind = state.RequiredCardKind };

    private bool GetCardUseCausedDamage(long id) => _resolutionStack.FirstOrDefault(frame => frame.Id == id) switch
    {
        CardUseFrame use => use.CausedDamage,
        ProgramSkillFrame program => program.CardAttack?.DamageWasApplied == true,
        JudgmentFrame judgment => judgment.CardAttack?.DamageWasApplied == true,
        _ => throw new InvalidOperationException("A completed card use lost its damage facts.")
    };
    private void SetCardUseCausedDamage(long id, bool value)
    {
        var index = _resolutionStack.FindIndex(frame => frame.Id == id);
        if (index < 0) throw new InvalidOperationException("A card use lost its damage facts.");
        if (_resolutionStack[index] is CardUseFrame use)
            ReplaceRuntimeFrame(_resolutionStack[index].Id, use with { CausedDamage = value });
    }

}
