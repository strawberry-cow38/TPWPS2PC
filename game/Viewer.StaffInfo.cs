using TPW.PS2.Data;

namespace TPWPS2Viewer;

public partial class Viewer
{
    // Native10BB68/10C138: types first, then map-list members of ONE selected type.
    List<StaffKind> StaffInfoTypes() => _staff?.TypesWithStaff().ToList() ?? new();
    static (StaffKind Kind, int Index) StaffInfoArg(string arg)
    {
        var bits = (arg ?? "0:0").Split(':');
        return ((StaffKind)int.Parse(bits[0]), bits.Length > 1 ? int.Parse(bits[1]) : 0);
    }
    void ShowStaffInfoTypes()
    {
        var names = StaffInfoTypes().Select(k =>
            _text?.Text("eng", StaffTables.TypeLabelRow(k)) ?? k.ToString()).ToList();
        _shopPanel.ShowMenu(names, 0, LaptopScreen.AllStaff.SceneFile);
        ClearLaptopModel(); RefreshLaptopBalance();
        Status(names.Count == 0 ? "no staff hired" : "staff information -- pick a type, or Back");
    }
    void ShowStaffInfoMember(string arg)
    {
        var (kind, index) = StaffInfoArg(arg);
        var crew = _staff?.MembersOfType(kind).ToList() ?? new();
        if (crew.Count == 0)
        {
            _laptopBack.RemoveAt(_laptopBack.Count - 1);
            ShowLaptopLevel(); return;
        }
        index = Math.Clamp(index, 0, crew.Count - 1);
        _laptopBack[^1] = ("staffitem", $"{(int)kind}:{index}");
        var who = crew[index];
        _shopPanel.ShowScreen(LaptopScreen.AllStaff, StaffName(who),
            LaptopScreen.AllStaff.Rows.Select(r => StaffCell(r, who)).ToList());
        BuildLaptopModelFor((ParkRide)null);
        RefreshLaptopBalance();
        Status($"{StaffName(who)} -- {index + 1} of {crew.Count}" + (crew.Count > 1 ? "; Up/Down to page" : ""));
    }
    bool PageStaffInfo(int by)
    {
        if (_laptopBack.Count == 0 || _laptopBack[^1].Kind != "staffitem") return false;
        var (kind, index) = StaffInfoArg(_laptopBack[^1].Arg);
        int count = _staff?.MembersOfType(kind).Count() ?? 0;
        if (count == 0) { ShowLaptopLevel(); return true; }
        int next = Math.Clamp(index + by, 0, count - 1);
        if (next != index)
        {
            _laptopBack[^1] = ("staffitem", $"{(int)kind}:{next}");
            ShowLaptopLevel();
        }
        return true;
    }
}
