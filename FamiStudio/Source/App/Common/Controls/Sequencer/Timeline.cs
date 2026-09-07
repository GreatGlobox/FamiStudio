using System;

namespace FamiStudio
{
    internal class Timeline : Control
    {
        const int DefaultHeaderIconPosX = 3;
        const int DefaultHeaderIconPosY = 3;
        const int DefaultBarTextPosY    = 2;
        const int OutlineThickness      = 3;

        private readonly Sequencer sequencer;
        private int hoverPattern = -1;
        private int barTextPosY;
        private float[] seekGeometry;

        private TextureAtlasRef bmpLoopPoint;
        private int headerIconPosX;
        private int headerIconPosY;
        private float bitmapScale;

        public delegate void EditPatternSettingsDelegate(int patternIdx, Point position);
        public EditPatternSettingsDelegate EditPatternSettings;

        public PointerEventDelegate SeekDragRequested;
        public PointerEventDelegate ColumnSelectionRequested;

        LocalizedString ClearLoopPointLabel;
        LocalizedString SetLoopPointLabel;
        LocalizedString CustomPatternSettingsLabel; 
        LocalizedString SeekTooltip;
        LocalizedString SelectColumnTooltip;

        private Song Song => App?.SelectedSong;

        private Color SelectedPatternVisibleColor => sequencer.SelectedPatternVisibleColor;
        private Color SeekBarColor                => sequencer.SeekBarColor;

        private Sequencer.SequencerViewport Viewport => sequencer.Viewport;

        private bool HasTimelineSelection        => sequencer.HasTimelineSelection;
        private bool LegacySelectMode            => sequencer.LegacySelectMode;
        private bool IsColumnSelectionCapture    => sequencer.IsColumnSelectionCapture;
        private bool ColumnSelectionThresholdMet => sequencer.ColumnSelectionThresholdMet;

        private int ChannelNameSizeX    => sequencer.ChannelNameSizeX;
        private int HeaderSizeY         => sequencer.HeaderSizeY;
        private int SelectionMinPattern => sequencer.SelectionMinPattern;
        private int SelectionMaxPattern => sequencer.SelectionMaxPattern;

        private float SeekFrameToDraw  => sequencer.SeekFrameToDraw;

        internal Timeline(Sequencer sequencer)
        {
            this.sequencer = sequencer;

            Localization.Localize(this);

            ToolTip =
                $"<MouseLeft> {SeekTooltip} - " +
                $"<MouseRight> {sequencer.MoreOptionsText} - " +
                $"<MouseRight><Drag> {SelectColumnTooltip}\n" +
                $"<L><MouseLeft> {sequencer.PanText} - " +
                $"<MouseWheel><Drag> {sequencer.SetLoopPointText}";

            SupportsLongPress = true;
        }

        protected override void OnAddedToContainer()
        {
            base.OnAddedToContainer();

            bmpLoopPoint = ParentWindow.Graphics.GetTextureAtlasRef("LoopSmallFill");

            headerIconPosX = DpiScaling.ScaleForWindow(DefaultHeaderIconPosX);
            headerIconPosY = DpiScaling.ScaleForWindow(DefaultHeaderIconPosY);
            barTextPosY    = DpiScaling.ScaleForWindow(DefaultBarTextPosY);

            bitmapScale = Platform.IsMobile ? DpiScaling.ScaleForWindowFloat(0.5f) : 1.0f;
        }

        internal void UpdateLayout()
        {
            Move(ChannelNameSizeX, 0);
            Resize(sequencer.Width - ChannelNameSizeX, HeaderSizeY + 1);
        }

        public void SetHoverPattern(int pattern)
        {
            if (hoverPattern != pattern)
            {
                hoverPattern = pattern;
                MarkDirty();
            }
        }

        private int GetPixelForNote(float note)
        {
            return (int)Math.Round(note * Viewport.NoteSizeX) - Viewport.ScrollX;
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

        private bool IsPatternColumnSelected(int patternIdx)
        {
            return sequencer.IsPatternColumnSelected(patternIdx);
        }

        private bool IsPointOnSeekBar(int x)
        {
            var seekX = GetPixelForNote(SeekFrameToDraw);
            var margin = DpiScaling.ScaleForWindow(12);

            return Math.Abs(x - seekX) <= margin;
        }

        private void SetLoopPoint(int patternIdx)
        {
            App.UndoRedoManager.BeginTransaction(TransactionScope.Song, Song.Id);
            Song.SetLoopPoint(Song.LoopPoint == patternIdx ? -1 : patternIdx);
            App.UndoRedoManager.EndTransaction();
            MarkDirty();
        }

        private void ShowContextMenu(int x, int y)
        {
            var patternIdx = GetPatternIndexForCoord(x);
            if (patternIdx < 0)
                return;

            var isLoopPoint = Song.LoopPoint == patternIdx;

            App.ShowContextMenuAsync(new[]
            {
                new ContextMenuOption(
                    isLoopPoint ? "MenuClearLoopPoint" : "MenuLoopPoint",
                    isLoopPoint ? ClearLoopPointLabel : SetLoopPointLabel,
                    () => SetLoopPoint(patternIdx)),

                new ContextMenuOption(
                    "MenuCustomPatternSettings",
                    CustomPatternSettingsLabel,
                    () => EditPatternSettings?.Invoke(patternIdx, new Point(x, y)))
            });
        }

        protected override void OnPointerDownDelayed(PointerEventArgs e)
        {
            base.OnPointerDownDelayed(e);

            if (e.IsTouchEvent)
            {
                if (LegacySelectMode)
                    ColumnSelectionRequested?.Invoke(this, e);

                return;
            }

            if (e.Right)
            {
                CapturePointer();
                ColumnSelectionRequested?.Invoke(this, e);
            }
        }

        protected override void OnPointerDown(PointerEventArgs e)
        {
            base.OnPointerDown(e);

            if (e.IsTouchEvent)
            {
                if (IsPointOnSeekBar(e.X))
                {
                    CapturePointer();
                    SeekDragRequested?.Invoke(this, e);
                }

                return;
            }

            if (e.Left && Settings.SetLoopPointShortcut.IsKeyDown(ParentWindow))
            {
                var patternIdx = GetPatternIndexForCoord(e.X);
                if (patternIdx >= 0)
                    SetLoopPoint(patternIdx);

                return;
            }

            if (e.Left)
            {
                SeekDragRequested?.Invoke(this, e);
                return;
            }

            if (e.Right)
                e.DelayRightClick();
        }

        protected override void OnPointerUp(PointerEventArgs e)
        {
            base.OnPointerUp(e);
            var columnSelection = IsColumnSelectionCapture;

            if (e.Right || e.IsLongPress)
            {
                if (!ColumnSelectionThresholdMet)
                    ShowContextMenu(e.X, e.Y);
            }
            else if (e.IsTouchEvent)
            {
                App.SeekSong(GetNoteForPixel(e.X));
            }

            if (columnSelection)
            {
                var p = sequencer.WindowToControl(ControlToWindow(e.Position));
                sequencer.EndColumnSelection(p.X, p.Y);
            }
        }

        protected override void OnPointerMove(PointerEventArgs e)
        {
            base.OnPointerMove(e);
            sequencer.SetTimelineHover(GetPatternIndexForCoord(e.X));

            if (IsColumnSelectionCapture)
            {
                var p = sequencer.WindowToControl(ControlToWindow(e.Position));
                sequencer.UpdateColumnSelection(p.X, p.Y);
            }
        }

        protected override void OnPointerEnter(EventArgs e)
        {
            base.OnPointerEnter(e);
            App.SetToolTip(ToolTip);
        }

        protected override void OnTouchLongPress(PointerEventArgs e)
        {
            var x = e.X;
            var y = e.Y;

            if (e.IsDoubleTapLongPress)
                return;

            // Trigger the context menu if using legacy selection. Otherwise, start a selection.
            if (LegacySelectMode)
            {
                ShowContextMenu(x, y);
            }
            else
            {
                Platform.VibrateClick();
                ColumnSelectionRequested?.Invoke(this, e);
            }

            MarkDirty();
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

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            var headerSizeY = Height - 1;

            seekGeometry = new float[]
            {
                -headerSizeY / 2, 1,
                0, headerSizeY - 2,
                headerSizeY / 2, 1
            };
        }

        protected override void OnRender(Graphics g)
        {
            var vp = Viewport;
            if (Song == null || vp.NoteSizeX <= 0.0f)
                return;

            var minVisibleNoteIdx = Math.Max(GetNoteForPixel(0), 0);
            var maxVisibleNoteIdx = Math.Min(GetNoteForPixel(Width) + 1, Song.GetPatternStartAbsoluteNoteIndex(Song.Length));
            var minVisiblePattern = Utils.Clamp(Song.PatternIndexFromAbsoluteNoteIndex(minVisibleNoteIdx), 0, Song.Length);
            var maxVisiblePattern = Utils.Clamp(Song.PatternIndexFromAbsoluteNoteIndex(maxVisibleNoteIdx) + 1, 0, Song.Length);

            var c = g.DefaultCommandList;
            var b = g.BackgroundCommandList;

            c.DrawLine(0, HeaderSizeY, Width, HeaderSizeY, Theme.BlackColor);

            // Background.
            for (int i = minVisiblePattern; i < maxVisiblePattern; i++)
            {
                var px = GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(i));
                var sx = (int)(Song.GetPatternLength(i) * (double)vp.NoteSizeX);
                var color = i == hoverPattern ? Theme.MediumGreyColor1: ((i & 1) == 0 ? Theme.DarkGreyColor4 : Theme.DarkGreyColor2);

                b.FillRectangle(px, 0, px + sx, HeaderSizeY, color);
            }

            // Selection.
            if (HasTimelineSelection && Song.Length > 0)
            {
                if (LegacySelectMode)
                {
                    c.FillRectangle(
                        GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(Math.Min(SelectionMinPattern,     Song.Length))), 0,
                        GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(Math.Min(SelectionMaxPattern + 1, Song.Length))), HeaderSizeY,
                        SelectedPatternVisibleColor);
                }
                else
                {
                    for (var i = minVisiblePattern; i < maxVisiblePattern; i++)
                    {
                        if (!IsPatternColumnSelected(i))
                            continue;

                        var px = GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(i));
                        var sx = (int)(Song.GetPatternLength(i) * (double)vp.NoteSizeX);

                        c.FillRectangle(px, 0, px + sx, HeaderSizeY, SelectedPatternVisibleColor);
                        c.DrawRectangle(px, 1, px + sx, HeaderSizeY - 1, Theme.WhiteColor, OutlineThickness, true);
                    }
                }
            }

            c.DrawLine(0, 0, Width, 0, Theme.BlackColor);

            // Vertical lines.
            for (int i = Math.Max(1, minVisiblePattern); i <= maxVisiblePattern; i++)
            {
                var px = GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(i));
                b.DrawLine(px, 0, px, HeaderSizeY, Theme.BlackColor);
            }

            // Pattern indexes.
            for (int i = minVisiblePattern; i < maxVisiblePattern; i++)
            {
                var patternLen = Song.GetPatternLength(i);
                var px = GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(i));
                var sx = (int)(patternLen * (double)vp.NoteSizeX);

                if (sx > 0)
                {
                    c.PushTranslation(px, 0);

                    var text = (i + 1).ToString();
                    var selected = IsPatternColumnSelected(i);

                    if (Song.PatternHasCustomSettings(i))
                        text += "*";

                    c.DrawText(text, Fonts.FontMedium, 0, barTextPosY, selected ? Theme.WhiteColor : Theme.LightGreyColor1, TextFlags.Center | TextFlags.Clip, sx);

                    if (i == Song.LoopPoint)
                        c.DrawTextureAtlas(bmpLoopPoint, headerIconPosX, headerIconPosY, bitmapScale, Theme.LightGreyColor1);

                    c.PopTransform();
                }
            }

            // Seek bar.
            var seekX = GetPixelForNote(SeekFrameToDraw);

            c.PushTranslation(seekX, 0);
            c.FillAndDrawGeometry(seekGeometry, SeekBarColor, Theme.BlackColor, 1, true);
            c.PopTransform();

            base.OnRender(g);
        }
    }
}