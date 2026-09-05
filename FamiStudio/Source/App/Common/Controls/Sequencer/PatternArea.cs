using System;
using System.Collections.Generic;

namespace FamiStudio
{
    internal class PatternArea : Control
    {
        const int DefaultPatternHeaderSizeY = 13;
        const int DefaultPatternNamePosX    = 2;

        private readonly Sequencer sequencer;

        private int patternHeaderSizeY;
        private int patternNamePosX;

        private float bitmapScale = 1.0f;

        private PatternBitmapCache patternCache;

        private TextureAtlasRef bmpDuplicate;
        private TextureAtlasRef bmpDuplicateMove;
        private TextureAtlasRef bmpMenuInstance;

        private Song Song => App?.SelectedSong;

        private int PatternHeaderSizeY            => patternHeaderSizeY;
        private int PatternNamePosX               => patternNamePosX;
        private int SelectionDragAnchorPatternIdx => sequencer.SelectionDragAnchorPatternIdx;
        private int DragSelectionPatternDelta     => sequencer.DragSelectionPatternDelta;
        private int DragSelectionRowDelta         => sequencer.DragSelectionRowDelta;
        private int DragSelectionX                => sequencer.DragSelectionX;

        private float SelectionDragAnchorPatternXFraction => sequencer.SelectionDragAnchorPatternXFraction;

        private bool LegacySelectMode => sequencer.LegacySelectMode;
        private bool HasSelection     => sequencer.HasSelection;

        private Sequencer.SequencerViewport Viewport => sequencer.Viewport;

        private PatternLocation SelectionMin        => sequencer.SelectionMin;
        private PatternLocation SelectionMax        => sequencer.SelectionMax;
        private PatternLocation CaptureSelectionMin => sequencer.CaptureSelectionMin;
        private PatternLocation CaptureSelectionMax => sequencer.CaptureSelectionMax;

        private bool IsTimeOnlySelection          => sequencer.IsTimeOnlySelection;
        private bool IsRectangleSelectionCapture  => sequencer.IsRectangleSelectionCapture;
        private bool IsColumnSelectionCapture     => sequencer.IsColumnSelectionCapture;
        private bool IsDragSelectionCapture       => sequencer.IsDragSelectionCapture;

        private Color SelectionColor                => sequencer.IsActiveControl ? SelectedPatternVisibleColor : SelectedPatternInvisibleColor;
        private Color SelectedPatternVisibleColor   => sequencer.SelectedPatternVisibleColor;
        private Color SelectedPatternInvisibleColor => sequencer.SelectedPatternInvisibleColor;
        private Color HighlightedPatternColor       => sequencer.HighlightedPatternColor;

        private PatternLocation HighlightLocation => sequencer.HighlightLocation;
        private IEnumerable<PatternLocation> SelectedPatternLocations => sequencer.SelectedPatternLocations;

        LocalizedString AddPatternTooltip;
        LocalizedString SelectRectangleTooltip;
        LocalizedString OrTooltip;
        LocalizedString DeletePatternTooltip;
        LocalizedString MovePatternTooltip;
        LocalizedString ClonePatternTooltip;
        LocalizedString GoToPianoRollLabel;
        LocalizedString ExpandSelectionLabel;
        LocalizedString InstanciateHereLabel;
        LocalizedString DuplicateHereLabel;
        LocalizedString ClearSelectionLabel;
        LocalizedString DeleteSelectionLabel;
        LocalizedString SelectedPatternPropertiesLabel;
        LocalizedString PatternPropertiesLabel;
        LocalizedString DeletePatternLabel;
        LocalizedString MakePatternsUniqueLabel;
        LocalizedString MergeIdenticalPatternsLabel;

        internal PatternArea(Sequencer sequencer)
        {
            this.sequencer = sequencer;
            Localization.Localize(this);
            supportsDoubleClick = true;
            supportsLongPress   = true;
        }

        protected override void OnAddedToContainer()
        {
            base.OnAddedToContainer();

            var g = ParentWindow.Graphics;

            patternCache = new PatternBitmapCache(g);
            bmpDuplicate = g.GetTextureAtlasRef("MenuCopy");
            bmpDuplicateMove = g.GetTextureAtlasRef("DuplicateMove");
            bmpMenuInstance = g.GetTextureAtlasRef("MenuInstance");

            // TODO: Should we be passing this from sequencer?
            if (Platform.IsMobile)
                bitmapScale = DpiScaling.ScaleForWindowFloat(0.5f);

            UpdateRenderCoords();
        }

        private void UpdateRenderCoords()
        {
            // Shave a couple pixels when the size is getting too small.
            patternHeaderSizeY = DpiScaling.ScaleForFont(Viewport.ChannelSizeY < DpiScaling.ScaleForWindow(24) ? DefaultPatternHeaderSizeY - 2 : DefaultPatternHeaderSizeY);
            patternNamePosX    = DpiScaling.ScaleForWindow(DefaultPatternNamePosX);
        }

        private void UpdateToolTip(PointerEventArgs e)
        {
            var tooltip = "";

            if (GetPatternForCoord(e.X, e.Y, out var location))
            {
                var pattern = Song.GetPatternInstance(location);
                var tooltipList = new List<string>();

                if (pattern == null)
                    tooltipList.Add($"<MouseLeft> {AddPatternTooltip}");

                if (Settings.SetLoopPointShortcut.IsShortcutValid(0))
                    tooltipList.Add($"{Settings.SetLoopPointShortcut.TooltipString}<MouseLeft> {sequencer.SetLoopPointText}");

                tooltipList.Add($"<MouseWheel><Drag> {sequencer.PanText}");
                tooltipList.Add($"<MouseRight><Drag> {SelectRectangleTooltip}");

                if (pattern != null)
                {
                    tooltipList.Add($"<MouseLeft><MouseLeft> {OrTooltip} <Shift><MouseLeft> {DeletePatternTooltip}");
                    tooltipList.Add($"<MouseRight> {sequencer.MoreOptionsText}");
                }

                if (sequencer.IsPatternSelected(location))
                {
                    tooltipList.Add($"<Drag> {MovePatternTooltip}");
                    tooltipList.Add($"<Ctrl><Drag> {ClonePatternTooltip}");
                }

                if (tooltipList.Count >= 3)
                {
                    var array = tooltipList.ToArray();
                    var numFirstLine = array.Length / 2;

                    tooltip =
                        string.Join(" - ", array, 0, numFirstLine) + "\n" +
                        string.Join(" - ", array, numFirstLine, array.Length - numFirstLine);
                }
                else
                {
                    tooltip = string.Join(" - ", tooltipList);
                }
            }

            App.SetToolTip(tooltip);
        }

        private int GetRowForChannel(int channelIdx)
        {
            return sequencer.GetRowForChannel(channelIdx);
        }

        private int GetPixelForNote(int note, bool scroll = true)
        {
            var x = (int)(note * (double)Viewport.NoteSizeX);

            if (scroll)
                x -= Viewport.ScrollX;

            return x;
        }

        private int GetPixelForNote(float note, bool scroll = true)
        {
            var x = (int)Math.Round(note * Viewport.NoteSizeX);

            if (scroll)
                x -= Viewport.ScrollX;

            return x;
        }

        private int GetNoteForPixel(int x)
        {
            x += Viewport.ScrollX;
            return (int)(x / (double)Viewport.NoteSizeX);
        }

        private int GetPatternIndexForCoord(int x)
        {
            var note = GetNoteForPixel(x);
            return Utils.Clamp(Song.PatternIndexFromAbsoluteNoteIndex(note), 0, Song.Length - 1);
        }

        internal void UpdateLayout()
        {
            UpdateRenderCoords();

            var sizeX = sequencer.Width - sequencer.ChannelNameSizeX - (sequencer.VerticalScrollBarVisible ? sequencer.ScrollBarThickness : 0);

            Move(sequencer.ChannelNameSizeX, sequencer.HeaderSizeY);
            Resize(sizeX, sequencer.ContentBottomY - sequencer.HeaderSizeY);
        }

        internal bool GetPatternForCoord(int x, int y, out PatternLocation location)
        {
            var noteIdx = GetNoteForPixel(x);

            if (noteIdx < 0 || noteIdx >= Song.GetPatternStartAbsoluteNoteIndex(Song.Length))
            {
                location = PatternLocation.Invalid;
                return false;
            }

            var channelIdx = sequencer.GetChannelIndexForCoord(y + sequencer.HeaderSizeY);

            if (channelIdx < 0)
            {
                location = PatternLocation.Invalid;
                return false;
            }

            location = new PatternLocation(
                channelIdx,
                Song.PatternIndexFromAbsoluteNoteIndex(noteIdx));

            return true;
        }

        private void ShowContextMenu(PatternLocation location, int x, int y)
        {
            var pattern = Song.GetPatternInstance(location);

            sequencer.SetHighlightedPattern(location);

            var menu = new List<ContextMenuOption>();

            if (Platform.IsMobile)
            {
                menu.Add(new ContextMenuOption(
                    "MenuPiano",
                    GoToPianoRollLabel,
                    () => sequencer.GotoPianoRoll(location)));
            }

            if (sequencer.HasSelection && !sequencer.IsPatternSelected(location))
            {
                if (Platform.IsMobile)
                {
                    menu.Add(new ContextMenuOption(
                        "MenuExpandSelection",
                        ExpandSelectionLabel,
                        () => sequencer.EnsureSelectionInclude(location)));
                }

                if (sequencer.IsSelectionOnChannel(location.ChannelIndex))
                {
                    menu.Add(new ContextMenuOption(
                        "MenuInstance",
                        InstanciateHereLabel,
                        () => sequencer.CopySelectionToCursor(false)));
                }

                menu.Add(new ContextMenuOption(
                    "MenuDuplicate",
                    DuplicateHereLabel,
                    () => sequencer.CopySelectionToCursor(true)));
            }

            if (sequencer.HasSelection)
            {
                menu.Add(new ContextMenuOption(
                    "MenuClearSelection",
                    ClearSelectionLabel,
                    () =>
                    {
                        sequencer.ClearSelection();
                        sequencer.ClearHighlightedPattern();
                    },
                    ContextMenuSeparator.Before));
            }

            if (pattern != null)
            {
                if (sequencer.IsPatternSelected(location) &&
                    sequencer.SelectionContainsMultiplePatterns())
                {
                    menu.Insert(0, new ContextMenuOption(
                        "MenuDeleteSelection",
                        DeleteSelectionLabel,
                        () => sequencer.DeleteSelection()));

                    menu.Add(new ContextMenuOption(
                        "MenuProperties",
                        SelectedPatternPropertiesLabel,
                        () => sequencer.EditPatternProperties(
                            new Point(x, y), pattern, location, true),
                        ContextMenuSeparator.Before));
                }
                else
                {
                    menu.Add(new ContextMenuOption(
                        "MenuProperties",
                        PatternPropertiesLabel,
                        () => sequencer.EditPatternProperties(
                            new Point(x, y), pattern, location, false),
                        ContextMenuSeparator.Before));
                }

                menu.Insert(0, new ContextMenuOption(
                    "MenuDelete",
                    DeletePatternLabel,
                    () => sequencer.DeletePattern(location)));
            }

            if (sequencer.IsPatternSelected(location))
            {
                if (sequencer.SelectedPatternsHaveSharedReferences())
                {
                    menu.Insert(1, new ContextMenuOption("MenuUnlink", MakePatternsUniqueLabel, () => sequencer.MakeSelectedPatternsUnique()));
                }

                if (sequencer.SelectionContainsMultiplePatterns())
                {
                    menu.Insert(1, new ContextMenuOption("MenuInstance",MergeIdenticalPatternsLabel, () => sequencer.MergeSelectedIdenticalPatterns()));
                }
            }

            if (menu.Count > 0)
                App.ShowContextMenuAsync(menu.ToArray());
        }

        internal void NotifyPatternChange(Pattern pattern)
        {
            if (pattern != null)
                patternCache?.Remove(pattern);

            MarkDirty();
        }

        public void ValidateIntegrity()
        {
            patternCache?.ValidateIntegrity();
        }
        
        internal void InvalidatePatternCache()
        {
            patternCache?.Clear();
            MarkDirty();
        }

        protected override void OnPointerMove(PointerEventArgs e)
        {
            base.OnPointerMove(e);
            UpdateToolTip(e);

            var patternIdx = GetPatternIndexForCoord(e.X);
            var rowIdx = (e.Y + Viewport.ScrollY) / Viewport.ChannelSizeY;

            if (rowIdx < 0 || rowIdx >= Viewport.VisibleRowCount)
                rowIdx = -1;

            sequencer.SetPatternAreaHover(rowIdx, patternIdx);

            var p = sequencer.WindowToControl(ControlToWindow(e.Position));
            sequencer.UpdatePointerCapture(p.X, p.Y);
            sequencer.ShowExpansionIcons = false;
        }

        protected override void OnPointerDown(PointerEventArgs e)
        {
            base.OnPointerDown(e);

            if (e.IsTouchEvent)
                return;

            if (!GetPatternForCoord(e.X, e.Y, out var location))
                return;

            if (e.Right)
            {
                if (ModifierKeys.IsAltDown && Settings.AltZoomAllowed)
                {
                    sequencer.StartAltZoom(this, e);
                }
                else
                {
                    e.DelayRightClick();
                }

                return;
            }

            if (!e.Left)
                return;

            var pattern = Song.GetPatternInstance(location);

            if (pattern == null)
            {
                sequencer.CreateNewPattern(location);
            }
            else if (ModifierKeys.IsShiftDown && !ModifierKeys.IsControlDown)
            {
                sequencer.DeletePattern(location);
            }
            else
            {
                sequencer.NotifyPatternClicked(location);

                if (!sequencer.IsPatternSelected(location))
                    sequencer.SetSelection(location, location);

                CapturePointer();

                sequencer.StartDragSelection(this, e, location.PatternIndex);
            }
        }

        protected override void OnPointerUp(PointerEventArgs e)
        {
            base.OnPointerUp(e);

            var selection = IsRectangleSelectionCapture;

            if (e.IsLongPress && !LegacySelectMode && selection && !sequencer.RectangleSelectionThresholdMet)
            {
                if (GetPatternForCoord(e.X, e.Y, out var location))
                    ShowContextMenu(location, e.X, e.Y);
            }

            if (sequencer.HasCaptureOperation)
            {
                var p = sequencer.WindowToControl(ControlToWindow(e.Position));
                sequencer.EndCaptureOperation(p.X, p.Y);
            }

            if (!selection && e.Right && GetPatternForCoord(e.X, e.Y, out var rightClickLocation))
            {
                ShowContextMenu(rightClickLocation, e.X, e.Y);
            }
        }
        
        protected override void OnPointerDownDelayed(PointerEventArgs e)
        {
            base.OnPointerDownDelayed(e);

            if (e.Right)
            {
                CapturePointer();
                sequencer.StartRectangleSelection(this, e);
            }
        }

        protected override void OnMouseDoubleClick(PointerEventArgs e)
        {
            base.OnMouseDoubleClick(e);

            if (!e.Left)
                return;

            if (!GetPatternForCoord(e.X, e.Y, out var location))
                return;

            var pattern = Song.GetPatternInstance(location);
            if (pattern != null)
                sequencer.DeletePattern(location);
        }

        protected override void OnMouseWheel(PointerEventArgs e)
        {
            base.OnMouseWheel(e);
            sequencer.HandleMouseWheel(this, e);
        }

        protected override void OnMouseHorizontalWheel(PointerEventArgs e)
        {
            base.OnMouseHorizontalWheel(e);
            sequencer.HandleMouseHorizontalWheel(this, e);
        }

        protected override void OnTouchClick(PointerEventArgs e)
        {
            base.OnTouchClick(e);

            if (!GetPatternForCoord(e.X, e.Y, out var location))
                return;

            var pattern = Song.GetPatternInstance(location);
            if (pattern == null)
            {
                sequencer.CreateNewPattern(location);
                sequencer.SetHighlightedPattern(location);
            }
            else
            {
                if (HighlightLocation == location)
                    sequencer.ClearHighlightedPattern();
                else
                    sequencer.SetHighlightedPattern(location);

                // Tapping inside the current selection only toggles highlight.
                if (sequencer.IsPatternSelected(location))
                    return;
            }

            sequencer.SetSelection(location, location);
        }

        protected override void OnTouchDoubleClick(PointerEventArgs e)
        {
            base.OnTouchDoubleClick(e);

            if (!GetPatternForCoord(e.X, e.Y, out var location))
                return;

            var pattern = Song.GetPatternInstance(location);
            if (pattern != null)
            {
                sequencer.DeletePattern(location);
                sequencer.ClearHighlightedPattern();
            }
        }

        protected override void OnTouchLongPress(PointerEventArgs e)
        {
            base.OnTouchLongPress(e);

            if (e.IsDoubleTapLongPress)
                return;

            sequencer.AbortCaptureOperation();

            if (LegacySelectMode)
            {
                if (GetPatternForCoord(e.X, e.Y, out var location))
                    ShowContextMenu(location, e.X, e.Y);
            }
            else
            {
                Platform.VibrateClick();

                sequencer.StartRectangleSelection(this, e);
                sequencer.ResetCaptureThreshold();
            }
        }
        
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateRenderCoords();
        }

        protected override void OnRender(Graphics g)
        {
            var vp = Viewport;

            if (Song == null || vp.NoteSizeX <= 0.0f)
                return;

            var b = g.BackgroundCommandList;
            var c = g.DefaultCommandList;
            var f = g.ForegroundCommandList;
            var minVisibleNoteIdx = Math.Max(GetNoteForPixel(0), 0);
            var maxVisibleNoteIdx = Math.Min(GetNoteForPixel(Width) + 1, Song.GetPatternStartAbsoluteNoteIndex(Song.Length));
            var minVisiblePattern = Utils.Clamp(Song.PatternIndexFromAbsoluteNoteIndex(minVisibleNoteIdx), 0, Song.Length);
            var maxVisiblePattern = Utils.Clamp(Song.PatternIndexFromAbsoluteNoteIndex(maxVisibleNoteIdx) + 1, 0, Song.Length);

            c.PushClipRegion(0, 0, Width, Height);

            for (var i = minVisiblePattern; i < maxVisiblePattern; i++)
            {
                var px = GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(i));
                var sx = GetPixelForNote(Song.GetPatternLength(i), false);
                var color = (i & 1) == 0 ? Theme.DarkGreyColor4 : Theme.DarkGreyColor2;

                b.FillRectangle(px, 0, px + sx, Height, color);
            }

            // Selection
            var valid = HasSelection;

            if (!sequencer.LegacySelectMode && sequencer.IsRectangleSelectionCapture && sequencer.CaptureSelectionMin.IsValid && sequencer.CaptureSelectionMax.IsValid)
            {
                var minRow = GetRowForChannel(sequencer.CaptureSelectionMin.ChannelIndex);
                var maxRow = GetRowForChannel(sequencer.CaptureSelectionMax.ChannelIndex);

                if (minRow >= 0 && maxRow >= 0)
                {
                    c.FillRectangle(
                        GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(sequencer.CaptureSelectionMin.PatternIndex)), vp.ChannelSizeY * minRow - vp.ScrollY,
                        GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(Math.Min(sequencer.CaptureSelectionMax.PatternIndex + 1, Song.Length))), vp.ChannelSizeY * (maxRow + 1) - vp.ScrollY,
                        SelectionColor);
                }
            }
            else if (!LegacySelectMode && IsColumnSelectionCapture && CaptureSelectionMin.PatternIndex >= 0 && CaptureSelectionMax.PatternIndex >= 0)
            {
                var minPatternIdx = Math.Max(sequencer.CaptureSelectionMin.PatternIndex, minVisiblePattern);
                var maxPatternIdx = Math.Min(sequencer.CaptureSelectionMax.PatternIndex, maxVisiblePattern - 1);

                for (var patternIdx = minPatternIdx; patternIdx <= maxPatternIdx; patternIdx++)
                {
                    c.FillRectangle(
                        GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(patternIdx)), 0,
                        GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(patternIdx + 1)), Height,
                        SelectionColor);
                }
            }
            else if (valid && LegacySelectMode)
            {
                if (IsTimeOnlySelection)
                {
                    c.FillRectangle(
                        GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(Math.Min(SelectionMin.PatternIndex,     Song.Length))), 0,
                        GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(Math.Min(SelectionMax.PatternIndex + 1, Song.Length))), Height,
                        SelectedPatternVisibleColor);
                }
                else if (sequencer.GetMinMaxSelectedRow(out var minSelRow, out var maxSelRow))
                {
                    c.FillRectangle(
                        GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(Math.Min(SelectionMin.PatternIndex,     Song.Length))), vp.ChannelSizeY *  minSelRow      - vp.ScrollY,
                        GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(Math.Min(SelectionMax.PatternIndex + 1, Song.Length))), vp.ChannelSizeY * (maxSelRow + 1) - vp.ScrollY,
                        SelectionColor);
                }
            }

            // Vertical lines
            for (int i = Math.Max(1, minVisiblePattern); i <= maxVisiblePattern; i++)
            {
                var px = GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(i));
                c.DrawLine(px, 0, px, Height, Theme.BlackColor);
            }

            c.PushTranslation(0, -vp.ScrollY);

            // Horizontal lines
            for (int i = 0, y = 0; i <= vp.VisibleRowCount; i++, y += vp.ChannelSizeY)
                c.DrawLine(0, y, Width, y, Theme.BlackColor);

            var dragCapture = IsDragSelectionCapture;
            if (dragCapture)
            {
                var patternIdxDelta = DragSelectionPatternDelta;
                var rowIdxDelta     = DragSelectionRowDelta;
                var dragX           = DragSelectionX - sequencer.ChannelNameSizeX;
                var songEndX        = GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(Song.Length));

                if (dragCapture && dragX >= 0 && dragX < songEndX)
                {
                    // The destination pattern under the drag anchor.
                    // No NEW state required for this.
                    var patternIdx = SelectionDragAnchorPatternIdx + patternIdxDelta;
                    var instance   = ModifierKeys.IsControlDown;
                    var duplicate  = instance && ModifierKeys.IsShiftDown;

                    var bmpCopy = (TextureAtlasRef)null;
                    var bmpSize = DpiScaling.ScaleCustom(bmpDuplicate.ElementSize.Width, bitmapScale);

                    if (rowIdxDelta != 0)
                        bmpCopy = (duplicate || instance) ? bmpDuplicate : bmpDuplicateMove;
                    else
                        bmpCopy = duplicate ? bmpDuplicate : (instance ? bmpMenuInstance : null);

                    if (LegacySelectMode)
                    {
                        if (sequencer.GetMinMaxSelectedRow(out var minSelRow, out var maxSelRow))
                        {
                            for (int j = minSelRow + rowIdxDelta; j <= maxSelRow + rowIdxDelta; j++)
                            {
                                if (j < 0 || j >= vp.VisibleRowCount)
                                    continue;

                                var y = j * vp.ChannelSizeY;

                                // Center.
                                var patternSizeX       = GetPixelForNote(Song.GetPatternLength(patternIdx), false);
                                var anchorOffsetLeftX  = (int)(patternSizeX * SelectionDragAnchorPatternXFraction);
                                var anchorOffsetRightX = (int)(patternSizeX * (1.0f - SelectionDragAnchorPatternXFraction));

                                c.PushTranslation(dragX, y);
                                c.FillAndDrawRectangle(-anchorOffsetLeftX, 0, -anchorOffsetLeftX + patternSizeX, vp.ChannelSizeY, SelectedPatternVisibleColor, Theme.BlackColor);

                                if (bmpCopy != null)
                                {
                                    c.DrawTextureAtlas(bmpCopy, -anchorOffsetLeftX + patternSizeX / 2 - bmpSize / 2, PatternHeaderSizeY / 2 + vp.ChannelSizeY / 2 - bmpSize / 2, bitmapScale, Theme.LightGreyColor1);
                                }

                                // Left side.
                                for (int p = patternIdx - 1; p >= SelectionMin.PatternIndex + patternIdxDelta && p >= 0; p--)
                                {
                                    patternSizeX = GetPixelForNote(Song.GetPatternLength(p), false);
                                    anchorOffsetLeftX += patternSizeX;

                                    c.FillAndDrawRectangle(-anchorOffsetLeftX, 0, -anchorOffsetLeftX + patternSizeX, vp.ChannelSizeY, SelectedPatternVisibleColor, Theme.BlackColor);

                                    if (bmpCopy != null)
                                    {
                                        c.DrawTextureAtlas(bmpCopy, -anchorOffsetLeftX + patternSizeX / 2 - bmpSize / 2, PatternHeaderSizeY / 2 + vp.ChannelSizeY / 2 - bmpSize / 2, bitmapScale, Theme.LightGreyColor1);
                                    }
                                }

                                // Right side.
                                for (int p = patternIdx + 1; p <= SelectionMax.PatternIndex + patternIdxDelta && p < Song.Length; p++)
                                {
                                    patternSizeX = GetPixelForNote(Song.GetPatternLength(p), false);

                                    c.FillAndDrawRectangle(anchorOffsetRightX, 0, anchorOffsetRightX + patternSizeX, vp.ChannelSizeY, SelectedPatternVisibleColor, Theme.BlackColor);

                                    if (bmpCopy != null)
                                    {
                                        c.DrawTextureAtlas(bmpCopy, anchorOffsetRightX + patternSizeX / 2 - bmpSize / 2, PatternHeaderSizeY / 2 + vp.ChannelSizeY / 2 - bmpSize / 2, bitmapScale, Theme.LightGreyColor1);
                                    }

                                    anchorOffsetRightX += patternSizeX;
                                }

                                c.PopTransform();
                            }
                        }
                    }
                    else
                    {
                        var anchorPatternSizeX = GetPixelForNote(Song.GetPatternLength(patternIdx), false);
                        var anchorOffsetLeftX = (int)(anchorPatternSizeX * SelectionDragAnchorPatternXFraction);
                        var anchorX = dragX - anchorOffsetLeftX;
                        var anchorGridX = GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(patternIdx));

                        foreach (var location in SelectedPatternLocations)
                        {
                            if (Song.GetPatternInstance(location) == null)
                                continue;

                            var sourceRow = GetRowForChannel(location.ChannelIndex);
                            if (sourceRow < 0)
                                continue;

                            var destRow = sourceRow + rowIdxDelta;
                            var destPattern = location.PatternIndex + patternIdxDelta;

                            if (destRow < 0 ||
                                destRow >= vp.VisibleRowCount ||
                                destPattern < 0 ||
                                destPattern >= Song.Length)
                            {
                                continue;
                            }

                            var destGridX = GetPixelForNote(
                                Song.GetPatternStartAbsoluteNoteIndex(destPattern));

                            var patternSizeX = GetPixelForNote(
                                Song.GetPatternLength(destPattern), false);

                            var x = anchorX + (destGridX - anchorGridX);
                            var y = destRow * vp.ChannelSizeY;

                            c.FillAndDrawRectangle(x, y, x + patternSizeX, y + vp.ChannelSizeY, SelectedPatternVisibleColor, Theme.BlackColor);

                            if (bmpCopy != null)
                            {
                                c.DrawTextureAtlas(bmpCopy, x + patternSizeX / 2 - bmpSize / 2, y + PatternHeaderSizeY / 2 + vp.ChannelSizeY / 2 - bmpSize / 2, bitmapScale, Theme.LightGreyColor1);
                            }
                        }
                    }
                }
            }

            // Patterns
            var patternCacheSizeY = vp.ChannelSizeY - PatternHeaderSizeY - 1;
            patternCache.Update(patternCacheSizeY);

            for (int pi = minVisiblePattern; pi < maxVisiblePattern; pi++)
            {
                var patternLen = Song.GetPatternLength(pi);
                var noteLen = Song.UsesFamiTrackerTempo ? 1 : Song.GetPatternNoteLength(pi);
                var px = GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(pi));
                var sx = GetPixelForNote(patternLen, false);

                c.PushTranslation(px, 0);

                // TODO : Dont draw channels that are not visible!
                for (int ci = 0, py = 0; ci < Song.Channels.Length; ci++)
                {
                    if (sequencer.IsChannelVisible(ci))
                    {
                        var location = new PatternLocation(ci, pi);
                        var pattern  = Song.GetPatternInstance(location);
                        if (pattern != null)
                        {
                            var bmp          = patternCache.GetOrAddPattern(pattern, patternLen, noteLen, out var u0, out var v0, out var u1, out var v1);
                            var isSelected   = sequencer.IsPatternSelected(location);
                            var newSelection = isSelected && !LegacySelectMode;
                            var count        = sequencer.GetSelectedPatternRefCount(pattern);

                            c.PushTranslation(0, py);
                            c.FillRectangleGradient(1, 1, sx, PatternHeaderSizeY, pattern.Color, pattern.Color.Scaled(0.8f), true, PatternHeaderSizeY);
                            c.FillRectangle(1, PatternHeaderSizeY, sx, vp.ChannelSizeY, Color.FromArgb(75, pattern.Color));
                            c.DrawLine(0, PatternHeaderSizeY, sx, PatternHeaderSizeY, newSelection ? Theme.WhiteColor : Theme.BlackColor);
                            c.DrawTexture(bmp, 1.0f, 1.0f + PatternHeaderSizeY, sx - 1, patternCacheSizeY, u0, v0, u1, v1);

                            if (isSelected)
                            {
                                if (!LegacySelectMode)
                                {
                                    c.DrawText(pattern.Name, Fonts.FontSmallBold, PatternNamePosX + 1, 1, Theme.BlackColor, TextFlags.Left | TextFlags.Middle | TextFlags.Clip, sx - PatternNamePosX, PatternHeaderSizeY + 1);
                                    f.FillRectangle(1, PatternHeaderSizeY, sx, vp.ChannelSizeY, HighlightedPatternColor);
                                }
                                    
                                f.DrawRectangle(0, 0, sx, vp.ChannelSizeY, LegacySelectMode ? Theme.LightGreyColor1 : Theme.WhiteColor, 3, true, true);
                            }

                            c.DrawText(pattern.Name, newSelection ? Fonts.FontSmallBold : Fonts.FontSmall, PatternNamePosX, 0, newSelection ? Theme.WhiteColor : Theme.BlackColor, TextFlags.Left | TextFlags.Middle | TextFlags.Clip, sx - PatternNamePosX, PatternHeaderSizeY + 1);
                            c.PopTransform();

                            if (!dragCapture && valid && count > 1)
                            {
                                // TODO: Use correct icon for mobile in place of original instantiate one, rather than resizing.
                                var scale   = bitmapScale / (Platform.IsMobile ? 4 : 1);
                                var bmpSize = DpiScaling.ScaleCustom(bmpMenuInstance.ElementSize.Width, scale);

                                f.PushTranslation(0, py);
                                f.DrawTextureAtlas(bmpMenuInstance, sx / 2 - bmpSize / 2 + scale, PatternHeaderSizeY / 2 + vp.ChannelSizeY / 2 - bmpSize / 2 + scale, scale, Theme.BlackColor);
                                f.DrawTextureAtlas(bmpMenuInstance, sx / 2 - bmpSize / 2, PatternHeaderSizeY / 2 + vp.ChannelSizeY / 2 - bmpSize / 2, scale, Theme.WhiteColor);
                                f.PopTransform();
                            }
                        }

                        if (Platform.IsMobile && HighlightLocation == location)
                        {
                            c.DrawRectangle(0, py, sx, py + vp.ChannelSizeY, Theme.WhiteColor, 3, true, true);
                        }

                        py += vp.ChannelSizeY;
                    }
                }

                c.PopTransform();
            }

            // Piano roll view rect
            if (App.GetPianoRollViewRange(out float pianoRollMinNoteIdx, out float pianoRollMaxNoteIdx, out var pianoRollChannelIndex))
            {
                var rowIdx = GetRowForChannel(pianoRollChannelIndex);
                if (rowIdx >= 0)
                {
                    var x0 = GetPixelForNote(pianoRollMinNoteIdx);
                    var x1 = GetPixelForNote(pianoRollMaxNoteIdx);

                    c.PushTranslation(x0, rowIdx * vp.ChannelSizeY);
                    c.DrawRectangle(1, PatternHeaderSizeY + 1, x1 - x0 - 1, vp.ChannelSizeY - 1, Theme.LightGreyColor2);
                    c.PopTransform();
                }
            }

            c.PopTransform();

            // Seek bar
            var seekX = GetPixelForNote(sequencer.SeekFrameToDraw);
            b.DrawLine(seekX, 0, seekX, Height, sequencer.SeekBarColor, 3);

            // Top line (beneath timeline).
            c.DrawLine(0, 0, Width, 0, Theme.BlackColor);
            c.PopClipRegion();
        }
    }
}