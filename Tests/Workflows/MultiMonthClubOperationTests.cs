using JustAnotherHemaClub.Models;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Tests.Workflows;

/// <summary>
/// Complex integration-style tests simulating 2–3 months of club operation:
/// weekly trainings, attendance tracking, payments, credit rollover, and billing.
/// Exercises the DuesCalculator with realistic multi-month data the way the
/// FencersViewModel.LoadAsync does it.
/// </summary>
public class MultiMonthClubOperationTests
{
    // ======================================================================
    // SCENARIO 1: Regular member, attends weekly for 3 months, pays monthly
    // ======================================================================

    [Fact]
    public void Scenario_RegularMember_WeeklyFor3Months_PaysMonthly()
    {
        var fencer = new Fencer { Id = "alice", Name = "Alice", IsStudent = false };
        var rules = DefaultRules();

        // Generate weekly trainings (Tuesdays) for Jan–Mar 2024
        var trainings = GenerateWeeklyTrainings(
            DayOfWeek.Tuesday,
            new DateTime(2024, 1, 2), // first Tuesday of Jan
            new DateTime(2024, 3, 31),
            topic: "Longsword",
            attendeeIds: new[] { fencer.Id });

        // MONTH 1: January — 4–5 sessions, pays full pass (12000)
        var janSessions = CountAttendance(trainings, fencer.Id, 2024, 1);
        var janQuote = DuesCalculator.Calculate(janSessions, fencer.IsStudent, rules, alreadyPaid: 12000m);

        janSessions.Should().BeInRange(4, 5);
        janQuote.TotalDue.Should().Be(12000m); // unlimited pass kicks in at 5+
        janQuote.IsCovered.Should().BeTrue();
        janQuote.Outstanding.Should().Be(0m);

        // MONTH 2: February — 4 sessions, pays full pass
        var febSessions = CountAttendance(trainings, fencer.Id, 2024, 2);
        var febQuote = DuesCalculator.Calculate(febSessions, fencer.IsStudent, rules, alreadyPaid: 12000m);

        febSessions.Should().BeInRange(4, 5);
        febQuote.IsCovered.Should().BeTrue();

        // MONTH 3: March — 4–5 sessions, pays full pass
        var marSessions = CountAttendance(trainings, fencer.Id, 2024, 3);
        var marQuote = DuesCalculator.Calculate(marSessions, fencer.IsStudent, rules, alreadyPaid: 12000m);

        marSessions.Should().BeGreaterThanOrEqualTo(4);
        marQuote.IsCovered.Should().BeTrue();

        // Total over 3 months: 3 × 12000 = 36000
        var totalPaid = 36000m;
        var totalDue = janQuote.TotalDue + febQuote.TotalDue + marQuote.TotalDue;
        totalPaid.Should().BeGreaterThanOrEqualTo(totalDue);
    }

    // ======================================================================
    // SCENARIO 2: Student with irregular attendance, overpays once, credit rolls
    // ======================================================================

    [Fact]
    public void Scenario_Student_IrregularAttendance_CreditRollover()
    {
        var fencer = new Fencer { Id = "bob", Name = "Bob", IsStudent = true };
        var rules = DefaultRules();

        // Jan: attends 2 sessions, pays 9000 (half-pass) — overpays
        int janSessions = 2;
        decimal janPaid = 9000m;
        var janQuote = DuesCalculator.Calculate(janSessions, fencer.IsStudent, rules, alreadyPaid: janPaid);

        // Best option for 2 sessions as student:
        // single = 2 × 2000 = 4000, 4-pack = 5500 (applicable since 2 ? 4)
        // cheapest = 4000 (single tickets)
        janQuote.TotalDue.Should().BeLessThan(janPaid);
        janQuote.Overpayment.Should().BeGreaterThan(0);
        var janCredit = janQuote.Overpayment;

        // Feb: attends 4 sessions, carries credit from Jan
        int febSessions = 4;
        var febQuote = DuesCalculator.Calculate(febSessions, fencer.IsStudent, rules, alreadyPaid: janCredit);

        // Student 4 sessions: single = 4×2000=8000, 4-pack = 5500, unlimited = 7000
        // cheapest = 5500 (4-pack)
        febQuote.TotalDue.Should().Be(5500m);
        var febOutstanding = febQuote.Outstanding;

        // If credit wasn't enough, there's still outstanding
        // Let's verify: credit from Jan (janPaid - janDue) applied to Feb
        febQuote.EffectivePaid.Should().Be(janCredit);

        // Mar: attends 1 session, pays exact amount
        int marSessions = 1;
        decimal marPaid = 2000m; // student single = 2000
        var marQuote = DuesCalculator.Calculate(marSessions, fencer.IsStudent, rules, alreadyPaid: marPaid);

        marQuote.TotalDue.Should().Be(2000m);
        marQuote.IsCovered.Should().BeTrue();
        marQuote.Overpayment.Should().Be(0m);
    }

    // ======================================================================
    // SCENARIO 3: Fencer doesn't pay for 2 months, accumulates debt
    // ======================================================================

    [Fact]
    public void Scenario_NonPayingFencer_AccumulatesDebt()
    {
        var fencer = new Fencer { Id = "charlie", Name = "Charlie", IsStudent = false };
        var rules = DefaultRules();

        // Month 1: 4 sessions, pays nothing
        var m1 = DuesCalculator.Calculate(4, fencer.IsStudent, rules, alreadyPaid: 0m);
        m1.IsCovered.Should().BeFalse();
        m1.Outstanding.Should().BeGreaterThan(0);
        var debt1 = m1.Outstanding;

        // Month 2: 3 sessions, still pays nothing
        var m2 = DuesCalculator.Calculate(3, fencer.IsStudent, rules, alreadyPaid: 0m);
        m2.IsCovered.Should().BeFalse();
        var debt2 = m2.Outstanding;

        // Month 3: 5 sessions, pays both months' debt + current
        var m3Due = DuesCalculator.Calculate(5, fencer.IsStudent, rules, alreadyPaid: 0m).TotalDue;
        var totalDebt = debt1 + debt2 + m3Due;

        // Fencer pays total accumulated debt in month 3
        var m3WithPayment = DuesCalculator.Calculate(5, fencer.IsStudent, rules, alreadyPaid: m3Due);
        m3WithPayment.IsCovered.Should().BeTrue();
        m3WithPayment.Outstanding.Should().Be(0m);

        // But months 1 & 2 are still individually unpaid (DuesCalculator is per-month)
        totalDebt.Should().BeGreaterThan(m3Due, "accumulated debt from prior months adds up");
    }

    // ======================================================================
    // SCENARIO 4: Two fencers, same trainings, different billing (student vs not)
    // ======================================================================

    [Fact]
    public void Scenario_TwoFencers_SameTrainings_DifferentBilling()
    {
        var regular = new Fencer { Id = "dana", Name = "Dana", IsStudent = false };
        var student = new Fencer { Id = "erik", Name = "Erik", IsStudent = true };
        var rules = DefaultRules();

        // Both attend 4 sessions in a month
        int sessions = 4;
        var regularQuote = DuesCalculator.Calculate(sessions, regular.IsStudent, rules);
        var studentQuote = DuesCalculator.Calculate(sessions, student.IsStudent, rules);

        // Regular: 4 sessions ? 4-pack = 9000
        regularQuote.TotalDue.Should().Be(9000m);
        regularQuote.TierLabel.Should().Be("4-session pass");

        // Student: 4 sessions ? student 4-pack = 5500
        studentQuote.TotalDue.Should().Be(5500m);
        studentQuote.TierLabel.Should().Be("4-session pass");

        studentQuote.TotalDue.Should().BeLessThan(regularQuote.TotalDue);
    }

    // ======================================================================
    // SCENARIO 5: Full 3-month simulation with credit carry (like FencersViewModel)
    // ======================================================================

    [Fact]
    public void Scenario_ThreeMonths_CreditCarryLikeFencersVm()
    {
        var fencer = new Fencer { Id = "fiona", Name = "Fiona", IsStudent = false };
        var rules = DefaultRules();

        // Simulate the FencersViewModel credit-carry pre-pass:
        // Walk each month chronologically, carry overpayment forward.

        var months = new[]
        {
            (Year: 2024, Month: 1, Sessions: 3, CashPaid: 12000m), // overpays
            (Year: 2024, Month: 2, Sessions: 5, CashPaid: 0m),     // uses credit
            (Year: 2024, Month: 3, Sessions: 4, CashPaid: 10000m), // partial
        };

        decimal credit = 0m;
        var results = new List<(int Month, DuesQuote Quote, decimal CreditAfter)>();

        foreach (var (year, month, sessions, cashPaid) in months)
        {
            var quote = DuesCalculator.Calculate(sessions, fencer.IsStudent, rules, alreadyPaid: cashPaid + credit);
            credit = quote.Overpayment;
            results.Add((month, quote, credit));
        }

        // Month 1: 3 sessions ? 4-pack 9000 (cheapest). Paid 12000. Overpay = 3000.
        results[0].Quote.TotalDue.Should().Be(9000m);
        results[0].Quote.IsCovered.Should().BeTrue();
        results[0].CreditAfter.Should().Be(3000m);

        // Month 2: 5 sessions ? unlimited 12000. Credit 3000 + cash 0 = 3000.
        // Outstanding = 12000 - 3000 = 9000. No overpayment.
        results[1].Quote.TotalDue.Should().Be(12000m);
        results[1].Quote.IsCovered.Should().BeFalse();
        results[1].Quote.Outstanding.Should().Be(9000m);
        results[1].CreditAfter.Should().Be(0m);

        // Month 3: 4 sessions ? 4-pack 9000. Credit 0 + cash 10000. Overpay = 1000.
        results[2].Quote.TotalDue.Should().Be(9000m);
        results[2].Quote.IsCovered.Should().BeTrue();
        results[2].CreditAfter.Should().Be(1000m);
    }

    // ======================================================================
    // SCENARIO 6: Recurring training rule generates correct sessions
    // ======================================================================

    [Fact]
    public void Scenario_RecurringTrainingRule_GeneratesWeeklySessions()
    {
        var rule = new RecurringTrainingRule
        {
            DayOfWeek = DayOfWeek.Wednesday,
            TimeOfDay = new TimeSpan(18, 0, 0),
            EndTimeOfDay = new TimeSpan(20, 0, 0),
            Topic = "Messer & Buckler",
            StartDate = new DateTime(2024, 1, 1),
            EndDate = new DateTime(2024, 3, 31)
        };

        // Simulate generating sessions from the rule for 3 months
        var sessions = new List<TrainingSession>();
        for (var date = rule.StartDate; date <= rule.EndDate; date = date.AddDays(1))
        {
            if (rule.IsActiveOn(date))
            {
                sessions.Add(new TrainingSession
                {
                    Date = date.Add(rule.TimeOfDay),
                    EndDate = date.Add(rule.EndTimeOfDay),
                    Topic = rule.Topic
                });
            }
        }

        // Should have ~13 Wednesdays in 3 months
        sessions.Count.Should().BeInRange(12, 14);
        sessions.Should().AllSatisfy(s =>
        {
            s.Date.DayOfWeek.Should().Be(DayOfWeek.Wednesday);
            s.Topic.Should().Be("Messer & Buckler");
            s.Date.Hour.Should().Be(18);
        });

        // RecurringTrainingRule.IsActiveOn should return false outside range
        rule.IsActiveOn(new DateTime(2023, 12, 27)).Should().BeFalse(); // before start
        rule.IsActiveOn(new DateTime(2024, 4, 3)).Should().BeFalse();   // after end
        rule.IsActiveOn(new DateTime(2024, 2, 5)).Should().BeFalse();   // Monday (wrong day)
        rule.IsActiveOn(new DateTime(2024, 2, 7)).Should().BeTrue();    // Wednesday in range
    }

    // ======================================================================
    // SCENARIO 7: Mixed group, various attendance patterns, verify billing
    // ======================================================================

    [Fact]
    public void Scenario_MixedGroup_VariousPatterns_CorrectBilling()
    {
        var rules = DefaultRules();

        // Group of 5 fencers with different attendance over 2 months
        var fencers = new[]
        {
            new Fencer { Id = "f1", Name = "Heavy", IsStudent = false },     // attends every session
            new Fencer { Id = "f2", Name = "Regular", IsStudent = false },   // attends 3-4/month
            new Fencer { Id = "f3", Name = "Student", IsStudent = true },    // attends 4/month
            new Fencer { Id = "f4", Name = "Casual", IsStudent = false },    // attends 1-2/month
            new Fencer { Id = "f5", Name = "Newbie", IsStudent = true },     // just joined month 2
        };

        // Month 1 attendance
        var month1Attendance = new Dictionary<string, int>
        {
            ["f1"] = 8, ["f2"] = 4, ["f3"] = 4, ["f4"] = 1, ["f5"] = 0
        };

        // Month 1 payments (everyone pays exact dues)
        var month1Quotes = fencers.ToDictionary(
            f => f.Id,
            f => DuesCalculator.Calculate(
                month1Attendance[f.Id], f.IsStudent, rules, alreadyPaid: 0m));

        // Heavy (8 sessions) ? unlimited 12000
        month1Quotes["f1"].TotalDue.Should().Be(12000m);
        month1Quotes["f1"].TierLabel.Should().Be("unlimited monthly pass");

        // Regular (4 sessions) ? 4-pack 9000
        month1Quotes["f2"].TotalDue.Should().Be(9000m);

        // Student (4 sessions) ? student 4-pack 5500
        month1Quotes["f3"].TotalDue.Should().Be(5500m);

        // Casual (1 session) ? single 3500
        month1Quotes["f4"].TotalDue.Should().Be(3500m);

        // Newbie (0 sessions) ? nothing
        month1Quotes["f5"].TotalDue.Should().Be(0m);

        // Month 2: Everyone pays month 1 dues as their payment + continues
        var month2Attendance = new Dictionary<string, int>
        {
            ["f1"] = 7, ["f2"] = 3, ["f3"] = 5, ["f4"] = 2, ["f5"] = 3
        };

        var month2Quotes = fencers.ToDictionary(
            f => f.Id,
            f => DuesCalculator.Calculate(
                month2Attendance[f.Id], f.IsStudent, rules,
                alreadyPaid: month1Quotes[f.Id].TotalDue)); // paid exact month 1 dues

        // Each paid exactly month 1, so no credit carries (credit = 0).
        // Month 2 billing is fresh:
        // Heavy (7) ? unlimited 12000
        month2Quotes["f1"].TotalDue.Should().Be(12000m);
        month2Quotes["f1"].Outstanding.Should().Be(0m); // paid 12000

        // Regular (3) ? 4-pack 9000 (3 ? 4)
        month2Quotes["f2"].TotalDue.Should().Be(9000m);
        month2Quotes["f2"].Outstanding.Should().Be(0m); // paid 9000

        // Student (5) ? student unlimited 7000
        month2Quotes["f3"].TotalDue.Should().Be(7000m);
        month2Quotes["f3"].IsCovered.Should().BeFalse(); // paid 5500 < 7000
        month2Quotes["f3"].Outstanding.Should().Be(1500m);

        // Casual (2) ? single 2×3500=7000 vs 4-pack 9000 ? single cheaper
        month2Quotes["f4"].TotalDue.Should().Be(7000m);
        month2Quotes["f4"].IsCovered.Should().BeFalse(); // paid 3500 < 7000

        // Newbie student (3) ? student single 3×2000=6000 vs student 4-pack 5500
        // 5500 < 6000, so 4-pack wins
        month2Quotes["f5"].TotalDue.Should().Be(5500m);
        month2Quotes["f5"].IsCovered.Should().BeFalse(); // paid 0
    }

    // ======================================================================
    // SCENARIO 8: Prepay large amount, covers multiple months
    // ======================================================================

    [Fact]
    public void Scenario_PrepayLargeAmount_CoversMultipleMonths()
    {
        var fencer = new Fencer { Id = "greg", Name = "Greg", IsStudent = false };
        var rules = DefaultRules();

        // Greg prepays 36000 Ft in January (3× unlimited pass)
        decimal prepayment = 36000m;
        decimal credit = prepayment;
        var monthlyResults = new List<(string Month, decimal Due, decimal Outstanding, decimal CreditAfter)>();

        // Jan: 5 sessions
        var jan = DuesCalculator.Calculate(5, false, rules, alreadyPaid: credit);
        credit = jan.Overpayment;
        monthlyResults.Add(("Jan", jan.TotalDue, jan.Outstanding, credit));

        // Feb: 4 sessions
        var feb = DuesCalculator.Calculate(4, false, rules, alreadyPaid: credit);
        credit = feb.Overpayment;
        monthlyResults.Add(("Feb", feb.TotalDue, feb.Outstanding, credit));

        // Mar: 6 sessions
        var mar = DuesCalculator.Calculate(6, false, rules, alreadyPaid: credit);
        credit = mar.Overpayment;
        monthlyResults.Add(("Mar", mar.TotalDue, mar.Outstanding, credit));

        // All 3 months should be covered
        monthlyResults.Should().AllSatisfy(r => r.Outstanding.Should().Be(0m));

        // Jan: due 12000, credit after = 36000-12000 = 24000
        monthlyResults[0].Due.Should().Be(12000m);
        monthlyResults[0].CreditAfter.Should().Be(24000m);

        // Feb: due 9000 (4-pack), credit after = 24000-9000 = 15000
        monthlyResults[1].Due.Should().Be(9000m);
        monthlyResults[1].CreditAfter.Should().Be(15000m);

        // Mar: due 12000, credit after = 15000-12000 = 3000
        monthlyResults[2].Due.Should().Be(12000m);
        monthlyResults[2].CreditAfter.Should().Be(3000m);
    }

    // ======================================================================
    // SCENARIO 9: Attendance tracking correctness across multiple trainings
    // ======================================================================

    [Fact]
    public void Scenario_AttendanceTracking_CorrectCounts()
    {
        var fencers = new[]
        {
            new Fencer { Id = "a", Name = "Alpha" },
            new Fencer { Id = "b", Name = "Beta" },
            new Fencer { Id = "c", Name = "Gamma" },
        };

        // Generate 2 months of twice-weekly trainings
        var trainings = new List<TrainingSession>();
        var start = new DateTime(2024, 1, 1);
        var end = new DateTime(2024, 2, 29);

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (d.DayOfWeek == DayOfWeek.Tuesday || d.DayOfWeek == DayOfWeek.Thursday)
            {
                var attendees = new List<string>();
                // Alpha attends every session
                attendees.Add("a");
                // Beta attends Tuesdays only
                if (d.DayOfWeek == DayOfWeek.Tuesday) attendees.Add("b");
                // Gamma attends only in February
                if (d.Month == 2) attendees.Add("c");

                trainings.Add(new TrainingSession
                {
                    Date = d.AddHours(18),
                    EndDate = d.AddHours(20),
                    Topic = "Training",
                    AttendeeFencerIds = attendees
                });
            }
        }

        // January attendance
        var janAlpha = CountAttendance(trainings, "a", 2024, 1);
        var janBeta = CountAttendance(trainings, "b", 2024, 1);
        var janGamma = CountAttendance(trainings, "c", 2024, 1);

        janAlpha.Should().BeInRange(8, 10); // all Tue+Thu in Jan
        janBeta.Should().BeInRange(4, 5);   // Tuesdays only
        janGamma.Should().Be(0);             // doesn't attend in Jan

        // February attendance
        var febAlpha = CountAttendance(trainings, "a", 2024, 2);
        var febBeta = CountAttendance(trainings, "b", 2024, 2);
        var febGamma = CountAttendance(trainings, "c", 2024, 2);

        febAlpha.Should().BeInRange(8, 9);  // all Tue+Thu in Feb
        febBeta.Should().BeInRange(4, 5);   // Tuesdays only
        febGamma.Should().BeInRange(8, 9);  // all sessions in Feb

        // Total sessions check
        var totalSessions = trainings.Count;
        totalSessions.Should().BeInRange(16, 18); // ~8-9 per month × 2 months
    }

    // ======================================================================
    // SCENARIO 10: Price rule change mid-season affects billing
    // ======================================================================

    [Fact]
    public void Scenario_PriceRuleChange_MidSeason()
    {
        var fencer = new Fencer { Id = "hana", Name = "Hana", IsStudent = false };

        // Old rules (valid Jan–Feb)
        var oldRules = new List<PriceRule>
        {
            new() { SessionCount = 1, FullPrice = 3000m, StudentPrice = 2000m,
                    StartDate = new DateTime(2024, 1, 1), EndDate = new DateTime(2024, 2, 29) },
            new() { SessionCount = 0, FullPrice = 10000m, StudentPrice = 6000m,
                    StartDate = new DateTime(2024, 1, 1), EndDate = new DateTime(2024, 2, 29) },
        };

        // New rules (valid from March)
        var newRules = new List<PriceRule>
        {
            new() { SessionCount = 1, FullPrice = 4000m, StudentPrice = 2500m,
                    StartDate = new DateTime(2024, 3, 1) },
            new() { SessionCount = 0, FullPrice = 14000m, StudentPrice = 8000m,
                    StartDate = new DateTime(2024, 3, 1) },
        };

        // Combined rules (what the app sees)
        var allRules = oldRules.Concat(newRules).ToList();

        // Helper to filter active rules for a month
        List<PriceRule> RulesForMonth(int y, int m)
        {
            var from = new DateTime(y, m, 1);
            var to = from.AddMonths(1).AddDays(-1);
            return allRules.Where(r => r.StartDate.Date <= to &&
                                       (r.EndDate is null || r.EndDate.Value.Date >= from))
                           .ToList();
        }

        // Jan: 5 sessions with old prices ? unlimited 10000
        var janRules = RulesForMonth(2024, 1);
        var jan = DuesCalculator.Calculate(5, false, janRules);
        jan.TotalDue.Should().Be(10000m);

        // Mar: 5 sessions with new prices ? unlimited 14000
        var marRules = RulesForMonth(2024, 3);
        var mar = DuesCalculator.Calculate(5, false, marRules);
        mar.TotalDue.Should().Be(14000m);

        // Price increased by 4000
        (mar.TotalDue - jan.TotalDue).Should().Be(4000m);
    }

    // ======================== HELPERS ========================

    private static List<PriceRule> DefaultRules() => new()
    {
        new() { SessionCount = 1, FullPrice = 3500m, StudentPrice = 2000m,
                StartDate = new DateTime(2024, 1, 1) },
        new() { SessionCount = 4, FullPrice = 9000m, StudentPrice = 5500m,
                StartDate = new DateTime(2024, 1, 1) },
        new() { SessionCount = 0, FullPrice = 12000m, StudentPrice = 7000m,
                StartDate = new DateTime(2024, 1, 1) },
    };

    private static List<TrainingSession> GenerateWeeklyTrainings(
        DayOfWeek day, DateTime start, DateTime end, string topic, string[] attendeeIds)
    {
        var sessions = new List<TrainingSession>();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            if (d.DayOfWeek == day)
            {
                sessions.Add(new TrainingSession
                {
                    Date = d.AddHours(18),
                    EndDate = d.AddHours(20),
                    Topic = topic,
                    AttendeeFencerIds = attendeeIds.ToList()
                });
            }
        }
        return sessions;
    }

    private static int CountAttendance(List<TrainingSession> trainings, string fencerId, int year, int month) =>
        trainings.Count(t => t.Date.Year == year && t.Date.Month == month &&
                             t.AttendeeFencerIds.Contains(fencerId));
}
