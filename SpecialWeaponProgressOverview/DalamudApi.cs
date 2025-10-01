using System;
using Dalamud.Game;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace SpecialWeaponProgressOverview;

public class DalamudApi
{
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; set; } = null!;

    [PluginService] public static IDataManager DataManager { get; set; } = null!;

    [PluginService] public static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] public static IChatGui ChatGui { get; set; } = null!;
    [PluginService] public static IPluginLog PluginLog { get; set; } = null!;
    [PluginService] public static IClientState ClientState { get; set; } = null!;
    [PluginService] public static IObjectTable ObjectTable { get; set; } = null!;
    [PluginService] public static ISigScanner SigScanner { get; set; } = null!;
    [PluginService] public static IKeyState KeyState { get; set; } = null!;
    [PluginService] public static ICondition Condition { get; set; } = null!;
    public static Configuration Config = new();


    internal static bool IsInitialized;
    public static void Init(IDalamudPluginInterface pi)
    {
        if (IsInitialized)
        {
            PluginLog.Info("Services already initialized, skipping");
        }
        IsInitialized = true;
        try
        {
            pi.Create<DalamudApi>();
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Error initialising {nameof(DalamudApi)}", ex);
        }
    }
}
