using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using TPW.PS2.Data;

static class SaveFileChecks
{
    sealed record Fixture
    {
        public required int SchemaVersion { get; init; }
        public required ParkFinances.State Finances { get; init; }
        public required ParkClock.State Clock { get; init; }
        public required ParkAwards.State Awards { get; init; }
    }
    public static void Run(Action<bool,string> check)
    {
        string folder=Path.Combine(Path.GetTempPath(),"tpw-save-audit-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string file=Path.Combine(folder,"park.tpwsave");
            var finances=new ParkFinances();finances.Credit(1234,7);finances.PayWage(67);
            var clock=new ParkClock();clock.Advance(12345,out _,out _,out _);
            var awards=new ParkAwards();awards.AwardGoldTickets(3);awards.GrantHiddenAward(2);
            var dto=new Fixture{SchemaVersion=1,Finances=finances.CaptureState(),Clock=clock.CaptureState(),Awards=awards.CaptureState()};
            ParkSaveFile.Write(file,dto);
            var disk=ParkSaveFile.Read<Fixture>(file);
            var f=new ParkFinances();var c=new ParkClock();var a=new ParkAwards();
            f.RestoreState(disk.Finances);c.RestoreState(disk.Clock);a.RestoreState(disk.Awards);
            check(f.Balance==finances.Balance&&c.Accumulator==12345&&a.GoldTickets==awards.GoldTickets,"file -> actual foundation owners, not geometry-only fixture");
            byte[] first=File.ReadAllBytes(file);
            finances.Credit(999);ParkSaveFile.Write(file,dto with {Finances=finances.CaptureState()});
            check(File.ReadAllBytes(file+".bak").SequenceEqual(first),"replace retains exact prior save as backup");
            check(ParkSaveFile.Read<Fixture>(file).Finances.Balance==finances.Balance,"replacement is the new complete payload");
            byte[] good=File.ReadAllBytes(file);
            void Reject(byte[] bytes,string why)
            {
                string corrupt=Path.Combine(folder,"bad.tpwsave");File.WriteAllBytes(corrupt,bytes);
                bool refused=false;try{ParkSaveFile.Read<Fixture>(corrupt);}catch(InvalidDataException){refused=true;}
                check(refused,why);check(File.ReadAllBytes(file).SequenceEqual(good),"read failure never changes current save");
            }
            var bad=(byte[])good.Clone();bad[0]^=1;Reject(bad,"bad magic refused");
            bad=(byte[])good.Clone();BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(8,4),2);Reject(bad,"future envelope version refused");
            bad=(byte[])good.Clone();BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(12,4),int.MaxValue);Reject(bad,"oversized declared length refused before allocating");
            bad=(byte[])good.Clone();bad[^1]^=1;Reject(bad,"checksum catches a damaged payload");
            Reject(good[..^1],"truncated payload refused");Reject(good.Concat(new byte[]{0}).ToArray(),"trailing data refused");
            Reject(Array.Empty<byte>(),"empty file refused");
            void BadJson(string json,string why)
            {
                byte[] payload=Encoding.UTF8.GetBytes(json);byte[] bytes=new byte[48+payload.Length];
                good.AsSpan(0,12).CopyTo(bytes);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12,4),payload.Length);
                SHA256.HashData(payload).CopyTo(bytes,16);payload.CopyTo(bytes,48);Reject(bytes,why);
            }
            BadJson("{}","missing required owner sections refused even with valid checksum");
            BadJson("null","null snapshot refused");BadJson("{","malformed JSON refused");
            check(!Directory.EnumerateFiles(folder,"*.tmp").Any(),"no pending save temp file after completed writes");
            // Capture/validation can fail before touching an existing destination.
            bool nullRefused=false;try{ParkSaveFile.Write<Fixture>(file,null!);}catch(ArgumentNullException){nullRefused=true;}
            check(nullRefused&&File.ReadAllBytes(file).SequenceEqual(good),"invalid write leaves the old save untouched");
        }
        finally{Directory.Delete(folder,recursive:true);}
    }
}
