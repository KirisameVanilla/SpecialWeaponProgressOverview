using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Ipc;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Bindings.ImGui;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace SpecialWeaponProgressOverview;

public class InventoryWindow : Window, IDisposable
{
    private Plugin plugin;
    
    private static ICallGateSubscriber<uint, ulong, uint, uint>? ItemCount;
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

    private static void LogoutCacheClear(int a, int b)
    {
        RetainerData.Clear();
    }

    private static void SetupIPC(bool obj)
    {
        
        ItemCount = DalamudApi.PluginInterface.GetIpcSubscriber<uint, ulong, uint, uint>("AllaganTools.ItemCount");
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

    public static uint GetRetainerInventoryItem(uint itemId, ulong retainerId, bool hqonly = false)
    {
        if (ATools)
        {
            return ItemCount.InvokeFunc(itemId, retainerId, 10000) +
                   ItemCount.InvokeFunc(itemId, retainerId, 10001) +
                   ItemCount.InvokeFunc(itemId, retainerId, 10002) +
                   ItemCount.InvokeFunc(itemId, retainerId, 10003) +
                   ItemCount.InvokeFunc(itemId, retainerId, 10004) +
                   ItemCount.InvokeFunc(itemId, retainerId, 10005) +
                   ItemCount.InvokeFunc(itemId, retainerId, 10006) +
                   ItemCount.InvokeFunc(itemId, retainerId, (uint)InventoryType.RetainerCrystals);
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
                return (int)RetainerData.SelectMany(x => x.Value).Where(x => x.Key == ItemId).Sum(x => x.Value.Quantity);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        return 0;
    }

    private unsafe int GetItemCountTotal(uint itemId)
    {
        var countInRetainers = GetRetainerItemCount(itemId);
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

        this.plugin = plugin;
    }

    public void Dispose()
    {
        Initialized?.Unsubscribe(SetupIPC);
        DalamudApi.ClientState.Logout -= LogoutCacheClear;
        Initialized = null;
        IsInitialized = null;
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

   private static readonly List<List<uint>> ZodiacWeaponId = new List<List<uint>>();
   

    private static readonly List<List<uint>> AnimaWeaponId = new List<List<uint>>
    {
        
        GetListIntInRange(13611, 13),//魂武一阶段，元灵武器·元灵
        GetListIntInRange(13597, 13),//魂武二阶段，元灵武器·觉醒
        GetListIntInRange(13223, 13),//魂武三阶段，新元灵武器
        GetListIntInRange(14870, 13),//魂武四阶段，元灵武器·超导
        GetListIntInRange(15223, 13),//魂武五阶段，百炼成钢的元灵武器
        GetListIntInRange(15237, 13),//魂武六阶段，元灵武器·灵慧
        GetListIntInRange(15251, 13),//魂武七阶段，真元灵武器
        GetListIntInRange(16050, 13),//魂武八阶段，真元灵武器·灵光
    };

    private static readonly List<List<uint>> EurekaWeaponId = new List<List<uint>>
    {
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
    };

    private static readonly List<List<uint>> BozjaWeaponId = new List<List<uint>>
    {
        GetListIntInRange(30228, 17),//义军武器
        GetListIntInRange(30767, 17),//改良型义军武器
        GetListIntInRange(30785, 17),//回忆
        GetListIntInRange(32651, 17),//裁决
        GetListIntInRange(32669, 17),//改良型裁决
        GetListIntInRange(33462, 17),//女王武器
    };

    private static readonly List<List<uint>> MandervillousWeaponId = new List<List<uint>>
    {
        GetListIntInRange(38400, 19), //曼德维尔武器
        GetListIntInRange(39144, 19), //曼德维尔武器·惊异
        GetListIntInRange(39920, 19), //曼德维尔武器·威严
        GetListIntInRange(40932, 19), //曼德维尔武器·盈满
    };

    private static readonly List<List<uint>> PhantomWeaponId = new List<List<uint>>
    {
        GetListIntInRange(47869, 21), //幻境武器·半影
        GetListIntInRange(47006, 21), //幻境武器·本影
    };

    private static readonly List<List<uint>> SkysteelWeaponId = new List<List<uint>>
    {
        GetListIntInRange(29612, 11), //天钢工具
        GetListIntInRange(29623, 11), //天钢工具+1
        GetListIntInRange(29634, 13), //龙诗工具
        GetListIntInRange(30282, 13), //改良型龙诗工具
        GetListIntInRange(30293, 13), //天诗工具
        GetListIntInRange(31714, 13), //天工工具
    };

    private static readonly List<List<uint>> SplendorousWeaponId = new List<List<uint>>
    {
        GetListIntInRange(38715, 11), //卓越
        GetListIntInRange(38726, 11), //改良型卓越
        GetListIntInRange(38737, 11), //水晶
        GetListIntInRange(39732, 11), //乔菈水晶
        GetListIntInRange(39743, 11), //乔菈卓绝
        GetListIntInRange(41180, 11), //诺弗兰特远见
        GetListIntInRange(41191, 11), //领航星
    };

    
    // Job ID
    
    // 19骑士 21战士 32黑骑 37绝枪
    // 24白魔 28学者 33占星 40贤者
    // 20武僧 22龙骑 30忍者 34武士 39镰刀 41蝰蛇
    // 23诗人 31机工 38舞者
    // 25黑魔 27召唤 35赤魔 42画家
    // 8刻木 9锻铁 10铸甲 11雕金
    // 12制革 13裁缝 14炼金 15烹调
    // 16采矿 17园艺 18捕鱼
    
    
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

    private static readonly List<uint> PhantomWeaponJobIdList = new()
    {
        19, 21, 32, 37,
        24, 28, 33, 40,
        20, 22, 39, 34, 30, 41,
        23, 31, 38,
        25, 27, 35, 42,
    };
    
    private static readonly List<uint> SkysteelWeaponJobIdList = new()
    {
        8, 9, 10, 11,
        12, 13, 14, 15,
        16, 17, 18,
    };
    
    private static readonly List<uint> SplendorousWeaponJobIdList = new()
    {
        8, 9, 10, 11,
        12, 13, 14, 15,
        16, 17, 18,
    };

    private static readonly Dictionary<uint, int> JobIndex = new()
    {
        { 19, 0 }, { 21, 2 }, { 32, 6 }, { 37, 15 },
        { 24, 8 }, { 28, 11 }, { 33, 12 }, { 40, 17 },
        { 20, 1 }, { 22, 3 }, { 34, 13 }, { 39, 18 }, { 30, 5 },
        { 23, 4 }, { 31, 7 }, { 38, 16 },
        { 25, 9 }, { 27, 10 }, { 35, 14 },
    };

    private static readonly Dictionary<uint, int> PhantomJobIndex = new()
    {
        { 19, 0 }, { 21, 2 }, { 32, 10 }, { 37, 15 },
        { 24, 5 }, { 28, 8 }, { 33, 12 }, { 40, 18 },
        { 20, 1 }, { 22, 3 }, { 34, 13 }, { 39, 17 }, { 30, 9 }, { 41, 19 },
        { 23, 4 }, { 31, 11 }, { 38, 16 },
        { 25, 6 }, { 27, 7 }, { 35, 14 }, { 42, 20 }
    };
    
    private static readonly Dictionary<uint, int> LifeJobIndex = new()
    {
        { 8, 0 }, { 9, 1 }, { 10, 2 }, { 11, 3 },
        { 12, 4 }, { 13, 5 }, { 14, 6 }, { 15, 7 },
        { 16, 8 }, { 17, 9 }, { 18, 10 },
    };

    private static readonly Dictionary<int, int> JobsOfSpecialWeapon = new()
    {
        {1,10},//古武
        {2,13},//魂武
        {3,15},//优武
        {4,17},//义武
        {5,19},//曼武
        {6,21},//幻武
        {7,11},//天钢
        {8,11},//莫雯
    };

    private Dictionary<uint, List<int>> zodiacWeaponProcess = new();
    private Dictionary<uint, List<int>> animaWeaponProcess = new();
    private Dictionary<uint, List<int>> eurekaWeaponProcess = new();
    private Dictionary<uint, List<int>> bozjaWeaponProcess = new();
    private Dictionary<uint, List<int>> mandervillousWeaponProcess = new();
    private Dictionary<uint, List<int>> phantomWeaponProcess = new();
    private Dictionary<uint, List<int>> skysteelWeaponProcess = new();
    private Dictionary<uint, List<int>> splendorousWeaponProcess = new();




    private readonly string[] specialWeaponSeriesList =
    {
        "未选中","古武","魂武","优武","义武","曼武","幻武","天钢","莫雯"
    };

    private int selectedWeaponSeriesIndex;

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
        //幻武
        for (var i = 0; i < JobsOfSpecialWeapon[6]; i++)
        {
            phantomWeaponProcess.Add(PhantomWeaponJobIdList[i], new List<int>(new int[PhantomWeaponId.Count]));
        }
        //天钢
        for (var i = 0; i < JobsOfSpecialWeapon[7]; i++)
        {
            skysteelWeaponProcess.Add(SkysteelWeaponJobIdList[i], new List<int>(new int[SkysteelWeaponId.Count]));
        }
        //莫雯
        for (var i = 0; i < JobsOfSpecialWeapon[8]; i++)
        {
            splendorousWeaponProcess.Add(SplendorousWeaponJobIdList[i], new List<int>(new int[SplendorousWeaponId.Count]));
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
        // var playerJobId = localPlayer.ClassJob.RowId;
        ImGui.Text($"Is Allagan Tools available: {ATools}");
        ImGui.Text($"点一下数字能获取对应武器名字（然后打开item search可以查预览）");
        if(ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("item search没开ipc也没开指令交互");
        }
        ImGui.Combo("武器系列##选武器", ref selectedWeaponSeriesIndex, specialWeaponSeriesList, 6);
        if (selectedWeaponSeriesIndex != 0)
        {
            switch (selectedWeaponSeriesIndex)
            {
                case 1:
                    {
                        GetProcessData(1, ZodiacWeaponId, ZodiacWeaponJobIdList, ref zodiacWeaponProcess);
                        DrawZodiac();
                        break;
                    }
                case 2:
                    {
                        GetProcessData(2, AnimaWeaponId, AnimaWeaponJobIdList, ref animaWeaponProcess);
                        DrawAnima();
                        break;
                    }
                case 3:
                    {
                        GetProcessData(3, EurekaWeaponId, EurekaWeaponJobIdList, ref eurekaWeaponProcess);
                        DrawEureka();
                        break;
                    }
                case 4:
                    {
                        GetProcessData(4, BozjaWeaponId, BozjaWeaponJobIdList, ref bozjaWeaponProcess);
                        DrawBozja();
                        break;
                    }
                case 5:
                    {
                        GetProcessData(5, MandervillousWeaponId, MandervillousWeaponJobIdList, ref mandervillousWeaponProcess);
                        DrawMandervillous();
                        break;
                    }
                case 6:
                    {
                        GetProcessData(6, PhantomWeaponId, PhantomWeaponJobIdList, ref phantomWeaponProcess);
                        DrawPhantom();
                        break;
                    }
                case 7:
                    {
                        GetProcessData(7, SkysteelWeaponId, SkysteelWeaponJobIdList, ref skysteelWeaponProcess);
                        DrawSkysteel();
                        break;
                    }
                case 8:
                    {
                        GetProcessData(8, SplendorousWeaponId, SplendorousWeaponJobIdList, ref splendorousWeaponProcess);
                        DrawSplendorous();
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

    private void GetProcessData(int weaponIndex, List<List<uint>> weaponIdList, List<uint> jobIdList, ref Dictionary<uint, List<int>> weaponProcess)
    {
        for (var i = 0; i < JobsOfSpecialWeapon[weaponIndex]; i++)//Job Index
        {
            for (var j = 0; j < weaponIdList.Count; j++)//阶段
            {
                var curJobId = jobIdList[i];
                // 针对Phantom武器使用专用索引
                var jobIndex = weaponIndex == 6 ? PhantomJobIndex[curJobId] : 
                                   (weaponIndex is 7 or 8) ? LifeJobIndex[curJobId] :
                                   JobIndex[curJobId];
                var curWeaponId = weaponIdList[j][jobIndex];
                var curWeaponCount = GetItemCountTotal(curWeaponId);
                weaponProcess[curJobId][j] = curWeaponCount;
            }
        }
    }


    // private string ComputeNeedsZodiac()
    // {
    //     Dictionary<uint, List<int>> zodiacWeaponNeed = new();
    //     for (var i = 0; i < JobsOfSpecialWeapon[1]; i++)
    //     {
    //         zodiacWeaponNeed.Add(ZodiacWeaponJobIdList[i], new List<int>(new int[ZodiacWeaponId.Count]));
    //     }
    //     return "";
    // }

    // private string ComputeNeedsAnima()
    // {
    //     Dictionary<uint, List<int>> animaWeaponNeed = new();
    //     for (var i = 0; i < JobsOfSpecialWeapon[2]; i++)
    //     {
    //         animaWeaponNeed.Add(AnimaWeaponJobIdList[i], new List<int>(new int[AnimaWeaponId.Count]));
    //     }
    //
    //     for (var i = 0; i < JobsOfSpecialWeapon[2]; i++)//Job Index
    //     {
    //         for (var j = 0; j < AnimaWeaponId.Count; j++)//阶段
    //         {
    //             var curWeaponId = AnimaWeaponId[j][i];
    //             var curJobId = AnimaWeaponJobIdList[i];
    //             var curWeaponCount = GetItemCountTotal(curWeaponId);
    //             if (curWeaponCount > 0)
    //             {
    //                 AddOneToTheFollowingIndex(animaWeaponNeed[curJobId], j);
    //             }
    //         }
    //     }
    //     return "";
    // }

    // private string ComputeNeedsEureka()
    // {
    //     Dictionary<uint, List<int>> eurekaWeaponNeed = new();
    //     for (var i = 0; i < JobsOfSpecialWeapon[3]; i++)
    //     {
    //         eurekaWeaponNeed.Add(EurekaWeaponJobIdList[i], new List<int>(new int[EurekaWeaponId.Count]));
    //     }
    //
    //     for (var i = 0; i < JobsOfSpecialWeapon[3]; i++)//Job Index
    //     {
    //         for (var j = 0; j < EurekaWeaponId.Count; j++)//阶段
    //         {
    //             var curWeaponId = EurekaWeaponId[j][i];
    //             var curJobId = EurekaWeaponJobIdList[i];
    //             var curWeaponCount = GetItemCountTotal(curWeaponId);
    //             if (curWeaponCount > 0)
    //             {
    //                 AddOneToTheFollowingIndex(eurekaWeaponNeed[curJobId], j);
    //             }
    //         }
    //     }
    //     return "";
    // }

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

        List<int> have = new List<int>();
        List<int> needs = new List<int> { 0, 0, 0, 0, 0, 0 };
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
        List<int> newNeeds = new List<int> {needs[0], needs[1], needs[1], needs[1], needs[2], needs[3], needs[4], needs[5]};
        List<uint> needItemId = new List<uint> {30273, 31573, 31574, 31575, 31576, 32956, 32959, 33767};
        needs = newNeeds;
        foreach (var id in needItemId)
        {
            have.Add(GetItemCountTotal(id));
        }

        var res = "需要";
        for (var i = 0; i < needs.Count; i++)
        {
            if (needs[i] == 0) continue;
            res += $"{needs[i]}个{ItemSheet.GetRow(needItemId[i]).Name.ExtractText()}, ";
        }
        res += "\n仍需";
        for (var i = 0; i < needs.Count; i++)
        {
            if (needs[i] == 0) continue;
            res += $"{needs[i] - have[i]}个{ItemSheet.GetRow(needItemId[i]).Name.ExtractText()}, ";
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

        List<int> have = new List<int>{GetItemCountTotal(38420), GetItemCountTotal(38940), GetItemCountTotal(40322), GetItemCountTotal(41032)};
        List<int> needs = new List<int> { 0, 0, 0, 0 };
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
    
    private string ComputeNeedsPhantom()
    {
        List<int> have = new List<int> { GetItemCountTotal(47750), GetItemCountTotal(46850) };
        List<int> needs = new List<int> { 0, 0 };
        foreach (var jobId in PhantomWeaponJobIdList)
        {
            var process = phantomWeaponProcess[jobId];
            bool process1 = process[1] > 0;
            bool process0 = process[0] > 0;
            if (!process1 && !process0)
            {
                // 半影未拥有且本影未拥有，需材料
                needs[0] += 3;
            }
            if (!process1)
            {
                // 本影未拥有，需材料
                needs[1] += 3;
            }
            // 如果本影已拥有，则不计半影材料
        }
        
        var res = $"需要: {needs[0]}个新月矿石, {needs[1]}个上弦月矿石\n" +
                  $"仍需: {needs[0] - have[0]}个新月矿石, {needs[1] - have[1]}个上弦月矿石\n" + 
                  $"共计: {(needs[0] + needs[1] - have[0] - have[1]) * 500}天道神典石";
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
            ImGui.Text(ClassJobSheet.GetRow(jobId).Name.ExtractText());

            for (var j = 0; j < line.Count; j++)
            {
                Vector4 color = line[j] > 0 ? new(0, 255, 0, 255) : new(255, 0, 0, 255);
                ImGui.TableNextColumn();
                ImGui.TextColored(color, $"{line[j]}");
                if (ImGui.IsItemClicked())
                {
                    ImGui.SetClipboardText($"{ItemSheet.GetRow(AnimaWeaponId[j][JobIndex[jobId]]).Name.ExtractText()}");
                    DalamudApi.ChatGui.Print($"{ItemSheet.GetRow(AnimaWeaponId[j][JobIndex[jobId]]).Name.ExtractText()} 已复制到剪贴板");
                }
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
            ImGui.Text(ClassJobSheet.GetRow(jobId).Name.ExtractText());

            for (var j = 0; j < line.Count; j++)
            {
                Vector4 color = line[j] > 0 ? new(0, 255, 0, 255) : new(255, 0, 0, 255);
                ImGui.TableNextColumn();
                ImGui.TextColored(color, $"{line[j]}");
                if (ImGui.IsItemClicked())
                {
                    ImGui.SetClipboardText($"{ItemSheet.GetRow(EurekaWeaponId[j][JobIndex[jobId]]).Name.ExtractText()}");
                    DalamudApi.ChatGui.Print($"{ItemSheet.GetRow(EurekaWeaponId[j][JobIndex[jobId]]).Name.ExtractText()} 已复制到剪贴板");
                }
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
        foreach (var jobId in BozjaWeaponJobIdList)
        {
            var line = bozjaWeaponProcess[jobId];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(ClassJobSheet.GetRow(jobId).Name.ExtractText());

            for (var j = 0; j < line.Count; j++)
            {
                Vector4 color = line[j] > 0 ? new(0, 255, 0, 255) : new(255, 0, 0, 255);
                ImGui.TableNextColumn();
                ImGui.TextColored(color, $"{line[j]}");
                if (ImGui.IsItemClicked())
                {
                    ImGui.SetClipboardText($"{ItemSheet.GetRow(BozjaWeaponId[j][JobIndex[jobId]]).Name.ExtractText()}");
                    DalamudApi.ChatGui.Print($"{ItemSheet.GetRow(BozjaWeaponId[j][JobIndex[jobId]]).Name.ExtractText()} 已复制到剪贴板");
                }
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
            ImGui.Text(ClassJobSheet.GetRow(jobId).Name.ExtractText());

            for (var j = 0; j < line.Count; j++)
            {
                Vector4 color = line[j] > 0 ? new(0, 255, 0, 255) : new(255, 0, 0, 255);
                ImGui.TableNextColumn();
                ImGui.TextColored(color, $"{line[j]}");
                if (ImGui.IsItemClicked())
                {
                    ImGui.SetClipboardText($"{ItemSheet.GetRow(MandervillousWeaponId[j][JobIndex[jobId]]).Name.ExtractText()}");
                    DalamudApi.ChatGui.Print($"{ItemSheet.GetRow(MandervillousWeaponId[j][JobIndex[jobId]]).Name.ExtractText()} 已复制到剪贴板");
                }
            }
        }
        ImGui.EndTable();
    }
    
    private void DrawPhantom()
    { 
        ImGui.Text($"{ComputeNeedsPhantom()}");
        ImGui.BeginTable("PhantomWeaponChart", PhantomWeaponId.Count + 1, ImGuiTableFlags.Resizable);
        ImGui.TableSetupColumn("职业", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn(label:"幻境武器·半影", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn(label:"幻境武器·本影", ImGuiTableColumnFlags.None);
        ImGui.TableHeadersRow();
        foreach (var jobId in PhantomWeaponJobIdList)
        { 
            var line = phantomWeaponProcess[jobId];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(ClassJobSheet.GetRow(jobId).Name.ExtractText());
            
            for (var j = 0; j < line.Count; j++)
            {
                Vector4 color = line[j] > 0 ? new(0, 255, 0, 255) : new(255, 0, 0, 255);
                ImGui.TableNextColumn();
                ImGui.TextColored(color, $"{line[j]}");
                if (ImGui.IsItemClicked())
                {
                    ImGui.SetClipboardText($"{ItemSheet.GetRow(PhantomWeaponId[j][PhantomJobIndex[jobId]]).Name.ExtractText()}");
                    DalamudApi.ChatGui.Print($"{ItemSheet.GetRow(PhantomWeaponId[j][PhantomJobIndex[jobId]]).Name.ExtractText()} 已复制到剪贴板");
                }
            }
        }
        ImGui.EndTable();
    }

    private void DrawSkysteel()
    {
        ImGui.Text("");
        ImGui.BeginTable("SkysteelWeaponChart", SkysteelWeaponId.Count + 1, ImGuiTableFlags.Resizable);
        ImGui.TableSetupColumn("职业", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("天钢工具", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("天钢工具+1", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("龙诗工具", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("改良型龙诗工具", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("天诗工具", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("天工工具", ImGuiTableColumnFlags.None);
        ImGui.TableHeadersRow();
        foreach (var jobId in SkysteelWeaponJobIdList)
        { 
            var line = skysteelWeaponProcess[jobId];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(ClassJobSheet.GetRow(jobId).Name.ExtractText());

            for (var j = 0; j < line.Count; j++)
            {
                Vector4 color = line[j] > 0 ? new(0, 255, 0, 255) : new(255, 0, 0, 255);
                ImGui.TableNextColumn();
                ImGui.TextColored(color, $"{line[j]}");
                if (ImGui.IsItemClicked())
                {
                    ImGui.SetClipboardText($"{ItemSheet.GetRow(SkysteelWeaponId[j][JobIndex[jobId]]).Name.ExtractText()}");
                    DalamudApi.ChatGui.Print($"{ItemSheet.GetRow(SkysteelWeaponId[j][JobIndex[jobId]]).Name.ExtractText()} 已复制到剪贴板");
                }
            }
        }
        ImGui.EndTable();
    }
    
    private void DrawSplendorous()
    { 
        ImGui.Text("");
        ImGui.BeginTable("SplendorousWeaponChart", SplendorousWeaponId.Count + 1, ImGuiTableFlags.Resizable);
        ImGui.TableSetupColumn("职业", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("卓越工具", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("改良型卓越工具", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("水晶工具", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("乔菈水晶工具", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("乔菈卓绝工具", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("诺弗兰特远见工具", ImGuiTableColumnFlags.None);
        ImGui.TableSetupColumn("领航星工具", ImGuiTableColumnFlags.None);
        ImGui.TableHeadersRow();
        foreach (var jobId in SplendorousWeaponJobIdList)
        { 
            var line = splendorousWeaponProcess[jobId];
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(ClassJobSheet.GetRow(jobId).Name.ExtractText());
            
            for (var j = 0; j < line.Count; j++)
            {
                Vector4 color = line[j] > 0 ? new(0, 255, 0, 255) : new(255, 0, 0, 255);
                ImGui.TableNextColumn();
                ImGui.TextColored(color, $"{line[j]}");
                if (ImGui.IsItemClicked())
                {
                    ImGui.SetClipboardText($"{ItemSheet.GetRow(SplendorousWeaponId[j][JobIndex[jobId]]).Name.ExtractText()}");
                    DalamudApi.ChatGui.Print($"{ItemSheet.GetRow(SplendorousWeaponId[j][JobIndex[jobId]]).Name.ExtractText()} 已复制到剪贴板");
                }
            }
        }
        ImGui.EndTable();
    }
}
