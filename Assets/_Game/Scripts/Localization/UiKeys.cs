namespace CluckWars.Localization
{
    /// <summary>
    /// Compile-time handles for every key in <c>Resources/Text/UiText.csv</c>. Code never
    /// passes a key literal to <see cref="UiText"/>; it names one of these, so a typo is a
    /// compile error and "which strings exist" is greppable.
    /// </summary>
    /// <remarks>
    /// <b>To add a string:</b> add the row to the CSV, add a constant here (named by
    /// PascalCasing the dotted key), and, if the template has <c>{placeholders}</c>, list
    /// them in <see cref="UiArgsAttribute"/> - that attribute is the contract between the
    /// template and every call site. <c>UiTextTests</c> fails if a CSV row has no constant,
    /// a constant has no row, or the attribute and the template disagree. UXML text keys
    /// (<c>text="@key"</c>) are literal strings in the UXML and checked against the CSV by
    /// the same suite.
    /// </remarks>
    public static class UiKeys
    {
        // ---- Main menu ----
        public const string MainTitle = "main.title";
        public const string MainSubtitle = "main.subtitle";
        public const string MainSoloHint = "main.soloHint";
        [UiArgs("version")] public const string MainBuild = "main.build";
        public const string BtnPlaySolo = "btn.playSolo";
        public const string BtnHost = "btn.host";
        public const string BtnJoin = "btn.join";
        public const string BtnAbilityLab = "btn.abilityLab";
        public const string BtnPlayAgain = "btn.playAgain";
        [UiArgs("cls", "perk")] public const string BtnPlayAgainSub = "btn.playAgain.sub";
        public const string BtnBackToLobby = "btn.backToLobby";

        // ---- Navigation and screen titles ----
        public const string NavHome = "nav.home";
        public const string NavBack = "nav.back";
        public const string ScreenClassTitle = "screen.class.title";
        public const string ScreenLoadoutTitle = "screen.loadout.title";
        public const string ScreenLobbyTitle = "screen.lobby.title";

        // ---- Primary buttons ----
        public const string BtnNext = "btn.next";
        public const string BtnReady = "btn.ready";
        public const string BtnPickMoreOne = "btn.pickMore.one";
        [UiArgs("n")] public const string BtnPickMoreMany = "btn.pickMore.many";
        public const string BtnStart = "btn.start";
        public const string BtnJoinMatch = "btn.joinMatch";
        public const string BtnShare = "btn.share";
        public const string BtnCopy = "btn.copy";

        // ---- Labels ----
        public const string LabelPerk = "label.perk";
        public const string LabelStarters = "label.starters";
        public const string LabelStarter = "label.starter";
        public const string LabelYou = "label.you";

        // ---- Classes: name (cast band, tiles, hero), role ----
        public const string ClassWarriorShort = "class.warrior.short";
        public const string ClassSpeedyShort = "class.speedy.short";
        public const string ClassFattyShort = "class.fatty.short";
        public const string ClassAssassinShort = "class.assassin.short";
        public const string RoleWarrior = "role.warrior";
        public const string RoleSpeedy = "role.speedy";
        public const string RoleFatty = "role.fatty";
        public const string RoleAssassin = "role.assassin";
        [UiArgs("quote")] public const string ClassQuote = "class.quote";

        // ---- Class callouts ----
        public const string CalloutWarriorStrong = "callout.warrior.strong";
        public const string CalloutWarriorWeak = "callout.warrior.weak";
        public const string CalloutSpeedyStrong = "callout.speedy.strong";
        public const string CalloutSpeedyWeak = "callout.speedy.weak";
        public const string CalloutFattyStrong = "callout.fatty.strong";
        public const string CalloutFattyWeak = "callout.fatty.weak";
        public const string CalloutAssassinStrong = "callout.assassin.strong";
        public const string CalloutAssassinWeak = "callout.assassin.weak";
        public const string GlyphPlus = "glyph.plus";
        public const string GlyphMinus = "glyph.minus";

        // ---- Perks (templated from each passive's own fields) ----
        [UiArgs("pct")] public const string PerkRelentlessLine = "perk.relentless.line";
        public const string PerkRelentlessDetail = "perk.relentless.detail";
        [UiArgs("steal", "cap")] public const string PerkBullyLine = "perk.bully.line";
        [UiArgs("steal")] public const string PerkThiefLine = "perk.thief.line";
        [UiArgs("pct")] public const string PerkSlipperyLine = "perk.slippery.line";
        public const string PerkSlipperyDetail = "perk.slippery.detail";
        public const string PerkBulwarkLine = "perk.bulwark.line";
        [UiArgs("kb", "cc")] public const string PerkBulwarkDetail = "perk.bulwark.detail";
        [UiArgs("cap")] public const string PerkHoarderLine = "perk.hoarder.line";
        [UiArgs("cap")] public const string PerkHoarderDetail = "perk.hoarder.detail";
        public const string PerkFeatherfootLine = "perk.featherfoot.line";
        public const string PerkFeatherfootDetail = "perk.featherfoot.detail";
        [UiArgs("food")] public const string PerkSpoilerLine = "perk.spoiler.line";
        [UiArgs("goal", "food")] public const string PerkSpoilerDetail = "perk.spoiler.detail";

        // ---- Loadout ----
        public const string LoadoutRowShared = "loadout.row.shared";
        [UiArgs("cls")] public const string LoadoutRowClass = "loadout.row.class";
        public const string LoadoutDetailEmpty = "loadout.detail.empty";
        public const string LoadoutNext = "loadout.next";
        public const string LoadoutRegistryMissing = "loadout.registryMissing";
        public const string SlotEmpty = "slot.empty";
        public const string HintConsole = "hint.console";
        public const string CategorySteal = "category.steal";
        public const string CategoryControl = "category.control";
        public const string CategoryDefense = "category.defense";
        public const string CategoryUtility = "category.utility";
        public const string CooldownShort = "cooldown.short";
        public const string CooldownMed = "cooldown.med";

        // ---- Settings ----
        public const string SettingsTitle = "settings.title";
        public const string SettingsClose = "settings.close";
        public const string SettingsRangeGuides = "settings.rangeGuides";
        public const string SettingsRangeGuidesDesc = "settings.rangeGuides.desc";
        public const string SettingsDevMode = "settings.devMode";
        public const string SettingsDevModeDesc = "settings.devMode.desc";
        public const string SettingsPerformance = "settings.performance";
        public const string SettingsPerformanceDesc = "settings.performance.desc";
        public const string SettingsReducedMotion = "settings.reducedMotion";
        public const string SettingsReducedMotionDesc = "settings.reducedMotion.desc";
        public const string SettingsAutoPeck = "settings.autoPeck";
        public const string SettingsAutoPeckDesc = "settings.autoPeck.desc";
        public const string SettingsQuickMoves = "settings.quickMoves";
        public const string SettingsQuickMovesDesc = "settings.quickMoves.desc";

        // ---- Lobby ----
        public const string LobbyRules = "lobby.rules";
        public const string LobbyInviteLabel = "lobby.invite.label";
        public const string LobbyJoinLabel = "lobby.join.label";
        public const string LobbySettingArena = "lobby.setting.arena";
        public const string LobbySettingTime = "lobby.setting.time";
        public const string LobbySettingGoal = "lobby.setting.goal";
        public const string LobbySettingMode = "lobby.setting.mode";
        public const string LobbyValueArena = "lobby.value.arena";
        public const string LobbyValueMode = "lobby.value.mode";
        [UiArgs("n")] public const string LobbyValueGoal = "lobby.value.goal";
        [UiArgs("n", "max")] public const string LobbyCount = "lobby.count";
        public const string LobbyStatusSolo = "lobby.status.solo";
        public const string LobbyStatusWaiting = "lobby.status.waiting";
        public const string LobbyHintSolo = "lobby.hint.solo";
        public const string LobbyStatusEnterCode = "lobby.status.enterCode";
        [UiArgs("n")] public const string LobbyWaitingFor = "lobby.waitingFor";
        /// <summary>An open seat whose corner is not known yet (a guest before joining).</summary>
        public const string LobbyWaitingForPlayer = "lobby.waitingForPlayer";
        [UiArgs("n")] public const string LobbyPlayerTag = "lobby.playerTag";
        [UiArgs("cls", "role")] public const string LobbyClassLine = "lobby.classLine";
        public const string LobbyCodePlaceholder = "lobby.codePlaceholder";
        [UiArgs("code")] public const string LobbyCopied = "lobby.copied";
        public const string LobbyErrCreate = "lobby.err.create";
        public const string LobbyErrNoCode = "lobby.err.noCode";
        public const string LobbyErrJoin = "lobby.err.join";
        public const string LobbyErrLoad = "lobby.err.load";
        public const string LobbyJoining = "lobby.joining";
        public const string TagHost = "tag.host";
        public const string TagCpu = "tag.cpu";
        public const string StateReady = "state.ready";
        public const string StatePicking = "state.picking";
        public const string LobbyAllReady = "lobby.allReady";
        public const string CountdownGo = "countdown.go";
        public const string LobbyBot1 = "lobby.bot.1";
        public const string LobbyBot2 = "lobby.bot.2";
        public const string LobbyBot3 = "lobby.bot.3";

        // ---- Post-match overlay ----
        public const string PostmatchStandings = "postmatch.standings";
        [UiArgs("name")] public const string PostmatchWins = "postmatch.wins";
        public const string PostmatchEnded = "postmatch.ended";
        [UiArgs("n")] public const string PostmatchTargetReached = "postmatch.target.reached";
        [UiArgs("n")] public const string PostmatchTargetTimeout = "postmatch.target.timeout";
        public const string PostmatchHostOnly = "postmatch.hostOnly";
        [UiArgs("n")] public const string PostmatchKos = "postmatch.kos";
        [UiArgs("cls")] public const string PostmatchWinSub = "postmatch.winSub";
        public const string PostmatchNoWinner = "postmatch.noWinner";

        // ---- Phase 4 overlays (in-match lobby, session end, intro, HUD goal) ----
        public const string PostmatchYouWin = "postmatch.youWin";
        public const string PostmatchDraw = "postmatch.draw";
        [UiArgs("you", "place")] public const string PostmatchYouPlace = "postmatch.youPlace";
        public const string LobbyHintHost = "lobby.hint.host";
        public const string LobbyHintGuest = "lobby.hint.guest";
        public const string SessionEnded = "session.ended";
        [UiArgs("n")] public const string SessionReturning = "session.returning";
        public const string CountdownGetReady = "countdown.getReady";
        [UiArgs("n")] public const string HudGoal = "hud.goal";
        public const string HudGoalUnknown = "hud.goal.unknown";
        [UiArgs("name")] public const string NameplateBounty = "nameplate.bounty";

        // ---- Phase 4 menu (GEAR UP caret, join status) ----
        public const string LoadoutSwap = "loadout.swap";
        public const string LobbyStatusCodeReady = "lobby.status.codeReady";

        // ---- Phase 4 HUD (re-audit item 4: top-bar timer + ranks, final-minute event banner) ----
        public const string HudTimerNone = "hud.timer.none";
        public const string HudTimerWait = "hud.timer.wait";
        public const string HudTimerEnded = "hud.timer.ended";
        public const string HudRank1 = "hud.rank.1";
        public const string HudRank2 = "hud.rank.2";
        public const string HudRank3 = "hud.rank.3";
        public const string HudRank4 = "hud.rank.4";
        [UiArgs("n")] public const string HudEventHeader = "hud.event.header";
        public const string HudEventGoldenPile = "hud.event.goldenPile";
        public const string HudEventUnderdogSurge = "hud.event.underdogSurge";
        public const string HudEventLeaderBounty = "hud.event.leaderBounty";
        public const string HudEventRestock = "hud.event.restock";

        // ---- Phase 5 (round-2 findings 11 + 13: GET READY waiting line, host-left notice) ----
        [UiArgs("name")] public const string CountdownWaitingFor = "countdown.waitingFor";
        public const string CountdownWaitingSomeone = "countdown.waitingSomeone";
        public const string SessionHostLeft = "session.hostLeft";

        // ---- Phase 5 chunk 2 (round-2 findings 2 + 12: in-match waiting room, session end) ----
        public const string NavLeave = "nav.leave";
        public const string BtnChangeBird = "btn.changeBird";
        public const string SessionClosed = "session.closed";
        public const string SessionReasonSignal = "session.reason.signal";
        public const string SessionReasonClosed = "session.reason.closed";
        public const string SessionReasonFull = "session.reason.full";
        public const string SessionReasonBusy = "session.reason.busy";
        public const string SessionReasonNotFound = "session.reason.notFound";
        public const string SessionReasonAuth = "session.reason.auth";
        public const string SessionReasonVersion = "session.reason.version";
        public const string SessionReasonGeneric = "session.reason.generic";

        // ---- Phase 5 chunk 5 (round-2 finding 10: join pill for a half-typed code) ----
        public const string LobbyStatusKeepTyping = "lobby.status.keepTyping";

    }
}
