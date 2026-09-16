using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CluckWars.Progression;
using NUnit.Framework;
using UnityEngine;
using static CluckWars.Tests.ProgressionFixtures;
using LogLevel = CluckWars.Logging.LogLevel;

namespace CluckWars.Tests
{
    // ==== Profile lines in the journal ==================================================

    public sealed class ProfileJournalTests
    {
        private string _dir;
        private JournalStore _store;

        [SetUp]
        public void SetUp()
        {
            _dir = TempJournal.NewDirectory();
            _store = new JournalStore(_dir);
        }

        [TearDown]
        public void TearDown() => TempJournal.Delete(_dir);

        private static ProfileEvent Name(string value, string id, DateTime at) =>
            ProfileEvent.Of(ProfileEventTypes.Name, value, id, at);

        private void AppendRaw(string text)
        {
            Directory.CreateDirectory(_dir);
            using (var stream = new FileStream(_store.JournalPath, FileMode.Append, FileAccess.Write))
            {
                byte[] bytes = TempJournal.Utf8.GetBytes(text);
                stream.Write(bytes, 0, bytes.Length);
            }
        }

        [Test]
        public void AProfileLine_RoundTrips_UnderASpanishCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("es-ES");
                Assert.AreEqual("44,97", 44.97f.ToString(CultureInfo.CurrentCulture),
                    "This test is only meaningful under a comma-decimal culture.");

                var written = Name("SwiftBeak#2213", "abc123", Noon);
                var append = _store.Append(written);
                Assert.IsTrue(append.Succeeded, append.Error?.Message);

                var read = _store.Load().ProfileEvents.Single();
                Assert.AreEqual(ProfileEvent.ProfileKind, read.Kind);
                Assert.AreEqual(ProfileEvent.CurrentSchemaVersion, read.SchemaVersion);
                Assert.AreEqual("abc123", read.EventId);
                Assert.AreEqual(ProfileEventTypes.Name, read.Type);
                Assert.AreEqual("SwiftBeak#2213", read.Value);
                Assert.IsTrue(ProgressionCalendar.TryParseUtc(read.AtUtc, out var at), "The instant parses under es-ES.");
                Assert.AreEqual(Noon, at);

                Assert.AreEqual(JsonUtility.ToJson(read), JsonUtility.ToJson(append.Event),
                    "The event the append hands back must be the one the line reads back as.");
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Test]
        public void RoundsAndProfileEvents_ShareTheFile_AndLoadIntoTheirOwnLists()
        {
            Assert.IsTrue(_store.Append(Outcome("r1", Noon)).Succeeded);
            Assert.IsTrue(_store.Append(Name("MerryComb#0007", "e1", Noon.AddMinutes(1))).Succeeded);
            Assert.IsTrue(_store.Append(Outcome("r2", Noon.AddMinutes(2))).Succeeded);
            Assert.IsTrue(_store.Append(ProfileEvent.Of(ProfileEventTypes.Banner, "banner.harvest", "e2", Noon.AddMinutes(3))).Succeeded);

            var load = _store.Load();
            Assert.IsTrue(load.Succeeded);
            Assert.IsEmpty(load.SkippedLines);
            CollectionAssert.AreEqual(new[] { "r1", "r2" }, load.Records.Select(r => r.Outcome.RoundId).ToArray(),
                "The round fold's input must be exactly the rounds, as it was before profile lines existed.");
            CollectionAssert.AreEqual(new[] { "e1", "e2" }, load.ProfileEvents.Select(e => e.EventId).ToArray());
        }

        [Test]
        public void ARoundLine_CarriesNoKind_AndASlice2LineStillReadsIdentically()
        {
            // The exact shape slice 2 wrote: no Kind field anywhere on the line.
            var outcome = Outcome("legacy", Noon, placement: 2, banked: 12.5f, stolen: 3f);
            string slice2Line = JsonUtility.ToJson(outcome);
            StringAssert.DoesNotContain("\"Kind\"", slice2Line,
                "A round line must stay byte-identical to slice 2's, or every journal already on a device changes meaning.");

            AppendRaw(slice2Line + "\n");
            var read = _store.Load().Records.Single().Outcome;

            Assert.AreEqual(JsonUtility.ToJson(outcome), JsonUtility.ToJson(read));
            Assert.AreEqual(2, read.Placement);
            Assert.AreEqual(12.5f, read.BankedTotal, 0.0001f);
        }

        [Test]
        public void ATornProfileTail_ThatIsComplete_IsKeptAndItsNewlineWritten()
        {
            Assert.IsTrue(_store.Append(Outcome("r1", Noon)).Succeeded);
            AppendRaw(JsonUtility.ToJson(Name("TornBeak#0001", "e1", Noon.AddMinutes(1))));

            var load = _store.Load();
            Assert.IsTrue(load.Succeeded);
            Assert.AreEqual(TornTail.Repaired, load.TornTail);
            Assert.AreEqual("e1", load.ProfileEvents.Single().EventId, "A complete profile line that lost its newline is kept.");

            // And the repaired file appends cleanly afterwards.
            Assert.IsTrue(_store.Append(Outcome("r2", Noon.AddMinutes(2))).Succeeded);
            var again = _store.Load();
            Assert.AreEqual(TornTail.None, again.TornTail);
            CollectionAssert.AreEqual(new[] { "r1", "r2" }, again.Records.Select(r => r.Outcome.RoundId).ToArray());
            Assert.AreEqual(1, again.ProfileEvents.Count);
        }

        [Test]
        public void AnIncompleteProfileTail_IsMovedToTheTornFile()
        {
            Assert.IsTrue(_store.Append(Outcome("r1", Noon)).Succeeded);
            AppendRaw("{\"Kind\":\"prof");

            var load = _store.Load();
            Assert.IsTrue(load.Succeeded);
            Assert.AreEqual(TornTail.Dropped, load.TornTail);
            Assert.IsEmpty(load.ProfileEvents);
            Assert.AreEqual(1, load.Records.Count, "The complete round before the fragment survives.");
            StringAssert.Contains("prof", File.ReadAllText(_store.TornPath), "The fragment is preserved, not deleted.");
        }

        [Test]
        public void AKindThisBuildDoesNotKnow_IsOneSkippedLine_NotAFailure()
        {
            Assert.IsTrue(_store.Append(Outcome("r1", Noon)).Succeeded);
            AppendRaw("{\"Kind\":\"ritual\",\"SchemaVersion\":1,\"EventId\":\"x\"}\n");
            Assert.IsTrue(_store.Append(Outcome("r2", Noon.AddMinutes(1))).Succeeded);

            var load = _store.Load();
            Assert.IsTrue(load.Succeeded, "A newer build's line must not stop the journal loading.");
            Assert.AreEqual(1, load.SkippedLines.Count);
            StringAssert.Contains("ritual", load.SkippedLines[0]);
            CollectionAssert.AreEqual(new[] { "r1", "r2" }, load.Records.Select(r => r.Outcome.RoundId).ToArray(),
                "The rounds either side of it are unaffected.");
        }

        [Test]
        public void AProfileLineWithoutAnId_OrAnInstant_IsMalformed()
        {
            AppendRaw("{\"Kind\":\"profile\",\"SchemaVersion\":1,\"Type\":\"name\",\"AtUtc\":\"" +
                      ProgressionCalendar.FormatUtc(Noon) + "\"}\n");
            AppendRaw("{\"Kind\":\"profile\",\"SchemaVersion\":1,\"EventId\":\"e\",\"Type\":\"name\",\"AtUtc\":\"whenever\"}\n");
            AppendRaw("{\"Kind\":\"profile\",\"SchemaVersion\":99,\"EventId\":\"e\",\"Type\":\"name\",\"AtUtc\":\"" +
                      ProgressionCalendar.FormatUtc(Noon) + "\"}\n");

            var load = _store.Load();
            Assert.IsTrue(load.Succeeded);
            Assert.IsEmpty(load.ProfileEvents);
            Assert.AreEqual(3, load.SkippedLines.Count, string.Join(" | ", load.SkippedLines));
        }

        [Test]
        public void AnEventThatWouldNotReadBack_IsRefused_NotWritten()
        {
            var unreadable = ProfileEvent.Of(ProfileEventTypes.Name, "X#0001", string.Empty, Noon);
            var append = _store.Append(unreadable);

            Assert.IsFalse(append.Succeeded, "An event with no id would be skipped on load; writing it is worth nothing.");
            Assert.IsNull(append.Event);
            Assert.IsFalse(File.Exists(_store.JournalPath), "Nothing may be written when the line is refused.");
        }
    }

    // ==== The service's identity commands ===============================================

    public sealed class ProgressionIdentityServiceTests
    {
        private const int Local = 1042, Rival = 1040;

        private string _dir;
        private RecordingLogService _log;
        private MatchTracker _tracker;
        private ProgressionConfigSO _cfg;
        private DateTime _now;
        private int _roundIds, _eventIds;
        private readonly List<ProgressionService> _services = new List<ProgressionService>();

        [SetUp]
        public void SetUp()
        {
            _dir = TempJournal.NewDirectory();
            _log = new RecordingLogService();
            _cfg = Config();
            _now = Noon;
            _roundIds = 0;
            _eventIds = 0;
            _tracker = new MatchTracker(_log, () => _now,
                () => "round-" + (++_roundIds).ToString("D3", CultureInfo.InvariantCulture), TimeZoneInfo.Utc);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var service in _services) service.Dispose();
            _services.Clear();
            TempJournal.Delete(_dir);
        }

        private ProgressionService NewService(int seed = 1)
        {
            var service = new ProgressionService(_log, _tracker, _cfg, new JournalStore(_dir), TimeZoneInfo.Utc,
                () => _now,
                () => "event-" + (++_eventIds).ToString("D3", CultureInfo.InvariantCulture),
                new System.Random(seed));
            _services.Add(service);
            return service;
        }

        private List<ProfileEvent> EventsOnDisk() => new JournalStore(_dir).Load().ProfileEvents;

        private void PlayRound(int placement = 1, float banked = 10f, float stolen = 0f)
        {
            _tracker.RoundStarted(Ruleset);
            if (stolen > 0f) _tracker.ResourceStolen(Local, Rival, stolen);
            _tracker.RoundEnded(Standings((Local, placement, banked), (Rival, placement == 1 ? 2 : 1, 1f)), Local, 45f);
            _now = _now.AddMinutes(1);
        }

        [Test]
        public void TheFirstLoad_GeneratesExactlyOneName_AndAReloadGeneratesNone()
        {
            var first = NewService();
            first.Initialize();

            Assert.IsTrue(first.Identity.Plate.HasName, "A player with no name in the journal is given one.");
            var written = EventsOnDisk().Where(e => e.Type == ProfileEventTypes.Name).ToList();
            Assert.AreEqual(1, written.Count, "Exactly one name event, written before anything showed it.");
            Assert.AreEqual(first.Identity.Plate.Name, written[0].Value);

            var second = NewService();
            second.Initialize();
            Assert.AreEqual(written[0].Value, second.Identity.Plate.Name, "A reload wears the name the journal holds.");
            Assert.AreEqual(1, EventsOnDisk().Count(e => e.Type == ProfileEventTypes.Name),
                "A second launch must not roll a new name.");
        }

        [Test]
        public void ARerolledName_IsOnDisk_BeforeAnySubscriberSeesIt()
        {
            var service = NewService();
            service.Initialize();
            string before = service.Identity.Plate.Name;

            string onDiskWhenRaised = null;
            service.OnIdentityChanged += identity =>
            {
                // Reading the file from inside the handler is the whole point: if the write happened
                // after the raise, this would still be the old name.
                onDiskWhenRaised = EventsOnDisk().Last(e => e.Type == ProfileEventTypes.Name).Value;
            };

            Assert.IsTrue(service.TryRerollName());
            Assert.AreNotEqual(before, service.Identity.Plate.Name, "A re-roll must visibly change the name.");
            Assert.AreEqual(service.Identity.Plate.Name, onDiskWhenRaised,
                "The journal is the wallet: the name was flushed before OnIdentityChanged fired.");
        }

        [Test]
        public void AFailedWrite_ChangesNothing_AndIsReported()
        {
            var service = NewService();
            service.Initialize();

            var identityBefore = service.Identity;
            string nameBefore = identityBefore.Plate.Name;
            TempJournal.BlockWithADirectory(new JournalStore(_dir).JournalPath);

            var faults = new List<ProgressionFault>();
            service.OnFault += faults.Add;
            bool raised = false;
            service.OnIdentityChanged += _ => raised = true;

            Assert.IsFalse(service.TryRerollName(), "A name that could not be written was not earned.");
            Assert.AreSame(identityBefore, service.Identity, "The snapshot must be untouched by a failed write.");
            Assert.AreEqual(nameBefore, service.Identity.Plate.Name);
            Assert.IsFalse(raised, "Nothing may announce a change that did not happen.");
            Assert.AreEqual(1, faults.Count(f => f.Kind == ProgressionFaultKind.WriteFailed));
            Assert.IsNotEmpty(_log.OfLevel(LogLevel.Error));
        }

        [Test]
        public void APartThePlayerDoesNotOwn_IsRefused_AndNothingIsWritten()
        {
            var service = NewService();
            service.Initialize();
            int before = EventsOnDisk().Count;

            Assert.IsFalse(service.TrySelectNameplatePart(NameplateSlot.Title, "record.full_coop"),
                "No round has been played, so no title is owned.");
            Assert.IsFalse(service.TrySelectNameplatePart(NameplateSlot.Banner, "banner.midnight"));
            Assert.IsFalse(service.TrySelectNameplatePart(NameplateSlot.Emblem, "class.assassin"),
                "An emblem for a role never played is not owned.");

            Assert.AreEqual(before, EventsOnDisk().Count, "A refused choice must leave the journal alone.");
            Assert.AreEqual(3, _log.OfLevel(LogLevel.Warn).Count(e => e.Message.Contains("not")), string.Join(" | ",
                _log.OfLevel(LogLevel.Warn).Select(e => e.Message)));
        }

        [Test]
        public void AnOwnedPart_IsWornAndWritten()
        {
            var service = NewService();
            service.Initialize();

            // A starter banner is owned from the first launch.
            Assert.IsTrue(service.TrySelectNameplatePart(NameplateSlot.Banner, "banner.barnwood"));
            Assert.AreEqual("banner.barnwood", service.Identity.Plate.BannerKey);
            Assert.AreEqual("banner.barnwood", EventsOnDisk().Last(e => e.Type == ProfileEventTypes.Banner).Value);

            // Clearing is always allowed, and is how a player takes a title off.
            Assert.IsTrue(service.TrySelectNameplatePart(NameplateSlot.Title, string.Empty));
            Assert.IsNull(service.Identity.Plate.TitleKey);
        }

        [Test]
        public void PlayingARound_MovesTheIdentity_NotJustTheBalance()
        {
            var service = NewService();
            service.Initialize();
            Assert.AreEqual(0, service.Identity.Summary.Rounds);

            var seen = new List<ProgressionIdentity>();
            service.OnIdentityChanged += seen.Add;

            PlayRound(placement: 1, banked: 41f);

            Assert.AreEqual(1, service.Identity.Summary.Rounds);
            Assert.AreEqual(1, service.Identity.Summary.Wins);
            Assert.IsNotEmpty(seen, "Records and mastery move with play, so the identity is announced too.");
            Assert.AreSame(service.Identity, seen.Last());
        }

        [Test]
        public void BeforeTheJournalLoads_CommandsAreRefused_AndWriteNothing()
        {
            var service = NewService();

            Assert.IsFalse(service.TryRerollName());
            Assert.IsFalse(service.TrySelectNameplatePart(NameplateSlot.Banner, "banner.barnwood"));
            Assert.IsFalse(File.Exists(new JournalStore(_dir).JournalPath),
                "Nothing may be written before the journal has been read.");
            Assert.AreSame(ProgressionIdentity.Empty, service.Identity);
        }

        [Test]
        public void AJournalThatCannotLoad_LeavesTheIdentityEmpty_AndWritesNoName()
        {
            TempJournal.BlockWithADirectory(new JournalStore(_dir).JournalPath);

            var service = NewService();
            service.Initialize();

            Assert.IsFalse(service.IsReady);
            Assert.AreSame(ProgressionIdentity.Empty, service.Identity,
                "With no journal there is no identity — not an invented one.");
            Assert.IsTrue(_log.OfLevel(LogLevel.Error).Any(e => e.Message.Contains("journal")));
        }
    }
}
