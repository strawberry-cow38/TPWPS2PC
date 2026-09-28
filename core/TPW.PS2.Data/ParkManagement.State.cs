#nullable enable
using System.Text.Json.Serialization;
namespace TPW.PS2.Data;
public sealed partial class ParkManagement
{
    static bool DefaultGoalsRecordPresent()=>true;
    static bool DefaultTestPark()=>false;
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record State
    {
        public required int Version {get;init;}
        public required string ClockKey {get;init;}
        public required string AwardsKey {get;init;}
        public required string? FinanceKey {get;init;}
        public required string? StaffKey {get;init;}
        public required string? AdvisorKey {get;init;}
        public required string? UiSoundKey {get;init;}
        public required string? GoalsKey {get;init;}
        public required string? TestParkKey {get;init;}
        public required int MonthsElapsed {get;init;}
        public required int RedMonths {get;init;}
        public required int MonthChanges {get;init;}
        public required int WeeklyPasses {get;init;}
        public required int LastWages {get;init;}
    }
    public State CaptureState(Func<object,string> id)
    {
        string? Key(object? value)=>value==null?null:"external/"+id(value);
        return new(){Version=1,ClockKey=Key(Clock)!,AwardsKey=Key(Awards)!,FinanceKey=Key(Finances),StaffKey=Key(Staff),
            AdvisorKey=Key(Advisor),UiSoundKey=Key(UiSound),
            GoalsKey=GoalsRecordPresent==(Func<bool>)DefaultGoalsRecordPresent?"default/goals":Key(GoalsRecordPresent),
            TestParkKey=TestPark==(Func<bool>)DefaultTestPark?"default/test":Key(TestPark),
            MonthsElapsed=MonthsElapsed,RedMonths=RedMonths,MonthChanges=MonthChanges,WeeklyPasses=WeeklyPasses,LastWages=LastWages};
    }
    /// <summary>Call on an unpublished manager. Resource identities must already be staged;
    /// do not advance the clock, charge wages, run strikes or replay advisor messages.</summary>
    public void RestoreState(State s,Func<string,object> resolve)
    {
        ArgumentNullException.ThrowIfNull(s);ArgumentNullException.ThrowIfNull(resolve);
        T? R<T>(string? key) where T:class {
            if(key==null)return null;
            if(!key.StartsWith("external/",StringComparison.Ordinal)||key.Length<=9||key.Length>1024)
                throw new ArgumentException("Invalid management reference");
            return resolve(key[9..]) as T ?? throw new ArgumentException("Unknown management reference"); }
        if(s.Version!=1 || !ReferenceEquals(R<ParkClock>(s.ClockKey),Clock)||!ReferenceEquals(R<ParkAwards>(s.AwardsKey),Awards)
            ||s.MonthsElapsed<0||s.RedMonths<0||s.MonthChanges<0||s.WeeklyPasses<0)
            throw new ArgumentException("Invalid management state");
        var finance=R<ParkFinances>(s.FinanceKey);var staff=R<ParkStaff>(s.StaffKey);
        var advisor=R<Action<int>>(s.AdvisorKey);var ui=R<Action<int>>(s.UiSoundKey);
        var goals=s.GoalsKey=="default/goals"?DefaultGoalsRecordPresent:R<Func<bool>>(s.GoalsKey);
        var test=s.TestParkKey=="default/test"?DefaultTestPark:R<Func<bool>>(s.TestParkKey);
        Finances=finance;Staff=staff;Advisor=advisor;UiSound=ui;GoalsRecordPresent=goals;TestPark=test;
        MonthsElapsed=s.MonthsElapsed;RedMonths=s.RedMonths;MonthChanges=s.MonthChanges;WeeklyPasses=s.WeeklyPasses;LastWages=s.LastWages;
    }
}
