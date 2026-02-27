using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LoyalCompanion
{
    public class MinionSelectWindow : Window
    {
        private readonly Configuration configuration;
        private int currentGearsetId = -1;
        private string searchText = string.Empty;
        private string renameText = string.Empty;
        private bool isRenaming;
        private bool confirmingDelete;
        private List<MinionEntry>? cachedMinions;
        private Vector2? pendingPosition;

        private struct MinionEntry
        {
            public uint Id;
            public string Name;
            public uint IconId;
        }

        public MinionSelectWindow(Configuration configuration)
            : base("Minion List###MinionSelect", ImGuiWindowFlags.NoResize)
        {
            this.configuration = configuration;
            this.Size = new Vector2(350, 500);
            this.SizeCondition = ImGuiCond.FirstUseEver;
        }

        public void SetGearset(int gearsetId, string gearsetName, Vector2? buttonPos)
        {
            this.currentGearsetId = gearsetId;
            this.WindowName = $"Minions - {gearsetName}###MinionSelect";
            this.IsOpen = true;
            this.cachedMinions = null;
            this.pendingPosition = buttonPos;
            this.isRenaming = false;
            this.confirmingDelete = false;
        }

        public override void PreDraw()
        {
            if (pendingPosition.HasValue)
            {
                ImGui.SetNextWindowPos(pendingPosition.Value, ImGuiCond.Always);
                pendingPosition = null;
            }
        }

        public override unsafe void Draw()
        {
            // Auto-close when gearset panel closes
            try
            {
                var addonPtr = Service.GameGui.GetAddonByName("GearSetList");
                if (addonPtr.Address == nint.Zero)
                {
                    this.IsOpen = false;
                    return;
                }
                var addon = (AtkUnitBase*)addonPtr.Address;
                if (addon == null || !addon->IsVisible)
                {
                    this.IsOpen = false;
                    return;
                }
            }
            catch { }

            if (currentGearsetId < 0)
            {
                ImGui.TextUnformatted("No gearset selected.");
                return;
            }

            var assignedList = configuration.GetListForGearset(currentGearsetId);

            // List assignment dropdown
            ImGui.SetNextItemWidth(-1);
            var previewName = assignedList != null ? $"{assignedList.Name} ({assignedList.Minions.Count})" : "None";
            if (ImGui.BeginCombo("##listCombo", previewName))
            {
                if (ImGui.Selectable("None", assignedList == null))
                {
                    configuration.GearsetListAssignments.Remove(currentGearsetId);
                    configuration.Save();
                }

                foreach (var list in configuration.MinionLists)
                {
                    var isSelected = assignedList != null && assignedList.Id == list.Id;
                    var label = $"{list.Name} ({list.Minions.Count})";
                    if (ImGui.Selectable(label, isSelected))
                    {
                        configuration.GearsetListAssignments[currentGearsetId] = list.Id;
                        configuration.Save();
                    }
                }

                ImGui.EndCombo();
            }

            // New / Rename / Delete buttons
            if (ImGui.Button("New"))
            {
                var newList = new MinionList { Name = $"List {configuration.MinionLists.Count + 1}" };
                configuration.MinionLists.Add(newList);
                configuration.GearsetListAssignments[currentGearsetId] = newList.Id;
                configuration.Save();
            }

            if (assignedList != null)
            {
                ImGui.SameLine();
                if (!isRenaming)
                {
                    if (ImGui.Button("Rename"))
                    {
                        renameText = assignedList.Name;
                        isRenaming = true;
                    }
                }
                else
                {
                    ImGui.SetNextItemWidth(100);
                    if (ImGui.InputText("##rename", ref renameText, 128, ImGuiInputTextFlags.EnterReturnsTrue))
                    {
                        ApplyRename(assignedList);
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("OK"))
                    {
                        ApplyRename(assignedList);
                    }
                    ImGui.SameLine();
                    if (ImGui.Button("Cancel##ren"))
                    {
                        isRenaming = false;
                    }
                }

                ImGui.SameLine();
                if (!confirmingDelete)
                {
                    if (ImGui.Button("Delete"))
                    {
                        confirmingDelete = true;
                    }
                }
                else
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
                    if (ImGui.Button("Confirm"))
                    {
                        var keysToRemove = configuration.GearsetListAssignments
                            .Where(kv => kv.Value == assignedList.Id)
                            .Select(kv => kv.Key)
                            .ToList();
                        foreach (var key in keysToRemove)
                            configuration.GearsetListAssignments.Remove(key);

                        configuration.MinionLists.Remove(assignedList);
                        configuration.Save();
                        confirmingDelete = false;
                        assignedList = null;
                    }
                    ImGui.PopStyleColor();
                    ImGui.SameLine();
                    if (ImGui.Button("Cancel##del"))
                    {
                        confirmingDelete = false;
                    }
                }

                // Shared indicator
                if (assignedList != null)
                {
                    var usageCount = configuration.GearsetListAssignments.Count(kv => kv.Value == assignedList.Id);
                    if (usageCount > 1)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), $"(shared by {usageCount})");
                    }
                }
            }

            ImGui.Separator();

            // If no list assigned, stop here
            if (assignedList == null)
                return;

            var selectedMinions = assignedList.Minions;

            // Search filter
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##search", "Search minions...", ref searchText, 256);
            ImGui.Separator();

            // Select All / Clear All
            if (ImGui.Button("Select All Unlocked"))
            {
                SelectAllUnlocked(selectedMinions);
            }
            ImGui.SameLine();
            if (ImGui.Button("Clear All"))
            {
                selectedMinions.Clear();
                configuration.Save();
            }
            ImGui.SameLine();
            ImGui.TextUnformatted($"{selectedMinions.Count} selected");
            ImGui.Separator();

            // Build cache if needed
            if (cachedMinions == null)
                BuildMinionCache();

            // Minion list
            if (ImGui.BeginChild("minionList", new Vector2(0, 0), false))
            {
                try
                {
                    DrawMinionList(selectedMinions);
                }
                catch (Exception ex)
                {
                    Service.PluginLog.Error(ex, "Error drawing minion list");
                }
            }
            ImGui.EndChild();
        }

        private unsafe void BuildMinionCache()
        {
            cachedMinions = new List<MinionEntry>();
            var sheet = Service.DataManager.GetExcelSheet<Companion>();
            if (sheet == null)
                return;

            var uiState = UIState.Instance();
            if (uiState == null)
                return;

            foreach (var row in sheet)
            {
                if (row.RowId == 0)
                    continue;

                // Only show unlocked minions
                if (!uiState->IsCompanionUnlocked(row.RowId))
                    continue;

                var name = row.Singular.ExtractText();
                if (string.IsNullOrEmpty(name))
                    continue;

                cachedMinions.Add(new MinionEntry
                {
                    Id = row.RowId,
                    Name = name,
                    IconId = row.Icon,
                });
            }

            cachedMinions.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        private void DrawMinionList(List<uint> selectedMinions)
        {
            if (cachedMinions == null)
                return;

            foreach (var minion in cachedMinions)
            {
                if (searchText.Length > 0 && !minion.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    continue;

                var isSelected = selectedMinions.Contains(minion.Id);

                // Track row bounds for hover detection
                var rowMin = ImGui.GetCursorScreenPos();

                // Draw icon
                var icon = Service.TextureProvider.GetFromGameIcon(new GameIconLookup(minion.IconId));
                if (icon.TryGetWrap(out var texture, out _) && texture != null)
                {
                    ImGui.Image(texture.Handle, new Vector2(24, 24));
                    ImGui.SameLine();
                }

                // Draw checkbox
                if (ImGui.Checkbox($"{minion.Name}##{minion.Id}", ref isSelected))
                {
                    if (isSelected && !selectedMinions.Contains(minion.Id))
                        selectedMinions.Add(minion.Id);
                    else if (!isSelected)
                        selectedMinions.Remove(minion.Id);
                    configuration.Save();
                }

                // Check hover on entire row
                var rowMax = new Vector2(ImGui.GetContentRegionAvail().X + ImGui.GetWindowPos().X, ImGui.GetItemRectMax().Y);
                var isRowHovered = ImGui.IsMouseHoveringRect(rowMin, rowMax);
                if (isRowHovered)
                {
                    var portraitIcon = Service.TextureProvider.GetFromGameIcon(new GameIconLookup(minion.IconId + 64000));
                    if (portraitIcon.TryGetWrap(out var portraitTex, out _) && portraitTex != null)
                    {
                        ImGui.BeginTooltip();
                        ImGui.Image(portraitTex.Handle, new Vector2(256, 256));
                        ImGui.EndTooltip();
                    }
                }
            }
        }

        private unsafe void SelectAllUnlocked(List<uint> selectedMinions)
        {
            if (cachedMinions == null)
                BuildMinionCache();
            if (cachedMinions == null)
                return;

            selectedMinions.Clear();
            foreach (var minion in cachedMinions)
            {
                selectedMinions.Add(minion.Id);
            }
            configuration.Save();
        }

        private void ApplyRename(MinionList list)
        {
            if (!string.IsNullOrWhiteSpace(renameText))
            {
                list.Name = renameText.Trim();
                configuration.Save();
            }
            isRenaming = false;
        }

        public override void OnClose()
        {
            this.searchText = string.Empty;
            this.isRenaming = false;
            this.confirmingDelete = false;
        }
    }
}
