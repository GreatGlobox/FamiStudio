using System;

namespace FamiStudio
{
    public class PianoRollTimeline : Control
    {
        const int DefaultPianoSizeX   = 94;
        const int DefaultBeatTextPosX = 3;

        const float DefaultZoomWaveTime = 0.25f;

        public enum EditMode
        {
            Channel,
            Envelope,
            Arpeggio,
            Dpcm
        }

        private PianoRoll pianoRoll;
        private EditMode editMode;

        private int beatTextPosX;
        private int fontSmallCharSizeX;

        private float bitmapScale = 1.0f;
        
        private float[] seekGeometry;

        Color loopSectionColor = Color.FromArgb( 64, Theme.BlackColor);

        TextureAtlasRef bmpLoopSmallFill;
        TextureAtlasRef bmpReleaseSmallFill;
        TextureAtlasRef bmpEnvResize;

        private Song Song => App?.SelectedSong;

        private Color SelectionBgVisibleColor => pianoRoll.SelectionBgVisibleColor;

        private int ScrollX              => pianoRoll.ViewScrollX;
        private int EditChannel          => pianoRoll.EditChannel;
        private int SelectionMinX        => pianoRoll.SelectionMinX;
        private int SelectionMaxX        => pianoRoll.SelectionMaxX;
        private int HeaderAndEffectSizeY => pianoRoll.HeaderAndEffectSizeY;
        private int EditEnvelopeType     => pianoRoll.EditEnvelopeType;

        private int HoverNoteIndex => pianoRoll.HoverNoteIndex;
        private int HoverNoteCount => pianoRoll.HoverNoteCount;

        private float NoteSizeX => pianoRoll.NoteSizeX;
        private float Zoom      => pianoRoll.Zoom;

        // TODO: Check which of these should be here instead of being forwarded.
        private Envelope EditEnvelope => pianoRoll.CurrentEditEnvelope;
        private Envelope EditRepeatEnvelope => pianoRoll.EditRepeatEnvelope;
        private Instrument EditInstrument => pianoRoll.EditInstrument;
        private Arpeggio EditArpeggio => pianoRoll.EditArpeggio;
        private DPCMSample EditSample => pianoRoll.EditSample;

        internal int EnvelopeResizeWidth => bmpEnvResize.ElementSize.Width;

        LocalizedString SeekTooltip;
        LocalizedString SelectTooltip;
        LocalizedString SelectPatternContext;
        LocalizedString SelectAllContext;

        internal PianoRollTimeline(PianoRoll pianoRoll)
        {
            this.pianoRoll = pianoRoll;
        }

        protected override void OnAddedToContainer()
        {
            base.OnAddedToContainer();
            Localization.Localize(this);

            var g = ParentWindow.Graphics;

            bmpLoopSmallFill    = g.GetTextureAtlasRef("LoopSmallFill");
            bmpReleaseSmallFill = g.GetTextureAtlasRef("ReleaseSmallFill");
            bmpEnvResize        = g.GetTextureAtlasRef("EnvResize");

            ToolTip =
                $"<MouseLeft> {SeekTooltip} - " +
                $"<MouseRight> {pianoRoll.PianoRollMoreOptionsTooltip} - " +
                $"<MouseRight><Drag> {SelectTooltip}";

            beatTextPosX = DpiScaling.ScaleForWindow(DefaultBeatTextPosX);
            fontSmallCharSizeX = ParentWindow.Fonts.FontSmall.MeasureString("0", false);

            if (Platform.IsMobile)
                bitmapScale = 0.5f;
        }

        internal void UpdateLayout()
        {
            Move(pianoRoll.PianoWidth, 0);
            Resize(pianoRoll.Width - pianoRoll.PianoWidth, pianoRoll.HeaderSizeY);

            seekGeometry = new float[]
            {
                -Height / 4, 1,
                0, Height / 2 - 2,
                Height / 4, 1
            };
        }

        private Color GetSeekBarColor()
        {
            if (editMode == EditMode.Channel)
            {
                if (App.IsRecording)
                {
                    return Theme.DarkRedColor;
                }
                else if (App.IsSeeking)
                {
                    return Theme.Lighten(Theme.YellowColor, (int)(Math.Abs(Math.Sin(Platform.TimeSeconds() * 12.0)) * 75));
                }
            }

            return Theme.YellowColor;
        }

        private void ForEachWaveTimecode(Graphics g, Action<float, float, int, int> function)
        {
            var textSize  = g.MeasureString("99.999", ParentWindow.Fonts.FontMedium);
            var numLabels = Math.Floor(Width / textSize);

            var minVisibleWaveTime = GetWaveTimeForPixel(0);
            var maxVisibleWaveTime = GetWaveTimeForPixel(Width);

            for (int i = 2; i >= 0; i--)
            {
                var divTime = Math.Pow(10.0, -i - 1);

                var minLabel = (int)Math.Floor(minVisibleWaveTime / divTime);
                var maxLabel = (int)Math.Ceiling(maxVisibleWaveTime / divTime);

                if (i == 0 || numLabels > maxLabel - minLabel)
                {
                    for (var t = minLabel; t <= maxLabel; t++)
                    {
                        var time = t * divTime;
                        var x = GetPixelForWaveTime((float)time, ScrollX);

                        function((float)time, x, i, t);
                    }

                    break;
                }
            }
        }

        private NoteLocation GetLocationForCoord(int x)
        {
            var noteIndex = GetAbsoluteNoteIndexForPixelX(x);
            return NoteLocation.FromAbsoluteNoteIndex(Song, noteIndex);
        }

        private bool ShowContextMenu(int x, int y)
        {
            var location = GetLocationForCoord(x);

            if (location.IsInSong(Song))
            {
                App.ShowContextMenuAsync(new[]
                {
                    new ContextMenuOption("MenuSelectPattern", SelectPatternContext, () => pianoRoll.SelectPattern(location.PatternIndex)),
                    new ContextMenuOption("MenuSelectAll",     SelectAllContext,     () => pianoRoll.SelectAll()),
                });
            }

            return false;
        }

        private int GetPixelXForAbsoluteNoteIndex(int n, bool scroll = true)
        {
            var x = (int)(n * (double)NoteSizeX);

            if (scroll)
                x -= ScrollX;

            return x;
        }

        private int GetPixelXForAbsoluteNoteIndex(float n, bool scroll = true)
        {
            var x = (int)Math.Round(n * NoteSizeX);

            if (scroll)
                x -= ScrollX;

            return x;
        }
        
        private int GetAbsoluteNoteIndexForPixelX(int x)
        {
            x += ScrollX;
            return (int)(x / (double)NoteSizeX);
        }

        private float GetPixelForWaveTime(float time, int scroll = 0)
        {
            var viewTime = DefaultZoomWaveTime / Zoom;
            return time / viewTime * Width - scroll;
        }

        private float GetWaveTimeForPixel(int x)
        {
            var viewTime = DefaultZoomWaveTime / Zoom;
            return (x + ScrollX) / (float)Width * viewTime;
        }

        private float GetWaveTimeForSample(int sample, bool end)
        {
            return pianoRoll.GetWaveTimeForSample(sample, end);
        }

        public void SetEditMode(EditMode mode)
        {
            if (editMode == mode)
                return;

            editMode = mode;
            MarkDirty();
        }
        
        public bool CanEnvelopeDisplayFrame()
        {
            return EditEnvelopeType != EnvelopeType.FdsModulation && EditEnvelopeType != EnvelopeType.WaveformRepeat;
        }

        protected override void OnPointerEnter(EventArgs e)
        {
            App.SetToolTip(tooltip);
        }

        protected override void OnPointerDown(PointerEventArgs e)
        {
            base.OnPointerDown(e);

            var middle = e.Middle || (e.Left && ModifierKeys.IsAltDown && Settings.AltLeftForMiddle);

            if (middle)
            {
                CapturePointer();

                var p = pianoRoll.WindowToControl(ControlToWindow(e.Position));
                pianoRoll.StartTimelinePan(p.X, p.Y);
                return;
            }

            var pos = pianoRoll.WindowToControl(ControlToWindow(e.Position));

            if (editMode == EditMode.Envelope || editMode == EditMode.Arpeggio)
            {
                if (pianoRoll.HandleTimelineEnvelopePointerDown(pos.X, pos.Y, e.Left, e.Right))
                {
                    CapturePointer();
                    return;
                }

                if (e.Right)
                    e.DelayRightClick();

                return;
            }

            if (editMode == EditMode.Channel && e.Left)
            {
                pianoRoll.StartTimelineSeek(pos.X, pos.Y);
                return;
            }

            if (e.Right)
                e.DelayRightClick();
        }

        protected override void OnPointerUp(PointerEventArgs e)
        {
            base.OnPointerUp(e);

            if (e.Right && !pianoRoll.TimelineCaptureThresholdMet)
            {
                if (editMode == EditMode.Channel)
                {
                    ShowContextMenu(e.X, e.Y);
                }
                else if ((editMode == EditMode.Envelope || editMode == EditMode.Arpeggio) && !pianoRoll.IsTimelineEnvelopeCapture)
                {
                    var p = pianoRoll.WindowToControl(ControlToWindow(e.Position));
                    pianoRoll.HandleContextMenuEnvelope(p.X, p.Y);
                }
            }

            if (pianoRoll.IsTimelineSeekCapture ||
                pianoRoll.IsTimelineColumnSelectionCapture ||
                pianoRoll.IsTimelineEnvelopeCapture)
            {
                var p = pianoRoll.WindowToControl(ControlToWindow(e.Position));
                pianoRoll.EndTimelineCapture(p.X, p.Y);
            }
            else if (pianoRoll.IsTimelinePanCapture)
            {
                pianoRoll.EndTimelinePan();
            }
        }

        protected override void OnPointerMove(PointerEventArgs e)
        {
            base.OnPointerMove(e);

            var p = pianoRoll.WindowToControl(ControlToWindow(e.Position));

            if (pianoRoll.IsTimelineColumnSelectionCapture || pianoRoll.IsTimelineEnvelopeCapture)
            {
                pianoRoll.UpdateTimelineCapture(p.X, p.Y);
            }
            else if (pianoRoll.IsTimelinePanCapture)
            {
                pianoRoll.UpdateTimelinePan(p.X, p.Y);
            }
        }

        protected override void OnPointerDownDelayed(PointerEventArgs e)
        {
            base.OnPointerDownDelayed(e);

            if (e.Right)
            {
                CapturePointer();

                var p = pianoRoll.WindowToControl(ControlToWindow(e.Position));
                pianoRoll.StartTimelineSelection(p.X, p.Y);
            }
        }

        protected override void OnRender(Graphics g)
        {
            var b = g.BackgroundCommandList;
            var c = g.DefaultCommandList;
            var f = g.ForegroundCommandList;
            var fonts = ParentWindow.Fonts;
            var halfHeight = height / 2;

            var minVisibleNoteIdx = Math.Max(GetAbsoluteNoteIndexForPixelX(0), 0);
            var maxVisibleNoteIdx = Math.Min(GetAbsoluteNoteIndexForPixelX(Width) + 1, Song.GetPatternStartAbsoluteNoteIndex(Song.Length));
            var minVisiblePattern = Utils.Clamp(Song.PatternIndexFromAbsoluteNoteIndex(minVisibleNoteIdx),     0, Song.Length);
            var maxVisiblePattern = Utils.Clamp(Song.PatternIndexFromAbsoluteNoteIndex(maxVisibleNoteIdx) + 1, 0, Song.Length);

            c.PushClipRegion(0, 0, Width, Height);

            if (Platform.IsDesktop && pianoRoll.IsMaximized)
                c.DrawLine(0, 0, width, 0, Theme.BlackColor);

            if ((editMode == EditMode.Envelope || editMode == EditMode.Arpeggio) && EditEnvelope != null)
            {
                var env = EditEnvelope;
                var rep = EditRepeatEnvelope;
                var iconPos = (halfHeight - DpiScaling.ScaleCustom(bmpLoopSmallFill.ElementSize.Width, bitmapScale)) / 2;

                c.PushTranslation(0, halfHeight);

                if (env.ChunkLength > 1)
                    c.FillRectangle(0, 0, GetPixelXForAbsoluteNoteIndex(env.Length), halfHeight, EditInstrument.Color);

                if (env.Loop >= 0)
                {
                    c.PushTranslation(GetPixelXForAbsoluteNoteIndex(env.Loop), 0);
                    b.FillRectangle(0, 0, GetPixelXForAbsoluteNoteIndex(((env.Release >= 0 ? env.Release : env.Length) - env.Loop), false), HeaderAndEffectSizeY, rep != null ? loopSectionColor : Theme.DarkGreyColor5);
                    b.DrawLine(0, 0, 0, HeaderAndEffectSizeY, Theme.BlackColor);
                    c.DrawTextureAtlas(bmpLoopSmallFill, iconPos + 1, iconPos, bitmapScale, rep != null ? Theme.BlackColor : Theme.LightGreyColor1);
                    c.PopTransform();
                }
                if (env.Release >= 0)
                {
                    c.PushTranslation(GetPixelXForAbsoluteNoteIndex(env.Release), 0);
                    b.DrawLine(0, 0, 0, HeaderAndEffectSizeY, Theme.BlackColor);
                    c.DrawTextureAtlas(bmpReleaseSmallFill, iconPos + 1, iconPos, bitmapScale, rep != null ? Theme.BlackColor : Theme.LightGreyColor1);
                    c.PopTransform();
                }
                if (env.Length > 0)
                {
                    c.PushTranslation(GetPixelXForAbsoluteNoteIndex(env.Length), 0);
                    b.DrawLine(0, 0, 0, HeaderAndEffectSizeY, Theme.BlackColor);
                    c.PopTransform();
                }

                c.PopTransform();

                if (env.CanResize)
                {
                    c.PushTranslation(GetPixelXForAbsoluteNoteIndex(env.Length), 0);
                    c.DrawTextureAtlas(bmpEnvResize, iconPos + 1, iconPos, bitmapScale, Theme.LightGreyColor1);
                    c.PopTransform();
                }

                if (HoverNoteIndex >= 0 && HoverNoteIndex < env.Length)
                {
                    var x0 = GetPixelXForAbsoluteNoteIndex(HoverNoteIndex + 0);
                    var x1 = GetPixelXForAbsoluteNoteIndex(HoverNoteIndex + 1);
                    c.PushTranslation(x0, 0);
                    c.FillRectangle(0, 0, x1 - x0, halfHeight, Theme.MediumGreyColor1);
                    c.PopTransform();
                }

                pianoRoll.DrawSelectionRect(c, height);

                // Draw the header bars
                for (int n = 0; n <= env.Length; n++)
                {
                    int x = GetPixelXForAbsoluteNoteIndex(n);
                    if (x != 0)
                    {
                        b.DrawLine(x, 0, x, halfHeight, Theme.BlackColor, env.ChunkLength > 1 && n % env.ChunkLength == 0 && n != env.Length ? 3 : 1);
                    }
                    if (n != env.Length)
                    {
                        if (env.ChunkLength > 1 && n % env.ChunkLength == 0)
                        {
                            if (x != 0)
                                c.DrawLine(x, halfHeight, x, height, Theme.BlackColor, 3);
                            int x1 = GetPixelXForAbsoluteNoteIndex(n + env.ChunkLength);
                            c.DrawText((n / env.ChunkLength).ToString(), fonts.FontMedium, x, halfHeight - 1, Theme.BlackColor, TextFlags.MiddleCenter, x1 - x, halfHeight);
                        }

                        var label = (EditEnvelopeType == EnvelopeType.N163Waveform ? EditInstrument.N163WavePos : 0) + (env.ChunkLength > 1 ? n % env.ChunkLength : n);
                        var labelString = label.ToString();
                        if (labelString.Length * fontSmallCharSizeX + 2 < NoteSizeX)
                            c.DrawText(labelString, fonts.FontMedium, x, 0, Theme.LightGreyColor1, TextFlags.MiddleCenter, NoteSizeX, halfHeight - 1);
                    }
                }

                c.DrawLine(0, halfHeight - 1, Width, halfHeight - 1, Theme.BlackColor);
            }
            else if (editMode == EditMode.Channel)
            {
                // Draw colored header
                for (int p = minVisiblePattern; p < maxVisiblePattern; p++)
                {
                    var pattern = Song.Channels[EditChannel].PatternInstances[p];
                    if (pattern != null)
                    {
                        int sx = GetPixelXForAbsoluteNoteIndex(Song.GetPatternLength(p), false);
                        int px = GetPixelXForAbsoluteNoteIndex(Song.GetPatternStartAbsoluteNoteIndex(p), true);
                        c.FillRectangle(px, halfHeight, px + sx, height, pattern.Color);
                    }
                }

                // Hover
                if (HoverNoteIndex >= 0 && HoverNoteIndex < Song.GetPatternStartAbsoluteNoteIndex(Song.Length))
                {
                    int x0 = GetPixelXForAbsoluteNoteIndex(HoverNoteIndex, true);
                    int x1 = GetPixelXForAbsoluteNoteIndex(HoverNoteIndex + HoverNoteCount, true);
                    c.FillRectangle(x0, 0, x1, halfHeight - 1, Theme.MediumGreyColor1);
                }

                // Selection
                pianoRoll.DrawSelectionRect(c, height, false, true);

                var beatLabelSizeX = g.MeasureString("88.88", fonts.FontMedium);

                // Draw the header bars
                for (int p = minVisiblePattern; p < maxVisiblePattern; p++)
                {
                    var patternLen = Song.GetPatternLength(p);

                    var sx = GetPixelXForAbsoluteNoteIndex(patternLen, false);
                    var px = GetPixelXForAbsoluteNoteIndex(Song.GetPatternStartAbsoluteNoteIndex(p), true);
                    if (p != 0)
                        c.DrawLine(px, 0, px, height, Theme.BlackColor, 3);

                    var pattern = Song.Channels[EditChannel].PatternInstances[p];
                    var beatLen = Song.GetPatternBeatLength(p);
                    var beatSizeX = GetPixelXForAbsoluteNoteIndex(beatLen, false);

                    // Is there enough room to draw beat labels?
                    if ((beatSizeX + beatTextPosX) > beatLabelSizeX)
                    {
                        var numBeats = (int)Math.Ceiling(patternLen / (float)beatLen);
                        for (int i = 0; i < numBeats; i++)
                            c.DrawText($"{p + 1}.{i + 1}", fonts.FontMedium, px + beatTextPosX + beatSizeX * i, 0, Theme.LightGreyColor1, TextFlags.Middle, 0, halfHeight - 1);
                    }
                    else
                    {
                        c.DrawText((p + 1).ToString(), fonts.FontMedium, px, 0, Theme.LightGreyColor1, TextFlags.MiddleCenter, sx, halfHeight - 1);
                    }

                    if (pattern != null)
                        c.DrawText(pattern.Name, fonts.FontMedium, px, halfHeight, Theme.BlackColor, TextFlags.MiddleCenter | TextFlags.Clip, sx, halfHeight - 1);
                }

                int maxX = GetPixelXForAbsoluteNoteIndex(Song.GetPatternStartAbsoluteNoteIndex(maxVisiblePattern));
                c.DrawLine(maxX, 0, maxX, Height, Theme.BlackColor, 3);
                c.DrawLine(0, halfHeight - 1, Width, halfHeight - 1, Theme.BlackColor);
            }
            else if (editMode == EditMode.Dpcm)
            {
                // Selection rectangle
                if (pianoRoll.IsSelectionValid())
                {
                    c.FillRectangle(
                        GetPixelForWaveTime(GetWaveTimeForSample(SelectionMinX, true),  ScrollX), 0,
                        GetPixelForWaveTime(GetWaveTimeForSample(SelectionMaxX, false), ScrollX), height, SelectionBgVisibleColor);
                }

                ForEachWaveTimecode(g, (time, x, level, idx) =>
                {
                    if (time != 0.0f)
                        c.DrawText(time.ToString($"F{level + 1}"), fonts.FontMedium, x - 100, 0, Theme.LightGreyColor1, TextFlags.MiddleCenter, 200, height - 1);
                });

                // Processed Range
                c.FillRectangle(
                    GetPixelForWaveTime(EditSample.ProcessedStartTime, ScrollX), 0,
                    GetPixelForWaveTime(EditSample.ProcessedEndTime,   ScrollX), height, Color.FromArgb(64, EditSample.Color));
            }

            c.DrawLine(0, height - 1, Width, height - 1, Theme.BlackColor);

            if (editMode == EditMode.Channel || pianoRoll.CanDisplayEnvelopePlayhead)
            {
                var seekFrame = editMode == EditMode.Envelope || editMode == EditMode.Arpeggio ? App.GetEnvelopeFrame(EditInstrument, EditArpeggio, EditEnvelopeType, editMode == EditMode.Arpeggio) : pianoRoll.GetSeekFrameToDraw();
                if (seekFrame >= 0)
                {
                    c.PushTranslation(GetPixelXForAbsoluteNoteIndex(seekFrame), 0);
                    c.FillAndDrawGeometry(seekGeometry, GetSeekBarColor(), Theme.BlackColor, 1, true);
                    c.DrawLine(0, halfHeight, 0, height, GetSeekBarColor(), 3);
                    c.PopTransform();
                }
            }

            c.PopClipRegion();
        }
    }
}