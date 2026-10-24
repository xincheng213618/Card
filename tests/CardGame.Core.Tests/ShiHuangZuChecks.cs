using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiHuangZuChecks
{
    private const string Hero = "mobile:shi-huang-zu", Chizhang = "mobile:shi-chizhang", Duanyang = "mobile:shi-duanyang",
        Driver = "fixture:shi-huang-zu-driver", Watch = "fixture:shi-huang-zu-watch", Quiet = "fixture:shi-huang-zu-quiet",
        Peer = "fixture:shi-huang-zu-peer", Deck = "fixture:shi-huang-zu-deck";
    public static void HandDamageDistanceColorResponseAndUseDiscardExclusionCold()
    {
        var s = Scene.Start(); s.Play();
        var fired = s.V(0).Hand.Single(c => c.Kind == CardKind.FireSlash).Id;
        s.Native(CardKind.FireSlash, 2); s.Offer(Chizhang); s.Activate();
        var red = s.V(0).Hand.First(c => c.Kind == CardKind.Peach && c.Suit == Suit.Heart).Id;
        s.Answer(c => Key(c) == "select-owned-cards" && c.Cards.Contains(red));
        s.Cold(); s.Answer(c => Key(c) == "finish-owned-cards");
        s.Reach(() => s.P is { PlayerSeat: 2, Kind: DecisionKind.RespondDodge });
        var choices = s.P!.Choices.Where(c => c.Cards.Count > 0).ToArray();
        s.Need(choices.Length > 0 && choices.All(c => c.Cards.All(id => s.V(2).Hand.Single(h => h.Id == id).Suit is Suit.Club or Suit.Spade)) &&
            s.E<ShiHzColorsBoundEvent>() is [{ Red: true, Black: false }],
            "A physical hand Fire Slash reaches the genuinely distant seat, and only the paid red color is prohibited in its exact response.");
        s.Cold(); s.Play();
        s.Need(s.E<ShiHzStoredEvent>().Length == 0 && s.G.CardMovements.Any(m => m.CardId == fired && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile),
            "A true used physical Slash enters discard after normal Use completion but never creates a Duanyang hand-discard opportunity.");
        s.Replay();
    }

    public static void RandomOriginalSlashCurrentPhaseJudgmentRecastAndRewardChildrenCold()
    {
        var s = Scene.Start(); s.Play(); s.Native(CardKind.Indulgence, 1); s.Play();
        s.Need(s.V(1).Judgment.Any(c => c.Kind == CardKind.Indulgence), "The recast scene first creates a real judgment-area entity through native delayed-trick Use.");
        var slashes = s.V(0).Hand.Where(c => c.Kind is CardKind.Slash or CardKind.FireSlash).Select(c => c.Id).ToArray();
        s.DriverDiscard(slashes); s.Offer(Duanyang); s.Activate(); s.Play();
        var grant = s.E<ShiHzStoredEvent>().Single();
        s.Need(slashes.Contains(grant.CardId) && grant.CandidateCount == 2 && grant.CandidateIndex is >= 0 and < 2 &&
            (s.V(0).PublicPersistentPiles?.SelectMany(p => p.Cards) ?? s.V(0).PublicPersistentPileCards ?? []).Any(c => c.Id == grant.CardId),
            "The native same-batch hand discard randomly stores exactly one original printed Slash entity on the general.");
        s.End(); s.Reach(() => s.P?.Choices.Any(c => Key(c) == "shi-huang-zu" && c.Parameters.GetValueOrDefault("branch") == "use") == true);
        s.Need(s.E<ActualPhaseEndingEvent>().Last().Phase == grant.Phase && s.E<ShiHzNativeIssuedEvent>().Length == 0,
            "The stored entity is offered at this exact actual phase ending, before leaving that phase or issuing any card twice.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "use" && c.Targets.SequenceEqual([1]));
        s.Offer(Duanyang); s.Activate();
        s.Reach(() => s.P!.Choices.Any(c => Key(c) == "shi-huang-zu" && c.Parameters.GetValueOrDefault("branch") == "card"));
        s.Need(s.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Judgment)) &&
            s.P.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)).All(c => c.Cards.Count == 0),
            "A genuine stored-Slash hit can recast the victim's judgment zone, while all foreign Hand choices remain opaque slots.");
        s.Answer(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Judgment));
        s.Answer(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "finish");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        var root = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ShiHuangZu?.Stage == ShiHzStage.RecastCostChildren);
        s.Need(s.E<ShiHzRecastPaidEvent>().Count(e => e.FrameId == root.Id) == 2 && root.NativeRecastInvocation?.Materials.Any(m => m.From.Zone == CardZoneKind.Judgment) == true &&
            s.E<ShiHzRewardIssuedEvent>().Length == 0,
            "The two real HEJ recast payments commit together on their original native invoice, before any recast draw or four-card owner reward.");
        var invoice = root.NativeRecastInvocation!;
        var paidReceipt = root.ShiHuangZu!;
        var paid = s.E<ShiHzRecastPaidEvent>().Where(e => e.FrameId == root.Id).ToArray();
        var movements = s.G.CardMovements.Where(m => m.Sequence > invoice.SequenceBefore).ToArray();
        var batch = s.G.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.Batch.ParentFrameId == root.Id);
        var instance = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Watch).SkillInstanceId;
        var ownerHand = s.V(0).HandCount; var victimHand = s.V(1).HandCount;
        s.Need(batch.Batch.Movements.SequenceEqual(movements) && movements.Length == 2 &&
            movements.All(m => m.Reason == CardMoveReasons.RecastDiscard && m.To == CardLocation.DiscardPile) &&
            movements.Select(m => m.From.Zone).Order().SequenceEqual(new[] { CardZoneKind.Hand, CardZoneKind.Judgment }.Order()) &&
            batch.Candidates.Where(c => c.SkillId == Watch).Select(c => c.OccurrenceIndex).SequenceEqual([0, 1]),
            "Both original Hand and Judgment materials form one atomic recast batch with two explicit per-card observers.");
        void CheckPaidObserver(int index)
        {
            var watcher = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Watch);
            var current = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == root.Id);
            var window = s.G.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.Id == batch.Id);
            s.Need(watcher.SkillInstanceId == instance && watcher.WindowContext is
                    { Window: SkillProgramTriggerWindow.DiscardPileReceived, MovementBatch: { } originalBatch } context &&
                context.ParentFrameId == batch.Id && originalBatch.Id == batch.Id && originalBatch.ParentFrameId == root.Id &&
                originalBatch.Movements.SequenceEqual(movements) && context.OccurrenceIndex == index && context.MovementIndex == index &&
                window.CandidateIndex == index && current.ShiHuangZu is { Stage: ShiHzStage.RecastCostChildren } receipt &&
                receipt.Before == paidReceipt.Before && receipt.After == paidReceipt.After && receipt.Selected.SequenceEqual(paidReceipt.Selected) &&
                current.NativeRecastInvocation is { } sameInvoice && sameInvoice.InvocationId == invoice.InvocationId &&
                sameInvoice.SequenceBefore == invoice.SequenceBefore && sameInvoice.Materials.SequenceEqual(invoice.Materials) &&
                s.E<ShiHzRecastPaidEvent>().Where(e => e.FrameId == root.Id).SequenceEqual(paid) &&
                s.G.CardMovements.Where(m => m.Sequence > invoice.SequenceBefore).SequenceEqual(movements) &&
                s.E<NativeRecastInvocationRecordedEvent>().All(e => e.ParentFrameId != root.Id) &&
                s.E<ShiHzRewardIssuedEvent>().Length == 0 && s.V(0).HandCount == ownerHand && s.V(1).HandCount == victimHand,
                "Each distinct paid child retains the same complete two-material invoice, with no repeated payment, recast draw or owner reward before both observers finish.");
        }
        CheckPaidObserver(0);
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch); CheckPaidObserver(1);
        s.Cold(); CheckPaidObserver(1);
        s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        s.Reach(() => s.E<ShiHzRewardIssuedEvent>().Length == 1 && s.E<ShiHzNativeReturnedEvent>().Length == 1);
        var observers = s.E<ProgramBindingStartedEvent>().Where(e => e.SkillId == Watch).ToArray();
        var returns = s.E<ProgramBindingResolvedEvent>().Where(e => e.SkillId == Watch).ToArray();
        s.Need(observers.Length == 2 && observers.Select(e => e.FrameId).Distinct().Count() == 2 && returns.Length == 2 &&
            returns.All(e => e.Activated && e.Completed) && observers.Select(e => e.FrameId).Order().SequenceEqual(returns.Select(e => e.FrameId).Order()),
            "The two explicit per-card observers each finish exactly once, including the second child's cold recovery.");
        var native = s.E<ShiHzNativeIssuedEvent>().Single();
        s.Need(s.E<CardActionAcceptedEvent>().Single(e => e.Action.ActionId == native.ActionId).Action is
                { Type: CardActionType.Use, ConversionChain.Count: 0, PhysicalCards.Count: 1 } action && action.PhysicalCards[0].CardId == grant.CardId &&
            action.PhysicalCards[0].From == grant.Pile.Location &&
            s.E<CardUseDebitRecordedEvent>().All(e => e.Debit.CardActionId != native.ActionId) &&
            s.E<NativeRecastInvocationRecordedEvent>().Single(e => e.ParentFrameId == root.Id) is { ExecutorSeat: 1, PrimaryCount: 2, ActualDrawCount: 2 } &&
            s.E<ShiHzRewardIssuedEvent>() is [{ ActualCount: 4 }],
            "The original stored entity issues a true unconverted native Slash without a quota debit, the victim completes a two-primary recast, and Huang Zu then really draws four.");
        s.Replay();
    }
    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int i) => G.CreateSnapshot(i).Players.Single(p => p.Seat == i);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Native(CardKind kind, int target)
        {
            var action = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId is > 0 && a.ConversionSource is null && a.Kind != LegalActionKind.UseProgramSkill &&
                V(0).Hand.Any(c => c.Id == a.CardId && c.Kind == kind) && a.TargetSeats.SequenceEqual([target]));
            Need(action is not null, "The prepared physical " + kind + " must be genuinely legal against " + target + ".");
            Accept(new PlayCardCommand(0, action!.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId));
        }
        public void DriverDiscard(int[] cards) => Accept(new UseProgramSkillCommand(0, Driver, "discard", cards, [], G.Revision, P!.PromptId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Offer(string skill) => Reach(() => P?.SkillPrompt?.SkillId == skill && P.Choices.Any(c => Key(c) == "activate"));
        public void Activate() => Answer(c => Key(c) == "activate");
        public void Answer(Func<PromptChoice, bool> choose)
        {
            var p = P!; var c = p.Choices.FirstOrDefault(choose); Need(c is not null, "The expected native choice exists.");
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision));
        }
        public void Reach(Func<bool> predicate)
        {
            for (var n = 0; n < 200; n++)
            {
                if (predicate()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Choices.Any(c => Key(c) == "skip")) { Answer(c => Key(c) == "skip"); continue; }
                    if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    { Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("conversion-skill-id") is null); continue; }
                    throw new InvalidOperationException("Unexpected native Shi Huang Zu boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The bounded scene retains living participants."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "The bounded actual Shi Huang Zu path reaches its expected boundary.");
        }
        public void Cold()
        { var state = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == state, "The original native frame, random result and paid children cold-restore exactly."); }
        public void Replay()
        { var state = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == state, "The native selected material, phase boundary, recast and colors replay exactly."); }
        public void Need(bool okay, string message) { if (!okay) throw new InvalidOperationException(message + " " + Dump()); }
        private void Accept(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Shi Huang Zu command."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), G.CardMovements,
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, P, Grants = E<ShiHzStoredEvent>(), Native = E<ShiHzNativeIssuedEvent>(),
            Receipts = G.ResolutionStack.OfType<ProgramSkillFrame>().Select(f => f.ShiHuangZu).Where(r => r is not null),
            Watches = G.ResolutionStack.OfType<ProgramSkillFrame>().Where(f => f.SkillId == Watch).Select(f => new { f.Id, f.TriggerId, f.SkillInstanceId,
                Window = f.WindowContext?.Window, ParentFrameId = f.WindowContext?.ParentFrameId, MovementIndex = f.WindowContext?.MovementIndex, BatchId = f.WindowContext?.MovementBatch?.Id }),
            MovementWindows = G.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Select(f => new { f.Id, f.Batch, f.CandidateIndex, f.Candidates }),
            WatchStarts = E<ProgramBindingStartedEvent>().Where(e => e.SkillId == Watch),
            WatchReturns = E<ProgramBindingResolvedEvent>().Where(e => e.SkillId == Watch),
            RecastInvoices = G.ResolutionStack.OfType<ProgramSkillFrame>().Where(f => f.NativeRecastInvocation is not null).Select(f => f.NativeRecastInvocation),
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }) });
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(85, 1, "shi-huang-zu", Hero, 4, 0, 0, 0, 0, [Driver, Watch],
                [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.FireSlash, Suit.Heart, 8), new(CardKind.Indulgence, Suit.Club, 6), new(CardKind.Peach, Suit.Heart, 4), new(CardKind.Dodge, Suit.Club, 2)], [],
                [new(Peer, Hp: 8) { StartingHand = [new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Peach, Suit.Diamond, 4)] },
                 new(Peer, Hp: 8) { StartingHand = [new(CardKind.Dodge, Suit.Club, 2), new(CardKind.Dodge, Suit.Heart, 3)] },
                 new(Peer, Hp: 8) { StartingHand = [new(CardKind.Peach, Suit.Heart, 6)] }], [], new string('e', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var scene = new Scene(game, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-huang-zu", new(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":20,"priority":0}],"activations":[
                {"id":"discard","minCards":1,"maxCards":2,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"cost"},{"op":"moveBoundCards","target":"owner","sourceBind":"cost","destination":"discardPile","awaitMovementTriggers":true}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-recast","window":"discardPileReceived","subject":"owner","discardOwnerScope":"other","movementOccurrence":"perCard","movementReasons":["card.recast.discard"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"skip-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string,object> { [Driver] = new { name = "原生手牌弃置", description = "一次多张真实弃牌。" },
                    [Watch] = new { name = "原生重铸子结算", description = "支付后的子观察。", optionLabels = new Dictionary<string,string> { ["continue"] = "继续" } },
                    [Quiet] = new { name = "安静同桌", description = "真实回合跳过出牌。" } } }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "实际同桌角色", "", Quiet, "wei", 10));
            b.AddDeck(new(Deck, "固定真实桃牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 100).Select(_ => new ContentDeckPhysicalCard("standard:peach", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势黄祖真实阶段末杀与三区重铸", 4, 4, new Dictionary<string,int>(), Deck,
                GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string,int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
