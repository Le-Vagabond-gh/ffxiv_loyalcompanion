using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace LoyalCompanion
{
    public class MinionSelectWindow : Window
    {
        private readonly Configuration configuration;
        private MinionList? currentList;
        private string searchText = string.Empty;
        private string renameText = string.Empty;
        private bool isRenaming;
        private List<MinionEntry>? cachedMinions;
        private Vector2? pendingPosition;

        private struct MinionEntry
        {
            public uint Id;
            public string Name;
            public uint IconId;
        }

        public MinionSelectWindow(Configuration configuration)
            : base("Minion List", ImGuiWindowFlags.None)
        {
            this.configuration = configuration;
            this.SizeConstraints = new WindowSizeConstraints
            {
                MinimumSize = new Vector2(300, 400),
                MaximumSize = new Vector2(500, 800),
            };
        }

        public void SetList(MinionList list, Vector2? buttonPos)
        {
            this.currentList = list;
            this.WindowName = $"Minion List - {list.Name}###MinionSelect";
            this.IsOpen = true;
            this.cachedMinions = null;
            this.pendingPosition = buttonPos;
            this.isRenaming = false;
        }

        public override void PreDraw()
        {
            if (pendingPosition.HasValue)
            {
                ImGui.SetNextWindowPos(pendingPosition.Value, ImGuiCond.Always);
                pendingPosition = null;
            }
        }

        public override void Draw()
        {
            if (currentList == null)
            {
                ImGui.TextUnformatted("No list selected.");
                return;
            }

            var selectedMinions = currentList.Minions;

            // List name / rename
            if (isRenaming)
            {
                ImGui.SetNextItemWidth(150);
                if (ImGui.InputText("##rename", ref renameText, 128, ImGuiInputTextFlags.EnterReturnsTrue))
                {
                    ApplyRename();
                }
                ImGui.SameLine();
                if (ImGui.Button("OK"))
                {
                    ApplyRename();
                }
                ImGui.SameLine();
                if (ImGui.Button("Cancel"))
                {
                    isRenaming = false;
                }
            }
            else
            {
                ImGui.TextUnformatted(currentList.Name);
                ImGui.SameLine();
                if (ImGui.Button("Rename"))
                {
                    renameText = currentList.Name;
                    isRenaming = true;
                }
            }
            ImGui.Separator();

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

        private void ApplyRename()
        {
            if (currentList != null && !string.IsNullOrWhiteSpace(renameText))
            {
                currentList.Name = renameText.Trim();
                this.WindowName = $"Minion List - {currentList.Name}###MinionSelect";
                configuration.Save();
            }
            isRenaming = false;
        }

        public override void OnClose()
        {
            this.searchText = string.Empty;
            this.isRenaming = false;
        }
    }
}
