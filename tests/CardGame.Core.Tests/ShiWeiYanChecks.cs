using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiWeiYanChecks
{
    private const string Hero = "mobile:shi-wei-yan", Zhuangshi = "mobile:shi-zhuangshi", Yinzhan = "mobile:shi-yinzhan",
        Zhongao = "mobile:shi-zhongao", Kuanggu = "mobile:shi-kuanggu", Kuanggu2 = "mobile:shi-kuanggu-upgraded", Kunfen = "mobile:shi-kunfen",
        Quiet = "fixture:shi-wei-yan-quiet", Driver = "fixture:shi-wei-yan-driver", Peer = "fixture:shi-wei-yan-peer", Deck = "fixture:shi-wei-yan-deck";

    public static void PaidPhaseQuotasYinzhanMissionAndBackwaterCold()
    {
        var s = Scene.Start(4); s.ZhuangshiPrompt();
        s.Need(s.HasSkill(Kuanggu), "GameStarting grants real level-one Kuanggu before the first play phase.");
        foreach (var card in s.V(0).Hand.Where(c => c.Kind is CardKind.Dodge or CardKind.DrawTwo or CardKind.Peach).Take(3).ToArray())
            s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "card-" + card.Id);
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "hp-2");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "pay"); s.Play();
        var grant = s.E<MobileShiWeiYanPhaseGrantEvent>().Single();
        s.Need(grant.DiscardedCards == 3 && grant.LostHp == 2 && s.V(0).Hp == 2,
            "Both chosen costs pay once on their original phase frame and survive cold choice recovery.");
        s.Need(s.Quota("zhuangshi-discard") is { Value: 3, Used: 0 } && s.Quota("zhuangshi-hp") is { Value: 2, Used: 0 },
            "Both first-N phase allowances are publicly inspectable before use.");
        s.Slash(1); s.Play();
        s.Need(!s.V(1).IsAlive && s.HasSkill(Kuanggu2) && !s.HasSkill(Kuanggu) && s.HasSkill(Zhuangshi) &&
            s.E<MobileShiWeiYanMissionEvent>() is [{ Succeeded: true }],
            "The first genuine Slash kill completes the mission and upgrades through shared skill changes without losing Zhuangshi.");
        s.Need(s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 1 && e.Amount == 2) && s.V(0).Hp == 4,
            "Equal pre-damage HP grants Yinzhan's +1; the successful mission reads both unused paid phase budgets and Chengshi independently recovers even after that target dies.");
        s.Slash(2); s.Play();
        s.Need(s.E<MobileShiYinzhanClaimedEvent>().Length >= 1 && s.G.CardMovements.Any(m => m.From == CardLocation.DiscardPile &&
            m.To == CardLocation.Hand(0) && m.Reason.Value == "skill.mobile-shi-wei-yan.obtain") && s.V(0).Hp == 4,
            "A surviving actual Slash target pays one opaque legal discard and Chengshi recovers and obtains its real discarded entity.");
        var yinzhanDiscard = s.G.CardMovements.Single(m => m.From == CardLocation.Hand(2) && m.To == CardLocation.DiscardPile &&
            m.Reason.Value == "skill.mobile-shi-wei-yan.discard");
        s.Need(s.E<NativeLogicalDiscardInvocationRecordedEvent>().Where(e => e.Reason == yinzhanDiscard.Reason &&
                e.SequenceBefore < yinzhanDiscard.Sequence && e.SequenceAfter >= yinzhanDiscard.Sequence).ToArray() is
                [{ InitiatorSeat: 0, ExecutorSeat: 0, SubjectSeat: 2, ActualCount: 1, HandCount: 1 }],
            "Yinzhan records exactly one real discard executed by Wei Yan on the target's hand, rather than attributing the discard to its victim.");
        s.Need(s.Quota("zhuangshi-hp") is { Used: 2 } && s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "Two exempt Slashes leave the ordinary Slash allowance available after the paid first-two quota expires.");
        s.PauseKuanggu = true; s.Slash(3);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Kuanggu2 && s.P.Choices.Any(c => Key(c) == "activate"));
        s.Answer(c => Key(c) == "activate");
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "backwater") == true);
        var old = s.V(0).Hand.Select(c => c.Id).ToHashSet();
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "backwater");
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "kuanggu-cost") == true);
        var newlyDrawn = s.V(0).Hand.Single(c => !old.Contains(c.Id)).Id;
        s.Cold(); s.Answer(c => c.Cards.SequenceEqual([newlyDrawn])); s.PauseKuanggu = false; s.Play();
        var uses = s.E<MobileShiWeiYanUseEvent>().Where(e => e.OwnerSeat == 0).ToArray();
        s.Need(uses.Length == 3 && uses.All(e => e.DistanceAndNoResponse) && uses.Take(2).All(e => e.NoCount) && !uses[2].NoCount,
            "All real uses consume the same ordered phase slots; only the paid first two avoid ordinary count debit.");
        s.Need(s.E<MobileShiKuangguSlashGrantedEvent>().Length == 1 && s.G.CardMovements.Count(m =>
            m.CardId == newlyDrawn && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill.mobile-shi-wei-yan.discard") == 1 &&
            s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "Backwater may discard its newly drawn card after cold recovery and grants one real extra Slash this phase without replaying payment.");
        s.Replay();
    }

    public static void DyingMissionFailureKeepsPaidParentAndKunfenCold()
    {
        var s = Scene.Start(2); s.ZhuangshiPrompt();
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "hp-2");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "pay");
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        s.Need(!s.HasSkill(Zhuangshi) && s.HasSkill(Kunfen) && s.HasSkill(Kuanggu) &&
            s.E<MobileShiWeiYanMissionEvent>() is [{ Succeeded: false }],
            "Entering genuine Dying fails the mission once and replaces only Zhuangshi, before rescue.");
        s.Cold(); s.Rescue(); s.Play();
        s.Need(s.E<ProgramSkillHpLostEvent>().Count(e => e.SkillId == Zhuangshi && e.Amount == 2) == 1 &&
            s.E<MobileShiWeiYanPhaseGrantEvent>() is [{ LostHp: 2 }],
            "Rescuing and cold recovery return the paid original phase frame despite its initiating skill being replaced, without losing HP twice.");
        s.End(); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        s.Need(s.E<ProgramSkillHpLostEvent>().Count(e => e.SkillId == Kunfen && e.Amount == 1) == 1,
            "The acquired locked Kunfen pays its actual end-of-turn HP loss and waits for rescue before drawing.");
        var hand = s.V(0).HandCount; s.Rescue(); s.Play();
        s.Need(s.V(0).HandCount >= hand + 1 && !s.HasSkill(Zhuangshi) && !s.HasSkill(Kuanggu2) &&
            s.E<MobileShiWeiYanMissionEvent>().Length == 1, "Kunfen's draw resumes once after its paid Dying child and mission failure stays terminal.");
        s.Replay();
    }

    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public bool PauseKuanggu { get; set; }
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool HasSkill(string id) => V(0).Skills?.Any(s => s.ContentId == id) == true;
        public ProgramPublicRuleStateSnapshot Quota(string id) => V(0).SkillRuntimeStates!.Single(s => s.SkillId == Zhuangshi).PublicRuleStates!.Single(s => s.StateId == id);
        public void ZhuangshiPrompt() => Reach(() => P?.SkillPrompt?.SkillId == Zhuangshi && P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "hp-2"));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> pick)
        { var p = P!; var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "The expected native choice must exist."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Rescue() => Answer(c => c.Cards.Count == 1 && V(0).Hand.Any(h => h.Id == c.Cards[0] && h.Kind == CardKind.Peach));
        public void Slash(int target)
        {
            var a = G.GetHumanLegalActions().FirstOrDefault(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([target]));
            Need(a is not null, "The actual hand Slash must be legal against target " + target);
            Accept(new PlayCardCommand(0, a!.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
        }
        public void Reach(Func<bool> ready)
        {
            for (var n = 0; n < 180; n++)
            {
                if (ready()) return;
                if (P is { } p)
                {
                    if (p.Kind == DecisionKind.SelectFaction) { Answer(_ => true); continue; }
                    if (p.SkillPrompt?.SkillId is Kuanggu or Kuanggu2 && !PauseKuanggu)
                    { Answer(c => Key(c) == "skip" || c.Parameters.GetValueOrDefault("branch") == "cancel"); continue; }
                    if (p.SkillPrompt?.SkillId == Yinzhan) { Answer(c => c.Parameters.GetValueOrDefault("branch") == "discard"); continue; }
                    if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Shi Wei Yan boundary: " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The bounded scene retains two other living opponents.");
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "The short real native flow must reach its expected boundary.");
        }
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold recovery preserves the paid original frames, current prompt, zones and journal."); }
        public void Replay()
        { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "The command journal reproduces the actual phase quotas, mission, payments and native card result."); }
        public void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + " " + Dump()); }
        private void Accept(GameCommand command) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "The native command was rejected."); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), G.CardMovements, Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).HandCount }), Last = G.Events.TakeLast(5).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() });
        public static Scene Start(int hp)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] opening = [new(CardKind.Slash, Suit.Spade, 3), new(CardKind.Slash, Suit.Heart, 4), new(CardKind.Slash, Suit.Club, 5), new(CardKind.Slash, Suit.Diamond, 6), new(CardKind.Peach, Suit.Heart, 7), new(CardKind.Peach, Suit.Heart, 8), new(CardKind.Dodge, Suit.Club, 9), new(CardKind.DrawTwo, Suit.Spade, 10)];
            var roster = Enumerable.Range(1, 3).Select(seat => new ShanheCombatantDefinition(Peer, Hp: seat == 1 ? 2 : 4)
                { StartingHand = Enumerable.Range(0, 8).Select(i => new ShanheStartingCard(CardKind.Dodge, Suit.Club, i + 1)).ToArray() }).ToArray();
            var setup = new ShanheBattleSetup(615, 1, "shi-wei-yan-" + hp, Hero, hp, 0, 0, 0, 0, [Driver], opening, [], roster, [], new string('b', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-wei-yan", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            // Load the real passive fact consumer without adding it to the table.
            // The native invoice stays demand-driven, as in a game containing Yangwei.
            CharacterFixtureContent.Register(b, Hero, "mou:hua-xiong-ol");
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep-hand","query":"handLimit","operation":"add","value":20,"priority":0}],"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},{"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var p = new Dictionary<string, object> { [Driver] = new { name = "真实手牌场景", description = "保留真实手牌与阶段。" }, [Quiet] = new { name = "安静同桌", description = "保留实际回合，不主动使用牌。" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = p }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "同桌", "", Quiet, "wei", 4));
            b.AddDeck(new(Deck, "固定真实牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势魏延真实支付及使命", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
