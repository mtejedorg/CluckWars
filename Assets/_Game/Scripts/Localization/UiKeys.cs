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

        // ---- Classes: short (cast band), full name, role ----
        public const string ClassWarriorShort = "class.warrior.short";
        public const string ClassSpeedyShort = "class.speedy.short";
        public const string ClassFattyShort = "class.fatty.short";
        public const string ClassAssassinShort = "class.assassin.short";
        public const string ClassWarriorName = "class.warrior.name";
        public const string ClassSpeedyName = "class.speedy.name";
        public const string ClassFattyName = "class.fatty.name";
        public const string ClassAssassinName = "class.assassin.name";
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
        public const string PerkHoarderLine = "perk.hoarder.line";
        [UiArgs("cap")] public const string PerkHoarderDetail = "perk.hoarder.detail";
        public const string PerkFeatherfootLine = "perk.featherfoot.line";
        public const string PerkFeatherfootDetail = "perk.featherfoot.detail";
        [UiArgs("food")] public const string PerkSpoilerLine = "perk.spoiler.line";
        [UiArgs("goal", "food")] public const string PerkSpoilerDetail = "perk.spoiler.detail";

        // ---- Class select: pre-filled slots and perk pills ----
        [UiArgs("abilities")] public const string TagPreFilledOne = "tag.preFilled.one";
        [UiArgs("abilities")] public const string TagPreFilledTwo = "tag.preFilled.two";
        [UiArgs("abilities")] public const string PillStartsWith = "pill.startsWith";

        // ---- Loadout ----
        public const string LoadoutRowShared = "loadout.row.shared";
        [UiArgs("cls")] public const string LoadoutRowClass = "loadout.row.class";
        public const string LoadoutHintShared = "loadout.hint.shared";
        public const string LoadoutHintClass = "loadout.hint.class";
        [UiArgs("n")] public const string LoadoutCount = "loadout.count";
        public const string LoadoutDetailEmpty = "loadout.detail.empty";
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

        // ---- Lobby ----
        public const string LobbyRules = "lobby.rules";
        public const string LobbyFlock = "lobby.flock";
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
        public const string LobbyCountUnknown = "lobby.count.unknown";
        public const string LobbyStatusSolo = "lobby.status.solo";
        public const string LobbyStatusWaiting = "lobby.status.waiting";
        public const string LobbyStatusEnterCode = "lobby.status.enterCode";
        [UiArgs("n")] public const string LobbyWaitingFor = "lobby.waitingFor";
        [UiArgs("n")] public const string LobbyPlayerTag = "lobby.playerTag";
        [UiArgs("cls", "role")] public const string LobbyClassLine = "lobby.classLine";
        public const string LobbyCodePlaceholder = "lobby.codePlaceholder";
        [UiArgs("code")] public const string LobbyCopied = "lobby.copied";
        public const string LobbyErrCreate = "lobby.err.create";
        public const string LobbyErrNoCode = "lobby.err.noCode";
        public const string LobbyErrJoin = "lobby.err.join";
        public const string LobbyJoining = "lobby.joining";
        public const string TagHost = "tag.host";
        public const string TagCpu = "tag.cpu";
        public const string StateReady = "state.ready";
        public const string StatePicking = "state.picking";
        public const string LobbyBot1 = "lobby.bot.1";
        public const string LobbyBot2 = "lobby.bot.2";
        public const string LobbyBot3 = "lobby.bot.3";

    }
}
