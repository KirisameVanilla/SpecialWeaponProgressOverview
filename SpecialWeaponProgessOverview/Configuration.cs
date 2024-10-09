using System;
using Dalamud.Configuration;

namespace SpecialWeaponProgessOverview;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public void Save()
    {
        DalamudApi.PluginInterface.SavePluginConfig(this);
    }
}
