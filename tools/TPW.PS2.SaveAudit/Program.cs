using System.Text.Json;
using System.Text.Json.Nodes;
using TPW.PS2.Data;

// Disc-free owner-level audit. This is deliberately NOT a whole-park persistence claim.
int checks = 0;
void Check(bool condition, string name)
{
    checks++;
    if (!condition) throw new InvalidOperationException(name);
}
string Json<T>(T value) => JsonSerializer.Serialize(value);
T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(Json(value))!;
void Equal<T>(T a, T b, string name) => Check(Json(a) == Json(b), name);
void Reject<T>(T invalid, Action<T> restore, Func<T> capture, string name)
{
    string before = Json(capture());
    bool rejected = false;
    try { restore(invalid); }
    catch (ArgumentException) { rejected = true; }
    Check(rejected, name + ": rejected");
    Check(before == Json(capture()), name + ": no partial mutation (including shared tables)");
}
void RequireEveryJsonMember<T>(T state)
{
    var original = JsonNode.Parse(Json(state))!.AsObject();
    foreach (string key in original.Select(p => p.Key).ToArray())
    {
        var missing = original.DeepClone().AsObject();
        missing.Remove(key);
        bool rejected = false;
        try { JsonSerializer.Deserialize<T>(missing.ToJsonString()); }
        catch (JsonException) { rejected = true; }
        Check(rejected, typeof(T).FullName + ": missing " + key);
    }
}

if(args.Length>0 && args[0]=="--presentation-only")
{
    checks+=AdvisorSaveChecks.Run();checks+=NativePresentationSaveChecks.Run();
    if(args.Length==2){using var disc=new Disc(args[1]);checks+=AdvisorSaveChecks.Run(AdvisorCatalogue.Load(disc));checks+=AdvisorGraphSaveChecks.Run(disc);}
    Console.WriteLine($"PASS PresentationSaveAudit: {checks} checks (native/advisor core; not a whole world save).");return 0;
}

if(args.Length>0 && args[0]=="--staff-only")
{
    checks+=StaffMemberSaveChecks.Run();checks+=StaffLeafSaveChecks.Run();checks+=StaffSecuritySaveChecks.Run();checks+=ParkStaffGraphSaveChecks.Run();
    Console.WriteLine($"PASS StaffSaveAudit: {checks} checks (staff owners, not a full world save).");return 0;
}

if(args.Length>0 && args[0]=="--controllers-only")
{
    checks += NativeEntranceFlowSaveChecks.Run();
    checks += NativeRideQueueSaveChecks.Run();
    checks += StaffRouteServiceSaveChecks.Run();
    Console.WriteLine($"PASS ControllerSaveAudit: {checks} checks (native controllers; not a whole world save).");
    return 0;
}

if(args.Length>0 && args[0]=="--visitors-only")
{
    GuestWalkSaveChecks.Run(Check);VisitorNeedsSaveChecks.Run(Check);ParkVisitorsSaveChecks.Run(Check);
    if(args.Length==2){using var d=new Disc(args[1]);GuestWalkSaveChecks.Run(d,Check);DiscVisitorGraphSaveChecks.Run(d,Check);}
    Console.WriteLine($"PASS VisitorSaveAudit: {checks} checks (visitor graph, not a full world save).");
    return 0;
}

if(args.Length>0 && args[0]=="--ground-only")
{
    if(args.Length!=2) throw new ArgumentException("--ground-only needs the owner disc path");
    using var groundDisc=new Disc(args[1]);
    ParkPathsSaveChecks.Run(groundDisc,Check);PathToolSaveChecks.Run(groundDisc,Check);
    Console.WriteLine($"PASS GroundSaveAudit: {checks} checks (walking/path owners; no Viewer claim).");
    return 0;
}

if(args.Length>0)
{
    using var disc=new Disc(args[0]);
    checks+=AdvisorSaveChecks.Run(AdvisorCatalogue.Load(disc));checks+=AdvisorGraphSaveChecks.Run(disc);
    GuestWalkSaveChecks.Run(disc,Check);
    ParkPathsSaveChecks.Run(disc,Check);
    PathToolSaveChecks.Run(disc,Check);
    DiscRseSaveChecks.Run(disc,Check);
    DiscParkSimSaveChecks.Run(disc,Check);
    DiscVisitorGraphSaveChecks.Run(disc,Check);
}
checks+=AdvisorSaveChecks.Run();checks+=NativePresentationSaveChecks.Run();
checks+=StaffMemberSaveChecks.Run();checks+=StaffLeafSaveChecks.Run();checks+=StaffSecuritySaveChecks.Run();checks+=ParkStaffGraphSaveChecks.Run();
checks += NativeEntranceFlowSaveChecks.Run();
checks += NativeRideQueueSaveChecks.Run();
checks += StaffRouteServiceSaveChecks.Run();
GuestWalkSaveChecks.Run(Check);
VisitorNeedsSaveChecks.Run(Check);
ParkVisitorsSaveChecks.Run(Check);
TrackRideSaveChecks.Run(Check);
CoasterSaveChecks.Run(Check);
ParkSimVehicleSaveChecks.Run(Check);
ParkRideSaveChecks.Run(Check);
ParkSimSaveChecks.Run(Check);
SnapshotRandomChecks.Run(Check);
RseMachineSaveChecks.Run(Check);
RseHostSaveChecks.Run(Check);
NativeResourceSaveChecks.Run(Check);
SaveFileChecks.Run(Check);

var savedCalendar = (byte[])ParkClock.DaysInMonth.Clone();
var savedMapping = (int[])ParkAwards.MedalOfHiddenAward.Clone();
try
{
    var clock = new ParkClock();
    // TotalDays and Countdown intentionally differ from the calendar date (Set semantics).
    clock.Advance(ParkClock.UnitsPerDay, out _, out _, out _);
    clock.Set(30, 11, 7);
    clock.Advance(ParkClock.UnitsPerDay - 19, out _, out _, out _);
    var clockState = RoundTrip(clock.CaptureState());
    var loadedClock = new ParkClock();
    loadedClock.RestoreState(clockState);
    Equal(clock.CaptureState(), loadedClock.CaptureState(), "clock JSON round trip");
    Check(loadedClock.Accumulator == ParkClock.UnitsPerDay - 19
        && loadedClock.TotalDays == 1 && loadedClock.Countdown == -1, "partial day/counters retained");
    RequireEveryJsonMember(clockState);

    var finances = new ParkFinances { Unlimited = false, Balance = 50_000 };
    for (int i = 0; i < 155; i++)
    {
        finances.Credit(1000 + i, i % 7 - 2);
        finances.PayWage(11 + i);
        finances.Debit(i);
        finances.MonthEnd(300 + i);
    }
    // Distinct nonzero history/current values in all added rings, including wraparound.
    for(int i=0;i<150;i++)
    {
        finances.CreditAdmission(31+i);finances.CreditByKind(4,71+i);finances.CreditByKind(5,131+i);
        finances.MonthEnd(19+i);
    }
    finances.CreditAdmission(919);finances.CreditByKind(4,1923);finances.CreditByKind(5,2901);
    finances.Credit(987, int.MinValue); // Nonempty current slot, unusual supported category.
    var financeState = RoundTrip(finances.CaptureState());
    var loadedFinances = new ParkFinances();
    loadedFinances.Credit(1, 999); // Restore replaces, rather than merges, the old ledger.
    loadedFinances.RestoreState(financeState);
    Equal(finances.CaptureState(), loadedFinances.CaptureState(), "finance JSON round trip");
    Check(!loadedFinances.IncomeByCategory.ContainsKey(999), "old ledger removed");
    RequireEveryJsonMember(financeState);
    for(int k=0;k<ParkFinances.PeriodSlots;k++)
        Check(finances.BalanceInPeriod(k)==loadedFinances.BalanceInPeriod(k)
            &&finances.GateInPeriod(k)==loadedFinances.GateInPeriod(k)
            &&finances.ShopInPeriod(k)==loadedFinances.ShopInPeriod(k)
            &&finances.SideshowInPeriod(k)==loadedFinances.SideshowInPeriod(k),"all live graph getters agree after restore");
    for(int k=0;k<150;k++)
    {
        foreach(var owner in new[]{finances,loadedFinances})
        {owner.CreditAdmission(100+k);owner.CreditByKind(4,200+k);owner.CreditByKind(5,300+k);owner.MonthEnd(k);}
        Equal(finances.CaptureState(),loadedFinances.CaptureState(),"six finance rings/totals continue across another wrap");
    }
    foreach(var invalid in new[]{financeState with{BalanceHistory=null!},financeState with{Gate=new int[143]},
        financeState with{Shop=null!},financeState with{Sideshow=new int[145]}})
        Reject(invalid,loadedFinances.RestoreState,loadedFinances.CaptureState,"new finance ring invalid state");
    var copy=loadedFinances.CaptureState();int preserved=copy.BalanceHistory[0];
    copy.BalanceHistory[0]^=1;copy.Gate[0]^=1;copy.Shop[0]^=1;copy.Sideshow[0]^=1;
    Check(loadedFinances.CaptureState().BalanceHistory[0]==preserved,"new ring capture does not alias owner storage");

    var awards = new ParkAwards { UltimateCoasters = 9 };
    awards.AwardGoldTickets(12);
    awards.GoldTickets -= 5;
    awards.GrantHiddenAward(0);
    awards.GrantHiddenAward(3);
    awards.Medals[0] = true; // Independent UI medal state must not be inferred from the bits.
    var awardState = RoundTrip(awards.CaptureState());
    var loadedAwards = new ParkAwards();
    loadedAwards.RestoreState(awardState);
    Equal(awards.CaptureState(), loadedAwards.CaptureState(), "award JSON round trip");
    RequireEveryJsonMember(awardState);

    // Advance through two years, crossing month/year boundaries, and more than two ring laps.
    int months = 0, years = 0;
    for (int i = 0; i < 800; i++)
    {
        foreach (int units in new[] { 18, 1, ParkClock.UnitsPerDay + 3 })
        {
            bool r1 = clock.Advance(units, out bool d1, out bool m1, out bool y1);
            bool r2 = loadedClock.Advance(units, out bool d2, out bool m2, out bool y2);
            Check((r1, d1, m1, y1) == (r2, d2, m2, y2), "clock continuation rollover flags");
            if (m1) { months++; finances.MonthEnd(71); loadedFinances.MonthEnd(71); }
            if (y1) years++;
            Equal(clock.CaptureState(), loadedClock.CaptureState(), "clock continuation state");
        }
        if (i < 310)
        {
            finances.Credit(701 + i, i % 9);
            loadedFinances.Credit(701 + i, i % 9);
            Check(finances.PayWage(i) == loadedFinances.PayWage(i), "wage continuation result");
            finances.MonthEnd(808 + i);
            loadedFinances.MonthEnd(808 + i);
        }
        Equal(finances.CaptureState(), loadedFinances.CaptureState(), "finance continuation state");
        Check(finances.WagesHigh == loadedFinances.WagesHigh, "advisor wage continuation");
        for (int back = 0; back < ParkFinances.PeriodSlots; back++)
            Check(finances.IncomeInPeriod(back) == loadedFinances.IncomeInPeriod(back)
                && finances.WagesInPeriod(back) == loadedFinances.WagesInPeriod(back), "ring history continuation");
    }
    Check(months >= 24 && years >= 2, "month and year boundaries exercised");
    foreach (int bit in Enumerable.Range(0, 5))
    {
        if (!awards.HasHiddenAward(bit)) { awards.AwardGoldTickets(1); awards.GrantHiddenAward(bit); }
        if (!loadedAwards.HasHiddenAward(bit)) { loadedAwards.AwardGoldTickets(1); loadedAwards.GrantHiddenAward(bit); }
        Equal(awards.CaptureState(), loadedAwards.CaptureState(), "award continuation, including prior hidden flags");
    }
    foreach (bool unlimited in new[] { false, true })
    foreach (bool free in new[] { false, true })
    {
        finances.Balance = -3; finances.Unlimited = unlimited; finances.FreeBuild = free;
        loadedFinances.RestoreState(RoundTrip(finances.CaptureState()));
        Check(finances.Debit(90) == loadedFinances.Debit(90), "debit flags round trip");
        Equal(finances.CaptureState(), loadedFinances.CaptureState(), "debit flags continuation");
    }
    // Signed finance totals are not incorrectly rejected after Int32 overflow.
    var overflow = new ParkFinances();
    overflow.Credit(int.MaxValue, -99); overflow.Credit(1, -99);
    overflow.PayWage(int.MaxValue); overflow.PayWage(1);
    loadedFinances.RestoreState(RoundTrip(overflow.CaptureState()));
    Equal(overflow.CaptureState(), loadedFinances.CaptureState(), "signed accounting wraparound retained");

    // Capture owns its collections; mutating the DTO must not mutate live state.
    var fBefore = finances.CaptureState();
    var fCopy = finances.CaptureState();
    fCopy.Income[0]++; fCopy.Wages[0]++; fCopy.IncomeByCategory.Clear();
    Equal(fBefore, finances.CaptureState(), "finance capture detached");
    var cBefore = clock.CaptureState();
    clock.CaptureState().DaysInMonth[0] = 1;
    Equal(cBefore, clock.CaptureState(), "clock capture detached");
    var aBefore = awards.CaptureState();
    var aCopy = awards.CaptureState();
    aCopy.Medals[0] = !aCopy.Medals[0]; aCopy.MedalOfHiddenAward[0] = 4;
    Equal(aBefore, awards.CaptureState(), "award capture detached");

    loadedFinances.RestoreState(financeState);
    var fRestored = loadedFinances.CaptureState();
    financeState.Income[0]++; financeState.Wages[0]++; financeState.IncomeByCategory.Clear();
    Equal(fRestored, loadedFinances.CaptureState(), "finance restore detached");
    var fInput = RoundTrip(fRestored); loadedFinances.RestoreState(fInput);
    loadedFinances.Credit(84, 3); loadedFinances.MonthEnd(5);
    Equal(fRestored, fInput, "finance live changes do not change restore input");
    loadedClock.RestoreState(clockState);
    var cRestored = loadedClock.CaptureState();
    clockState.DaysInMonth[0] = 1;
    Equal(cRestored, loadedClock.CaptureState(), "clock restore detached");
    loadedAwards.RestoreState(awardState);
    var aRestored = loadedAwards.CaptureState();
    awardState.Medals[0] = !awardState.Medals[0]; awardState.MedalOfHiddenAward[0] = 4;
    Equal(aRestored, loadedAwards.CaptureState(), "award restore detached");
    var aInput = RoundTrip(aRestored); loadedAwards.RestoreState(aInput);
    loadedAwards.GrantHiddenAward(2);
    Equal(aRestored, aInput, "award live changes do not change restore input");

    // Verify that the exposed mutable global tables really round trip too.
    ParkClock.DaysInMonth[1] = 29;
    var customClock = RoundTrip(clock.CaptureState());
    ParkClock.DaysInMonth[1] = 28;
    loadedClock.RestoreState(customClock);
    Equal(customClock, loadedClock.CaptureState(), "global calendar table round trip");
    ParkClock.DaysInMonth[1] = 28;
    Check(customClock.DaysInMonth[1] == 29, "global calendar does not alias restore input");
    ParkAwards.MedalOfHiddenAward[0] = 4;
    var customAward = RoundTrip(awards.CaptureState());
    ParkAwards.MedalOfHiddenAward[0] = 1;
    loadedAwards.RestoreState(customAward);
    Equal(customAward, loadedAwards.CaptureState(), "global medal mapping round trip");
    ParkAwards.MedalOfHiddenAward[0] = 1;
    Check(customAward.MedalOfHiddenAward[0] == 4, "global medal mapping does not alias restore input");

    // Each rejection checks every captured field, not just the public headline values.
    var f = loadedFinances.CaptureState();
    foreach (var invalid in new[] {
        f with { SchemaVersion = 0 }, f with { SchemaVersion = 1 }, f with { SchemaVersion = 3 }, f with { PeriodCount = -1 },
        f with { Balance = 42, Income = null! }, f with { Balance = 42, Income = new int[143] },
        f with { Balance = 42, Wages = null! }, f with { Balance = 42, Wages = new int[145] },
        f with { Balance = 42, IncomeByCategory = null! } })
        Reject(invalid, loadedFinances.RestoreState, loadedFinances.CaptureState, "corrupt finances");
    Reject<ParkFinances.State>(null!, loadedFinances.RestoreState, loadedFinances.CaptureState, "null finances");
    var c = loadedClock.CaptureState();
    var badDays = (byte[])c.DaysInMonth.Clone(); badDays[11] = 0;
    var longDays = (byte[])c.DaysInMonth.Clone(); longDays[11] = 32;
    foreach (var invalid in new[] {
        c with { SchemaVersion = 0 }, c with { SchemaVersion = 2 },
        c with { Accumulator = -1 }, c with { Accumulator = ParkClock.UnitsPerDay },
        c with { Month = -1 }, c with { Month = 12 }, c with { Year = -1 }, c with { Year = int.MaxValue },
        c with { Day = -1 }, c with { Day = c.DaysInMonth[c.Month] }, c with { TotalDays = -1 },
        c with { Countdown = 99, DaysInMonth = null! }, c with { Countdown = 99, DaysInMonth = new byte[11] },
        c with { Countdown = 99, DaysInMonth = badDays }, c with { Countdown = 99, DaysInMonth = longDays } })
        Reject(invalid, loadedClock.RestoreState, loadedClock.CaptureState, "corrupt clock");
    Reject<ParkClock.State>(null!, loadedClock.RestoreState, loadedClock.CaptureState, "null clock");
    var a = loadedAwards.CaptureState();
    foreach (var invalid in new[] {
        a with { SchemaVersion = 0 }, a with { SchemaVersion = 2 },
        a with { GoldTickets = -1 }, a with { GoldTicketsEarned = -1 },
        a with { UltimateCoasters = -1 }, a with { UltimateCoasters = 15 },
        a with { HiddenAwards = -1 }, a with { HiddenAwards = 32 },
        a with { GoldTickets = 99, Medals = null! }, a with { GoldTickets = 99, Medals = new bool[4] },
        a with { GoldTickets = 99, MedalOfHiddenAward = null! },
        a with { GoldTickets = 99, MedalOfHiddenAward = new int[6] },
        a with { GoldTickets = 99, MedalOfHiddenAward = new[] { 1, 0, 4, 3, -1 } },
        a with { GoldTickets = 99, MedalOfHiddenAward = new[] { 1, 0, 4, 3, 5 } } })
        Reject(invalid, loadedAwards.RestoreState, loadedAwards.CaptureState, "corrupt awards");
    Reject<ParkAwards.State>(null!, loadedAwards.RestoreState, loadedAwards.CaptureState, "null awards");

    // A malformed JSON representation must fail before RestoreState can run.
    string before = Json(loadedFinances.CaptureState());
    bool malformedRejected = false;
    try { loadedFinances.RestoreState(JsonSerializer.Deserialize<ParkFinances.State>("{\"Balance\":2147483648}")!); }
    catch (JsonException) { malformedRejected = true; }
    Check(malformedRejected && before == Json(loadedFinances.CaptureState()), "malformed JSON rejected without mutation");
    Console.WriteLine($"PASS SaveAudit: {checks} checks (JSON, continuation, copies, schema/range rejection and atomic owner restore).");
    Console.WriteLine("Scope: owner/VM/host/file snapshots; optional disc-backed script continuation. NOT total park persistence or live Viewer loading.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"FAIL SaveAudit after {checks} checks: {ex}");
    return 1;
}
finally
{
    savedCalendar.CopyTo(ParkClock.DaysInMonth, 0);
    savedMapping.CopyTo(ParkAwards.MedalOfHiddenAward, 0);
}
