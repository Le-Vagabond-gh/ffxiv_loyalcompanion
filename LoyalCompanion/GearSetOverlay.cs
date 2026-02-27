using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Numerics;
using System.Text;

namespace LoyalCompanion
{
    public class GearSetOverlay : IDisposable
    {
        private readonly Configuration configuration;
        private readonly ListAssignWindow listAssignWindow;

        // Layout constants (unscaled pixels) - adjust if alignment is off
        private const float HeaderOffset = 39f;
        private const float RowHeight = 28.5f;

        public GearSetOverlay(Configuration configuration, ListAssignWindow listAssignWindow)
        {
            this.configuration = configuration;
            this.listAssignWindow = listAssignWindow;
        }

        public void Dispose() { }

        public unsafe void Draw()
        {
            try
            {
                var addonPtr = Service.GameGui.GetAddonByName("GearSetList");
                if (addonPtr.Address == nint.Zero)
                    return;

                var addon = (AtkUnitBase*)addonPtr.Address;
                if (addon == null || !addon->IsVisible || !addon->IsReady)
                    return;

                var scale = addon->Scale;
                var rootNode = addon->RootNode;
                if (rootNode == null)
                    return;

                var addonX = (float)addon->X;
                var addonY = (float)addon->Y;
                var addonWidth = rootNode->Width * scale;
                var addonHeight = rootNode->Height * scale;

                var buttonColumnWidth = 36f * scale;
                ImGui.SetNextWindowPos(new Vector2(addonX + addonWidth, addonY), ImGuiCond.Always);
                ImGui.SetNextWindowSize(new Vector2(buttonColumnWidth, addonHeight), ImGuiCond.Always);

                var flags = ImGuiWindowFlags.NoTitleBar |
                            ImGuiWindowFlags.NoBackground |
                            ImGuiWindowFlags.NoMove |
                            ImGuiWindowFlags.NoResize |
                            ImGuiWindowFlags.NoCollapse |
                            ImGuiWindowFlags.NoScrollbar |
                            ImGuiWindowFlags.NoScrollWithMouse |
                            ImGuiWindowFlags.NoSavedSettings |
                            ImGuiWindowFlags.NoFocusOnAppearing |
                            ImGuiWindowFlags.NoBringToFrontOnFocus;

                if (!ImGui.Begin("##LoyalCompanionOverlay", flags))
                {
                    ImGui.End();
                    return;
                }

                ImGui.SetWindowFontScale(scale);

                var gearsetModule = RaptureGearsetModule.Instance();
                if (gearsetModule != null)
                {
                    DrawGearsetButtons(gearsetModule, scale);
                }

                ImGui.End();
            }
            catch (Exception ex)
            {
                Service.PluginLog.Error(ex, "Error in GearSetOverlay");
            }
        }

        private unsafe void DrawGearsetButtons(RaptureGearsetModule* gearsetModule, float scale)
        {
            var headerY = HeaderOffset * scale;
            var rowH = RowHeight * scale;
            var buttonHeight = ImGui.GetFrameHeight();

            for (byte i = 0; i < 100; i++)
            {
                var gearsetId = gearsetModule->ResolveIdFromEnabledIndex(i);
                if (gearsetId < 0)
                    break;
                if (!gearsetModule->IsValidGearset(gearsetId))
                    continue;

                // Position button to align with the gearset row
                var rowY = headerY + i * rowH;
                ImGui.SetCursorPosY(rowY + (rowH - buttonHeight) * 0.5f);
                ImGui.SetCursorPosX(4f * scale);

                var assignedList = configuration.GetListForGearset(gearsetId);
                var hasMinions = assignedList != null && assignedList.Minions.Count > 0;

                if (hasMinions)
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.0f, 0.8f, 0.4f, 1.0f));

                if (ImGuiComponents.IconButton($"##paw{gearsetId}", FontAwesomeIcon.Paw))
                {
                    var btnRight = ImGui.GetItemRectMax();
                    var gearset = gearsetModule->GetGearset(gearsetId);
                    var name = gearset != null ? GetGearsetName(gearset) : $"Gearset {gearsetId + 1}";
                    listAssignWindow.SetGearset(gearsetId, name, new Vector2(btnRight.X + 4, ImGui.GetItemRectMin().Y));
                }

                // Overlay gearset number on the button
                var btnMin = ImGui.GetItemRectMin();
                var btnMax = ImGui.GetItemRectMax();
                var label = (gearsetId + 1).ToString();
                var textSize = ImGui.CalcTextSize(label);
                var textPos = new Vector2(
                    btnMin.X + (btnMax.X - btnMin.X - textSize.X) * 0.5f,
                    btnMin.Y + (btnMax.Y - btnMin.Y - textSize.Y) * 0.5f);
                var drawList = ImGui.GetForegroundDrawList();
                var outlineColor = ImGui.GetColorU32(new Vector4(0, 0, 0, 1));
                var textColor = hasMinions
                    ? ImGui.GetColorU32(new Vector4(0.0f, 0.8f, 0.4f, 1.0f))
                    : ImGui.GetColorU32(new Vector4(1, 1, 1, 1));
                for (var dx = -1; dx <= 1; dx++)
                    for (var dy = -1; dy <= 1; dy++)
                        if (dx != 0 || dy != 0)
                            drawList.AddText(textPos + new Vector2(dx, dy), outlineColor, label);
                drawList.AddText(textPos, textColor, label);

                if (hasMinions)
                    ImGui.PopStyleColor();

                if (ImGui.IsItemHovered())
                {
                    if (assignedList != null)
                        ImGui.SetTooltip($"{assignedList.Name} ({assignedList.Minions.Count} minions)");
                    else
                        ImGui.SetTooltip("No list assigned");
                }
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
    }
}
