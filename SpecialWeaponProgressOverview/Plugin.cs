using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;

namespace SpecialWeaponProgressOverview;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/pover";

    public readonly WindowSystem WindowSystem = new("SpecialWeaponProgressOverview");
    private InventoryWindow InventoryWindow { get; init; }
    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        InventoryWindow = new InventoryWindow(this);
        DalamudApi.Init(pluginInterface);
        WindowSystem.AddWindow(InventoryWindow);

        DalamudApi.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open main page"
        });

        pluginInterface.UiBuilder.Draw += DrawUI;
        pluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
        InventoryWindow.Init();
        InventoryWindow.InitChart();
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();
        InventoryWindow.Dispose();

        DalamudApi.CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        ToggleMainWindow();
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleMainWindow() => InventoryWindow.Toggle();

}
