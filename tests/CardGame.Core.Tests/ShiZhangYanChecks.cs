using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiZhangYanChecks
{
    private const string Hero = "mobile:shi-zhang-yan", Feijing = "mobile:shi-feijing", Xiaoge = "mobile:shi-xiaoge",
        Driver = "fixture:shi-zhang-yan-driver", Quiet = "fixture:shi-zhang-yan-quiet", Watch = "fixture:shi-zhang-yan-watch", After = "fixture:shi-zhang-yan-after", Peer = "fixture:shi-zhang-yan-peer", Deck = "fixture:shi-zhang-yan-deck";
    public static void FeijingSealedPathOrderedDiscardsPreventRecoverAndExactObtainCold()
    {
        var s = Scene.Start(); s.Play(); var material = s.V(0).Hand.Single(c => c.Kind == CardKind.Duel).Id;
        var unexpected = s.V(0).Hand.Single(c => c.Kind == CardKind.UnexpectedAssault).Id;
        s.Need(s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId == unexpected && a.ConversionSource?.SkillId == Feijing),
            "The genuine Unexpected Assault damage trick is eligible for Feijing's physical Slash conversion.");
        var first = s.V(1).Hand.Single().Id; var second = s.V(2).Hand.Single().Id;
        s.Slash(material, 3, converted: true); s.Offer(); s.Answer(c => Key(c) == "activate"); s.Answer(c => Branch(c) == "path" && c.Parameters["token"] == "1");
        s.Need(s.P is { PlayerSeat: 1, IsPrivate: true } && s.G.CreateSnapshot(0).PendingDecision is null && s.G.CreateSnapshot(2).PendingDecision is null,
            "The chosen path excludes both endpoints and first asks only its actual first participant to commit an original Hand entity privately.");
        s.Answer(c => c.Cards.SequenceEqual([first]));
        s.Need(s.P is { PlayerSeat: 2, IsPrivate: true } && s.E<ShiZyHandCommittedEvent>().Length == 1 && s.E<ShiZyHandRevealedEvent>().Length == 0 &&
            !s.G.CardMovements.Any(m => m.Reason.Value == "skill.mobile-shi-zhang-yan.feijing.discard") && s.G.CreateSnapshot(0).PublicRevealedCards.All(c => c.Id != first),
            "The first sealed choice does not reveal its entity, inspect the next player's choice or pay early.");
        s.Cold(); s.Answer(c => c.Cards.SequenceEqual([second])); s.AtWatch();
        s.Need(s.E<ShiZyHandRevealedEvent>().Select(e => e.CardId).SequenceEqual([first, second]) && s.E<ShiZyHandDiscardedEvent>() is [{ Seat: 1, CardId: var paid }] && paid == first &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Feijing).ShiZhangYan is { Stage: ShiZyStage.DiscardChildren, Cursor: 1 },
            "Both committed original entities are publicly revealed together, then the first real Hand discard pauses on its original paid frame before the second payment.");
        s.Cold(); s.ContinueWatch(); s.AtWatch();
        s.Need(s.E<ShiZyHandDiscardedEvent>().Select(e => e.Seat).SequenceEqual([1, 2]), "Path players genuinely discard in the chosen path's order, including real movement children between the two payments.");
        s.ContinueWatch(); s.Reach(() => s.P?.Choices.Any(c => Branch(c) == "color") == true); s.Answer(c => Branch(c) == "color" && c.Parameters["token"] == "red");
        s.AtWatch();
        s.Need(s.E<ShiZyTargetAddedEvent>().Select(e => e.Target).SequenceEqual([1, 2]) && s.E<ShiZyPreventedEvent>() is [{ Target: 1, CardId: var obtained }] && obtained == first &&
            s.V(0).Hp == 3 && s.V(1).Hp == 8 && s.V(3).Hp == 7 && s.V(0).Hand.Any(c => c.Id == first) && s.E<ShiZyDuelIssuedEvent>().Length == 0,
            "The same native Slash gains the paid red participants, prevents damage to its first extra target, truly recovers one HP and obtains that exact discarded entity before its gain child returns.");
        s.Cold(); s.ContinueWatch(); s.AtWatch(); s.ContinueWatch(); s.Play();
        var action = s.E<CardUseAppearanceCapturedEvent>().Select(e => e.Action).First(a => a.PhysicalCards.Any(c => c.CardId == material));
        s.Need(action is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0, EffectiveKind: CardKind.Slash } && action.ConversionChain is [{ SkillId: Feijing, OwnerSeat: 0 }] &&
            action.PhysicalCards.Single().From == CardLocation.Hand(0) && s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.V(0).Hp == 4 && s.V(2).Hp == 8 && s.V(0).Hand.Any(c => c.Id == second) && s.E<ShiZyPreventedEvent>().Length == 2 &&
            s.E<DamageAppliedEvent>().All(e => e.TargetSeat is not (1 or 2)) && s.E<ShiZyDuelIssuedEvent>().Length == 0,
            "Damage-trick conversion preserves the real use, provider, original Hand payment and single entity debit; each extra target is prevented once and a multi-target Slash never gains a follow-up Duel.");
        s.Cold(); s.Replay();
    }
    public static void XiaogeCompletedSoleSlashIssuesRealNativeDuelAndReturnsCold()
    {
        var s = Scene.Start(duel: true); s.SkipFeijing = true; s.Play(); var material = s.V(0).Hand.First(c => c.Kind == CardKind.Slash).Id;
        var responseMaterial = s.V(1).Hand.Single().Id;
        s.Slash(material, 1); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        var issued = s.E<ShiZyDuelIssuedEvent>().Single(); var origin = issued.Origin;
        var parent = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Xiaoge);
        var original = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == parent.ShiZhangYan!.UseId);
        var native = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == issued.UseId);
        s.Need(original.Step == ResolutionFrameStep.Completed && origin.ParentProgramFrameId == parent.Id && origin.InitialActorSeat == 0 && origin.InitialTargetSeat == 1 &&
            issued.UseId != original.Id && native is { CardId: 0, CardKind: CardKind.Duel, PhysicalCardIds.Count: 0 } &&
            native.Action is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0, EffectiveKind: CardKind.Duel, PhysicalCards.Count: 0 } a && a.ParentActionId == original.Action!.ActionId &&
            s.V(1).Hp == 1 && s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.E<DuelResponseEvent>().Count(e => e.ResolutionId == issued.UseId && e.ResponderSeat == 1 && e.UsedSlash && e.SlashCardId == responseMaterial) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == responseMaterial && m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing) == 1,
            "The completed sole Slash retains its original paid parent; Xiaoge creates a different genuine zero-entity native Duel with the same original actor and target, and reaches its real alternating Slash response without paying the first Slash again.");
        s.Need(parent.ShiZhangYan is { OriginalAttackActive: true, OriginalTargetIndex: 0 } && original.CardAttack is { Active: false },
            "The issued scalar source freezes the completed Slash's original active cursor and suspends it while the genuine native Duel owns the response.");
        s.Cold(); s.Answer(c => c.Cards.Count == 0); s.Reach(() => s.P?.SkillPrompt?.SkillId == After);
        var completed = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == original.Id);
        var after = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == After);
        s.Need(completed.Step == ResolutionFrameStep.Completed && completed.CardAttack is { Active: true } && completed.TargetIndex == 0 &&
            after.WindowContext?.CardUse is { } observed && observed.ParentCardUseFrameId == original.Id && observed.CardActionId == original.Action!.ActionId &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().All(f => f.SkillId != Xiaoge) &&
            s.E<ShiZyAttackRestoredEvent>() is [{ UseId: var restored, Active: true, TargetIndex: 0 }] && restored == original.Id,
            "After the whole typed Duel return, Xiaoge restores only its exact original Slash cursor before leaving; the genuinely later completion candidate can still suspend that original paid parent.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); s.Play();
        s.Need(s.V(0).Hp == 3 && s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 1 && e.TargetSeat == 0 && e.Amount == 1) &&
            s.E<ShiZyDuelReturnedEvent>().Length == 1 && s.E<ShiZyDuelReturnedEvent>().Single().UseId == issued.UseId &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().All(f => f.SkillId != Xiaoge) && s.E<ShiZyDuelIssuedEvent>().Length == 1,
            "Cold resuming the actual Duel response allows its original opponent to deal the real damage, then returns once to the original completed-Slash program without a fabricated response or recursive Duel.");
        s.Replay();
    }
    private static string? Branch(PromptChoice c) => c.Parameters.GetValueOrDefault("branch");
    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public bool SkipFeijing;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message + " " + Dump()); }
        public void Answer(Func<PromptChoice, bool> choose)
        { var p = P!; var choice = p.Choices.FirstOrDefault(choose); Need(choice is not null, "The exact native choice must be published."); Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision)); }
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Offer() => Reach(() => P?.SkillPrompt?.SkillId == Feijing && P.Choices.Any(c => Key(c) == "activate"));
        public void AtWatch() => Reach(() => P?.SkillPrompt?.SkillId == Watch);
        public void ContinueWatch() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Slash(int id, int target, bool converted = false)
        {
            var a = G.GetHumanLegalActions().FirstOrDefault(a => a.Kind == LegalActionKind.Slash && a.CardId == id && a.TargetSeats.SequenceEqual([target]) && (!converted || a.ConversionSource?.SkillId == Feijing));
            Need(a is not null, "The exact original physical or damage-trick converted Slash must be legal.");
            Submit(new PlayCardCommand(0, id, a!.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        }
        public void Reach(Func<bool> ready)
        {
            for (var n = 0; n < 600; n++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (SkipFeijing && p.SkillPrompt?.SkillId == Feijing && p.Choices.Any(c => Key(c) == "skip")) { Answer(c => Key(c) == "skip"); continue; }
                    if (p.SkillPrompt?.SkillId is not (Feijing or Xiaoge or Watch) && p.Choices.Any(c => Key(c) == "skip")) { Answer(c => Key(c) == "skip"); continue; }
                    throw new InvalidOperationException("Unexpected Shi Zhang Yan boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The short genuine scene must keep its participants alive."); Submit(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "The bounded Shi Zhang Yan scene missed its native boundary.");
        }
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold restore preserves original independent Hand commitments, ordered paid children and typed native use returns."); }
        public void Replay()
        { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "The real command journal reproduces every rule fact, physical payment and participant view."); }
        private void Submit(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Shi Zhang Yan command."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(),
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }),
            Starts = E<ShiZyStartedEvent>(), Commits = E<ShiZyHandCommittedEvent>(), Discards = E<ShiZyHandDiscardedEvent>(), Prevents = E<ShiZyPreventedEvent>(), Duels = E<ShiZyDuelIssuedEvent>() });
        public static Scene Start(bool duel = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] own = duel ? [new(CardKind.Slash, Suit.Heart, 5), new(CardKind.Slash, Suit.Diamond, 8)] : [new(CardKind.Duel, Suit.Spade, 5), new(CardKind.UnexpectedAssault, Suit.Club, 9)];
            // At one HP after the original Slash, the actual AI must spend its genuine Slash to survive the native Duel.
            ShanheCombatantDefinition At(int seat) => new(Peer, Hp: duel && seat == 1 ? 2 : 8, ExtraSkillIds: [Watch])
                { StartingHand = [new(duel && seat == 1 ? CardKind.Slash : CardKind.Peach, seat == 2 ? Suit.Diamond : Suit.Heart, 7)] };
            var setup = new ShanheBattleSetup(622, 1, duel ? "duel" : "path", Hero, duel ? 4 : 2, 0, 0, 0, 0, duel ? [Driver, Watch, After] : [Driver, Watch], own, [], [At(1), At(2), At(3)], [], new string('e', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 5 }, registry);
            var s = new Scene(g, registry); s.Submit(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-zhang-yan", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var catalog = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":12,"priority":0}],"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"priority":0,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"priority":0,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},{"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"priority":0,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{After}}","revision":1,"triggers":[{"id":"after-original-slash","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,"priority":-100,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"after","options":[{"id":"continue"}]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"paid-discard","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.mobile-shi-zhang-yan.feijing.discard"],"optional":false,"priority":0,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]},
                {"id":"paid-obtain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.mobile-shi-zhang-yan.xiaoge.obtain"],"optional":false,"priority":0,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> {
                [Driver] = new { name = Driver, description = "保留真实手牌、正常出牌次数。" }, [Quiet] = new { name = Quiet, description = "安静真实对手。" },
                [After] = new { name = "原杀后续完成候选", description = "在骁戈完全返回后继续原杀的真实完成窗。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Watch] = new { name = "原生已支付子结算", description = "停在真实手牌弃置或获得批次后。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "安静真实同桌", "", Quiet, "wei", 8));
            b.AddDeck(new(Deck, "固定真实实体牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 5)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势张燕真实路径及原生决斗检查", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
