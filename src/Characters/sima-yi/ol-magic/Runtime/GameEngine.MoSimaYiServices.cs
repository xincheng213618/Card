namespace CardGame.Core;

public sealed partial class GameEngine
{
    private MoSimaYiRuntime MoSimaYi => GetRuntimeModule<MoSimaYiRuntime>();
    private bool HasMoSimaYi => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.AcquireMoSimaYiGuifuDodge);

    [ProgramCapability]
    private sealed class MoSimaYiFacts(GameEngine engine) : IMoSimaYiFacts
    {
        public int Round => engine._roundNumber;
        public int Turn => engine._turnNumber;
        public bool IsGameOver => engine._winner != Winner.None;
        public bool IsAlive(int seat) => engine.IsValidPlayerSeat(seat) && engine._players[seat].IsAlive;
        public bool IsHuman(int seat) => engine._players[seat].IsHuman;
        public IReadOnlyList<T> History<T>() where T : IGameEvent => Array.AsReadOnly(engine.ProgramEventHistory<T>().ToArray());
        public IReadOnlyList<CardConversionSource> GuiFuOwners() => Array.AsReadOnly(engine._players
            .Where(p => p.IsAlive && engine.HasRuntimeSkill(p, MoSimaYiComposition.Guifu))
            .Select(p => new CardConversionSource(MoSimaYiComposition.Guifu, "acquire-round", p.Seat,
                engine.GetRuntimeSkillInstanceId(p, MoSimaYiComposition.Guifu))).ToArray());
        public IReadOnlyList<string> EffectiveSkills(int owner) => Array.AsReadOnly(engine.GetSkillBindingShard(engine._players[owner])
            .ActiveGrants.Select(g => g.SkillId).Distinct(StringComparer.Ordinal).ToArray());
        public IReadOnlyList<int> RetaliationActors(int owner, int round) => Array.AsReadOnly(engine.ProgramEventHistory<MoSimaYiDamageCauseEvent>()
            .Where(e => e.Cause.Round == round && e.Cause.SourceSeat == owner).Select(e => e.Cause.TargetSeat)
            .Where(IsAlive).Distinct().OrderBy(seat => (seat - owner + engine._players.Count) % engine._players.Count).ToArray());
        public MoSimaYiDodge? FindDodge()
        {
            foreach (var location in new[] { CardLocation.DrawPile, CardLocation.DiscardPile })
                if (engine._cardZones.CardsAt(location).FirstOrDefault(c => c.Kind == CardKind.Dodge) is { } card)
                    return new(card.Id, location);
            return null;
        }
        public string SkillName(string id) => engine._contentRegistry.GetSkill(id).Name;
        public bool HasJudgmentArea(int actor) => !engine._players[actor].JudgmentAreaAbolished;
    }

    [ProgramCapability]
    private sealed class MoSimaYiOperations(GameEngine engine) : IMoSimaYiNative
    {
        public void ObtainDodge(ProgramSkillFrame f, MoSimaYiDodge entity)
        {
            if (entity.CardId <= 0 || engine._cardZones.GetLocation(entity.CardId) != entity.From ||
                engine.GetAdvancedCard(entity.CardId).Kind != CardKind.Dodge)
                throw new InvalidOperationException("Guifu needs the actual stock Dodge frozen for this obtain.");
            engine.MoveCard(engine.GetAdvancedCard(entity.CardId), entity.From, CardLocation.Hand(f.OwnerSeat),
                new("skill-program.ol:mo-guifu.obtain"), m => engine.AdvanceEventRulesAndQueueFact(
                    new MoSimaYiDodgeMarkedEvent(f.Id, f.OwnerSeat, m.CardId, m.From, m.Sequence)),
                disclosure: entity.From == CardLocation.DrawPile ? CardMovementDisclosure.FaceDown : CardMovementDisclosure.FaceUp);
        }
        public bool DrainChildren(long id) => engine.TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
            engine.TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            engine.TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) ||
            engine.TryBeginCardsMovedProgramWindow(id) || engine.TryBeginAdvancedSkillsChanged(id);
        public void GrantSkill(ProgramSkillFrame f, string skill, bool temporary)
        {
            if (!engine._players[f.OwnerSeat].IsAlive || engine._winner != Winner.None) return;
            _ = engine._contentRegistry.GetSkill(skill);
            var player = engine._players[f.OwnerSeat];
            var source = temporary ? $"turn:{engine._turnNumber}:mo-sima-yi:{f.Id}:{skill}" : $"mo-sima-yi:{f.Id}:{skill}";
            if (engine.ProgramEventHistory<MoSimaYiSkillGrantedEvent>().Any(e => e.FrameId == f.Id && e.SkillId == skill))
                throw new InvalidOperationException("A frozen Sima Yi skill grant cannot be paid twice.");
            var old = engine.GetSkillBindingShard(player).ActiveGrants.FirstOrDefault(g => g.SkillId == skill);
            var expiry = temporary ? new SkillGrantNextActualTurnStartExpiry(engine._turnNumber, engine._currentSeat, player.Seat) : null;
            player.SkillGrants.Grant(new SkillGrant($"{source}:{skill}", skill,
                old?.SkillInstanceId ?? $"mo-sima-yi-shared:{player.Seat}:{skill}", source)
                { NextActualTurnStartExpiry = expiry });
            engine.AdvanceEventRulesAndQueueFact(new ProgramTurnSkillsGrantedEvent(f.Id, f.SkillId,
                GameEngine.GetProgramBindingId(f), f.OwnerSeat, Array.AsReadOnly(new[] { skill }))
                { RecipientSeat = player.Seat, GrantSourceId = source, NextActualTurnStartExpiry = expiry });
            engine.AdvanceEventRulesAndQueueFact(new MoSimaYiSkillGrantedEvent(f.Id, skill, source, temporary));
        }
        public void EnterRumo(ProgramSkillFrame f) => engine.EnterOlMoRumo(f);
        public IReadOnlyList<string> Sample(IReadOnlyList<string> skills, int count, long frameId)
        {
            var pool = skills.ToList();
            var result = new List<string>(count);
            var before = engine._random.State;
            while (result.Count < count)
            {
                var index = engine._random.Next(pool.Count);
                result.Add(pool[index]); pool.RemoveAt(index);
            }
            engine.AdvanceEventRulesAndQueueFact(new MoSimaYiSampledEvent(frameId, before, engine._random.State, count));
            return Array.AsReadOnly(result.ToArray());
        }
        public bool BeginSlash(ProgramSkillFrame f, int actor, int target)
        {
            if (!engine.CanMoSimaYiRetaliationSlash(actor, target)) return false;
            engine.BeginOfferedProgramVirtualSlash(f, actor, target);
            return true;
        }
        public void ApplyLieshi(ProgramSkillFrame f, int actor, int option)
        {
            if (option == 0)
            {
                var player = engine._players[actor];
                if (player.JudgmentAreaAbolished) throw new InvalidOperationException("Lieshi cannot abolish an absent judgment area.");
                player.JudgmentAreaAbolished = true;
                engine.AdvanceEventRulesAndQueueFact(new PlayerAreasAbolishedEvent(actor, player.EquipmentAreaAbolished, true));
                engine.BeginProgramSkillDamage(f, actor, 1, nature: DamageNature.Fire);
                return;
            }
            var kind = option == 1 ? CardKind.Dodge : CardKind.Slash;
            var playerHand = engine.GetHand(engine._players[actor]);
            var cards = playerHand.Where(c => kind == CardKind.Slash ? IsSlashCard(c.Kind) : c.Kind == kind).ToArray();
            var reason = new CardMoveReason("skill-program.ol:mo-lieshi.discard");
            var logical = engine.CaptureNativeLogicalDiscardInvocation(actor, actor, cards.Select(c => c.Id).ToArray(), reason, f.Id);
            engine.MoveCards(cards, CardLocation.Hand(actor), CardLocation.DiscardPile, reason);
            engine.CompleteNativeLogicalDiscardInvocation(logical);
        }
        public bool FinishDamage(ProgramSkillFrame f)
        {
            if (f.AttackAttempt is not null) { engine.CompleteDamageAttack(new ProgramAttackHandle(engine, f.Id)); return true; }
            if (f.AttackReturn is not null) throw new InvalidOperationException("Lieshi cannot skip its native damage return.");
            return false;
        }
        public void Complete(ProgramSkillFrame f) { engine.ReplaceRuntimeTop(f); engine.FinishProgramSkill(f, true); }
        public void PublishState() => engine.AdvanceRulesAndPublishState();
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.Collection, 33900,
        SkillProgramEffectOp.ResolveMoSimaYiMoubian, SkillProgramEffectOp.ResolveMoSimaYiRetaliation)]
    private bool CanOfferMoSimaYi(ProgramTriggerLegalityContext c) => c.Candidate.SkillId switch
    {
        MoSimaYiComposition.Moubian => MoSimaYi.Records(c.Candidate.OwnerSeat).Count >= 3,
        MoSimaYiComposition.Zhouxi => new MoSimaYiFacts(this).RetaliationActors(c.Candidate.OwnerSeat, _roundNumber).Count > 0,
        _ => true
    };

    [ProgramPipelineHook(ProgramPipelinePhase.RumoEntry, 33900)]
    private bool IsMoSimaYiRumoEntry(ProgramSkillFrame f) => f.MoSimaYi is
        { Operation: SkillProgramEffectOp.ResolveMoSimaYiMoubian, Stage: MoSimaYiStage.Granting } && MoSimaYi.Valid(f);
    [ProgramPipelineHook(ProgramPipelinePhase.RumoOrigin, 33900)]
    private bool IsMoSimaYiRumoOrigin(long id, CardConversionSource source, string hash) =>
        ProgramEventHistory<MoSimaYiStartedEvent>().Count(e => e.FrameId == id && e.Source == source &&
            e.GameplayHash == hash && e.Operation == SkillProgramEffectOp.ResolveMoSimaYiMoubian) == 1 &&
        ProgramEventHistory<MoSimaYiSkillFrozenEvent>().Any(e => e.FrameId == id && e.SkillId == MoSimaYiComposition.Zhouxi);

    [ProgramDamageHook(ProgramDamagePhase.AppliedBeforeDying, 33900)]
    private bool RecordMoSimaYiDamage(IDamageAttempt attack, long damageFrameId)
    {
        if (!HasMoSimaYi || !attack.DamageWasApplied) return false;
        var kind = LifecycleCardUse(attack.ResolutionId)?.Action?.EffectiveKind ?? attack.EffectiveCardKind;
        var direct = kind is null ? _resolutionStack.OfType<ProgramSkillFrame>()
            .SingleOrDefault(f => f.Id == attack.ResolutionId && f.AttackAttempt is not null) : null;
        MoSimaYi.RecordDamage(new(damageFrameId, attack.ResolutionId, attack.IsSourceLess ? null : attack.SourceSeat,
            attack.TargetSeat, kind, direct?.SkillId, direct?.GameplayHash, _roundNumber));
        return false;
    }
}
