using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace LoyalCompanion
{
    public class GearSetOverlay : IDisposable
    {
        private readonly Configuration configuration;
        private readonly MinionSelectWindow minionSelectWindow;

        // Layout constants (unscaled pixels) - used only as a fallback if the list can't be read
        private const float HeaderOffset = 39f;
        private const float RowHeight = 28.5f;

        public GearSetOverlay(Configuration configuration, MinionSelectWindow minionSelectWindow)
        {
            this.configuration = configuration;
            this.minionSelectWindow = minionSelectWindow;
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
                    DrawGearsetButtons(gearsetModule, addon, scale);
                }

                ImGui.End();
            }
            catch (Exception ex)
            {
                Service.PluginLog.Error(ex, "Error in GearSetOverlay");
            }
        }

        private unsafe void DrawGearsetButtons(RaptureGearsetModule* gearsetModule, AtkUnitBase* addon, float scale)
        {
            var buttonHeight = ImGui.GetFrameHeight();
            var addonY = (float)addon->Y;

            // The gearset list is a flat AtkComponentList (one row per gearset, in display
            // order). It repositions its row nodes as it scrolls, so we read each rendered
            // row's node position live and key it by the list item index (== display index).
            var list = FindGearsetList(addon);
            var rowScreen = new Dictionary<int, (float Y, float H)>();
            bool haveList = false;
            float listTopScreen = 0f, listBottomScreen = 0f;

            if (list != null)
            {
                var listNode = (AtkResNode*)list->OwnerNode;
                if (listNode != null)
                {
                    listTopScreen = listNode->ScreenY;
                    listBottomScreen = listTopScreen + listNode->Height * scale;

                    var renderers = list->ItemRendererList;
                    if (renderers != null)
                    {
                        for (int r = 0; r < list->AllocatedItemRendererListLength; r++)
                        {
                            var renderer = renderers[r].AtkComponentListItemRenderer;
                            if (renderer == null)
                                continue;
                            var node = (AtkResNode*)renderer->OwnerNode;
                            if (node == null || !node->IsVisible())
                                continue;
                            rowScreen[renderer->ListItemIndex] = (node->ScreenY, node->Height * scale);
                        }
                    }

                    haveList = rowScreen.Count > 0;
                }
            }

            for (byte i = 0; i < 100; i++)
            {
                var gearsetId = gearsetModule->ResolveIdFromEnabledIndex(i);
                if (gearsetId < 0)
                    break;
                if (!gearsetModule->IsValidGearset(gearsetId))
                    continue;

                float rowY, rowH;
                if (haveList)
                {
                    // The display index i is the list item index; no rendered row -> scrolled out.
                    if (!rowScreen.TryGetValue(i, out var pos))
                        continue;

                    var centre = pos.Y + pos.H * 0.5f;
                    if (centre < listTopScreen || centre > listBottomScreen)
                        continue;

                    rowH = pos.H;
                    rowY = pos.Y - addonY;
                }
                else
                {
                    // Fallback to fixed layout if the list can't be read.
                    rowH = RowHeight * scale;
                    rowY = HeaderOffset * scale + i * rowH;
                }

                ImGui.SetCursorPosY(rowY + (rowH - buttonHeight) * 0.5f);
                ImGui.SetCursorPosX(4f * scale);

                DrawPawButton(gearsetModule, gearsetId);
            }
        }

        private unsafe void DrawPawButton(RaptureGearsetModule* gearsetModule, int gearsetId)
        {
            var assignedList = configuration.GetListForGearset(gearsetId);
            var hasMinions = assignedList != null && assignedList.Minions.Count > 0;

            if (hasMinions)
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.0f, 0.8f, 0.4f, 1.0f));

            if (ImGuiComponents.IconButton($"##paw{gearsetId}", FontAwesomeIcon.Paw))
            {
                var btnRight = ImGui.GetItemRectMax();
                var gearset = gearsetModule->GetGearset(gearsetId);
                var name = gearset != null ? GetGearsetName(gearset) : $"Gearset {gearsetId + 1}";
                minionSelectWindow.SetGearset(gearsetId, name, new Vector2(btnRight.X + 4, ImGui.GetItemRectMin().Y));
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

        private static unsafe AtkComponentList* FindGearsetList(AtkUnitBase* addon)
        {
            for (var j = 0; j < addon->UldManager.NodeListCount; j++)
            {
                var node = addon->UldManager.NodeList[j];
                if (node == null || (ushort)node->Type < 1000)
                    continue;
                var componentNode = (AtkComponentNode*)node;
                if (componentNode->Component != null &&
                    componentNode->Component->GetComponentType() == ComponentType.List)
                {
                    return (AtkComponentList*)componentNode->Component;
                }
            }
            return null;
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
