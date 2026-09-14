using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CluckWars.Installers;
using CluckWars.Progression;
using CluckWars.UI;
using NUnit.Framework;
using UnityEngine;
using Zenject;
using static CluckWars.Tests.ProgressionFixtures;
using LogLevel = CluckWars.Logging.LogLevel;
using Object = UnityEngine.Object;

namespace CluckWars.Tests
{
    /// <summary>Temporary journal directories: every persistence test runs against its own, never the real journal.</summary>
    internal static class TempJournal
    {
        public static string NewDirectory() =>
            Path.Combine(Path.GetTempPath(), "cw-progression-tests", Guid.NewGuid().ToString("N"));

        public static void Delete(string dir)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }

        public static readonly UTF8Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// Makes every write to <paramref name="journalPath"/> fail, portably: a directory where the file
        /// should be (no reliance on Windows file locks).
        /// </summary>
        public static void BlockWithADirectory(string journalPath)
        {
            if (File.Exists(journalPath)) File.Delete(journalPath);
            Directory.CreateDirectory(journalPath);
        }
    }

    // ==== JournalStore =================================================================

    public sealed class JournalStoreTests
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

        private static string Line(RoundOutcome outcome) => JsonUtility.ToJson(outcome);

        private void WriteRaw(byte[] bytes)
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllBytes(_store.JournalPath, bytes);
        }

        private void AppendRaw(string text)
        {
            byte[] bytes = TempJournal.Utf8.GetBytes(text);
            using (var stream = new FileStream(_store.JournalPath, FileMode.Append, FileAccess.Write))
                stream.Write(bytes, 0, bytes.Length);
        }

        private static string[] Ids(JournalLoadResult load) => load.Records.Select(r => r.Outcome.RoundId).ToArray();

        [Test]
        public void Constructing_AndLoadingAMissingJournal_TouchNoFile_AndLoadEmpty()
        {
            Assert.IsFalse(Directory.Exists(_dir), "The constructor must do no I/O.");

            var load = _store.Load();
            Assert.IsTrue(load.Succeeded, "A missing journal is a first run, not a failure.");
            Assert.IsEmpty(load.Records);
            Assert.IsEmpty(load.SkippedLines);
            Assert.AreEqual(TornTail.None, load.TornTail);
            Assert.IsFalse(Directory.Exists(_dir), "Loading a journal that does not exist must create nothing.");
        }

        [Test]
        public void EveryField_RoundTrips_UnderASpanishCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("es-ES");
                Assert.AreEqual("44,97", 44.97f.ToString(CultureInfo.CurrentCulture),
                    "Precondition: this culture writes a decimal comma (slice 1's log printed 44,97).");

                var written = new RoundOutcome
                {
                    SchemaVersion = RoundOutcome.CurrentSchemaVersion,
                    RoundId = "0f3c9a7e5b2d4c18a6e1f0b9d8c7a6e5",
                    EndedAtUtc = ProgressionCalendar.FormatUtc(Noon.AddTicks(1234567)),
                    LocalDay = ProgressionCalendar.FormatDay(new DateTime(2026, 9, 13)),
                    Ruleset = new RoundRuleset { ResourceTargetToWin = 40.5f, RoundDurationSeconds = 45f, MaxActors = 4 },
                    DurationSeconds = 44.97f,
                    RoleKey = "role.speedy",
                    Placement = 2,
                    BankedTotal = 12.25f,
                    StolenTotal = 3.33f,
                    RivalsRobbed = 2,
                    OpponentsDisabled = 1,
                    Abilities = new[]
                    {
                        new AbilityTally { Key = "ability.egg_shell", Casts = 3, Connected = 0 },
                        new AbilityTally { Key = "ability.snatch", Casts = 2, Connected = 1 },
                    },
                };

                Assert.IsTrue(_store.Append(written).Succeeded);

                byte[] bytes = File.ReadAllBytes(_store.JournalPath);
                Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF, "The journal is UTF-8 without a BOM.");
                string text = TempJournal.Utf8.GetString(bytes);
                Assert.AreEqual(1, text.Count(c => c == '\n'), "One round is one line.");
                StringAssert.EndsWith("\n", text);
                StringAssert.Contains("44.97", text, "Numbers are written in the invariant culture.");
                StringAssert.DoesNotContain("44,97", text, "A decimal comma would corrupt the JSON under es-ES.");
                StringAssert.Contains("\"LocalDay\":\"2026-09-13\"", text);

                var load = _store.Load();
                Assert.IsTrue(load.Succeeded);
                var read = load.Records.Single().Outcome;

                Assert.AreEqual(written.SchemaVersion, read.SchemaVersion);
                Assert.AreEqual(written.RoundId, read.RoundId);
                Assert.AreEqual(written.EndedAtUtc, read.EndedAtUtc);
                Assert.AreEqual(written.LocalDay, read.LocalDay);
                Assert.AreEqual(written.Ruleset.ResourceTargetToWin, read.Ruleset.ResourceTargetToWin);
                Assert.AreEqual(written.Ruleset.RoundDurationSeconds, read.Ruleset.RoundDurationSeconds);
                Assert.AreEqual(written.Ruleset.MaxActors, read.Ruleset.MaxActors);
                Assert.AreEqual(written.DurationSeconds, read.DurationSeconds);
                Assert.AreEqual(written.RoleKey, read.RoleKey);
                Assert.AreEqual(written.Placement, read.Placement);
                Assert.AreEqual(written.BankedTotal, read.BankedTotal);
                Assert.AreEqual(written.StolenTotal, read.StolenTotal);
                Assert.AreEqual(written.RivalsRobbed, read.RivalsRobbed);
                Assert.AreEqual(written.OpponentsDisabled, read.OpponentsDisabled);
                Assert.AreEqual(written.Abilities.Length, read.Abilities.Length);
                for (int i = 0; i < written.Abilities.Length; i++)
                {
                    Assert.AreEqual(written.Abilities[i].Key, read.Abilities[i].Key);
                    Assert.AreEqual(written.Abilities[i].Casts, read.Abilities[i].Casts);
                    Assert.AreEqual(written.Abilities[i].Connected, read.Abilities[i].Connected);
                }
                Assert.AreEqual(JsonUtility.ToJson(written), load.Records.Single().Canonical);

                Assert.IsTrue(ProgressionCalendar.TryParseUtc(read.EndedAtUtc, out var utc), "The instant parses under es-ES.");
                Assert.AreEqual(Noon.AddTicks(1234567), utc);
                Assert.AreEqual(DateTimeKind.Utc, utc.Kind);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Test]
        public void Append_ReturnsTheRecordExactlyAsTheWrittenLineReadsBack()
        {
            // Awkward floats: whatever JsonUtility writes for them, memory must equal what a reload reads.
            float[] awkward = { 1f / 3f, 0.1f, 1e-7f, 123456.79f, float.Epsilon, float.MaxValue, 16777217f };
            var returned = new Dictionary<string, JournalRecord>();
            for (int i = 0; i < awkward.Length; i++)
            {
                var outcome = Outcome("f" + i.ToString(CultureInfo.InvariantCulture), Noon.AddMinutes(i), 1, awkward[i], awkward[i]);
                outcome.DurationSeconds = awkward[i];
                var result = _store.Append(outcome);
                Assert.IsTrue(result.Succeeded, result.Error?.ToString());
                returned.Add(outcome.RoundId, result.Record);
            }

            var load = _store.Load();
            Assert.AreEqual(awkward.Length, load.Records.Count);
            foreach (var onDisk in load.Records)
            {
                var inMemory = returned[onDisk.Outcome.RoundId];
                Assert.AreEqual(onDisk.Canonical, inMemory.Canonical, $"{onDisk.Outcome.RoundId}: memory must equal disk.");
                Assert.AreEqual(onDisk.Outcome.BankedTotal, inMemory.Outcome.BankedTotal);
                Assert.AreEqual(onDisk.Outcome.StolenTotal, inMemory.Outcome.StolenTotal);
                Assert.AreEqual(onDisk.Outcome.DurationSeconds, inMemory.Outcome.DurationSeconds);
            }
        }

        [Test]
        public void Append_RefusesAnOutcomeThatWouldNotReadBack_AndWritesNothing()
        {
            var noId = Outcome(null, Noon);
            var future = Outcome("future", Noon);
            future.SchemaVersion = RoundOutcome.CurrentSchemaVersion + 1;

            foreach (var outcome in new[] { noId, future })
            {
                var result = _store.Append(outcome);
                Assert.IsFalse(result.Succeeded, "A line Load would skip is worth nothing: refuse it.");
                Assert.IsInstanceOf<InvalidDataException>(result.Error);
                Assert.IsNull(result.Record.Outcome);
            }
            Assert.IsFalse(File.Exists(_store.JournalPath), "Nothing may be written for a refused outcome.");
        }

        [Test]
        public void AnAppend_AfterAPartialLine_StartsOnALineOfItsOwn()
        {
            // A failed earlier append left a fragment with no newline. The next round must not be swallowed.
            Assert.IsTrue(_store.Append(Outcome("a", Noon)).Succeeded);
            AppendRaw("{\"SchemaVersion\":1,\"Ro");

            Assert.IsTrue(_store.Append(Outcome("b", Noon.AddMinutes(1))).Succeeded);

            var load = _store.Load();
            Assert.IsTrue(load.Succeeded);
            Assert.AreEqual(TornTail.None, load.TornTail, "The journal ends with a whole line again.");
            CollectionAssert.AreEqual(new[] { "a", "b" }, Ids(load), "Round b must survive the fragment before it.");
            Assert.AreEqual(1, load.SkippedLines.Count, "The fragment is one malformed line of its own.");
            StringAssert.StartsWith("line 2:", load.SkippedLines[0]);
        }

        [Test]
        public void ATornFinalLine_IsDropped_PreservedToTorn_Truncated_AndTheNextAppendParses()
        {
            Assert.IsTrue(_store.Append(Outcome("a", Noon)).Succeeded);
            long whole = new FileInfo(_store.JournalPath).Length;
            string fragment = Line(Outcome("b", Noon)).Substring(0, 25);
            AppendRaw(fragment); // a crash mid-write: no closing brace, no newline

            var load = _store.Load();

            Assert.IsTrue(load.Succeeded, "A torn tail is recovered, not a load failure.");
            Assert.AreEqual(TornTail.Dropped, load.TornTail);
            StringAssert.Contains("line 2", load.TornTailDetail, "The report must say which line was torn.");
            CollectionAssert.AreEqual(new[] { "a" }, Ids(load));
            Assert.AreEqual(whole, new FileInfo(_store.JournalPath).Length,
                "The journal must be cut back to its last whole line, so the next append starts a line of its own.");
            Assert.AreEqual(fragment + "\n", File.ReadAllText(_store.TornPath, TempJournal.Utf8),
                "The fragment is preserved in the .torn file, never silently deleted.");

            Assert.IsTrue(_store.Append(Outcome("c", Noon)).Succeeded);
            var reload = _store.Load();
            Assert.AreEqual(TornTail.None, reload.TornTail);
            Assert.IsEmpty(reload.SkippedLines);
            CollectionAssert.AreEqual(new[] { "a", "c" }, Ids(reload));
        }

        [Test]
        public void AJournalThatIsOnlyATornLine_LoadsEmpty_AndIsCutToNothing()
        {
            WriteRaw(TempJournal.Utf8.GetBytes("{\"SchemaVersion\":1,\"Rou"));

            var load = _store.Load();

            Assert.IsTrue(load.Succeeded);
            Assert.AreEqual(TornTail.Dropped, load.TornTail);
            Assert.IsEmpty(load.Records);
            Assert.AreEqual(0, new FileInfo(_store.JournalPath).Length);
        }

        [Test]
        public void ACompleteFinalRecordWithoutItsNewline_IsKept_AndTheNewlineRepaired()
        {
            WriteRaw(TempJournal.Utf8.GetBytes(Line(Outcome("a", Noon)) + "\n" + Line(Outcome("b", Noon))));

            var load = _store.Load();

            Assert.IsTrue(load.Succeeded);
            Assert.AreEqual(TornTail.Repaired, load.TornTail);
            CollectionAssert.AreEqual(new[] { "a", "b" }, Ids(load));
            StringAssert.EndsWith("\n", File.ReadAllText(_store.JournalPath, TempJournal.Utf8));
            Assert.IsFalse(File.Exists(_store.TornPath), "A complete record is not torn.");

            Assert.IsTrue(_store.Append(Outcome("c", Noon)).Succeeded);
            var reload = _store.Load();
            Assert.AreEqual(TornTail.None, reload.TornTail);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, Ids(reload));
        }

        [Test]
        public void MalformedMiddleLines_AreSkipped_Reported_AndLeftInPlace()
        {
            var noInstant = Outcome("x", Noon);
            noInstant.EndedAtUtc = "13/09/2026 12:00";
            var future = Outcome("y", Noon);
            future.SchemaVersion = RoundOutcome.CurrentSchemaVersion + 1;

            var bytes = new List<byte>();
            bytes.AddRange(TempJournal.Utf8.GetBytes(Line(Outcome("a", Noon)) + "\n")); // 1
            bytes.AddRange(TempJournal.Utf8.GetBytes("not json at all\n"));              // 2
            bytes.AddRange(TempJournal.Utf8.GetBytes("{}\n"));                           // 3: parses, but schema 0
            bytes.AddRange(TempJournal.Utf8.GetBytes("\n"));                             // 4: blank, carries nothing
            bytes.AddRange(new byte[] { 0xC3, 0x28, (byte)'\n' });                       // 5: not valid UTF-8
            bytes.AddRange(TempJournal.Utf8.GetBytes(Line(noInstant) + "\n"));           // 6: no ISO-8601 instant
            bytes.AddRange(TempJournal.Utf8.GetBytes(Line(future) + "\n"));              // 7: a newer build's schema
            bytes.AddRange(TempJournal.Utf8.GetBytes(Line(Outcome("b", Noon)) + "\n")); // 8
            WriteRaw(bytes.ToArray());

            var load = _store.Load();

            Assert.IsTrue(load.Succeeded);
            CollectionAssert.AreEqual(new[] { "a", "b" }, Ids(load));
            Assert.AreEqual(5, load.SkippedLines.Count, string.Join("; ", load.SkippedLines));
            foreach (int n in new[] { 2, 3, 5, 6, 7 })
            {
                Assert.IsTrue(load.SkippedLines.Any(s => s.StartsWith($"line {n}:", StringComparison.Ordinal)),
                    $"Line {n} must be reported by number. Got: {string.Join("; ", load.SkippedLines)}");
            }
            CollectionAssert.AreEqual(bytes.ToArray(), File.ReadAllBytes(_store.JournalPath),
                "Malformed middle lines are reported, never rewritten.");
        }

        [Test]
        public void AByteOrderMark_IsTolerated()
        {
            var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(TempJournal.Utf8.GetBytes(Line(Outcome("a", Noon)) + "\n"));
            WriteRaw(withBom.ToArray());

            var load = _store.Load();
            CollectionAssert.AreEqual(new[] { "a" }, Ids(load));
            Assert.IsEmpty(load.SkippedLines);
        }

        [Test]
        public void AFailedAppend_IsReported_NeverThrown()
        {
            TempJournal.BlockWithADirectory(_store.JournalPath);

            JournalAppendResult result = default;
            Assert.DoesNotThrow(() => result = _store.Append(Outcome("a", Noon)));
            Assert.IsFalse(result.Succeeded, "A directory sits where the journal file should be; the write cannot land.");
            Assert.IsNotNull(result.Error);

            Assert.IsFalse(_store.Append(null).Succeeded, "A null outcome is refused in the result, not thrown.");
        }

        [Test]
        public void ADirectoryAtTheJournalPath_IsALoadFailure_NotAFirstRun()
        {
            TempJournal.BlockWithADirectory(_store.JournalPath);

            var load = _store.Load();

            Assert.IsFalse(load.Succeeded, "Nothing could ever be appended here, so this is not an empty journal.");
            Assert.IsInstanceOf<IOException>(load.Error);
        }
    }

    // ==== The day a round counts toward ================================================

    public sealed class LocalDayTests
    {
        private const int Local = 1042;
        private readonly List<Object> _copies = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var copy in _copies)
            {
                if (copy != null) Object.DestroyImmediate(copy);
            }
            _copies.Clear();
        }

        /// <summary>Mechanics, so a synthetic config: three rested rounds a day at ×2.</summary>
        private ProgressionConfigSO RestedConfig()
        {
            var c = Object.Instantiate(Config());
            _copies.Add(c);
            c.RestedRounds = 3;
            c.RestedMultiplier = 2f;
            return c;
        }

        private static RoundOutcome EndRound(TimeZoneInfo zone, DateTime utc)
        {
            var log = new RecordingLogService();
            var tracker = new MatchTracker(log, () => utc, () => "r", zone);
            RoundOutcome outcome = null;
            tracker.OutcomeReady += o => outcome = o;
            tracker.RoundStarted(Ruleset);
            tracker.RoundEnded(Standings((Local, 1, 5f)), Local, 45f);
            Assert.IsNotNull(outcome);
            return outcome;
        }

        [Test]
        public void TheTracker_StampsTheLocalDay_InItsZone()
        {
            Assert.AreEqual("2026-09-13", EndRound(TimeZoneInfo.Utc, Noon).LocalDay);
            Assert.AreEqual("2026-09-14", EndRound(FixedZone(14), Noon).LocalDay, "12:00Z is 02:00 on the 14th at UTC+14.");
            Assert.AreEqual("2026-09-13", EndRound(FixedZone(-12), Noon).LocalDay, "12:00Z is 00:00 on the 13th at UTC-12.");
            Assert.AreEqual("2026-09-12", EndRound(FixedZone(-12), Noon.AddMinutes(-1)).LocalDay);
        }

        [Test]
        public void TheTracker_WithoutAZone_StampsTheDevicesDay()
        {
            Assert.AreEqual(ProgressionCalendar.FormatDay(ProgressionCalendar.LocalDay(Noon, TimeZoneInfo.Local)),
                EndRound(null, Noon).LocalDay);
        }

        [Test]
        public void TheDayFormat_IsInvariant_AndStrict()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("es-ES");
                Assert.AreEqual("2026-09-03", ProgressionCalendar.FormatDay(new DateTime(2026, 9, 3, 23, 59, 0)));
                Assert.IsTrue(ProgressionCalendar.TryParseDay("2026-09-03", out var day));
                Assert.AreEqual(new DateTime(2026, 9, 3), day);
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }

            foreach (var bad in new[] { null, "", "13/09/2026", "2026-9-3", "2026-09-13T00:00:00Z", "tomorrow" })
                Assert.IsFalse(ProgressionCalendar.TryParseDay(bad, out _), $"'{bad}' is not a LocalDay.");
        }

        /// <summary>Five rounds on the 13th by the device's day, spread so that other zones would split them.</summary>
        private static List<JournalRecord> FiveRoundsOnThe13th(bool stamped)
        {
            var instants = new[] { 0.5, 5, 11, 13, 23.5 }.Select(h => new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc).AddHours(h));
            return instants.Select((utc, i) =>
            {
                var o = Outcome("r" + i.ToString(CultureInfo.InvariantCulture), utc, 2, 10f);
                if (stamped) o.LocalDay = "2026-09-13";
                return JournalRecord.Of(o);
            }).ToList();
        }

        [Test]
        public void AStampedJournal_FoldsToTheSameBalance_InEveryZone()
        {
            var cfg = RestedConfig();

            // Non-blind: without the stamp, these zones genuinely disagree about the days.
            var legacy = FiveRoundsOnThe13th(stamped: false);
            Assert.AreNotEqual(ProgressionLedger.Fold(legacy, cfg, TimeZoneInfo.Utc).GrainBalance,
                ProgressionLedger.Fold(legacy, cfg, FixedZone(-12)).GrainBalance,
                "Precondition: zone-based days split these rounds differently at UTC and UTC-12.");

            var stamped = FiveRoundsOnThe13th(stamped: true);
            long utc = ProgressionLedger.Fold(stamped, cfg, TimeZoneInfo.Utc).GrainBalance;
            Assert.AreEqual(utc, ProgressionLedger.Fold(stamped, cfg, FixedZone(14)).GrainBalance, "UTC+14");
            Assert.AreEqual(utc, ProgressionLedger.Fold(stamped, cfg, FixedZone(-12)).GrainBalance, "UTC-12");
            Assert.AreEqual(ProgressionLedger.Fold(legacy, cfg, TimeZoneInfo.Utc).GrainBalance, utc,
                "All five are stamped on the 13th: the same as five UTC rounds on the 13th.");
        }

        [Test]
        public void LinesWithoutAUsableStamp_FallBackToTheZoneDay()
        {
            var cfg = RestedConfig();
            var zone = FixedZone(-12);

            var legacy = FiveRoundsOnThe13th(stamped: false);
            var garbled = FiveRoundsOnThe13th(stamped: false).Select(r =>
            {
                r.Outcome.LocalDay = "not-a-day";
                return JournalRecord.Of(r.Outcome);
            }).ToList();
            var explicitZoneDays = FiveRoundsOnThe13th(stamped: false).Select(r =>
            {
                ProgressionCalendar.TryLocalDay(r.Outcome.EndedAtUtc, zone, out var day);
                r.Outcome.LocalDay = ProgressionCalendar.FormatDay(day);
                return JournalRecord.Of(r.Outcome);
            }).ToList();

            long expected = ProgressionLedger.Fold(explicitZoneDays, cfg, TimeZoneInfo.Utc).GrainBalance;
            Assert.AreEqual(expected, ProgressionLedger.Fold(legacy, cfg, zone).GrainBalance, "No stamp: the zone's day.");
            Assert.AreEqual(expected, ProgressionLedger.Fold(garbled, cfg, zone).GrainBalance, "An unparseable stamp: the zone's day.");
        }

        [Test]
        public void StampedAndLegacyLines_ShareTheDaysRestedCount()
        {
            // This machine's dev journal: lines written before LocalDay existed, then stamped ones.
            var cfg = RestedConfig();
            var records = new List<JournalRecord>
            {
                Rec("legacy-1", Noon, 2, 10f),
                Rec("legacy-2", Noon.AddMinutes(1), 2, 10f),
                Rec("legacy-3", Noon.AddMinutes(2), 2, 10f),
            };
            var stamped = Outcome("stamped", Noon.AddMinutes(3), 2, 10f);
            stamped.LocalDay = "2026-09-13";
            records.Add(JournalRecord.Of(stamped));

            var ledger = ProgressionLedger.Fold(records, cfg, TimeZoneInfo.Utc);
            Assert.IsTrue(ledger.TryGetAward("legacy-3", out var third));
            Assert.IsTrue(third.Rested);
            Assert.IsTrue(ledger.TryGetAward("stamped", out var fourth));
            Assert.IsFalse(fourth.Rested, "The stamped round is the day's 4th: the legacy lines count toward the same day.");
        }
    }

    // ==== ProgressionService ===========================================================

    public sealed class ProgressionServiceTests
    {
        private const int Local = 1042, Rival = 1040;

        private string _dir;
        private RecordingLogService _log;
        private MatchTracker _tracker;
        private ProgressionConfigSO _cfg;
        private DateTime _now;
        private int _ids;
        private readonly List<ProgressionService> _services = new List<ProgressionService>();

        [SetUp]
        public void SetUp()
        {
            _dir = TempJournal.NewDirectory();
            _log = new RecordingLogService();
            _cfg = Config();
            _now = Noon;
            _ids = 0;
            _tracker = new MatchTracker(_log, () => _now,
                () => "round-" + (++_ids).ToString("D3", CultureInfo.InvariantCulture), TimeZoneInfo.Utc);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var service in _services) service.Dispose();
            _services.Clear();
            TempJournal.Delete(_dir);
        }

        private ProgressionService NewService(MatchTracker tracker = null)
        {
            var service = new ProgressionService(_log, tracker ?? _tracker, _cfg, new JournalStore(_dir), TimeZoneInfo.Utc);
            _services.Add(service);
            return service;
        }

        private string JournalPath => new JournalStore(_dir).JournalPath;

        private void PlayRound(int placement = 1, float banked = 10f, float stolen = 0f)
        {
            _tracker.RoundStarted(Ruleset);
            if (stolen > 0f) _tracker.ResourceStolen(Local, Rival, stolen);
            _tracker.RoundEnded(Standings((Local, placement, banked), (Rival, placement == 1 ? 2 : 1, 1f)), Local, 45f);
            _now = _now.AddMinutes(1);
        }

        /// <summary>The journal as a fresh reader finds it on disk right now.</summary>
        private List<RoundOutcome> JournalOnDisk() =>
            new JournalStore(_dir).Load().Records.Select(r => r.Outcome).ToList();

        /// <summary>A journal with one good line and a torn tail: anything that loads it would repair it.</summary>
        private byte[] SeedTornJournal()
        {
            var store = new JournalStore(_dir);
            Assert.IsTrue(store.Append(Outcome("seed", Noon.AddDays(-1))).Succeeded);
            File.AppendAllText(store.JournalPath, "{\"SchemaVers", TempJournal.Utf8);
            return File.ReadAllBytes(store.JournalPath);
        }

        private void AssertUntouched(byte[] seeded, string when)
        {
            CollectionAssert.AreEqual(seeded, File.ReadAllBytes(JournalPath), $"{when}: the journal's bytes must be unchanged.");
            Assert.IsFalse(File.Exists(new JournalStore(_dir).TornPath), $"{when}: no .torn file may be created.");
        }

        [Test]
        public void BeforeInitialize_ItIsNotReady_TouchesNoFile_AndWarnsOnlyOnce()
        {
            byte[] seeded = SeedTornJournal();
            var service = NewService();
            var faults = new List<ProgressionFault>();
            service.OnFault += faults.Add;

            AssertUntouched(seeded, "Constructing the service");
            Assert.IsFalse(service.IsReady);
            Assert.AreSame(ProgressionProfile.Empty, service.Profile);
            Assert.IsNull(service.LatestAward);

            PlayRound();
            PlayRound();

            AssertUntouched(seeded, "Rounds ending before the journal loaded");
            Assert.IsNull(service.LatestAward);
            Assert.AreEqual(2, faults.Count(f => f.Kind == ProgressionFaultKind.NotReady), "Every unrecorded round is a fault.");
            Assert.AreEqual(1, _log.OfLevel(LogLevel.Warn).Count(e => e.Source == "Progression"),
                "The first unrecorded round warns; later ones are Debug, not a Warn per round.");
            Assert.IsTrue(_log.OfLevel(LogLevel.Debug).Any(e => e.Source == "Progression" && e.Message.Contains("2 round(s)")));
        }

        [Test]
        public void ResolvingTheServiceThroughTheInstaller_TouchesNoFile()
        {
            byte[] seeded = SeedTornJournal();

            var prefab = TestAssets.Load<GameObject>(TestAssets.ProjectContextPrefabPath);
            var installer = prefab.GetComponentInChildren<ProjectInstaller>(true);
            Assert.IsNotNull(installer, $"{TestAssets.ProjectContextPrefabPath} has no ProjectInstaller.");

            var restoreAudio = MatchEventSinkTests.SnapshotEmptyClipSlots(
                TestAssets.PrivateField<ScriptableObject>(installer, "_audioRegistry"));
            var containerProperty = typeof(MonoInstallerBase).GetProperty("Container",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(containerProperty, "Zenject's MonoInstallerBase.Container property was not found.");
            var previousContainer = containerProperty.GetValue(installer);

            try
            {
                var container = new DiContainer();
                container.Inject(installer);
                installer.InstallBindings();
                // No AsSingle: Zenject 6 refuses a second AsSingle for one type, and FromInstance is one instance anyway.
                container.Rebind<JournalStore>().FromInstance(new JournalStore(_dir));

                var service = container.Resolve<IProgressionService>();
                var sink = container.Resolve<IMatchEventSink>();
                sink.RoundStarted(Ruleset);
                sink.RoundEnded(Standings((Local, 1, 5f)), Local, 45f);

                Assert.IsFalse(service.IsReady, "Resolving the installer's service must not load the journal.");
                AssertUntouched(seeded, "Resolving the installer's bindings and ending a round");
            }
            finally
            {
                containerProperty.SetValue(installer, previousContainer);
                restoreAudio();
            }
        }

        [Test]
        public void Initialize_OnAnEmptyJournal_IsReady_WithAnEmptyProfile_AndIsIdempotent()
        {
            var service = NewService();
            int changes = 0;
            service.OnProfileChanged += _ => changes++;

            service.Initialize();
            service.Initialize();

            Assert.IsTrue(service.IsReady);
            Assert.AreEqual(0, service.Profile.GrainBalance);
            Assert.AreEqual(0, service.Profile.RoundsPlayed);
            Assert.AreEqual(1, changes, "A second Initialize must not load again.");
        }

        [Test]
        public void WithoutAZone_Initialize_ResolvesTheDevicesZone_AndLoads()
        {
            var service = new ProgressionService(_log, _tracker, _cfg, new JournalStore(_dir));
            _services.Add(service);
            service.Initialize();
            Assert.IsTrue(service.IsReady);
            PlayRound();
            Assert.IsTrue(service.LatestAward.HasValue);
        }

        [Test]
        public void TheRoundIsOnDisk_BeforeAnyoneHearsOfItsAward()
        {
            var service = NewService();
            service.Initialize();

            var order = new List<string>();
            bool onDiskAtProfile = false, onDiskAtAward = false, latestSetAtAward = false;
            service.OnProfileChanged += _ =>
            {
                order.Add("profile");
                onDiskAtProfile = JournalOnDisk().Any(o => o.RoundId == "round-001");
            };
            service.OnRoundAwarded += award =>
            {
                order.Add("award");
                onDiskAtAward = JournalOnDisk().Any(o => o.RoundId == award.RoundId);
                latestSetAtAward = service.LatestAward.HasValue && service.LatestAward.Value.RoundId == award.RoundId;
            };

            PlayRound();

            CollectionAssert.AreEqual(new[] { "profile", "award" }, order, "OnProfileChanged, then OnRoundAwarded, once each.");
            Assert.IsTrue(onDiskAtProfile, "The round must be flushed to the journal before the profile changes.");
            Assert.IsTrue(onDiskAtAward, "The round must be flushed to the journal before its award is announced.");
            Assert.IsTrue(latestSetAtAward, "LatestAward must already describe the round when OnRoundAwarded fires.");
            Assert.AreEqual("2026-09-13", JournalOnDisk().Single().LocalDay, "The recorded line carries its local day.");
        }

        [Test]
        public void EachAward_IsTheBalanceDelta_AndWhatEvaluateGivesItsLine()
        {
            var service = NewService();
            service.Initialize();
            var awards = new List<RoundAward>();
            service.OnRoundAwarded += awards.Add;

            int rounds = _cfg.RestedRounds + 2;
            long balance = service.Profile.GrainBalance;
            for (int i = 0; i < rounds; i++)
            {
                PlayRound(placement: 1 + i % _cfg.PlacementGrain.Length, banked: 4.5f * i, stolen: i);

                Assert.AreEqual(i + 1, awards.Count, $"Round {i + 1} must be awarded.");
                var award = awards[i];
                var line = JournalOnDisk().Single(o => o.RoundId == award.RoundId);
                var expected = ProgressionRules.Evaluate(line, _cfg, i);

                Assert.AreEqual(EvaluationStatus.Ok, expected.Status);
                Assert.AreEqual(expected.Grain, award.Grain, $"Round {i + 1}: the award is what Evaluate gives its journal line.");
                Assert.AreEqual(i < _cfg.RestedRounds, award.Rested, $"Round {i + 1} of the day: rested iff within RestedRounds.");
                Assert.AreEqual(balance + award.Grain, service.Profile.GrainBalance, $"Round {i + 1}: the award is the balance delta.");
                balance = service.Profile.GrainBalance;
            }

            Assert.AreEqual(rounds, service.Profile.RoundsPlayed);
        }

        [Test]
        public void ClockSkew_TheAwardIsStillExactlyTheBalanceDelta()
        {
            int n = _cfg.RestedRounds;
            Assert.Greater(n, 0, "Precondition: the shipped config has rested rounds, so there is a rested slot to displace.");
            Assert.Greater(_cfg.RestedMultiplier, 1f, "Precondition: RestedMultiplier > 1, or losing a rested slot changes nothing.");

            // The day's first RestedRounds rounds are on disk; the last of them banked a lot.
            var store = new JournalStore(_dir);
            var earlier = new List<RoundOutcome>();
            for (int i = 0; i < n; i++)
            {
                var old = Outcome("old-" + i.ToString(CultureInfo.InvariantCulture), Noon.AddHours(i + 1), 1, i == n - 1 ? 200f : 5f);
                old.LocalDay = "2026-09-13";
                earlier.Add(old);
                Assert.IsTrue(store.Append(old).Succeeded);
            }
            string lastId = earlier[n - 1].RoundId;
            var ledgerBefore = ProgressionLedger.Fold(store.Load().Records, _cfg, TimeZoneInfo.Utc);
            Assert.IsTrue(ledgerBefore.TryGetAward(lastId, out var lastBefore) && lastBefore.Rested,
                "Precondition: before the skewed round, the day's last earlier round holds a rested slot.");

            var service = NewService();
            service.Initialize();
            long before = service.Profile.GrainBalance;

            // The device clock went backwards: the new round ends earlier on the same day than all of them.
            _now = Noon;
            PlayRound(placement: _cfg.PlacementGrain.Length, banked: 0f);

            Assert.IsTrue(service.LatestAward.HasValue);
            var award = service.LatestAward.Value;
            var fresh = JournalOnDisk().Single(o => o.RoundId == award.RoundId);
            Assert.IsTrue(ProgressionCalendar.TryParseUtc(fresh.EndedAtUtc, out var freshUtc));
            Assert.IsTrue(ProgressionCalendar.TryParseUtc(earlier[0].EndedAtUtc, out var earliestUtc));
            Assert.Less(freshUtc, earliestUtc, "Precondition: the skewed round sorts before every earlier round of the day.");
            Assert.AreEqual(earlier[0].LocalDay, fresh.LocalDay, "Precondition: and it is stamped on the same day.");

            var ledgerAfter = ProgressionLedger.Fold(store.Load().Records, _cfg, TimeZoneInfo.Utc);
            Assert.IsTrue(ledgerAfter.TryGetAward(lastId, out var lastAfter));
            Assert.IsFalse(lastAfter.Rested, "Precondition: the skewed round displaced the last earlier round's rested slot.");
            int displaced = lastBefore.Grain - lastAfter.Grain;
            Assert.Greater(displaced, 0);

            long delta = service.Profile.GrainBalance - before;
            Assert.AreEqual(delta, award.Grain, "The award must be exactly what the balance moved.");
            int own = ProgressionRules.Evaluate(fresh, _cfg, 0).Grain;
            Assert.AreEqual(own - displaced, award.Grain,
                "The award is the new round's own Grain minus the rested bonus the displaced round lost.");

            var reloaded = NewService(new MatchTracker(_log, zone: TimeZoneInfo.Utc));
            reloaded.Initialize();
            Assert.AreEqual(service.Profile.GrainBalance, reloaded.Profile.GrainBalance, "A reload computes the same wallet.");

            // Which is why the results screen shows the line only for a positive award: never "+-N".
            Assert.AreEqual(award.Grain > 0, MatchOverlaysController.ShowsGrainLine(true, service, null, out _));
        }

        [Test]
        public void AFailedWrite_IsAnError_EarnsNothing_AndLeavesTheBalanceAlone()
        {
            var service = NewService();
            service.Initialize();

            var profile = service.Profile;
            var faults = new List<ProgressionFault>();
            service.OnFault += faults.Add;
            int awarded = 0;
            service.OnRoundAwarded += _ => awarded++;

            TempJournal.BlockWithADirectory(JournalPath);
            PlayRound();

            Assert.AreEqual(0, awarded, "A round that was not written earns nothing.");
            Assert.IsNull(service.LatestAward);
            Assert.AreSame(profile, service.Profile, "The balance must not move on a failed write.");
            Assert.AreEqual(ProgressionFaultKind.WriteFailed, faults.Single().Kind);
            Assert.IsNotNull(faults[0].Exception);
            Assert.IsTrue(_log.OfLevel(LogLevel.Error).Any(e => e.Source == "Progression" && e.Exception != null),
                "A failed write is an Error carrying the exception.");

            Directory.Delete(JournalPath);
            PlayRound();
            Assert.AreEqual(1, awarded, "The next round records and pays normally.");
            Assert.AreEqual(1, JournalOnDisk().Count, "Only the written round is in the journal.");

            var reloaded = NewService(new MatchTracker(_log, zone: TimeZoneInfo.Utc));
            reloaded.Initialize();
            Assert.AreEqual(service.Profile.GrainBalance, reloaded.Profile.GrainBalance, "The wallet is the journal.");
        }

        [Test]
        public void AJournalItCannotRead_SwitchesProgressionOff_ForTheSession()
        {
            TempJournal.BlockWithADirectory(JournalPath);

            var service = NewService();
            var faults = new List<ProgressionFault>();
            service.OnFault += faults.Add;
            service.Initialize();

            Assert.IsFalse(service.IsReady);
            Assert.AreEqual(ProgressionFaultKind.LoadFailed, faults[0].Kind);
            Assert.IsTrue(_log.OfLevel(LogLevel.Error).Any(e => e.Source == "Progression"));

            PlayRound();
            Assert.AreEqual(ProgressionFaultKind.NotReady, faults.Last().Kind);
            Assert.IsNull(service.LatestAward);
            Assert.IsTrue(Directory.Exists(JournalPath) && Directory.GetFileSystemEntries(JournalPath).Length == 0,
                "Nothing may be written anywhere for a journal that did not load.");
        }

        [Test]
        public void TheAward_IsClearedWhenTheNextRoundOpens()
        {
            var service = NewService();
            service.Initialize();
            PlayRound();
            Assert.IsTrue(service.LatestAward.HasValue);

            _tracker.RoundStarted(Ruleset);
            Assert.IsNull(service.LatestAward, "The previous round's award must never show on the next results screen.");
        }

        [Test]
        public void ARoundItCannotEvaluate_IsJournaled_ButEarnsNothing()
        {
            var service = NewService();
            service.Initialize();
            var faults = new List<ProgressionFault>();
            service.OnFault += faults.Add;
            int awarded = 0;
            service.OnRoundAwarded += _ => awarded++;

            PlayRound(placement: _cfg.PlacementGrain.Length + 1);

            Assert.AreEqual(1, JournalOnDisk().Count, "Facts are journaled: a config fix credits the round on a later load.");
            Assert.AreEqual(0, awarded);
            Assert.IsNull(service.LatestAward);
            Assert.AreEqual(0, service.Profile.GrainBalance);
            Assert.AreEqual(0, service.Profile.RoundsPlayed);
            Assert.AreEqual(ProgressionFaultKind.NotEvaluable, faults.Single().Kind);
            StringAssert.Contains(nameof(EvaluationStatus.InvalidPlacement), faults[0].Message);
            Assert.IsTrue(_log.OfLevel(LogLevel.Warn).Any(e => e.Source == "Progression"));
        }

        [Test]
        public void Reloading_ReproducesTheBalance_AsTheSumOfTheAwards_ToTheLastBit()
        {
            var service = NewService();
            service.Initialize();
            long sum = 0;
            int wins = 0;
            service.OnRoundAwarded += award => sum += award.Grain;

            float[] banked = { 1f / 3f, 7.25f, 0.1f, 12.345678f, 16777217f / 1000f, 2.2f };
            for (int i = 0; i < banked.Length; i++)
            {
                int placement = 1 + i % _cfg.PlacementGrain.Length;
                if (placement == 1) wins++;
                PlayRound(placement: placement, banked: banked[i], stolen: (i % 3) / 3f);
            }

            Assert.AreEqual(sum, service.Profile.GrainBalance);

            var reloaded = NewService(new MatchTracker(_log, zone: TimeZoneInfo.Utc));
            reloaded.Initialize();
            Assert.IsTrue(reloaded.IsReady);
            Assert.AreEqual(sum, reloaded.Profile.GrainBalance);
            Assert.AreEqual(banked.Length, reloaded.Profile.RoundsPlayed);
            Assert.AreEqual(wins, reloaded.Profile.Wins, "Wins are rounds finished at placement 1.");
            Assert.AreEqual(service.Profile.TotalBanked, reloaded.Profile.TotalBanked,
                "Memory equals disk: the session folded exactly what the reload read, to the last bit.");
            Assert.AreEqual(service.Profile.TotalStolen, reloaded.Profile.TotalStolen);
        }

        [Test]
        public void ATornJournal_StillLoads_AndReportsTheFault()
        {
            SeedTornJournal();

            var service = NewService();
            var faults = new List<ProgressionFault>();
            service.OnFault += faults.Add;
            service.Initialize();

            Assert.IsTrue(service.IsReady);
            Assert.AreEqual(1, service.Profile.RoundsPlayed);
            Assert.AreEqual(ProgressionFaultKind.TornTailDropped, faults.Single().Kind);
            Assert.IsTrue(_log.OfLevel(LogLevel.Warn).Any(e => e.Source == "Progression"));
        }

        [Test]
        public void AThrowingAwardSubscriber_NeitherLosesTheRound_NorReachesTheTracker()
        {
            var service = NewService();
            service.Initialize();
            service.OnRoundAwarded += _ => throw new InvalidOperationException("ui bug");

            PlayRound();

            Assert.AreEqual(1, service.Profile.RoundsPlayed);
            Assert.IsTrue(service.LatestAward.HasValue);
            Assert.AreEqual(0, _tracker.SubscriberFailureCount, "The service must contain its subscribers' exceptions.");
            Assert.IsTrue(_log.OfLevel(LogLevel.Error).Any(e => e.Exception is InvalidOperationException));
        }
    }

    // ==== -progressionDir ==============================================================

    public sealed class ProgressionDirectoryTests
    {
        private const string PersistentData = "C:/Users/someone/AppData/LocalLow/DefaultCompany/CluckWars";
        private static readonly string Default = Path.Combine(PersistentData, "progression");

        private static string Resolve(string[] args, out string warning) =>
            ProjectInstaller.ResolveProgressionDirectory(args, PersistentData, out warning);

        [Test]
        public void WithoutTheSwitch_TheJournalLivesInPersistentDataPath()
        {
            Assert.AreEqual(Default, Resolve(new[] { "CluckWars.exe", "-screen-width", "960" }, out var warning));
            Assert.IsNull(warning);
            Assert.AreEqual(Default, Resolve(null, out warning));
            Assert.IsNull(warning);
        }

        [Test]
        public void TheSwitch_MovesTheJournal_AbsoluteOrRelative_CaseInsensitively()
        {
            string absolute = Path.Combine(Path.GetTempPath(), "cw", "client1");
            Assert.AreEqual(Path.GetFullPath(absolute),
                Resolve(new[] { "CluckWars.exe", "-logFile", "x.log", "-progressionDir", absolute }, out var warning));
            Assert.IsNull(warning);

            const string relative = "Builds/Windows/progression/client2";
            Assert.AreEqual(Path.GetFullPath(relative), Resolve(new[] { "-PROGRESSIONDIR", relative }, out warning),
                "A relative path resolves against the working directory.");
            Assert.IsNull(warning);
        }

        [Test]
        public void TheSwitch_WithoutADirectory_KeepsTheDefault_AndSaysSo()
        {
            var commandLines = new[]
            {
                new[] { "CluckWars.exe", "-progressionDir" },
                new[] { "CluckWars.exe", "-progressionDir", "-logFile", "x.log" },
                new[] { "CluckWars.exe", "-progressionDir", "  " },
            };

            foreach (var args in commandLines)
            {
                Assert.AreEqual(Default, Resolve(args, out var warning), string.Join(" ", args));
                StringAssert.Contains(ProjectInstaller.ProgressionDirSwitch, warning, string.Join(" ", args));
            }
        }
    }

    // ==== The results panel's "+N Grain" line ==========================================

    public sealed class ResultsGrainLineTests
    {
        private sealed class FakeProgression : IProgressionService
        {
            public bool IsReady { get; set; }
            public ProgressionProfile Profile => ProgressionProfile.Empty;
            public RoundAward? LatestAward { get; set; }
            public event Action<ProgressionProfile> OnProfileChanged { add { } remove { } }
            public event Action<RoundAward> OnRoundAwarded { add { } remove { } }
            public event Action<ProgressionFault> OnFault { add { } remove { } }
        }

        private static bool Shows(IProgressionService p, string stale = null, bool panel = true) =>
            MatchOverlaysController.ShowsGrainLine(panel, p, stale, out _);

        [Test]
        public void TheLine_ShowsOnlyForAPositiveAward_OnAShownPanel_FromAReadyService()
        {
            var ready = new FakeProgression { IsReady = true, LatestAward = new RoundAward("r", 37, true) };
            Assert.IsTrue(MatchOverlaysController.ShowsGrainLine(true, ready, null, out var award));
            Assert.AreEqual(37, award.Grain);

            Assert.IsFalse(Shows(ready, panel: false), "Hidden with the panel.");
            Assert.IsFalse(Shows(null), "No service, no line.");
            Assert.IsFalse(Shows(new FakeProgression { IsReady = false, LatestAward = new RoundAward("r", 37, true) }),
                "Progression off this session: hidden.");
            Assert.IsFalse(Shows(new FakeProgression { IsReady = true }),
                "Not recorded yet (the frame the panel opens): hidden, no placeholder.");
            Assert.IsFalse(Shows(new FakeProgression { IsReady = true, LatestAward = new RoundAward("r", 0, false) }), "Zero: hidden.");
            Assert.IsFalse(Shows(new FakeProgression { IsReady = true, LatestAward = new RoundAward("r", -23, true) }),
                "A negative balance delta (device clock went backwards) must never render as '+-23 Grain'.");
        }

        [Test]
        public void AnAwardThatPredatesThePanel_IsNeverShown()
        {
            // Quit to the menu during results, then join a session already on its results screen: no
            // RoundStarted fires, so the service still holds the previous session's award.
            var progression = new FakeProgression { IsReady = true, LatestAward = new RoundAward("old", 30, true) };
            string stale = progression.LatestAward?.RoundId; // what the panel captures when it is created

            Assert.IsFalse(Shows(progression, stale), "The earlier round's award must not appear on this results screen.");

            progression.LatestAward = new RoundAward("new", 23, false);
            Assert.IsTrue(Shows(progression, stale), "A round recorded after the panel was created shows normally.");
        }

        [Test]
        public void TheLineText_IsPlusNGrain_InAnyCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("es-ES");
                Assert.AreEqual("+37 Grain", MatchOverlaysController.GrainLineText(37));
                Assert.AreEqual("+1234 Grain", MatchOverlaysController.GrainLineText(1234), "No thousands separator.");
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Test]
        public void MatchOverlaysUxml_HasTheGrainLine_BetweenTheRowsAndTheRestartLine_HiddenByDefault()
        {
            string uxml = File.ReadAllText(TestAssets.MatchOverlaysUxmlPath);
            var names = Regex.Matches(uxml, @"name=""(MeRows|MeGrain|MeRestart)""").Cast<Match>().Select(m => m.Groups[1].Value).ToArray();
            CollectionAssert.AreEqual(new[] { "MeRows", "MeGrain", "MeRestart" }, names,
                "The Grain line sits between the standings rows and the restart countdown, once. " +
                "MatchOverlaysController queries it by name.");

            var element = Regex.Match(uxml, @"<ui:Label[^>]*name=""MeGrain""[^>]*/>");
            Assert.IsTrue(element.Success, "MeGrain must be a ui:Label.");
            StringAssert.Contains("display: none", element.Value,
                "The line must start hidden: it shows only once the award exists, never as a placeholder.");
        }

        [Test]
        public void TheGrainLine_SharesOneRowWithTheRestartLine()
        {
            // A third stacked line made the whole results panel one line taller; the footer row adds no height.
            var doc = XDocument.Load(TestAssets.MatchOverlaysUxmlPath);
            var footer = doc.Descendants().SingleOrDefault(e => (string)e.Attribute("name") == "MeFooter");
            Assert.IsNotNull(footer, "MatchOverlays.uxml must hold a MeFooter element.");
            CollectionAssert.AreEqual(new[] { "MeGrain", "MeRestart" },
                footer.Elements().Select(e => (string)e.Attribute("name")).ToArray(),
                "MeGrain and MeRestart must be MeFooter's children, in that order.");
            StringAssert.Contains("cw-me-footer", (string)footer.Attribute("class"));

            string uss = File.ReadAllText("Assets/UI/Styles/MatchOverlays.uss");
            StringAssert.IsMatch(@"\.cw-me-footer\s*\{[^}]*flex-direction:\s*row", uss,
                ".cw-me-footer must lay its children out in a row.");
        }
    }
}
