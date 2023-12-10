﻿using CentrED.IO;
using CentrED.Map;
using ClassicUO.Assets;
using ImGuiNET;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using static CentrED.Application;
using Vector2 = System.Numerics.Vector2;

namespace CentrED.UI.Windows;

public class TilesWindow : Window
{
    private static readonly Random _random = new();

    public TilesWindow()
    {
        CEDClient.Connected += FilterTiles;
    }

    public override string Name => "Tiles";

    private string _filter = "";
    internal int SelectedLandId;
    internal int SelectedStaticId;
    private bool _updateScroll;
    private bool staticMode;
    private float _tableWidth;
    public const int MaxLandIndex = ArtLoader.MAX_LAND_DATA_INDEX_COUNT;
    private static readonly Vector2 TilesDimensions = new(44, 44);
    public const string Static_DragDrop_Target_Type = "StaticDragDrop";
    public const string Land_DragDrop_Target_Type = "LandDragDrop";

    private int[] _matchedLandIds;
    private int[] _matchedStaticIds;

    public bool LandMode => !staticMode;
    public bool StaticMode => staticMode;

    public ushort SelectedId => (ushort)(LandMode ? SelectedLandId : SelectedStaticId);

    public ushort ActiveId =>
        ActiveTileSetValues.Length > 0 ? ActiveTileSetValues[_random.Next(ActiveTileSetValues.Length)] : SelectedId;

    private void FilterTiles()
    {
        if (_filter.Length == 0)
        {
            _matchedLandIds = new int[CEDGame.MapManager.ValidLandIds.Length];
            CEDGame.MapManager.ValidLandIds.CopyTo(_matchedLandIds, 0);

            _matchedStaticIds = new int[CEDGame.MapManager.ValidStaticIds.Length];
            CEDGame.MapManager.ValidStaticIds.CopyTo(_matchedStaticIds, 0);
        }
        else
        {
            var filter = _filter.ToLower();
            var matchedLandIds = new List<int>();
            foreach (var index in CEDGame.MapManager.ValidLandIds)
            {
                var name = TileDataLoader.Instance.LandData[index].Name?.ToLower() ?? "";
                if (name.Contains(filter) || $"{index}".Contains(_filter) || $"0x{index:x4}".Contains(filter))
                    matchedLandIds.Add(index);
            }
            _matchedLandIds = matchedLandIds.ToArray();

            var matchedStaticIds = new List<int>();
            foreach (var index in CEDGame.MapManager.ValidStaticIds)
            {
                var name = TileDataLoader.Instance.StaticData[index].Name?.ToLower() ?? "";
                if (name.Contains(filter) || $"{index}".Contains(_filter) || $"0x{index:x4}".Contains(filter))
                    matchedStaticIds.Add(index);
            }
            _matchedStaticIds = matchedStaticIds.ToArray();
        }
    }

    public override void Draw()
    {
        if (!Show)
            return;
        ImGui.SetNextWindowSize
        (
            new Vector2
            (
                250,
                CEDGame._gdm.GraphicsDevice.PresentationParameters.BackBufferHeight - CEDGame.UIManager._mainMenuHeight
            ),
            ImGuiCond.FirstUseEver
        );
        ImGui.Begin(Name, ref _show);
        if (ImGui.Button("Scroll to selected"))
        {
            _updateScroll = true;
        }
        ImGui.Text("Filter");
        if (ImGui.InputText("", ref _filter, 64))
        {
            FilterTiles();
        }
        if (UIManager.TwoWaySwitch("Land", "Statics", ref staticMode))
        {
            _updateScroll = true;
            _tileSetIndex = 0;
            ActiveTileSetValues = Empty;
        }
        DrawTiles();
        DrawTileSets();
        ImGui.End();
    }

    private int _tileSetIndex;
    private bool _tileSetShowPopupNew;
    private bool _tileSetShowPopupDelete;
    private string _tileSetNewName = "";
    private static readonly ushort[] Empty = Array.Empty<ushort>();
    public ushort[] ActiveTileSetValues = Empty;

    private void DrawTiles()
    {
        ImGui.BeginChild("Tiles", new Vector2(), ImGuiChildFlags.Border | ImGuiChildFlags.ResizeY);
        var tilesPosY = ImGui.GetCursorPosY();
        if (ImGui.BeginTable("TilesTable", 3) && CEDClient.Initialized)
        {
            unsafe
            {
                ImGuiListClipperPtr clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
                ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("0x0000").X);
                ImGui.TableSetupColumn("Graphic", ImGuiTableColumnFlags.WidthFixed, TilesDimensions.X);
                _tableWidth = ImGui.GetContentRegionAvail().X;
                var ids = LandMode ? _matchedLandIds : _matchedStaticIds;
                clipper.Begin(ids.Length, TilesDimensions.Y);
                while (clipper.Step())
                {
                    for (int row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                    {
                        if (LandMode)
                            TilesDrawLand(ids[row], true);
                        else
                        {
                            TilesDrawStatic(ids[row], true);
                        }
                    }
                }
                clipper.End();

                if (_updateScroll)
                {
                    float itemPosY = clipper.StartPosY + TilesDimensions.Y * Array.IndexOf
                        (ids, LandMode ? SelectedLandId : SelectedStaticId);
                    ImGui.SetScrollFromPosY(itemPosY);
                    _updateScroll = false;
                }
            }
            ImGui.EndTable();
        }
        ImGui.EndChild();
    }

    private void DrawTileSets()
    {
        ImGui.BeginChild("TileSets");
        ImGui.Text("Tile Set");
        if (ImGui.Button("New"))
        {
            ImGui.OpenPopup("NewTileSet");
            _tileSetShowPopupNew = true;
        }
        ImGui.SameLine();
        ImGui.BeginDisabled(_tileSetIndex == 0);
        if (ImGui.Button("Delete"))
        {
            ImGui.OpenPopup("DeleteTileSet");
            _tileSetShowPopupDelete = true;
        }
        ImGui.EndDisabled();
        var tileSets = LandMode ?
            ProfileManager.ActiveProfile.LandTileSets :
            ProfileManager.ActiveProfile.StaticTileSets;
        //Probably slow, optimize
        var names = new[] { String.Empty }.Concat(tileSets.Keys).ToArray();
        if (ImGui.Combo("", ref _tileSetIndex, names, names.Length))
        {
            if (_tileSetIndex == 0)
            {
                ActiveTileSetValues = Empty;
            }
            else
            {
                ActiveTileSetValues = tileSets[names[_tileSetIndex]].ToArray();
            }
        }
        if (ImGui.BeginTable("TileSetTable", 3) && CEDClient.Initialized)
        {
            unsafe
            {
                ImGuiListClipperPtr clipper = new ImGuiListClipperPtr(ImGuiNative.ImGuiListClipper_ImGuiListClipper());
                ImGui.TableSetupColumn("Id", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("0x0000").X);
                ImGui.TableSetupColumn("Graphic", ImGuiTableColumnFlags.WidthFixed, TilesDimensions.X);
                _tableWidth = ImGui.GetContentRegionAvail().X;
                var ids = ActiveTileSetValues;
                clipper.Begin(ids.Length, TilesDimensions.Y);
                while (clipper.Step())
                {
                    for (int row = clipper.DisplayStart; row < clipper.DisplayEnd; row++)
                    {
                        if (LandMode)
                            TilesDrawLand(ids[row]);
                        else
                        {
                            TilesDrawStatic(ids[row]);
                        }
                    }
                }
                clipper.End();
            }
            ImGui.EndTable();
        }
        

        if (ImGui.BeginPopupModal
            (
                "NewTileSet",
                ref _tileSetShowPopupNew,
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar
            ))
        {
            ImGui.Text("Name");
            ImGui.SameLine();
            ImGui.InputText("", ref _tileSetNewName, 32);
            if (ImGui.Button("Add"))
            {
                tileSets.Add(_tileSetNewName, new HashSet<ushort>());
                _tileSetIndex = Array.IndexOf(tileSets.Keys.ToArray(), _tileSetNewName) + 1;
                ActiveTileSetValues = Empty;
                ProfileManager.Save();
                _tileSetNewName = "";
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        if (ImGui.BeginPopupModal
            (
                "DeleteTileSet",
                ref _tileSetShowPopupDelete,
                ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar
            ))
        {
            ImGui.Text($"Are you sure you want to delete tile set '{names[_tileSetIndex]}'?");
            if (ImGui.Button("Yes"))
            {
                tileSets.Remove(names[_tileSetIndex]);
                ProfileManager.Save();
                _tileSetIndex--;
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("No"))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        ImGui.EndChild();
        if (ImGui.BeginDragDropTarget())
        {
            var payloadPtr = ImGui.AcceptDragDropPayload(LandMode ? Land_DragDrop_Target_Type : Static_DragDrop_Target_Type);
            unsafe
            {
                if (payloadPtr.NativePtr != null)
                {
                    var dataPtr = (int*)payloadPtr.Data;
                    int id = dataPtr[0];
                    var tileSet = tileSets[names[_tileSetIndex]];
                    tileSet.Add((ushort)id);
                    ActiveTileSetValues = tileSet.ToArray();
                    ProfileManager.Save();
                }
            }
            ImGui.EndDragDropTarget();
        }
    }

    private void TilesDrawLand(int index, bool dragEnabled = false)
    {
        var texture = ArtLoader.Instance.GetLandTexture((uint)index, out var bounds);
        var name = TileDataLoader.Instance.LandData[index].Name;
        TilesDrawRow(index, index, texture, bounds, name, dragEnabled);
    }

    private void TilesDrawStatic(int index, bool dragEnabled = false)
    {
        var realIndex = index + MaxLandIndex;
        var texture = ArtLoader.Instance.GetStaticTexture((uint)index, out var bounds);
        var realBounds = ArtLoader.Instance.GetRealArtBounds(index);
        var name = TileDataLoader.Instance.StaticData[index].Name;
        TilesDrawRow
        (
            index,
            realIndex,
            texture,
            new Rectangle(bounds.X + realBounds.X, bounds.Y + realBounds.Y, realBounds.Width, realBounds.Height),
            name,
            dragEnabled
        );
    }

    private void TilesDrawRow(int index, int realIndex, Texture2D texture, Rectangle bounds, string name, bool dragEnabled = false)
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.None, TilesDimensions.Y);
        if (ImGui.TableNextColumn())
        {
            var startPos = ImGui.GetCursorPos();
            var selectableSize = new Vector2(_tableWidth, TilesDimensions.Y);
            if (ImGui.Selectable
                (
                    $"##tile{realIndex}",
                    LandMode ? SelectedLandId == index : SelectedStaticId == index,
                    ImGuiSelectableFlags.SpanAllColumns,
                    selectableSize
                ))
            {
                if (LandMode)
                    SelectedLandId = index;
                else
                    SelectedStaticId = index;
            }
            if (StaticMode && ImGui.BeginPopupContextItem())
            {
                if (ImGui.Button("Filter"))
                {
                    CEDGame.MapManager.StaticFilterIds.Add(index);
                    ImGui.CloseCurrentPopup();
                }
                ImGui.EndPopup();
            }
            if (dragEnabled && ImGui.BeginDragDropSource())
            {
                unsafe
                {
                    ImGui.SetDragDropPayload(LandMode ? Land_DragDrop_Target_Type : Static_DragDrop_Target_Type, (IntPtr)(&index), sizeof(int));
                }
                ImGui.Text(name);
                CEDGame.UIManager.DrawImage(texture, bounds, TilesDimensions);
                ImGui.EndDragDropSource();
            }
            ImGui.SetCursorPos
            (
                startPos with
                {
                    Y = startPos.Y + (TilesDimensions.Y - ImGui.GetFontSize()) / 2
                }
            );
            ImGui.Text($"0x{index:X4}");
        }

        if (ImGui.TableNextColumn())
        {
            CEDGame.UIManager.DrawImage(texture, bounds, TilesDimensions);
        }

        if (ImGui.TableNextColumn())
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (TilesDimensions.Y - ImGui.GetFontSize()) / 2);
            ImGui.TextUnformatted(name);
        }
    }

    public void UpdateSelectedId(TileObject mapObject)
    {
        if (mapObject is StaticObject)
            SelectedStaticId += mapObject.Tile.Id;
        else if (mapObject is LandObject)
            SelectedLandId += mapObject.Tile.Id;
        _updateScroll = true;
    }
}