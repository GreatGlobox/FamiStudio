using System;

namespace FamiStudio
{
    internal class Timeline : Container
    {
        const int DefaultHeaderIconPosX = 3;
        const int DefaultHeaderIconPosY = 3;
        const int DefaultBarTextPosY    = 2;
        const int OutlineThickness      = 3;

        private readonly Sequencer sequencer;
        private int headerSizeY;
        private int hoverPattern = -1;
        private int barTextPosY;
        private float[] seekGeometry;

        private TextureAtlasRef bmpLoopPoint;
        private int headerIconPosX;
        private int headerIconPosY;
        private float bitmapScale;
        private bool resizing;

        public delegate void EditPatternSettingsDelegate(int patternIdx, Point position);
        public EditPatternSettingsDelegate EditPatternSettings;

        public PointerEventDelegate SeekDragRequested;
        public PointerEventDelegate ColumnSelectionRequested;

        LocalizedString ClearLoopPointLabel;
        LocalizedString SetLoopPointLabel;
        LocalizedString CustomPatternSettingsLabel;

        private Song Song => App?.SelectedSong;

        internal Timeline(Sequencer sequencer)
        {
            this.sequencer = sequencer;

            Localization.Localize(this);
            SetTickEnabled(true);
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

        public void SetHoverPattern(int pattern)
        {
            if (hoverPattern != pattern)
            {
                hoverPattern = pattern;
                MarkDirty();
            }
        }

        private int GetPixelForNote(int note)
        {
            return (int)(note * (double)sequencer.NoteSizeX) - sequencer.ViewScrollX;
        }

        private int GetNoteForPixel(int x)
        {
            x += sequencer.ViewScrollX;
            return (int)(x / (double)sequencer.NoteSizeX);
        }

        private int GetPatternIndexForCoord(int x)
        {
            var note = GetNoteForPixel(x);
            return Utils.Clamp(Song.PatternIndexFromAbsoluteNoteIndex(note), 0, Song.Length - 1);
        }

        private bool IsPatternSelected(int patternIdx)
        {
            return sequencer.IsPatternColumnSelected(patternIdx);
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
            if (e.Right)
                ColumnSelectionRequested?.Invoke(this, e);
        }

        protected override void OnPointerDown(PointerEventArgs e)
        {
            if (e.IsTouchEvent)
            {
                if (Math.Abs(GetPixelForNote(sequencer.SeekFrameToDraw) - e.X) < headerSizeY)
                    SeekDragRequested?.Invoke(this, e);
                else
                    ColumnSelectionRequested?.Invoke(this, e);

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
            if (e.Right)
                ShowContextMenu(e.X, e.Y);
        }

        protected override void OnTouchClick(PointerEventArgs e)
        {
            // TODO: Can this not just call OnPointerDown?
            App.SeekSong(GetNoteForPixel(e.X));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);

            headerSizeY = Height - 1;

            seekGeometry = new float[]
            {
                -headerSizeY / 2, 1,
                0, headerSizeY - 2,
                headerSizeY / 2, 1
            };
        }

        protected override void OnRender(Graphics g)
        {
            if (Song == null || sequencer.NoteSizeX <= 0.0f)
                return;

            var minVisibleNoteIdx = Math.Max(GetNoteForPixel(0), 0);
            var maxVisibleNoteIdx = Math.Min(GetNoteForPixel(Width) + 1, Song.GetPatternStartAbsoluteNoteIndex(Song.Length));
            var minVisiblePattern = Utils.Clamp(Song.PatternIndexFromAbsoluteNoteIndex(minVisibleNoteIdx), 0, Song.Length);
            var maxVisiblePattern = Utils.Clamp(Song.PatternIndexFromAbsoluteNoteIndex(maxVisibleNoteIdx) + 1, 0, Song.Length);

            var c = g.DefaultCommandList;
            var b = g.BackgroundCommandList;

            c.DrawLine(0, headerSizeY, Width, headerSizeY, Theme.BlackColor);

            // Background.
            for (int i = minVisiblePattern; i < maxVisiblePattern; i++)
            {
                var px = GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(i));
                var sx = (int)(Song.GetPatternLength(i) * (double)sequencer.NoteSizeX);

                var color = !resizing && i == hoverPattern ? Theme.MediumGreyColor1  : ((i & 1) == 0 ? Theme.DarkGreyColor4 : Theme.DarkGreyColor2);

                b.FillRectangle(px, 0, px + sx, headerSizeY, color);
            }

            // Selection.
            if (sequencer.HasTimelineSelection && Song.Length > 0)
            {
                if (sequencer.LegacySelectMode)
                {
                    c.FillRectangle(
                        GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(Math.Min(sequencer.SelectionMinPattern,     Song.Length))), 0,
                        GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(Math.Min(sequencer.SelectionMaxPattern + 1, Song.Length))), headerSizeY,
                        sequencer.SelectedPatternVisibleColor);
                }
                else
                {
                    for (var i = minVisiblePattern; i < maxVisiblePattern; i++)
                    {
                        if (!sequencer.IsPatternColumnSelected(i))
                            continue;

                        var px = GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(i));
                        var sx = (int)(Song.GetPatternLength(i) * (double)sequencer.NoteSizeX);

                        c.FillRectangle(px, 0, px + sx, headerSizeY, sequencer.SelectedPatternVisibleColor);
                        c.DrawRectangle(px, 1, px + sx, headerSizeY - 1, Theme.WhiteColor, OutlineThickness, true);
                    }
                }
            }

            c.DrawLine(0, 0, Width, 0, Theme.BlackColor);

            // Vertical lines.
            for (int i = Math.Max(1, minVisiblePattern); i <= maxVisiblePattern; i++)
            {
                var px = GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(i));
                b.DrawLine(px, 0, px, headerSizeY, Theme.BlackColor);
            }

            // Pattern indexes.
            for (int i = minVisiblePattern; i < maxVisiblePattern; i++)
            {
                var patternLen = Song.GetPatternLength(i);
                var px = GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(i));
                var sx = (int)(patternLen * (double)sequencer.NoteSizeX);

                if (sx > 0)
                {
                    c.PushTranslation(px, 0);

                    var text = (i + 1).ToString();
                    var selected = IsPatternSelected(i);

                    if (Song.PatternHasCustomSettings(i))
                        text += "*";

                    c.DrawText(text, Fonts.FontMedium, 0, barTextPosY, selected ? Theme.WhiteColor : Theme.LightGreyColor1, TextFlags.Center | TextFlags.Clip, sx);

                    if (i == Song.LoopPoint)
                        c.DrawTextureAtlas(bmpLoopPoint, headerIconPosX, headerIconPosY, bitmapScale, Theme.LightGreyColor1);

                    c.PopTransform();
                }
            }

            // Seek bar.
            var seekX = GetPixelForNote(sequencer.SeekFrameToDraw);

            c.PushTranslation(seekX, 0);
            c.FillAndDrawGeometry(seekGeometry, sequencer.SeekBarColor, Theme.BlackColor, 1, true);
            c.PopTransform();

            base.OnRender(g);
        }
    }
}