using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game;
using ImGuiNET;
using Lumina.Excel;
using Lumina.Excel.GeneratedSheets;

namespace SpecialWeaponProgessOverview;

public class InventoryWindow : Window, IDisposable
{
    private Plugin Plugin;

    private static ICallGateSubscriber<ulong?, bool>? _OnRetainerChanged;
    private static ICallGateSubscriber<(uint, InventoryItem.ItemFlags, ulong, uint), bool>? _OnItemAdded;
    private static ICallGateSubscriber<(uint, InventoryItem.ItemFlags, ulong, uint), bool>? _OnItemRemoved;
    private static ICallGateSubscriber<uint, ulong, uint, uint>? ItemCount;
    private static ICallGateSubscriber<uint, ulong, uint, uint>? _ItemCountHQ;
    private static ICallGateSubscriber<bool, bool>? Initialized;
    private static ICallGateSubscriber<bool>? IsInitialized;

    private static ExcelSheet<Item> ItemSheet = DalamudApi.DataManager.GetExcelSheet<Item>();
    private static ExcelSheet<ClassJob> ClassJobSheet = DalamudApi.DataManager.GetExcelSheet<ClassJob>();

    public static bool AToolsInstalled
    {
        get
        {
            return DalamudApi.PluginInterface.InstalledPlugins.Any(x => x.InternalName is "Allagan Tools" or "InventoryTools");
        }
    }

    public static bool AToolsEnabled => AToolsInstalled && IsInitialized != null && IsInitialized.InvokeFunc();

    public static bool ATools
    {
        get
        {
            try
            {
                return AToolsEnabled;
            }
            catch
            {
                return false;
            }
        }
    }
    internal static void Init()
    {
        Initialized = DalamudApi.PluginInterface.GetIpcSubscriber<bool, bool>("AllaganTools.Initialized");
        IsInitialized = DalamudApi.PluginInterface.GetIpcSubscriber<bool>("AllaganTools.IsInitialized");
        Initialized.Subscribe(SetupIPC);
        DalamudApi.ClientState.Logout += LogoutCacheClear;
        SetupIPC(true);
    }

    private static void LogoutCacheClear()
    {
        RetainerData.Clear();
    }

    private static void SetupIPC(bool obj)
    {

        _OnRetainerChanged = DalamudApi.PluginInterface.GetIpcSubscriber<ulong?, bool>("AllaganTools.RetainerChanged");
        _OnItemAdded = DalamudApi.PluginInterface.GetIpcSubscriber<(uint, InventoryItem.ItemFlags, ulong, uint), bool>("AllaganTools.ItemAdded");
        _OnItemRemoved = DalamudApi.PluginInterface.GetIpcSubscriber<(uint, InventoryItem.ItemFlags, ulong, uint), bool>("AllaganTools.ItemRemoved");
        ItemCount = DalamudApi.PluginInterface.GetIpcSubscriber<uint, ulong, uint, uint>("AllaganTools.ItemCount");
        _ItemCountHQ = DalamudApi.PluginInterface.GetIpcSubscriber<uint, ulong, uint, uint>("AllaganTools.ItemCountHQ");
    }

    public static Dictionary<ulong, Dictionary<uint, ItemInfo>> RetainerData = new Dictionary<ulong, Dictionary<uint, ItemInfo>>();
    public class ItemInfo
    {
        public uint ItemId { get; set; }

        public uint Quantity { get; set; }

        public uint HQQuantity { get; set; }

        public ItemInfo(uint itemId, uint quantity, uint hqQuantity)
        {
            ItemId = itemId;
            Quantity = quantity;
            HQQuantity = hqQuantity;
        }
    }

    public static uint GetRetainerInventoryItem(uint ItemId, ulong retainerId, bool hqonly = false)
    {
        if (ATools)
        {
            return ItemCount.InvokeFunc(ItemId, retainerId, 10000) +
                   ItemCount.InvokeFunc(ItemId, retainerId, 10001) +
                   ItemCount.InvokeFunc(ItemId, retainerId, 10002) +
                   ItemCount.InvokeFunc(ItemId, retainerId, 10003) +
                   ItemCount.InvokeFunc(ItemId, retainerId, 10004) +
                   ItemCount.InvokeFunc(ItemId, retainerId, 10005) +
                   ItemCount.InvokeFunc(ItemId, retainerId, 10006) +
                   ItemCount.InvokeFunc(ItemId, retainerId, (uint)InventoryType.RetainerCrystals);
        }
        return 0;
    }
    public static unsafe int GetRetainerItemCount(uint ItemId, bool tryCache = true, bool hqOnly = false)
    {

        if (ATools)
        {
            if (!DalamudApi.ClientState.IsLoggedIn || DalamudApi.Condition[ConditionFlag.OnFreeTrial]) return 0;

            try
            {
                if (tryCache)
                {
                    if (RetainerData.SelectMany(x => x.Value).Any(x => x.Key == ItemId))
                    {
                        if (hqOnly)
                        {
                            return (int)RetainerData.Values.SelectMany(x => x.Values).Where(x => x.ItemId == ItemId).Sum(x => x.HQQuantity);
                        }

                        return (int)RetainerData.Values.SelectMany(x => x.Values).Where(x => x.ItemId == ItemId).Sum(x => x.Quantity);
                    }
                }

                for (var i = 0; i < 10; i++)
                {
                    var retainer = RetainerManager.Instance()->GetRetainerBySortedIndex((uint)i);

                    var retainerId = retainer->RetainerId;

                    if (retainerId > 0 && retainer->Available)
                    {
                        if (RetainerData.ContainsKey(retainerId))
                        {
                            var ret = RetainerData[retainerId];
                            if (ret.ContainsKey(ItemId))
                            {
                                var item = ret[ItemId];
                                item.ItemId = ItemId;
                                item.Quantity = GetRetainerInventoryItem(ItemId, retainerId);

                            }
                            else
                            {
                                ret.TryAdd(ItemId, new ItemInfo(ItemId, GetRetainerInventoryItem(ItemId, retainerId), GetRetainerInventoryItem(ItemId, retainerId, true)));
                            }
                        }
                        else
                        {
                            RetainerData.TryAdd(retainerId, new Dictionary<uint, ItemInfo>());
                            var ret = RetainerData[retainerId];
                            if (ret.ContainsKey(ItemId))
                            {
                                var item = ret[ItemId];
                                item.ItemId = ItemId;
                                item.Quantity = GetRetainerInventoryItem(ItemId, retainerId);

                            }
                            else
                            {
                                ret.TryAdd(ItemId, new ItemInfo(ItemId, GetRetainerInventoryItem(ItemId, retainerId), GetRetainerInventoryItem(ItemId, retainerId, true)));
                            }
                        }
                    }
                }

                if (hqOnly)
                {
                    return (int)RetainerData.Values.SelectMany(x => x.Values).Where(x => x.ItemId == ItemId).Sum(x => x.HQQuantity);
                }

                return (int)RetainerData.SelectMany(x => x.Value).Where(x => x.Key == ItemId).Sum(x => x.Value.Quantity);
            }
            catch (Exception ex)
            {
                return 0;
            }
        }

        return 0;
    }

    private unsafe int GetItemCountTotal(uint itemId)
    {
        var countInRetainers = ATools?GetRetainerItemCount(itemId):0;
        var countInBag = InventoryManager.Instance()->GetInventoryItemCount(itemId);
        var countInSaddleBag = InventoryManager.Instance()->GetItemCountInContainer(itemId, InventoryType.SaddleBag1)
                               + InventoryManager.Instance()->GetItemCountInContainer(itemId, InventoryType.SaddleBag2);
        var countTotal = countInRetainers + countInBag + countInSaddleBag;
        return countTotal;
    }

    public InventoryWindow(Plugin plugin)
        : base("InventoryWindow", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 330),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        Plugin = plugin;
    }

    public void Dispose()
    {
        Initialized?.Unsubscribe(SetupIPC);
        DalamudApi.ClientState.Logout -= LogoutCacheClear;
        Initialized = null;
        IsInitialized = null;
        _OnRetainerChanged = null;
        _OnItemAdded = null;
        _OnItemRemoved = null;
        ItemCount = null;
    }

    static List<uint> GetListIntInRange(int from, int count)
    {
        var numbers = new List<uint>();
        for (uint i = 0; i < count; i++)
        {
            numbers.Add((uint)from + i);
        }

        return numbers;
    }

    private static readonly List<List<uint>> ZodiacWeaponId = [];

    private static readonly List<List<uint>> AnimaWeaponId =
    [
        GetListIntInRange(13611, 13),//魂武一阶段，元灵武器·元灵
        GetListIntInRange(13597, 13),//魂武二阶段，元灵武器·觉醒
        GetListIntInRange(13223, 13),//魂武三阶段，新元灵武器
        GetListIntInRange(14870, 13),//魂武四阶段，元灵武器·超导
        GetListIntInRange(15223, 13),//魂武五阶段，百炼成钢的元灵武器
        GetListIntInRange(15237, 13),//魂武六阶段，元灵武器·灵慧
        GetListIntInRange(15251, 13),//魂武七阶段，真元灵武器
        GetListIntInRange(16050, 13),//魂武八阶段，真元灵武器·灵光
    ];

    private static readonly List<List<uint>> EurekaWeaponId =
    [
        GetListIntInRange(21942, 15),//禁地兵装
        GetListIntInRange(21958, 15),//禁地兵装+1
        GetListIntInRange(21974, 15),//禁地兵装+2
        GetListIntInRange(21990, 15),//常风
        GetListIntInRange(22925, 15),//恒冰
        GetListIntInRange(22941, 15),//恒冰+1
        GetListIntInRange(22957, 15),//元素
        GetListIntInRange(24039, 15),//元素+1
        GetListIntInRange(24055, 15),//元素+2
        GetListIntInRange(24071, 15),//涌火
        GetListIntInRange(24643, 15),//丰水
        GetListIntInRange(24659, 15),//丰水+1
        GetListIntInRange(24675, 15),//新兵装
        GetListIntInRange(24691, 15),//优雷卡
        GetListIntInRange(24707, 15),//优雷卡·改
    ];

    private static readonly List<List<uint>> BozjaWeaponId =
    [
        GetListIntInRange(30228, 17),//义军武器
        GetListIntInRange(30767, 17),//改良型义军武器
        GetListIntInRange(30785, 17),//回忆
        GetListIntInRange(32651, 17),//裁决
        GetListIntInRange(32669, 17),//改良型裁决
        GetListIntInRange(33462, 17),//女王武器
    ];

    private static readonly List<List<uint>> MandervillousWeaponId =
    [
        GetListIntInRange(38400, 19), //曼德维尔武器
        GetListIntInRange(39144, 19), //曼德维尔武器·惊异
        GetListIntInRange(39920, 19), //曼德维尔武器·威严
        GetListIntInRange(40932, 19), //曼德维尔武器·盈满
    ];

    private static readonly List<uint> AnimaWeaponJobIdList = new()
    {
        19, 21, 32,
        24, 28, 33,
        20, 22, 30,
        23, 31,
        25, 27
    };

    private static readonly List<uint> ZodiacWeaponJobIdList = new()
    {
        19, 21,
        24, 28,
        20, 22, 30,
        23,
        25, 27
    };

    private static readonly List<uint> EurekaWeaponJobIdList = new()
    {
        19, 21, 32,
        24, 28, 33,
        20, 22, 34, 30,
        23, 31,
        25, 27, 35
    };

    private static readonly List<uint> BozjaWeaponJobIdList = new()
    {
        19, 21, 32, 37,
        24, 28, 33,
        20, 22, 34, 30,
        23, 31, 38,
        25, 27, 35
    };

    private static readonly List<uint> MandervillousWeaponJobIdList = new()
    {
        19, 21, 32, 37,
        24, 28, 33, 40,
        20, 22, 39, 34, 30,
        23, 31, 38,
        25, 27, 35,
    };

    private static readonly Dictionary<int, int> JobsOfSpecialWeapon = new()
    {
        {1,10},//古武
        {2,13},//魂武
        {3,15},//优武
        {4,17},//义武
        {5,19},//曼武
    };

    private Dictionary<uint, List<int>> zodiacWeaponProcess = new();
    private Dictionary<uint, List<int>> animaWeaponProcess = new();
    private Dictionary<uint, List<int>> eurekaWeaponProcess = new();
    private Dictionary<uint, List<int>> bozjaWeaponProcess = new();
    private Dictionary<uint, List<int>> mandervillousWeaponProcess = new();




    private readonly string[] specialWeaponSeriesList =
    [
        "未选中","古武","魂武","优武","义武","曼武"
    ];

    private int selectedWeaponSeriesIndex = 0;

    public void InitChart()
    {
        //横坐标是jobId，纵坐标是阶段
        //古武
        for (var i = 0; i < JobsOfSpecialWeapon[1]; i++)
        {
            zodiacWeaponProcess.Add(ZodiacWeaponJobIdList[i], new List<int>(new int[ZodiacWeaponId.Count]));
        }
        //魂武
        for (var i = 0; i < JobsOfSpecialWeapon[2]; i++)
        {
            animaWeaponProcess.Add(AnimaWeaponJobIdList[i], new List<int>(new int[AnimaWeaponId.Count]));
        }
        //优武
        for (var i = 0; i < JobsOfSpecialWeapon[3]; i++)
        {
            eurekaWeaponProcess.Add(EurekaWeaponJobIdList[i], new List<int>(new int[EurekaWeaponId.Count]));
        }
        //义武
        for (var i = 0; i < JobsOfSpecialWeapon[4]; i++)
        {
            bozjaWeaponProcess.Add(BozjaWeaponJobIdList[i], new List<int>(new int[BozjaWeaponId.Count]));
        }
        //曼武
        for (var i = 0; i < JobsOfSpecialWeapon[5]; i++)
        {
            mandervillousWeaponProcess.Add(MandervillousWeaponJobIdList[i], new List<int>(new int[MandervillousWeaponId.Count]));
        }
    }

    public override void Draw()
    {
        var localPlayer = DalamudApi.ClientState.LocalPlayer;
        if (localPlayer is null)
        {
            ImGui.Text("未获取到角色信息");
            return;
        }
        var playerJobId = localPlayer.ClassJob.Id;
        ImGui.Combo("武器系列##选武器", ref selectedWeaponSeriesIndex, specialWeaponSeriesList, 6);
        if (selectedWeaponSeriesIndex != 0)
        {
            switch (selectedWeaponSeriesIndex)
            {
                case 1:
                    {
                        GetZodiacWeaponData();
                        DrawZodiac();
                        break;
                    }
                case 2:
                    {
                        GetAnimaWeaponData();
                        DrawAnima();
                        break;
                    }
                case 3:
                    {
                        GetEurekaWeaponData();
                        DrawEureka();
                        break;
                    }
                case 4:
                    {
                        GetBozjaWeaponData();
                        DrawBozja();
                        break;
                    }
                case 5:
                    {
                        GetMandervillousWeaponData();
                        DrawMandervillous();
                        break;
                    }
            }
        }
    }

    private void AddOneToTheFollowingIndex(List<int> array, int currentIndex)
    {
        for (var i = currentIndex + 1; i < array.Count; i++)
        {
            array[i] += 1;
        }
    }

    private void GetZodiacWeaponData()
    {
        for (var i = 0; i < JobsOfSpecialWeapon[1]; i++)//Job Index
        {
            for (var j = 0; j < ZodiacWeaponId.Count; j++)//阶段
            {
                var curWeaponId = ZodiacWeaponId[j][i];
                var curJobId = ZodiacWeaponJobIdList[i];
                var curWeaponCount = GetItemCountTotal(curWeaponId);
                zodiacWeaponProcess[curJobId][j] = curWeaponCount;
            }
        }
    }

    private void GetAnimaWeaponData()
    {
        for (var i = 0; i < JobsOfSpecialWeapon[2]; i++)//Job Index
        {
            for (var j = 0; j < AnimaWeaponId.Count; j++)//阶段
            {
                var curWeaponId = AnimaWeaponId[j][i];
                var curJobId = AnimaWeaponJobIdList[i];
                var curWeaponCount = GetItemCountTotal(curWeaponId);
                animaWeaponProcess[curJobId][j] = curWeaponCount;
            }
        }
    }

    private void GetEurekaWeaponData()
    {
        for (var i = 0; i < JobsOfSpecialWeapon[3]; i++)//Job Index
        {
            for (var j = 0; j < EurekaWeaponId.Count; j++) //阶段
            {
                var curWeaponId = EurekaWeaponId[j][i];
                var curJobId = EurekaWeaponJobIdList[i];
                var curWeaponCount = GetItemCountTotal(curWeaponId);
                eurekaWeaponProcess[curJobId][j] = curWeaponCount;
            }
        }
    }

    private void GetBozjaWeaponData()
    {
        for (var i = 0; i < JobsOfSpecialWeapon[4]; i++)//Job Index
        {
            for (var j = 0; j < BozjaWeaponId.Count; j++)//阶段
            {
                var curWeaponId = BozjaWeaponId[j][i];
                var curJobId = BozjaWeaponJobIdList[i];
                var curWeaponCount = GetItemCountTotal(curWeaponId);
                bozjaWeaponProcess[curJobId][j] = curWeaponCount;
            }
        }
    }

    private void GetMandervillousWeaponData()
    {
        for (var i = 0; i < JobsOfSpecialWeapon[5]; i++)//Job Index
        {
            for (var j = 0; j < MandervillousWeaponId.Count; j++)//阶段
            {
                var curWeaponId = MandervillousWeaponId[j][i];
                var curJobId = MandervillousWeaponJobIdList[i];
                var curWeaponCount = GetItemCountTotal(curWeaponId);
                mandervillousWeaponProcess[curJobId][j] = curWeaponCount;
            }
        }
    }


    private string ComputeNeedsZodiac()
    {
        Dictionary<uint, List<int>> zodiacWeaponNeed = new();
        for (var i = 0; i < JobsOfSpecialWeapon[1]; i++)
        {
            zodiacWeaponNeed.Add(ZodiacWeaponJobIdList[i], new List<int>(new int[ZodiacWeaponId.Count]));
        }
        return "";
    }

    private string ComputeNeedsAnima()
    {
        Dictionary<uint, List<int>> animaWeaponNeed = new();
        for (var i = 0; i < JobsOfSpecialWeapon[2]; i++)
        {
            animaWeaponNeed.Add(AnimaWeaponJobIdList[i], new List<int>(new int[AnimaWeaponId.Count]));
        }

        for (var i = 0; i < JobsOfSpecialWeapon[2]; i++)//Job Index
        {
            for (var j = 0; j < AnimaWeaponId.Count; j++)//阶段
            {
                var curWeaponId = AnimaWeaponId[j][i];
                var curJobId = AnimaWeaponJobIdList[i];
                var curWeaponCount = GetItemCountTotal(curWeaponId);
                if (curWeaponCount > 0)
                {
                    AddOneToTheFollowingIndex(animaWeaponNeed[curJobId], j);
                }
            }
        }
        return "";
    }

    private string ComputeNeedsEureka()
    {
        Dictionary<uint, List<int>> eurekaWeaponNeed = new();
        for (var i = 0; i < JobsOfSpecialWeapon[3]; i++)
        {
            eurekaWeaponNeed.Add(EurekaWeaponJobIdList[i], new List<int>(new int[EurekaWeaponId.Count]));
        }

        for (var i = 0; i < JobsOfSpecialWeapon[3]; i++)//Job Index
        {
            for (var j = 0; j < EurekaWeaponId.Count; j++)//阶段
            {
                var curWeaponId = EurekaWeaponId[j][i];
                var curJobId = EurekaWeaponJobIdList[i];
                var curWeaponCount = GetItemCountTotal(curWeaponId);
                if (curWeaponCount > 0)
                {
                    AddOneToTheFollowingIndex(eurekaWeaponNeed[curJobId], j);
                }
            }
        }
        return "";
    }

    private string ComputeNeedsBozja()
    {
        Dictionary<uint, List<int>> bozjaWeaponNeed = new();
        for (var i = 0; i < JobsOfSpecialWeapon[4]; i++)
        {
            bozjaWeaponNeed.Add(BozjaWeaponJobIdList[i], new List<int>(new int[BozjaWeaponId.Count]));
        }

        for (var i = 0; i < JobsOfSpecialWeapon[4]; i++)//Job Index
        {
            for (var j = 0; j < BozjaWeaponId.Count; j++)//阶段
            {
                var curWeaponId = BozjaWeaponId[j][i];
                var curJobId = BozjaWeaponJobIdList[i];
                var curWeaponCount = GetItemCountTotal(curWeaponId);
                if (curWeaponCount > 0)
                {
                    AddOneToTheFollowingIndex(bozjaWeaponNeed[curJobId], j);
                }
            }
        }

        List<int> have = [];
        List<int> needs = [0, 0, 0, 0, 0, 0];
        foreach (var jobId in BozjaWeaponJobIdList)
        {
            for (var i = 0; i < BozjaWeaponId.Count; i++)
            {
                needs[i] += bozjaWeaponNeed[jobId][i];
            }
        }
        needs[0] *= 4;
        needs[1] *= 20;
        needs[2] *= 6;
        needs[3] *= 15;
        needs[4] *= 15;
        needs[5] *= 15;
        List<int> newNeeds = [needs[0], needs[1], needs[1], needs[1], needs[2], needs[3], needs[4], needs[5]];
        List<uint> needItemId = [30273, 31573, 31574, 31575, 31576, 32956, 32959, 33767];
        needs = newNeeds;
        foreach (var id in needItemId)
        {
            have.Add(GetItemCountTotal(id));
        }

        var res = "需要";
        for (var i = 0; i < needs.Count; i++)
        {
            if (needs[i] == 0) continue;
            res += $"{needs[i]}个{ItemSheet.GetRow(needItemId[i])?.Name.RawString}, ";
        }
        res += "\n仍需";
        for (var i = 0; i < needs.Count; i++)
        {
            if (needs[i] == 0) continue;
            res += $"{needs[i] - have[i]}个{ItemSheet.GetRow(needItemId[i])?.Name.RawString}, ";
        }
        return res;
    }

    private string ComputeNeedsMandervillous()
    {
        Dictionary<uint, List<int>> mandervillousWeaponNeed = new();
        for (var i = 0; i < JobsOfSpecialWeapon[5]; i++)
        {
            mandervillousWeaponNeed.Add(MandervillousWeaponJobIdList[i], new List<int>(new int[MandervillousWeaponId.Count]));
        }

        for (var i = 0; i < JobsOfSpecialWeapon[5]; i++)//Job Index
        {
            for (var j = 0; j < MandervillousWeaponId.Count; j++)//阶段
            {
                var curWeaponId = MandervillousWeaponId[j][i];
                var curJobId = MandervillousWeaponJobIdList[i];
                var curWeaponCount = GetItemCountTotal(curWeaponId);
                if (curWeaponCount > 0)
                {
                    AddOneToTheFollowingIndex(mandervillousWeaponNeed[curJobId], j);
                }
            }
        }

        List<int> have = [GetItemCountTotal(38420), GetItemCountTotal(38940), GetItemCountTotal(40322), GetItemCountTotal(41032)];
        List<int> needs = [0, 0, 0, 0];
        foreach (var jobId in MandervillousWeaponJobIdList)
        {
            for (var i = 0; i < MandervillousWeaponId.Count; i++)
            {
                needs[i] += 3 * mandervillousWeaponNeed[jobId][i];
            }
        }
        var res = $"需要: {needs[0]}个稀少陨石, {needs[1]}个稀少球粒陨石, {needs[2]}个稀少无球粒陨石, {needs[3]}个雏晶\n" +
                  $"仍需: {needs[0] - have[0]}个稀少陨石, {needs[1] - have[1]}个稀少球粒陨石, {needs[2] - have[2]}个稀少无球粒陨石, {needs[3] - have[3]}个雏晶\n" +
                  $"共计: {(needs[0] + needs[1] + needs[2] + needs[3]) * 500}诗学神典石";
        return res;
    }

    private void DrawZodiac()
    {
        ImGui.Text($"太复杂了不做");
    }

    private void DrawAnima()
    {
        ImGui.BeginTable("AnimaWeaponChart", AnimaWeaponId.Count + 1, ImGuiTableFlags.Resizable);
        ImGui.TableSetupColumn("职业", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("元灵武器·元灵", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("元灵武器·觉醒", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("新元灵武器", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("元灵武器·超导", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("百炼成钢的元灵武器", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("元灵武器·灵慧", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("真元灵武器", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("真元灵武器·灵光", ImGuiTableColumnFlags.None);
        ImGui.TableHeadersRow();
        foreach (var jobId in AnimaWeaponJobIdList)
        {
            var line = animaWeaponProcess[jobId];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(ClassJobSheet.GetRow(jobId)?.Name.RawString);

            for (var j = 0; j < line.Count; j++)
            {
                Vector4 color = line[j] > 0 ? new(0, 255, 0, 255) : new(255, 0, 0, 255);
                ImGui.TableNextColumn();
                ImGui.TextColored(color, $"{line[j]}");
            }
        }
        ImGui.EndTable();
    }


    private void DrawEureka()
    {
        ImGui.BeginTable("EurekaWeaponChart", EurekaWeaponId.Count + 1, ImGuiTableFlags.Resizable);
        ImGui.TableSetupColumn("职业", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("禁地兵装", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("禁地兵装+1", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("禁地兵装+2", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("常风", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("恒冰", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("恒冰+1", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("元素", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("元素+1", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("元素+2", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("涌火", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("丰水", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("丰水+1", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("新兵装", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("优雷卡", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("优雷卡·改", ImGuiTableColumnFlags.None);
        ImGui.TableHeadersRow();
        foreach (var jobId in EurekaWeaponJobIdList)
        {
            var line = eurekaWeaponProcess[jobId];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(ClassJobSheet.GetRow(jobId)?.Name.RawString);

            for (var j = 0; j < line.Count; j++)
            {
                Vector4 color = line[j] > 0 ? new(0, 255, 0, 255) : new(255, 0, 0, 255);
                ImGui.TableNextColumn();
                ImGui.TextColored(color, $"{line[j]}");
            }
        }
        ImGui.EndTable();
    }

    private void DrawBozja()
    {
        ImGui.Text($"{ComputeNeedsBozja()}");
        ImGui.BeginTable("BozjaWeaponChart", BozjaWeaponId.Count + 1, ImGuiTableFlags.Resizable);
        ImGui.TableSetupColumn("职业", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("义军武器", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("改良型义军武器", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("回忆", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("裁决", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("改良型裁决", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("女王武器", ImGuiTableColumnFlags.None);
        ImGui.TableHeadersRow();
        foreach (var jobId in AnimaWeaponJobIdList)
        {
            var line = bozjaWeaponProcess[jobId];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(ClassJobSheet.GetRow(jobId)?.Name.RawString);

            for (var j = 0; j < line.Count; j++)
            {
                Vector4 color = line[j] > 0 ? new(0, 255, 0, 255) : new(255, 0, 0, 255);
                ImGui.TableNextColumn();
                ImGui.TextColored(color, $"{line[j]}");
            }
        }
        ImGui.EndTable();
    }

    private void DrawMandervillous()
    {
        ImGui.Text($"{ComputeNeedsMandervillous()}");
        ImGui.BeginTable("MandervillousWeaponChart", MandervillousWeaponId.Count + 1, ImGuiTableFlags.Resizable);
        ImGui.TableSetupColumn("职业", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("曼德维尔武器", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("曼德维尔武器·惊异", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("曼德维尔武器·威严", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("曼德维尔武器·盈满", ImGuiTableColumnFlags.None);
        ImGui.TableHeadersRow();
        foreach (var jobId in MandervillousWeaponJobIdList)
        {
            var line = mandervillousWeaponProcess[jobId];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(ClassJobSheet.GetRow(jobId)?.Name.RawString);

            for (var j = 0; j < line.Count; j++)
            {
                Vector4 color = line[j] > 0 ? new(0, 255, 0, 255) : new(255, 0, 0, 255);
                ImGui.TableNextColumn();
                ImGui.TextColored(color, $"{line[j]}");
            }
        }
        ImGui.EndTable();
    }
}
