using System;
using System.Collections.Generic;
using System.Globalization;
using CluckWars.Logging;
using CluckWars.Progression;
using UnityEngine;
using UnityEngine.UIElements;

namespace CluckWars.UI
{
    /// <summary>
    /// Binds the profile page: the nameplate and its pickers, every record (earned or not) and the
    /// career. A plain C# object owned by <see cref="MenuUiController"/>, not a MonoBehaviour — the
    /// page is one cloned UXML tree inside the menu's document.
    /// </summary>
    /// <remarks>
    /// <b>It reads, it never derives.</b> Everything shown comes from
    /// <see cref="IProgressionService.Identity"/>, which the service refolds from the journal. This
    /// class decides no truth of its own — including which parts are selectable, which the service
    /// refuses anyway.
    /// <para>
    /// <b>No free text and no glyphs.</b> There is no <c>TextField</c> here: names are generated and
    /// re-rolled. Rings, pips and locked pickers are USS shapes, because LilitaOne has no check, star,
    /// padlock or arrow to draw them with (see <c>Assets/UI/Styles/Profile.uss</c>).
    /// </para>
    /// </remarks>
    public sealed class ProfileController : IDisposable
    {
        private const string Source = "MenuUI";

        /// <summary>The filter that shows everything. Not a group name, so it cannot collide with one.</summary>
        private const string AllFilter = "";

        private readonly VisualElement _page;
        private readonly IProgressionService _progression;
        private readonly ILogService _log;
        private readonly Func<string, string> _roleLabel;
        private readonly Func<string, Color> _roleTint;

        private VisualElement _content, _plate, _emblem, _titleChoices, _emblemChoices, _bannerChoices;
        private VisualElement _recordFilters, _recordList, _careerGrid, _bestGrid, _recentStrip, _roleList;
        private Label _notReady, _plateName, _plateTitle, _emblemLevel, _recordsEmpty, _recentEmpty;
        private Button _rerollBtn;

        /// <summary>The USS banner class currently on the plate, so the next one can replace it.</summary>
        private string _plateBannerClass;

        private string _groupFilter = AllFilter;
        private bool _losingOnly;
        private bool _subscribed;

        /// <param name="roleLabel">Turns a role key into the name the player knows it by.</param>
        /// <param name="roleTint">Turns a role key into its class colour, for the emblem ring.</param>
        public ProfileController(VisualElement page, IProgressionService progression, ILogService log,
            Func<string, string> roleLabel, Func<string, Color> roleTint)
        {
            _page = page;
            _progression = progression;
            _log = log;
            _roleLabel = roleLabel ?? (key => key);
            _roleTint = roleTint ?? (_ => UiGfx.TextPrimary);

            Bind();
        }

        private void Bind()
        {
            if (_page == null) return;

            _content = _page.Q<VisualElement>("ProfileContent");
            _notReady = _page.Q<Label>("ProfileNotReady");
            _plate = _page.Q<VisualElement>("PlatePreview");
            _emblem = _page.Q<VisualElement>("PlateEmblem");
            _emblemLevel = _page.Q<Label>("PlateEmblemLevel");
            _plateName = _page.Q<Label>("PlateName");
            _plateTitle = _page.Q<Label>("PlateTitle");
            _titleChoices = _page.Q<VisualElement>("TitleChoices");
            _emblemChoices = _page.Q<VisualElement>("EmblemChoices");
            _bannerChoices = _page.Q<VisualElement>("BannerChoices");
            _recordFilters = _page.Q<VisualElement>("RecordFilters");
            _recordList = _page.Q<VisualElement>("RecordList");
            _recordsEmpty = _page.Q<Label>("RecordsEmpty");
            _careerGrid = _page.Q<VisualElement>("CareerGrid");
            _bestGrid = _page.Q<VisualElement>("BestGrid");
            _recentStrip = _page.Q<VisualElement>("RecentStrip");
            _recentEmpty = _page.Q<Label>("RecentEmpty");
            _roleList = _page.Q<VisualElement>("RoleList");

            _rerollBtn = _page.Q<Button>("RerollNameBtn");
            if (_rerollBtn != null) _rerollBtn.clicked += RerollName;

            if (_progression == null)
            {
                // IProgressionService is bound project-wide, so a null here is a wiring bug — and the
                // page would silently read as "you have done nothing" rather than "unavailable".
                _log?.Error(Source, "IProgressionService was not injected, so the profile page stays on its " +
                    "unavailable message. Check the ProjectInstaller bindings.");
                Refresh();
                return;
            }

            _progression.OnIdentityChanged += OnIdentityChanged;
            _progression.OnProfileChanged += OnProfileChanged;
            _subscribed = true;
            Refresh();
        }

        public void Dispose()
        {
            if (_rerollBtn != null) _rerollBtn.clicked -= RerollName;
            if (!_subscribed || _progression == null) return;

            _progression.OnIdentityChanged -= OnIdentityChanged;
            _progression.OnProfileChanged -= OnProfileChanged;
            _subscribed = false;
        }

        private void OnIdentityChanged(ProgressionIdentity identity) => Refresh();
        private void OnProfileChanged(ProgressionProfile profile) => Refresh();

        private void RerollName()
        {
            // The service writes the new name to the journal before it changes anything, and raises
            // OnIdentityChanged when it has. A false is already logged there; nothing to do here but
            // leave the old name on screen, which is the truth.
            _progression?.TryRerollName();
        }

        private void Select(NameplateSlot slot, string key)
        {
            _progression?.TrySelectNameplatePart(slot, key);
        }

        // ---- Refresh ---------------------------------------------------------------------

        /// <summary>Redraws the whole page from the current snapshot. Cheap enough to do wholesale.</summary>
        public void Refresh()
        {
            bool ready = _progression != null && _progression.IsReady;
            if (_content != null) _content.style.display = ready ? DisplayStyle.Flex : DisplayStyle.None;
            if (_notReady != null) _notReady.style.display = ready ? DisplayStyle.None : DisplayStyle.Flex;
            if (!ready) return;

            var identity = _progression.Identity;
            DrawPlate(identity);
            DrawTitleChoices(identity);
            DrawEmblemChoices(identity);
            DrawBannerChoices(identity);
            DrawFilters(identity);
            DrawRecords(identity);
            DrawCareer(identity, _progression.Profile);
            DrawRoles(identity);
        }

        private void DrawPlate(ProgressionIdentity identity)
        {
            var plate = identity.Plate;

            if (_plateName != null)
            {
                _plateName.text = plate.Name ?? string.Empty;
                _plateName.style.display = plate.HasName ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_plateTitle != null)
            {
                _plateTitle.text = plate.TitleText ?? string.Empty;
                _plateTitle.style.display = string.IsNullOrEmpty(plate.TitleText) ? DisplayStyle.None : DisplayStyle.Flex;
            }

            PaintEmblem(_emblem, _emblemLevel, plate.EmblemRoleKey, plate.EmblemMasteryLevel);

            if (_plate == null) return;
            if (!string.IsNullOrEmpty(_plateBannerClass)) _plate.RemoveFromClassList(_plateBannerClass);
            _plateBannerClass = plate.BannerUssClass;
            if (!string.IsNullOrEmpty(_plateBannerClass)) _plate.AddToClassList(_plateBannerClass);
        }

        /// <summary>Tints one emblem ring and writes the mastery level inside it.</summary>
        private void PaintEmblem(VisualElement ring, Label level, string roleKey, int masteryLevel)
        {
            if (ring != null && !string.IsNullOrEmpty(roleKey))
            {
                var tint = _roleTint(roleKey);
                ring.style.borderTopColor = tint;
                ring.style.borderBottomColor = tint;
                ring.style.borderLeftColor = tint;
                ring.style.borderRightColor = tint;
            }

            if (level != null) level.text = masteryLevel.ToString(CultureInfo.InvariantCulture);
        }

        private void DrawTitleChoices(ProgressionIdentity identity)
        {
            if (_titleChoices == null) return;
            _titleChoices.Clear();

            // "No title" first: taking one off has to be as easy as putting one on.
            _titleChoices.Add(Pick("NO TITLE", owned: true, on: string.IsNullOrEmpty(identity.Plate.TitleKey),
                () => Select(NameplateSlot.Title, string.Empty)));

            foreach (var record in identity.Records)
            {
                if (string.IsNullOrEmpty(record.Title)) continue;

                string key = record.Key;
                _titleChoices.Add(Pick(record.Title.ToUpperInvariant(), record.Earned,
                    string.Equals(key, identity.Plate.TitleKey, StringComparison.Ordinal),
                    () => Select(NameplateSlot.Title, key)));
            }
        }

        private void DrawEmblemChoices(ProgressionIdentity identity)
        {
            if (_emblemChoices == null) return;
            _emblemChoices.Clear();

            foreach (var role in identity.Roles)
            {
                string key = role.RoleKey;
                string label = $"{_roleLabel(key).ToUpperInvariant()} {role.Level.ToString(CultureInfo.InvariantCulture)}";
                var pick = Pick(label, role.Playable,
                    string.Equals(key, identity.Plate.EmblemRoleKey, StringComparison.Ordinal),
                    () => Select(NameplateSlot.Emblem, key));

                if (role.Playable) pick.style.color = _roleTint(key);
                _emblemChoices.Add(pick);
            }
        }

        private void DrawBannerChoices(ProgressionIdentity identity)
        {
            if (_bannerChoices == null) return;
            _bannerChoices.Clear();

            foreach (var banner in identity.Banners)
            {
                string key = banner.Key;
                _bannerChoices.Add(Pick(banner.DisplayName.ToUpperInvariant(), banner.Owned,
                    string.Equals(key, identity.Plate.BannerKey, StringComparison.Ordinal),
                    () => Select(NameplateSlot.Banner, key)));
            }
        }

        /// <summary>
        /// One picker chip. An unowned one is drawn locked and does nothing when tapped — not disabled,
        /// so it still reads at full contrast: seeing what is not yours yet is the point of showing it.
        /// </summary>
        private Button Pick(string text, bool owned, bool on, Action activate)
        {
            var button = new Button { text = text };
            button.AddToClassList("cw-pick");
            if (!owned) button.AddToClassList("cw-pick--locked");
            else if (on) button.AddToClassList("cw-pick--on");
            if (owned) button.clicked += activate;
            return button;
        }

        // ---- Records ---------------------------------------------------------------------

        private void DrawFilters(ProgressionIdentity identity)
        {
            if (_recordFilters == null) return;
            _recordFilters.Clear();

            _recordFilters.Add(Filter("ALL", _groupFilter == AllFilter && !_losingOnly, () =>
            {
                _groupFilter = AllFilter;
                _losingOnly = false;
            }));

            // Straight from the enum, so a group added later gets a filter without an edit here.
            foreach (string group in ProgressionIdentity.RecordGroups)
            {
                string chosen = group;
                _recordFilters.Add(Filter(group.ToUpperInvariant(),
                    !_losingOnly && string.Equals(_groupFilter, chosen, StringComparison.Ordinal), () =>
                    {
                        _groupFilter = chosen;
                        _losingOnly = false;
                    }));
            }

            _recordFilters.Add(Filter("WHILE LOSING", _losingOnly, () =>
            {
                _groupFilter = AllFilter;
                _losingOnly = true;
            }));
        }

        private Button Filter(string text, bool on, Action choose)
        {
            var button = new Button { text = text };
            button.AddToClassList("cw-pick");
            if (on) button.AddToClassList("cw-pick--on");
            button.clicked += () =>
            {
                choose();
                Refresh();
            };
            return button;
        }

        private void DrawRecords(ProgressionIdentity identity)
        {
            if (_recordList == null) return;
            _recordList.Clear();

            int shown = 0;
            foreach (var record in identity.Records)
            {
                if (_losingOnly && !record.AchievableWhileLosing) continue;
                if (!_losingOnly && _groupFilter != AllFilter &&
                    !string.Equals(record.Group, _groupFilter, StringComparison.Ordinal))
                {
                    continue;
                }

                _recordList.Add(RecordRow(record));
                shown++;
            }

            if (_recordsEmpty == null) return;
            _recordsEmpty.text = identity.Records.Count == 0
                ? "No records are defined in this build."
                : "Nothing matches that filter.";
            _recordsEmpty.style.display = shown == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private VisualElement RecordRow(ProgressionIdentity.RecordView record)
        {
            var row = new VisualElement();
            row.AddToClassList("cw-record");
            if (record.Earned) row.AddToClassList("cw-record--earned");

            var head = new VisualElement();
            head.AddToClassList("cw-record__head");

            var name = new Label(record.Name ?? record.Key);
            name.AddToClassList("cw-record__name");
            head.Add(name);

            var meta = new Label(MetaFor(record));
            meta.AddToClassList("cw-record__meta");
            head.Add(meta);
            row.Add(head);

            var description = new Label(DescriptionFor(record));
            description.AddToClassList("cw-record__desc");
            row.Add(description);

            if (record.HasProgress && !record.Earned) row.Add(ProgressTrack(record));
            return row;
        }

        private static string MetaFor(ProgressionIdentity.RecordView record)
        {
            if (record.Earned)
                return string.IsNullOrEmpty(record.EarnedOnDay) ? "EARNED" : "EARNED " + record.EarnedOnDay;

            return record.HasProgress
                ? $"BEST {Number(record.Progress)} OF {Number(record.Target)}"
                : record.Group.ToUpperInvariant();
        }

        private static string DescriptionFor(ProgressionIdentity.RecordView record) =>
            string.IsNullOrEmpty(record.Title)
                ? record.Description
                : $"{record.Description}  Title: {record.Title}.";

        private static VisualElement ProgressTrack(ProgressionIdentity.RecordView record)
        {
            var track = new VisualElement();
            track.AddToClassList("cw-record__track");

            var fill = new VisualElement();
            fill.AddToClassList("cw-record__fill");
            float fraction = record.Target > 0f ? Mathf.Clamp01(record.Progress / record.Target) : 0f;
            fill.style.width = Length.Percent(fraction * 100f);
            track.Add(fill);
            return track;
        }

        // ---- Career ----------------------------------------------------------------------

        private void DrawCareer(ProgressionIdentity identity, ProgressionProfile profile)
        {
            var career = identity.Summary;

            if (_careerGrid != null)
            {
                _careerGrid.Clear();
                _careerGrid.Add(Stat(Number(career.Rounds), "ROUNDS"));
                _careerGrid.Add(Stat(Number(career.Wins), "WINS"));
                _careerGrid.Add(Stat(Number(profile.GrainBalance), "GRAIN"));
                _careerGrid.Add(Stat(Number(career.Banked), "BANKED"));
                _careerGrid.Add(Stat(Number(career.Stolen), "STOLEN"));
                _careerGrid.Add(Stat(Number(career.RivalsRobbed), "RIVALS ROBBED"));
            }

            if (_bestGrid != null)
            {
                _bestGrid.Clear();
                _bestGrid.Add(Stat(Number(career.BestBankedInARound), "MOST BANKED"));
                _bestGrid.Add(Stat(Number(career.BestStolenInARound), "MOST STOLEN"));
                _bestGrid.Add(Stat(Number(career.BestRivalsRobbedInARound), "MOST ROBBED"));
            }

            if (_recentStrip == null) return;
            _recentStrip.Clear();
            foreach (int placement in career.RecentPlacements) _recentStrip.Add(FormPip(placement));
            if (_recentEmpty != null)
                _recentEmpty.style.display = career.RecentPlacements.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static VisualElement Stat(string value, string label)
        {
            var cell = new VisualElement();
            cell.AddToClassList("cw-career-stat");

            var big = new Label(value);
            big.AddToClassList("cw-career-stat__value");
            cell.Add(big);

            var caption = new Label(label);
            caption.AddToClassList("cw-career-stat__label");
            cell.Add(caption);
            return cell;
        }

        /// <summary>One round of recent form: a pip carrying its placement as a digit, no glyph.</summary>
        private static VisualElement FormPip(int placement)
        {
            var pip = new VisualElement();
            pip.AddToClassList("cw-form-pip");
            if (placement == 1) pip.AddToClassList("cw-form-pip--won");

            var label = new Label(placement.ToString(CultureInfo.InvariantCulture));
            label.AddToClassList("cw-form-pip__place");
            pip.Add(label);
            return pip;
        }

        private void DrawRoles(ProgressionIdentity identity)
        {
            if (_roleList == null) return;
            _roleList.Clear();

            foreach (var role in identity.Roles)
            {
                var row = new VisualElement();
                row.AddToClassList("cw-record");

                var head = new VisualElement();
                head.AddToClassList("cw-record__head");

                var name = new Label($"{_roleLabel(role.RoleKey).ToUpperInvariant()}  MASTERY {Number(role.Level)}");
                name.AddToClassList("cw-record__name");
                name.style.color = _roleTint(role.RoleKey);
                head.Add(name);

                var meta = new Label($"{Number(role.Rounds)} ROUNDS, {Number(role.Wins)} WINS");
                meta.AddToClassList("cw-record__meta");
                head.Add(meta);
                row.Add(head);

                var detail = new Label(role.RoundsToNextLevel > 0
                    ? $"Best round: {Number(role.BestBanked)} banked. {Number(role.RoundsToNextLevel)} more rounds to mastery {Number(role.Level + 1)}."
                    : $"Best round: {Number(role.BestBanked)} banked. Mastery is at its highest.");
                detail.AddToClassList("cw-record__desc");
                row.Add(detail);

                _roleList.Add(row);
            }
        }

        /// <summary>
        /// A number for display. Invariant always: this machine is es-ES, where the default would print
        /// <c>1,5</c> and a thousands separator as a dot.
        /// </summary>
        private static string Number(double value) => value.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
