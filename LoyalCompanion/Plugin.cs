using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using System;
using System.Text;

namespace LoyalCompanion
{
    public sealed class Plugin : IDalamudPlugin
    {
        public string Name => "LoyalCompanion";
        private IDalamudPluginInterface PluginInterface { get; init; }
        public Configuration Configuration { get; init; }

        private readonly WindowSystem windowSystem = new("LoyalCompanion");
        private readonly MinionSelectWindow minionSelectWindow;
        private readonly GearSetOverlay gearSetOverlay;
        private readonly MinionSummoner minionSummoner;

        public Plugin(IDalamudPluginInterface pluginInterface)
        {
            this.PluginInterface = pluginInterface;
            this.PluginInterface.Create<Service>();
            this.Configuration = this.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            this.Configuration.Initialize(this.PluginInterface);

            this.minionSelectWindow = new MinionSelectWindow(this.Configuration);
            this.gearSetOverlay = new GearSetOverlay(this.Configuration, this.minionSelectWindow);
            this.minionSummoner = new MinionSummoner(this.Configuration);

            this.windowSystem.AddWindow(this.minionSelectWindow);

            this.PluginInterface.UiBuilder.Draw += this.OnDraw;
            this.PluginInterface.UiBuilder.OpenMainUi += this.OpenMainUi;
            this.PluginInterface.UiBuilder.OpenConfigUi += this.OpenMainUi;

            Service.PluginLog.Info("LoyalCompanion initialized");
        }

        private unsafe void OpenMainUi()
        {
            try
            {
                var gearsetModule = RaptureGearsetModule.Instance();
                if (gearsetModule == null)
                    return;

                var gearsetId = gearsetModule->CurrentGearsetIndex;
                var gearset = gearsetModule->GetGearset(gearsetId);
                var name = gearset != null ? GetGearsetName(gearset) : $"Gearset {gearsetId + 1}";
                this.minionSelectWindow.SetGearset(gearsetId, name, null);
            }
            catch (Exception ex)
            {
                Service.PluginLog.Error(ex, "Error opening main UI");
            }
        }

        private static unsafe string GetGearsetName(RaptureGearsetModule.GearsetEntry* gearset)
        {
            var bytes = new byte[48];
            int len = 0;
            for (int i = 0; i < 48; i++)
            {
                var b = gearset->Name[i];
                if (b == 0) break;
                bytes[len++] = b;
            }
            return Encoding.UTF8.GetString(bytes, 0, len);
        }

        private void OnDraw()
        {
            this.gearSetOverlay.Draw();
            this.windowSystem.Draw();

            if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow))
            {
                ImGui.GetIO().WantCaptureMouse = true;
            }
        }

        public void Dispose()
        {
            this.PluginInterface.UiBuilder.Draw -= this.OnDraw;
            this.PluginInterface.UiBuilder.OpenMainUi -= this.OpenMainUi;
            this.PluginInterface.UiBuilder.OpenConfigUi -= this.OpenMainUi;
            this.windowSystem.RemoveAllWindows();
            this.gearSetOverlay.Dispose();
            this.minionSummoner.Dispose();
            Service.PluginLog.Info("LoyalCompanion disposed");
        }
    }
}
