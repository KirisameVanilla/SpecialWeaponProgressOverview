using System;
using Dalamud.Configuration;

namespace SpecialWeaponProgressOverview;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public void Save()
    {
        DalamudApi.PluginInterface.SavePluginConfig(this);
    }
}
