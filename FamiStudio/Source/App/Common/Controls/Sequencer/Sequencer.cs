using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace FamiStudio
{
    public class Sequencer : Container
    {
        const int DefaultChannelNameSizeX    = Platform.IsMobile ? 68 : 94;
        const int DefaultHeaderSizeY         = 17;
        const int DefaultScrollBarThickness1 = 10;
        const int DefaultScrollBarThickness2 = 16;
        const int DefaultMinScrollBarLength  = 64;
        const int ResizeBarHeight            = 5;
        const float ScrollSpeedFactor        = Platform.IsMobile ? 2.0f : 1.0f;
        const float DefaultZoom              = Platform.IsMobile ? 0.5f : 2.0f;

        const float MinZoom = 0.25f;
        const float MaxZoom = 16.0f;

        // Controls
        private ChannelArea channelArea;
        private PatternArea patternArea;
        private Timeline timeline;

        int channelNameSizeX;
        int headerSizeY;
        int channelSizeY;
        int resizeBarSizeY;
        int scrollMargin;
        int scrollBarThickness;
        int minScrollBarLength;
        int virtualSizeY;
        int numRows;
        int captureSequencerHeight;
        float noteSizeX;
        bool allowVerticalScrolling;
        bool legacySelectMode = Settings.UseLegacySelectionMode;

        int scrollX = 0;
        int scrollY = 0;
        int mouseLastX = 0;
        int mouseLastY = 0;
        int captureMouseX = -1;
        int captureMouseY = -1;
        int captureScrollX = -1;
        int captureScrollY = -1;
        int captureChannelIdx = -1;
        int captureRowIdx = -1;
        int capturePatternIdx = -1;
        int dragSeekPosition = -1;
        int selectionDragAnchorPatternIdx = -1;
        int sequencerHeightOverride = -1;
        float zoom = DefaultZoom;
        float flingVelX;
        float flingVelY;
        float selectionDragAnchorPatternXFraction = -1.0f;
        CaptureOperation captureOperation = CaptureOperation.None;
        bool panning = false;
        bool canFling = false;
        bool continuouslyFollowing = false;
        bool captureThresholdMet = false;
        bool mouseMovedDuringCapture = false;
        bool captureRealTimeUpdate = false;
        bool showExpansionIcons = false;
        bool timeOnlySelection = false;
        bool hideEmptyChannels = false;
        bool forceShyOff;
        bool[] channelVisible;
        int[] channelToRow;
        int[] rowToChannel;
        PatternLocation selectionMin = PatternLocation.Invalid;
        PatternLocation selectionMax = PatternLocation.Invalid;
        PatternLocation captureSelectionMin = PatternLocation.Invalid;
        PatternLocation captureSelectionMax = PatternLocation.Invalid;
        PatternLocation highlightLocation = PatternLocation.Invalid;
        Dictionary<Pattern, int> selectedPatternRefCounts = [];
        HashSet<PatternLocation> selectedPatternLocations = new HashSet<PatternLocation>();
        HashSet<PatternLocation> captureSelectedPatternLocations = new HashSet<PatternLocation>();
        HashSet<int> selectedPatternColumns = [];
        HashSet<int> captureSelectedPatternColumns = [];

        Color selectedPatternVisibleColor   = Color.FromArgb(64, Theme.LightGreyColor1);
        Color selectedPatternInvisibleColor = Color.FromArgb(16, Theme.LightGreyColor1);
        Color highlightedPatternColor       = Color.FromArgb(64, Theme.WhiteColor);

        enum CaptureOperation
        {
            None,
            SelectColumn,
            SelectRectangle,
            DragSelection,
            AltZoom,
            DragSeekBar,
            ScrollBarX,
            ScrollBarY,
            ResizeSequencer,
            MobileZoom,
            MobilePan,
        }

        static readonly bool[] captureNeedsThreshold = new[]
        {
            false, // None
            Platform.IsMobile,  // SelectColumn
            false, // SelectRectangle
            true,  // DragSelection
            false, // AltZoom
            false, // DragSeekBar
            false, // ScrollBarX
            false, // ScrollBarY
            false, // ResizeSequencer
            false, // MobileZoom,
            false, // MobilePan,
        };

        static readonly bool[] captureWantsRealTimeUpdate = new[]
        {
            false, // None
            true,  // SelectColumn
            true,  // SelectRectangle
            true,  // DragSelection
            false, // AltZoom
            true,  // DragSeekBar
            false, // ScrollBarX
            false, // ScrollBarY
            true , // ResizeSequencer
            false, // MobileZoom,
            false, // MobilePan,
        };

        public delegate void PatternClickedDelegate(int channelIdx, int patternIdx, bool setActive);
        public delegate void ChannelDelegate(int channelIdx);
        public delegate void EmptyDelegate();

        public event PatternClickedDelegate PatternClicked;
        public event EmptyDelegate PatternModified;
        public event EmptyDelegate PatternsPasted;
        public event EmptyDelegate SelectionChanged;
        public event EmptyDelegate ShyChanged;

        internal PatternLocation CaptureSelectionMax => captureSelectionMax;
        internal PatternLocation CaptureSelectionMin => captureSelectionMin;
        internal PatternLocation SelectionMax        => selectionMax;
        internal PatternLocation SelectionMin        => selectionMin;

        internal bool HasSelection                => IsSelectionValid();
        internal bool IsColumnSelectionCapture    => captureOperation == CaptureOperation.SelectColumn;
        internal bool IsDragSelectionCapture      => captureOperation == CaptureOperation.DragSelection;
        internal bool IsRectangleSelectionCapture => captureOperation == CaptureOperation.SelectRectangle;
        internal bool IsTimeOnlySelection         => timeOnlySelection;

        internal int[] ChannelToRow                => channelToRow;
        internal int ChannelNameSizeX              => channelNameSizeX;
        internal int ChannelSizeY                  => channelSizeY;
        internal int ContentBottomY                => height - resizeBarSizeY - scrollBarThickness;
        internal int HeaderSizeY                   => headerSizeY;
        internal int ResizeBarTopY                 => height - resizeBarSizeY;
        internal int ScrollBarThickness            => scrollBarThickness;
        internal int SeekFrameToDraw               => GetSeekFrameToDraw();
        internal int SelectionDragAnchorPatternIdx => selectionDragAnchorPatternIdx;
        internal int SelectionMaxPattern           => HasTimelineSelection ? selectionMax.PatternIndex : -1;
        internal int SelectionMinPattern           => HasTimelineSelection ? selectionMin.PatternIndex : -1;
        internal int ViewScrollX                   => scrollX;
        internal int ViewScrollY                   => scrollY;
        internal int VisibleRowCount               => rowToChannel?.Length ?? 0;

        internal float NoteSizeX                           => noteSizeX;
        internal float SelectionDragAnchorPatternXFraction => selectionDragAnchorPatternXFraction;

        internal bool ColumnSelectionThresholdMet    => captureOperation == CaptureOperation.SelectColumn && captureThresholdMet;
        internal bool RectangleSelectionThresholdMet => captureOperation == CaptureOperation.SelectRectangle && captureThresholdMet;
        internal bool HasTimelineSelection           => legacySelectMode ? IsValidTimeOnlySelection() : selectedPatternColumns.Count > 0;
        internal bool IsResizing                     => captureOperation == CaptureOperation.ResizeSequencer;
        internal bool IsPanning                      => panning;
        internal bool LegacySelectMode               => legacySelectMode;
        internal bool OutlineTimelineSelection       => !legacySelectMode && HasTimelineSelection;
        internal bool ShowTimelineSelectionFill      => HasTimelineSelection && (legacySelectMode || captureOperation == CaptureOperation.SelectColumn);
        internal bool HasCaptureOperation            => captureOperation != CaptureOperation.None;

        internal Color HighlightedPatternColor       => highlightedPatternColor;
        internal Color SeekBarColor                  => GetSeekBarColor();
        internal Color SelectedPatternInvisibleColor => selectedPatternInvisibleColor;
        internal Color SelectedPatternVisibleColor   => selectedPatternVisibleColor;


        internal PatternLocation HighlightLocation => highlightLocation;
        internal IEnumerable<PatternLocation> SelectedPatternLocations => selectedPatternLocations;
        internal LocalizedString MoreOptionsText  => MoreOptionsTooltip;
        internal LocalizedString SetLoopPointText => SetLoopPointTooltip;
        internal LocalizedString PanText          => PanTooltip;

        #region Localization

        // Tooltips
        LocalizedString MoreOptionsTooltip;
        LocalizedString SetLoopPointTooltip;
        LocalizedString PanTooltip;
        LocalizedString ResizeSequencerTooltip;

        // Messages
        LocalizedString MakePatternsUniqueMessage;
        LocalizedString MergeIdenticalPatternsMessage;
        LocalizedString MergeIdenticalPatternsErrorMessage;

        // Dialogs
        LocalizedString PasteTitle;
        LocalizedString PasteMissingInstrumentsMessage;
        LocalizedString PasteMissingArpeggiosMessage;
        LocalizedString PasteMissingSamplesMessage;

        // Paste special dialog
        LocalizedString PasteSpecialTitle;
        LocalizedString InsertLabel;
        LocalizedString InsertTooltip;
        LocalizedString ExtendSongLabel;
        LocalizedString ExtendSongTooltip;
        LocalizedString RepeatLabel;
        LocalizedString RepeatTooltip;

        //Custom pattern settings dialog
        LocalizedString CustomPatternLabel;
        LocalizedString CustomPatternTitle;
        LocalizedString CustomPatternTooltip;

        // Pattern properties dialog
        LocalizedString PatternPropertiesTitle;
        LocalizedString MultiplePatternsSelectedLabel;
        LocalizedString ErrorRenamingPattern;

        #endregion

        public Sequencer()
        {
            Localization.Localize(this);
            SetTickEnabled(true);
        }

        private Song Song
        {
            get { return App?.SelectedSong; }
        }

        public bool ShowExpansionIcons
        {
            get { return showExpansionIcons; }
            set
            {
                if (showExpansionIcons != value)
                {
                    showExpansionIcons = value;
                    MarkDirty();
                }
            }
        }

        private void UpdateRenderCoords()
        {
            var scrollBarSize = Settings.ScrollBars == 1 ? DefaultScrollBarThickness1 : Settings.ScrollBars == 2 ? DefaultScrollBarThickness2 :0;
            var patternZoom = Song != null ? 128.0f / Utils.NextPowerOfTwo(Song.PatternLength) : 1.0f;

            channelNameSizeX   = DpiScaling.ScaleForWindow(DefaultChannelNameSizeX);
            headerSizeY        = DpiScaling.ScaleForWindow(DefaultHeaderSizeY);
            scrollBarThickness = DpiScaling.ScaleForWindow(scrollBarSize);
            resizeBarSizeY     = Platform.IsDesktop && Settings.AllowSequencerVerticalScroll ? DpiScaling.ScaleForWindow(ResizeBarHeight) : 0;
            minScrollBarLength = DpiScaling.ScaleForWindow(DefaultMinScrollBarLength);

            RebuildChannelMap();
            ComputeDesiredSizeY(out var unscaledChannelSizeY, out allowVerticalScrolling);

            channelSizeY = DpiScaling.ScaleForWindow(unscaledChannelSizeY);
            noteSizeX    = DpiScaling.ScaleForWindowFloat(zoom * patternZoom);
            virtualSizeY = rowToChannel != null ? channelSizeY * rowToChannel.Length : 0;
            scrollMargin = (width - channelNameSizeX) / 8;
        }

        private int GetPixelForNote(int n, bool scroll = true)
        {
            // On PC, all math noteSizeX are always integer, but on mobile, they 
            // can be float. We need to cast into double since at the maximum zoom,
            // in a *very* long song, we are hitting the precision limit of floats.
            var x = (int)(n * (double)noteSizeX);
            if (scroll)
                x -= scrollX;
            return x;
        }

        private int GetNoteForPixel(int x, bool scroll = true)
        {
            if (scroll)
                x += scrollX;
            return (int)(x / (double)noteSizeX);
        }

        private int GetNonEmptyChannelCount()
        {
            var count = 0;

            foreach (var c in App.SelectedSong.Channels)
            {
                if (c.HasAnyPatternInstances)
                    count++;
            }

            return count;
        }

        private int GetChannelCount(bool allowHideEmptyChannel = true)
        {
            if (App != null && App.Project != null && App.SelectedSong != null)
            {
                if (hideEmptyChannels && allowHideEmptyChannel)
                    return GetNonEmptyChannelCount();

                return App.SelectedSong.Channels.Length;
            }
            else
            {
                return 5;
            }
        }

        internal int GetDragSelectionRowDelta(int y)
        {
            var pixelDelta  = (y - captureMouseY) + (scrollY - captureScrollY);
            return (int)Math.Round(pixelDelta / (double)channelSizeY);
        }

        public int ComputeDesiredSizeY(out int channelSizeY, out bool verticalScoll)
        {
            var channelCount = GetChannelCount(false);
            var visibleChannelCount = GetChannelCount(true);
            var constantSize  = DefaultHeaderSizeY + scrollBarThickness + resizeBarSizeY + 1;

            if (Platform.IsMobile)
            {
                verticalScoll = true;
                channelSizeY = visibleChannelCount > 0 ? Math.Clamp(((int)Math.Ceiling(height / DpiScaling.Window) - DefaultHeaderSizeY) / visibleChannelCount, 21, 80) : 21;
                return channelSizeY * channelCount + constantSize;
            }
            else
            {
                var frac = Utils.Frac(DpiScaling.Window);
                var divider = (frac == 0.25f || frac == 0.75f) ? 4 : (frac == 0.5f) ? 2 : 1;
                var minChannelSize = Utils.RoundUp(21, divider);
                var idealSequencerHeight = (int)Math.Round(ParentWindow.Height / DpiScaling.Window * Settings.IdealSequencerSize / 100);

                // Non-scrolling behaviour.
                if (!Settings.AllowSequencerVerticalScroll)
                {
                    channelSizeY = visibleChannelCount > 0 ? Math.Max(Utils.RoundDown(idealSequencerHeight / visibleChannelCount, divider), minChannelSize) : minChannelSize;
                    verticalScoll = false;

                    return channelSizeY * visibleChannelCount + constantSize;
                }

                // Scrollable and resizeable behaviour.
                channelSizeY = Utils.RoundUp(42, divider);

                var minHeight = 100 - constantSize;
                var maxHeight = (int)Math.Round(ParentWindow.Height / DpiScaling.Window * 0.9f) - constantSize;
                
                var sequencerHeight = sequencerHeightOverride >= 0 ? sequencerHeightOverride : Settings.SequencerHeight > 0 ? (int)Math.Round( Settings.SequencerHeight / DpiScaling.Window) - constantSize : idealSequencerHeight;
                sequencerHeight = Utils.Clamp(sequencerHeight, minHeight, maxHeight);

                var actualSequencerHeight = channelSizeY * visibleChannelCount;
                verticalScoll = actualSequencerHeight > sequencerHeight;

                return sequencerHeight + constantSize;
            }
        }

        public void ApplySettings()
        {
            legacySelectMode = Settings.UseLegacySelectionMode;
            sequencerHeightOverride = -1;

            ClearSelection();
        }

        public void SaveSettings()
        {
            if (Settings.AllowSequencerVerticalScroll)
                Settings.SequencerHeight = height;
        }

        public void LayoutChanged()
        {
            UpdateRenderCoords();
            ClampScroll();
            UpdateLayout();
            InvalidatePatternCache();
            MarkDirty();
        }

        private void UpdateLayout()
        {
            channelArea.UpdateLayout();
            timeline.UpdateLayout();
            patternArea.UpdateLayout();
        }
        
        public override void OnContainerPointerDownNotify(Control control, PointerEventArgs e)
        {
            App.SetActiveControl(this);
            
            if (e.IsTouchEvent)
            {
                if (control == patternArea || control == channelArea || control.IsInContainer(channelArea))
                {
                    var p = WindowToControl(control.ControlToWindow(e.Position));
                    StartCaptureOperation(p.X, p.Y, CaptureOperation.MobilePan, false);
                }

                return;
            }

            var pan = e.Middle || (e.Left && ModifierKeys.IsAltDown && Settings.AltLeftForMiddle);
            if (pan)
            {
                control.CapturePointer();

                var p = WindowToControl(control.ControlToWindow(e.Position));
                StartPan(p.X, p.Y, false);
            }
        }

        public override void OnContainerPointerMoveNotify(Control control, PointerEventArgs e)
        {
            if (control == patternArea)
                return;

            var p = WindowToControl(control.ControlToWindow(e.Position));

            if (panning)
                DoScroll(p.X - mouseLastX, p.Y - mouseLastY);

            SetMouseLastPos(p.X, p.Y);
            UpdateCursor();
        }

        public override void OnContainerPointerUpNotify(Control control, PointerEventArgs e)
        {
            panning = false;
            UpdateCursor();
        }

        public void Reset()
        {
            scrollX = 0;
            scrollY = 0;
            zoom = DefaultZoom;
            ClearSelection();
            SetHideEmptyChannels(false);
            channelArea.RecreateRows();
            InvalidatePatternCache();
        }

        public override void OnContainerPointerEnterNotify(Control control, EventArgs e)
        {
            while (control != null && string.IsNullOrEmpty(control.ToolTip))
                control = control.ParentContainer;

            App.SetToolTip(control?.ToolTip ?? "");
        }
        
        private void RebuildChannelMap()
        {
            if (Song == null)
            {
                return;
            }

            channelVisible = new bool[Song.Channels.Length];
            channelToRow = new int[Song.Channels.Length];

            if (hideEmptyChannels)
            {
                numRows = GetChannelCount(true);
                rowToChannel = new int[numRows];

                for (int i = 0, j = 0; i < Song.Channels.Length; i++)
                {
                    if (Song.Channels[i].HasAnyPatternInstances)
                    {
                        channelVisible[i] = true;
                        channelToRow[i] = j;
                        rowToChannel[j] = i;
                        j++;
                    }
                    else
                    {
                        channelToRow[i] = -1;
                    }
                }
            }
            else
            {
                numRows = channelVisible.Length;
                rowToChannel = channelToRow;

                for (int i = 0; i < channelVisible.Length; i++)
                {
                    channelVisible[i] = true;
                    channelToRow[i] = i;
                    rowToChannel[i] = i;
                }
            }
        }

        private void RebuildPatternLocationsFromSelectedColumns()
        {
            selectedPatternLocations.Clear();

            foreach (var patternIdx in selectedPatternColumns)
            {
                for (var channelIdx = 0; channelIdx < Song.Channels.Length; channelIdx++)
                {
                    var location = new PatternLocation(channelIdx, patternIdx);

                    if (Song.GetPatternInstance(location) != null)
                        selectedPatternLocations.Add(location);
                }
            }
        }

        public void SetHideEmptyChannels(bool hide)
        {
            hideEmptyChannels = hide;

            if (channelArea != null)
                channelArea.HideEmptyChannels = hide;

            MarkDirty();
        }

        internal void ClearSelection()
        {
            selectionMin = PatternLocation.Invalid;
            selectionMax = PatternLocation.Invalid;
            timeOnlySelection = false;

            selectedPatternLocations.Clear();
            captureSelectedPatternLocations.Clear();

            selectedPatternColumns.Clear();
            captureSelectedPatternColumns.Clear();

            selectedPatternRefCounts.Clear();
            SelectionChanged?.Invoke();
        }

        internal void SetSelection(PatternLocation min, PatternLocation max, bool timeOnly = false)
        {
            selectionMin = min;
            selectionMax = max;
            timeOnlySelection = timeOnly;

            selectedPatternLocations.Clear();

            for (var channelIdx = selectionMin.ChannelIndex; channelIdx <= selectionMax.ChannelIndex; channelIdx++)
            {
                for (var patternIdx = selectionMin.PatternIndex; patternIdx <= selectionMax.PatternIndex; patternIdx++)
                {
                    var location = new PatternLocation(channelIdx, patternIdx);

                    if (!legacySelectMode && Song.GetPatternInstance(location) == null)
                        continue;

                    selectedPatternLocations.Add(new PatternLocation(channelIdx, patternIdx));
                }
            }

            UpdateSelectedPatternRefCounts();
            SelectionChanged?.Invoke();
        }

        internal void EnsureSelectionInclude(PatternLocation location)
        {
            if (!IsSelectionValid())
            {
                SetSelection(location, location, false);
            }
            else
            {
                SetSelection(PatternLocation.Min(selectionMin, location), PatternLocation.Max(selectionMax, location));
            }
        }

        internal bool IsPatternSelected(PatternLocation location)
        {
            if (!IsSelectionValid())
                return false;

            if (legacySelectMode)
            {
                return location.ChannelIndex >= selectionMin.ChannelIndex &&
                       location.ChannelIndex <= selectionMax.ChannelIndex &&
                       location.PatternIndex >= selectionMin.PatternIndex &&
                       location.PatternIndex <= selectionMax.PatternIndex;
            }

            return selectedPatternLocations.Contains(location);
        }

        internal bool IsSelectionOnChannel(int channelIdx)
        {
            return selectionMin.ChannelIndex == channelIdx;
        }

        internal bool IsPatternColumnSelected(int patternIdx)
        {
            if (legacySelectMode)
            {
                return IsValidTimeOnlySelection() && patternIdx >= selectionMin.PatternIndex && patternIdx <= selectionMax.PatternIndex;
            }

            return selectedPatternColumns.Contains(patternIdx);
        }

        internal bool IsChannelVisible(int channelIdx)
        {
            return channelVisible != null && channelIdx >= 0 && channelIdx < channelVisible.Length && channelVisible[channelIdx];
        }

        internal int GetRowForChannel(int channelIdx)
        {
            return channelToRow != null && channelIdx >= 0 && channelIdx < channelToRow.Length ? channelToRow[channelIdx] : -1;
        }

        internal int GetSelectedPatternRefCount(Pattern pattern)
        {
            return selectedPatternRefCounts.TryGetValue(pattern, out var count) ? count : 0;
        }

        internal bool SelectionContainsMultiplePatterns()
        {
            return IsSelectionValid() && (selectionMax.ChannelIndex - selectionMin.ChannelIndex + 1) * (selectionMax.PatternIndex - selectionMin.PatternIndex + 1) > 1;
        }

        internal bool SelectedPatternsHaveSharedReferences()
        {
            return UpdateSelectedPatternRefCounts();
        }

        public bool GetPatternTimeSelectionRange(out int minPatternIdx, out int maxPatternIdx)
        {
            if (IsSelectionValid())
            {
                minPatternIdx = selectionMin.PatternIndex;
                maxPatternIdx = selectionMax.PatternIndex;
                return true;
            }
            else
            {
                minPatternIdx = -1;
                maxPatternIdx = -1;
                return false;
            }
        }

        private void SetMouseLastPos(int x, int y)
        {
            mouseLastX = x;
            mouseLastY = y;
        }

        private void SetFlingVelocity(float x, float y)
        {
            flingVelX = x;
            flingVelY = y;
        }

        protected override void OnAddedToContainer()
        {
            UpdateRenderCoords();

            channelArea = new ChannelArea(this)
            {
                HideEmptyChannels = hideEmptyChannels,
                ForceShyOff = forceShyOff
            };
            channelArea.ShyClicked += () =>
            {
                SetHideEmptyChannels(!hideEmptyChannels);
                ShyChanged?.Invoke();
            };

            timeline = new Timeline(this);
            timeline.SeekDragRequested        += Timeline_SeekDragRequested;
            timeline.ColumnSelectionRequested += Timeline_ColumnSelectionRequested;
            timeline.EditPatternSettings      += Timeline_EditPatternSettings;

            patternArea = new PatternArea(this);

            AddControl(channelArea);
            AddControl(timeline);
            AddControl(patternArea);

            UpdateLayout();
        }

        protected override void OnResize(EventArgs e)
        {
            UpdateRenderCoords();
            ClampScroll();
            UpdateLayout();
        }

        protected bool IsSelectionValid()
        {
            return selectionMin.IsValid && selectionMax.IsValid;
        }

        private bool IsValidTimeOnlySelection()
        {
            return IsSelectionValid() && timeOnlySelection;
        }

        internal void SetHighlightedPattern(PatternLocation location)
        {
            highlightLocation = location;
        }

        internal void ClearHighlightedPattern()
        {
            highlightLocation = PatternLocation.Invalid;
        }

        private Color GetSeekBarColor()
        {
            if (App.IsRecording)
            {
                return Theme.DarkRedColor;
            }
            else
            {
                if (App.IsSeeking)
                {
                    return Theme.Lighten(Theme.YellowColor, (int)(Math.Abs(Math.Sin(Platform.TimeSeconds() * 12.0)) * 75));
                }
                else
                {
                    return Theme.YellowColor;
                }
            }
        }

        public int GetSeekFrameToDraw()
        {
            return captureOperation == CaptureOperation.DragSeekBar ? dragSeekPosition : App.CurrentFrame;
        }

        internal bool GetMinMaxSelectedRow(out int minSelRow, out int maxSelRow)
        {
            var minSelChannel = selectionMin.ChannelIndex;
            var maxSelChannel = selectionMax.ChannelIndex;
            
            while (minSelChannel < Song.Channels.Length && channelToRow[minSelChannel] < 0) minSelChannel++;
            while (maxSelChannel >= 0 && channelToRow[maxSelChannel] < 0) maxSelChannel--;

            if (minSelChannel < Song.Channels.Length && maxSelChannel >= 0)
            {
                minSelRow = channelToRow[minSelChannel];
                maxSelRow = channelToRow[maxSelChannel];
                return true;
            }
            else
            {
                minSelRow = -1;
                maxSelRow = -1;
                return false;
            }
        }

        private void RenderScrollBars(Graphics g)
        {
            if (scrollBarThickness == 0)
                return;

            var c = g.DefaultCommandList;
            var bottomY = height - resizeBarSizeY;

            // Horizontal.
            if (GetScrollBarParams(true, out var thumbPosX, out var thumbSizeX, out var scrollSizeX))
            {
                c.PushTranslation(channelNameSizeX - 1, 0);
                c.FillAndDrawRectangle(0, ContentBottomY, scrollSizeX, bottomY - 1, Theme.DarkGreyColor4, Theme.BlackColor);
                c.FillAndDrawRectangle(thumbPosX, ContentBottomY, thumbPosX + thumbSizeX, bottomY, Theme.MediumGreyColor1, Theme.BlackColor);

                c.PopTransform();
            }

            // Vertical.
            if (GetScrollBarParams(false, out var thumbPosY, out var thumbSizeY, out var scrollSizeY))
            {
                c.PushTranslation(0, headerSizeY);
                c.FillAndDrawRectangle(width - scrollBarThickness, 0, width, scrollSizeY, Theme.DarkGreyColor4, Theme.BlackColor);
                c.FillAndDrawRectangle(width - scrollBarThickness,thumbPosY, width, thumbPosY + thumbSizeY, Theme.MediumGreyColor1, Theme.BlackColor);

                c.PopTransform();
            }

            c.DrawLine(0, ContentBottomY, width, ContentBottomY, Theme.BlackColor);
        }

        private void RenderResizeBar(Graphics g)
        {
            if (resizeBarSizeY <= 0)
                return;

            var c = g.DefaultCommandList;
            var topY = height - resizeBarSizeY;

            c.FillRectangle(0, topY, width, height, Theme.Darken(Theme.DarkGreyColor1, 5));
            c.DrawLine(0, topY, width, topY, Theme.BlackColor);
            c.DrawLine(0, height - 1, width, height - 1, Theme.BlackColor);
        }

        protected void RenderDebug(Graphics g)
        {
#if DEBUG
            if (Platform.IsMobile)
            {
                g.OverlayCommandList.FillRectangle(mouseLastX - 30, mouseLastY - 30, mouseLastX + 30, mouseLastY + 30, Theme.WhiteColor);
            }
#endif
        }

        protected override void OnRender(Graphics g)
        {
            // Piano roll maximized.
            if (height <= 1)
                return;

            RenderScrollBars(g);
            RenderResizeBar(g);
            RenderDebug(g);

            base.OnRender(g);
        }

        private void ReplaceSelectionUtil(Point pos, bool forceInSelection, Func<Channel, bool> channelValid, Action<Pattern> action)
        {
            Debug.Assert(!forceInSelection || IsSelectionValid());

            if (GetPatternForCoord(pos.X, pos.Y, out var location))
            {
                // If we drag on selection, we process the whole selection, otherwise
                // just the pattern under the mouse.
                if (IsPatternSelected(location))
                {
                    App.UndoRedoManager.BeginTransaction(TransactionScope.Song, Song.Id);
                    var replacedAnything = false;

                    for (int i = selectionMin.ChannelIndex; i <= selectionMax.ChannelIndex; i++)
                    {
                        var channel = Song.Channels[i];
                        if (channelValid(channel))
                        {
                            for (int j = selectionMin.PatternIndex; j <= selectionMax.PatternIndex; j++)
                            {
                                var pattern = channel.PatternInstances[j];
                                if (pattern != null)
                                {
                                    action(pattern);
                                    NotifyPatternChange(pattern);
                                    replacedAnything = true;
                                }
                            }

                            channel.InvalidateCumulativePatternCache();
                        }
                    }

                    App.UndoRedoManager.AbortOrEndTransaction(replacedAnything);
                    MarkDirty();
                }
                else
                {
                    var channel = Song.Channels[location.ChannelIndex];
                    if (channelValid(channel))
                    {
                        var pattern = channel.PatternInstances[location.PatternIndex];
                        if (pattern != null)
                        {
                            App.UndoRedoManager.BeginTransaction(TransactionScope.Pattern, pattern.Id);
                            action(pattern);
                            NotifyPatternChange(pattern);
                            channel.InvalidateCumulativePatternCache(pattern);
                            App.UndoRedoManager.EndTransaction();
                            MarkDirty();
                        }
                    }
                }
            }
        }

        public void ReplaceSelectionInstrument(Instrument instrument, Point pos, bool forceInSelection = false)
        {
            ReplaceSelectionUtil(
                pos, forceInSelection,
                (channel) => channel.SupportsInstrument(instrument),
                (pattern) =>
                {
                    foreach (var n in pattern.Notes.Values)
                    {
                        if (n.IsMusical)
                            n.Instrument = instrument;
                    }
                });
        }

        public void ReplaceSelectionArpeggio(Arpeggio arpeggio, Point pos, bool forceInSelection = false)
        {
            ReplaceSelectionUtil(
                pos, forceInSelection,
                (channel) => channel.SupportsArpeggios,
                (pattern) =>
                {
                    foreach (var n in pattern.Notes.Values)
                    {
                        if (n.IsMusical)
                            n.Arpeggio = arpeggio;
                    }
                });
        }

        private bool GetScrollBarParams(bool horizontal, out int thumbPos, out int thumbSize, out int scrollSize)
        {
            thumbPos   = 0;
            thumbSize  = 0;
            scrollSize = 0;

            if (scrollBarThickness > 0)
            {
                if (horizontal)
                {
                    GetMinMaxScroll(out _, out var maxScrollX, out _, out _);

                    scrollSize = width - channelNameSizeX + 1;
                    thumbSize = Math.Max(minScrollBarLength, (int)Math.Round(scrollSize * Math.Min(1.0f, scrollSize / (float)(maxScrollX + scrollSize))));
                    thumbPos = (int)Math.Round((scrollSize - thumbSize) * (scrollX / (float)maxScrollX));
                    return true;
                }
                else if (allowVerticalScrolling)
                {
                    GetMinMaxScroll(out _, out _, out _, out var maxScrollY);

                    scrollSize = height - headerSizeY - scrollBarThickness - resizeBarSizeY;
                    thumbSize = Math.Max(minScrollBarLength, (int)Math.Round(scrollSize * Math.Min(1.0f, scrollSize / (float)(maxScrollY + scrollSize))));
                    thumbPos = (int)Math.Round((scrollSize - thumbSize) * (scrollY / (float)maxScrollY));
                    return true;
                }
            }

            return false;
        }

        public void NotifyPatternChange(Pattern pattern)
        {
            patternArea.NotifyPatternChange(pattern);
        }

#if DEBUG
        public void ValidateIntegrity()
        {
            patternArea.ValidateIntegrity();
        }
#endif

        private void GetMinMaxScroll(out int minScrollX, out int maxScrollX, out int minScrollY, out int maxScrollY)
        {
            minScrollX = 0;
            maxScrollX = Song != null ? Math.Max(GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(Song.Length), false) - scrollMargin, 0) : 0;
            minScrollY = 0;
            maxScrollY = allowVerticalScrolling ? Math.Max(virtualSizeY + headerSizeY - height + scrollBarThickness, 0) : 0;
        }

        private bool ClampScroll()
        {
            GetMinMaxScroll(out var minScrollX, out var maxScrollX, out var minScrollY, out var maxScrollY);

            var scrolledX  = true;
            var scrolledY  = true;

            if (scrollX < minScrollX) { scrollX = minScrollX; scrolledX = false; }
            if (scrollX > maxScrollX) { scrollX = maxScrollX; scrolledY = false; }
            if (scrollY < minScrollY) { scrollY = minScrollY; scrolledY = false; }
            if (scrollY > maxScrollY) { scrollY = maxScrollY; scrolledY = false; }

            channelArea.UpdateScroll(scrollY); // TODO: Probably shouldn't need this?
            
            return scrolledX || scrolledY;
        }

        private bool DoScroll(int deltaX, int deltaY)
        {
            scrollX -= deltaX;
            scrollY -= deltaY; 
            MarkDirty();
            return ClampScroll();
        }

        private int GetPatternIndexForCoord(int x)
        {
            var noteIdx = GetNoteForPixel(x - channelNameSizeX);

            if (noteIdx < 0 || noteIdx >= Song.GetPatternStartAbsoluteNoteIndex(Song.Length))
                return -1;

            return Song.PatternIndexFromAbsoluteNoteIndex(noteIdx);
        }

        private bool GetPatternForCoord(int x, int y, out PatternLocation location)
        {
            var noteIdx = GetNoteForPixel(x - channelNameSizeX);

            if (noteIdx < 0 || noteIdx >= Song.GetPatternStartAbsoluteNoteIndex(Song.Length))
            {
                location = PatternLocation.Invalid;
                return false;
            }

            location = new PatternLocation(GetChannelIndexForCoord(y), Song.PatternIndexFromAbsoluteNoteIndex(noteIdx));

            return x > channelNameSizeX && y > headerSizeY && location.IsChannelInSong(Song);
        }

        private int GetRowIndexForCoord(int y)
        {
            return Utils.Clamp(((y - headerSizeY) + scrollY) / channelSizeY, 0, rowToChannel.Length - 1);
        }

        internal int GetChannelIndexForCoord(int y)
        {
            var rowY = y - headerSizeY + scrollY;

            if (rowToChannel.Length == 0 || rowY < 0 || rowY >= virtualSizeY)
                return -1;

            return rowToChannel[GetRowIndexForCoord(y)];
        }

        private void GetClampedPatternForCoord(int x, int y, out int channelIdx, out int patternIdx)
        {
            patternIdx = Song.PatternIndexFromAbsoluteNoteIndex(Utils.Clamp(GetNoteForPixel(x - channelNameSizeX), 0, Song.GetPatternStartAbsoluteNoteIndex(Song.Length) - 1));

            if (rowToChannel.Length > 0)
            {
                var rowIdx = GetRowIndexForCoord(y);
                channelIdx = rowToChannel[rowIdx];
            }
            else
            {
                channelIdx = -1;
            }
        }

        private bool HasPatternForCoord(int x, int y)
        {
            return GetPatternForCoord(x, y, out var location) &&
                Song.GetPatternInstance(location) != null;
        }

        private bool IsPointInResizeArea(int y)
        {
            return resizeBarSizeY > 0 && y >= height - resizeBarSizeY;
        }

        private void CaptureMouse(int x, int y, bool capturePointer = true)
        {
            SetMouseLastPos(x, y);

            captureMouseX = x;
            captureMouseY = y;
            captureScrollX = scrollX;
            captureScrollY = scrollY;

            if (capturePointer)
                CapturePointer();
        }

        private void StartCaptureOperation(int x, int y, CaptureOperation op, bool capturePointer = true)
        {
            Debug.Assert(captureOperation == CaptureOperation.None);

            CaptureMouse(x, y, capturePointer);

            canFling = false;
            captureOperation = op;
            captureThresholdMet = !captureNeedsThreshold[(int)op];
            mouseMovedDuringCapture = false;
            captureRealTimeUpdate = captureWantsRealTimeUpdate[(int)op];
            captureRowIdx = GetRowIndexForCoord(y);
            GetClampedPatternForCoord(x, y, out captureChannelIdx, out capturePatternIdx);
        }

        internal void CreateNewPattern(PatternLocation location)
        {
            var channel = Song.Channels[location.ChannelIndex];

            App.UndoRedoManager.BeginTransaction(TransactionScope.Channel, Song.Id, location.ChannelIndex);
            channel.PatternInstances[location.PatternIndex] = channel.CreatePattern();
            channel.InvalidateCumulativePatternCache();
            PatternClicked?.Invoke(location.ChannelIndex, location.PatternIndex, false);
            App.UndoRedoManager.EndTransaction();

            ClearSelection();
            MarkDirty();
        }

        internal void DeletePattern(PatternLocation location)
        {
            var channel = Song.Channels[location.ChannelIndex];
            var pattern = channel.PatternInstances[location.PatternIndex];

            App.UndoRedoManager.BeginTransaction(TransactionScope.Channel, Song.Id, location.ChannelIndex);
            patternArea.NotifyPatternChange(pattern);
            channel.PatternInstances[location.PatternIndex] = null;
            channel.InvalidateCumulativePatternCache();
            PatternModified?.Invoke();
            App.UndoRedoManager.EndTransaction();

            ClearSelection();
            MarkDirty();
        }

        private void Timeline_SeekDragRequested(Control sender, PointerEventArgs e)
        {
            var p = WindowToControl(timeline.ControlToWindow(e.Position));
            StartCaptureOperation(p.X, p.Y, CaptureOperation.DragSeekBar);
            UpdateSeekDrag(p.X, p.Y, false);
        }

        private void Timeline_ColumnSelectionRequested(Control sender, PointerEventArgs e)
        {
            var p = WindowToControl(timeline.ControlToWindow(e.Position));
            StartColumnSelection(p.X, p.Y, false);
        }

        private void Timeline_EditPatternSettings(int patternIdx, Point pt)
        {
            var p = WindowToControl(timeline.ControlToWindow(pt));
            EditPatternCustomSettings(p, patternIdx);
        }

        private void StartPan(int x, int y, bool capturePointer = true)
        {
            panning = true;
            CaptureMouse(x, y, capturePointer);
        }

        private bool HandleMouseDownScrollbar(PointerEventArgs e)
        {
            if (e.Left && scrollBarThickness > 0 && e.X > channelNameSizeX && e.Y > headerSizeY)
            {
                var bottomY    = height  - resizeBarSizeY;
                var scrollBarY = bottomY - scrollBarThickness;

                if (e.Y >= scrollBarY && e.Y < bottomY && GetScrollBarParams(true, out var scrollBarPosX, out var scrollBarSizeX, out _))
                {
                    var x = e.X - channelNameSizeX;
                    if (x < scrollBarPosX)
                    {
                        scrollX -= (width - channelNameSizeX);
                        ClampScroll();
                    }
                    else if (x > (scrollBarPosX + scrollBarSizeX))
                    {
                        scrollX += (width - channelNameSizeX);
                        ClampScroll();
                    }
                    else if (x >= scrollBarPosX && x <= (scrollBarPosX + scrollBarSizeX))
                    {
                        StartCaptureOperation(e.X, e.Y, CaptureOperation.ScrollBarX);
                    }
                    return true;
                }
                if (e.X >= (width - scrollBarThickness) && GetScrollBarParams(false, out var scrollBarThumbPosY, out var scrollBarThumbSizeY, out _))
                {
                    var y = e.Y - headerSizeY;
                    if (y < scrollBarThumbPosY)
                    {
                        scrollY -= (height - headerSizeY);
                        ClampScroll();
                        MarkDirty();
                    }
                    else if (y > (scrollBarThumbPosY + scrollBarThumbSizeY))
                    {
                        scrollY += (height - headerSizeY);
                        ClampScroll();
                        MarkDirty();
                    }
                    else if (y >= scrollBarThumbPosY && y <= (scrollBarThumbPosY + scrollBarThumbSizeY))
                    {
                        StartCaptureOperation(e.X, e.Y, CaptureOperation.ScrollBarY);
                    }
                    return true;
                }
            }

            return false;
        }

        private void HandleMouseWheel(int x, PointerEventArgs e)
        {
            if (Settings.TrackPadControls && !ModifierKeys.IsControlDown && !ModifierKeys.IsAltDown)
            {
                if (CanScrollVertically() && !ModifierKeys.IsShiftDown)
                    scrollY -= Utils.SignedCeil(e.ScrollY);
                else
                    scrollX -= Utils.SignedCeil(e.ScrollY);

                ClampScroll();
                MarkDirty();
            }
            else
            {
                ZoomAtLocation(x, e.ScrollY < 0.0f ? 0.5f : 2.0f);
            }
        }

        internal void HandleMouseWheel(Control control, PointerEventArgs e)
        {
            var p = WindowToControl(control.ControlToWindow(e.Position));
            HandleMouseWheel(p.X, e);
        }

        internal void HandleMouseHorizontalWheel(Control control, PointerEventArgs e)
        {
            scrollX += Utils.SignedCeil(e.ScrollX);
            ClampScroll();
            MarkDirty();
        }

        internal void StartDragSelection(Control control, PointerEventArgs e, int patternIdx)
        {
            var p = WindowToControl(control.ControlToWindow(e.Position));
            StartDragSelection(p.X, p.Y, patternIdx, false);
        }
        
        private void StartDragSelection(int x, int y, int patternIdx, bool capturePointer = true)
        {
            selectionDragAnchorPatternIdx = patternIdx;
            selectionDragAnchorPatternXFraction = (
                x - channelNameSizeX + scrollX - 
                GetPixelForNote(Song.GetPatternStartAbsoluteNoteIndex(patternIdx), false)) / 
                (float)GetPixelForNote(Song.GetPatternLength(patternIdx), false);

            StartCaptureOperation(x, y, CaptureOperation.DragSelection, capturePointer);
        }

        private bool HandleMouseDownResize(PointerEventArgs e)
        {
            captureSequencerHeight = height;

            if (e.Left && IsPointInResizeArea(e.Y))
            {
                StartCaptureOperation(e.X, e.Y, CaptureOperation.ResizeSequencer);
                return true;
            }

            return false;
        }

        protected override void OnPointerDown(PointerEventArgs e)
        {
            if (captureOperation != CaptureOperation.None && (e.Left || e.Right))
                return;

            UpdateCursor();

            var pan = e.Middle || (e.Left && ModifierKeys.IsAltDown && Settings.AltLeftForMiddle);
            if (pan)
            {
                StartPan(e.X, e.Y, false);
                MarkDirty();
                return;
            }

            if (HandleMouseDownResize(e)) goto Handled;
            if (HandleMouseDownScrollbar(e)) goto Handled;

            return;

        Handled:
            MarkDirty();
        }

        internal void GotoPianoRoll(PatternLocation location)
        {
            PatternClicked?.Invoke(location.ChannelIndex, location.PatternIndex, true);
        }

        internal void CopySelectionToCursor(bool copy)
        {
            var channelDeltaIdx = highlightLocation.ChannelIndex - selectionMin.ChannelIndex;
            var patternDeltaIdx = highlightLocation.PatternIndex - selectionMin.PatternIndex;

            MoveCopyOrDuplicateSelection(channelDeltaIdx, patternDeltaIdx, true, copy);
        }

        private void PreSelectionCapture()
        {
            selectedPatternRefCounts.Clear();

            captureSelectionMin = PatternLocation.Invalid;
            captureSelectionMax = PatternLocation.Invalid;

            captureSelectedPatternColumns.Clear();
            captureSelectedPatternLocations.Clear();

            foreach (var patternIdx in selectedPatternColumns)
                captureSelectedPatternColumns.Add(patternIdx);

            foreach (var location in selectedPatternLocations)
                captureSelectedPatternLocations.Add(location);
        }

        internal void StartRectangleSelection(Control control, PointerEventArgs e)
        {
            var p = WindowToControl(control.ControlToWindow(e.Position));
            StartRectangleSelection(p.X, p.Y, false);
        }

        internal void StartRectangleSelection(int x, int y, bool capturePointer = true)
        {
            PreSelectionCapture();
            StartCaptureOperation(x, y, CaptureOperation.SelectRectangle, capturePointer);
        }

        private void StartColumnSelection(int x, int y, bool capturePointer = true)
        {
            PreSelectionCapture();
            StartCaptureOperation(x, y, CaptureOperation.SelectColumn, capturePointer);
        }

        private bool UpdateSelectedPatternRefCounts()
        {
            if (!IsSelectionValid())
                return false;

            var selectedPatterns = new HashSet<Pattern>();

            foreach (var pattern in GetSelectedPatterns(out _))
            {
                if (pattern != null)
                    selectedPatterns.Add(pattern);
            }

            var counts = new Dictionary<Pattern, int>();
            bool duplicateFound = false;

            for (int i = 0; i < Song.Channels.Length; i++)
            {
                var channel = Song.Channels[i];

                for (int j = 0; j < Song.Length; j++)
                {
                    var pattern = channel.PatternInstances[j];
                    if (pattern != null && selectedPatterns.Contains(pattern))
                    {
                        var count = counts.TryGetValue(pattern, out var c) ? c + 1 : 1;
                        counts[pattern] = count;

                        if (count > 1)
                            duplicateFound = true;
                    }
                }
            }

            selectedPatternRefCounts = counts;
            return duplicateFound;
        }

        internal void MakeSelectedPatternsUnique()
        {
            if (selectedPatternRefCounts.Count == 0)
                return;

            App.UndoRedoManager.BeginTransaction(TransactionScope.Song, Song.Id);
            
            var patternCounts = new Dictionary<Pattern, int>(selectedPatternRefCounts); // Safety.
            var uniqueCount = 0;

            for (int i = selectionMin.ChannelIndex; i <= selectionMax.ChannelIndex; i++)
            {
                var channel = Song.Channels[i];
                for (int j = selectionMin.PatternIndex; j <= selectionMax.PatternIndex; j++)
                {
                    var pattern = channel.PatternInstances[j];
                    if (pattern != null && patternCounts.TryGetValue(pattern, out var count) && count > 1)
                    {
                        var newPattern = CreateUniquePatternClone(pattern, channel);
                        channel.PatternInstances[j] = newPattern;
                        patternCounts[pattern]--;
                        uniqueCount++;
                    }
                }
            }

            Song.DeleteNotesPastMaxInstanceLength();
            Song.InvalidateCumulativePatternCache();
            UpdateSelectedPatternRefCounts();

            App.UndoRedoManager.EndTransaction();
            
            var message = MakePatternsUniqueMessage.Format(uniqueCount);
            App.DisplayNotification(message, false);
        }

        internal void MergeSelectedIdenticalPatterns()
        {
            if (!IsSelectionValid())
                return;

            App.UndoRedoManager.BeginTransaction(TransactionScope.Song, Song.Id);

            var bestPatterns = new Dictionary<uint, Pattern>();
            var usageCounts  = new Dictionary<Pattern, int>();
            var mergeCount   = 0;

            // Find the most frequently used pattern for each CRC across the selected channels.
            for (int c = selectionMin.ChannelIndex; c <= selectionMax.ChannelIndex; c++)
            {
                var channel = Song.Channels[c];
                for (int p = 0; p < channel.PatternInstances.Length; p++)
                {
                    var pattern = channel.PatternInstances[p];
                    if (pattern == null)
                        continue;

                    usageCounts[pattern] = usageCounts.TryGetValue(pattern, out var count) ? count + 1 : 1;

                    var crc = pattern.ComputeCRC();

                    if (!bestPatterns.TryGetValue(crc, out var bestPattern) || usageCounts[pattern] > usageCounts[bestPattern] ||
                        (usageCounts[pattern] == usageCounts[bestPattern] && !selectedPatternRefCounts.ContainsKey(pattern) && selectedPatternRefCounts.ContainsKey(bestPattern)))
                    {
                        bestPatterns[crc] = pattern;
                    }
                }
            }

            // Replace selected instances with the preferred identical pattern.
            for (int c = selectionMin.ChannelIndex; c <= selectionMax.ChannelIndex; c++)
            {
                var channel = Song.Channels[c];

                for (int p = selectionMin.PatternIndex; p <= selectionMax.PatternIndex; p++)
                {
                    var pattern = channel.PatternInstances[p];
                    if (pattern != null && bestPatterns.TryGetValue(pattern.ComputeCRC(), out var bestPattern) && !ReferenceEquals(pattern, bestPattern))
                    {
                        channel.PatternInstances[p] = bestPattern;
                        mergeCount++;
                    }
                }
            }

            Song.InvalidateCumulativePatternCache();
            UpdateSelectedPatternRefCounts();

            App.UndoRedoManager.EndTransaction();

            var message = mergeCount > 0 ? MergeIdenticalPatternsMessage.Format(mergeCount): MergeIdenticalPatternsErrorMessage.Value;
            App.DisplayNotification(message, false);
        }

        protected override void OnTouchFling(PointerEventArgs e)
        {
            var x = e.X;
            var y = e.Y;

            if (canFling)
            {
                EndCaptureOperation(x, y);
                SetFlingVelocity(e.FlingVelocityX, e.FlingVelocityY);
            }
        }

        protected override void OnTouchScaleBegin(PointerEventArgs e)
        {
            var x = e.X;
            var y = e.Y;

            if (captureOperation != CaptureOperation.None)
            {
                Debug.Assert(captureOperation != CaptureOperation.MobileZoom);
                AbortCaptureOperation();
            }

            StartCaptureOperation(x, y, CaptureOperation.MobileZoom);
            SetMouseLastPos(x, y);
        }

        protected override void OnTouchScale(PointerEventArgs e)
        {
            var x = e.X;
            var y = e.Y;

            UpdateCaptureOperation(x, y, e.TouchScale);
            SetMouseLastPos(x, y);
        }

        protected override void OnTouchScaleEnd(PointerEventArgs e)
        {
            var x = e.X;
            var y = e.Y;

            EndCaptureOperation(x, y);
            SetMouseLastPos(x, y);
        }

        private Pattern[,] GetSelectedPatterns(out Song.PatternCustomSetting[] customSettings)
        {
            customSettings = null;

            if (!IsSelectionValid())
                return null;

            var patterns = new Pattern[selectionMax.PatternIndex - selectionMin.PatternIndex + 1, selectionMax.ChannelIndex - selectionMin.ChannelIndex + 1];

            for (int i = 0; i < patterns.GetLength(0); i++)
            {
                for (int j = 0; j < patterns.GetLength(1); j++)
                {
                    var location = new PatternLocation(selectionMin.ChannelIndex + j, selectionMin.PatternIndex + i);

                    if (legacySelectMode || IsPatternSelected(location))
                        patterns[i, j] = Song.GetPatternInstance(location);
                }
            }

            if (IsValidTimeOnlySelection())
            {
                customSettings = new Song.PatternCustomSetting[patterns.GetLength(0)];

                for (int i = 0; i < patterns.GetLength(0); i++)
                    customSettings[i] = Song.GetPatternCustomSettings(selectionMin.PatternIndex + i).Clone();
            }

            return patterns;
        }

        public bool CanCopy   => IsActiveControl && IsSelectionValid();
        public bool CanPaste  => IsActiveControl && ClipboardUtils.ContainsPatterns && (!legacySelectMode || IsSelectionValid());
        public bool CanDelete => CanCopy;
        public bool IsActiveControl => App != null && (App.ActiveControl == this || App.ActiveControl?.IsInContainer(this) == true);

        public void Copy()
        {
            if (IsSelectionValid())
            {
                var selPatterns = GetSelectedPatterns(out var customSettings);
                ClipboardUtils.SavePatterns(App.Project, selPatterns, customSettings);
            }
        }

        public void Cut()
        {
            if (IsSelectionValid())
            {
                var selPatterns = GetSelectedPatterns(out var customSettings);
                ClipboardUtils.SavePatterns(App.Project, selPatterns, customSettings);
                CancelDragSelection(); // Safety in case a transaction is in progress.
                DeleteSelection(true, customSettings != null);
            }
        }

        private void PasteInternal(bool insert, bool extend, int repeat, ClipboardImportFlags instImportFlags, ClipboardImportFlags arpImportFlags, ClipboardImportFlags sampleImportFlags, ClipboardImportFlags patternImportFlags)
        {
            var createAnythingMissing =
                instImportFlags.HasFlag(ClipboardImportFlags.CreateMissing) ||
                arpImportFlags.HasFlag(ClipboardImportFlags.CreateMissing)  ||
                sampleImportFlags.HasFlag(ClipboardImportFlags.CreateMissing);

            App.UndoRedoManager.BeginTransaction(createAnythingMissing ? TransactionScope.Project : TransactionScope.Song, Song.Id);

            var song = Song;
            var pasteIdx = legacySelectMode ? selectionMin.PatternIndex : song.PatternIndexFromAbsoluteNoteIndex(App.CurrentFrame);
            var patterns = ClipboardUtils.LoadPatterns(song, instImportFlags, arpImportFlags, sampleImportFlags, patternImportFlags, out var customSettings);

            if (patterns == null)
            {
                App.UndoRedoManager.AbortTransaction();
                return;
            }

            var numColumnsToPaste = patterns.GetLength(0) * repeat;

            if (numColumnsToPaste == 0)
            {
                App.UndoRedoManager.AbortTransaction();
                return;
            }

            if (insert)
            {
                var oldLength = song.Length;

                if (extend)
                    song.SetLength(Math.Min(oldLength + numColumnsToPaste, Song.MaxLength));

                // Move everything at and after the paste position to the right.
                for (int dstIndex = song.Length - 1; dstIndex >= pasteIdx + numColumnsToPaste; dstIndex--)
                {
                    var srcIndex = dstIndex - numColumnsToPaste;
                    if (srcIndex >= oldLength)
                        continue;

                    for (int j = 0; j < song.Channels.Length; j++)
                        song.Channels[j].PatternInstances[dstIndex] = song.Channels[j].PatternInstances[srcIndex];

                    if (song.PatternHasCustomSettings(srcIndex))
                    {
                        var settings = song.GetPatternCustomSettings(srcIndex);
                        song.SetPatternCustomSettings(dstIndex, settings.patternLength, settings.beatLength, settings.groove, settings.groovePaddingMode);
                    }
                    else if (song.PatternHasCustomSettings(dstIndex))
                    {
                        song.ClearPatternCustomSettings(dstIndex);
                    }
                }

                // Clear the newly inserted columns before pasting into them.
                var clearEnd = Math.Min(pasteIdx + numColumnsToPaste, song.Length);

                for (int i = pasteIdx; i < clearEnd; i++)
                {
                    song.ClearPatternCustomSettings(i);

                    for (int j = 0; j < song.Channels.Length; j++)
                        song.Channels[j].PatternInstances[i] = null;
                }
            }
            
            // Then do the actual paste.
            var startPatternIndex = pasteIdx;
            var pastedLocations   = new HashSet<PatternLocation>();

            for (int r = 0; r < repeat; r++)
            {
                for (int i = 0; i < patterns.GetLength(0); i++)
                {
                    for (int j = 0; j < patterns.GetLength(1); j++)
                    {
                        var pattern = patterns[i, j];

                        if (pattern != null && (i + startPatternIndex) < song.Length && song.Project.IsChannelActive(pattern.ChannelType))
                        {
                            var channelIdx = Channel.ChannelTypeToIndex(pattern.ChannelType, song.Project.ExpansionAudioMask, song.Project.ExpansionNumN163Channels);
                            var location   = new PatternLocation(channelIdx, i + startPatternIndex);

                            pattern.RemoveUnsupportedChannelFeatures();
                            song.Channels[channelIdx].PatternInstances[location.PatternIndex] = pattern;

                            pastedLocations.Add(location);
                        }
                    }
                }

                if (customSettings != null)
                {
                    for (int i = 0; i < patterns.GetLength(0); i++)
                    {
                        if (customSettings[i].useCustomSettings)
                        {
                            Song.SetPatternCustomSettings(
                                i + startPatternIndex,
                                customSettings[i].patternLength,
                                customSettings[i].beatLength,
                                customSettings[i].groove,
                                customSettings[i].groovePaddingMode);
                        }
                        else
                        {
                            Song.ClearPatternCustomSettings(i + startPatternIndex);
                        }
                    }
                }

                startPatternIndex += patterns.GetLength(0);
            }

            var pasteEndIdx = Math.Min(pasteIdx + numColumnsToPaste - 1, Song.Length - 1);

            if (legacySelectMode)
            {
                SetSelection(new PatternLocation(0, pasteIdx), new PatternLocation(Song.Channels.Length - 1, pasteEndIdx), true);
            }
            else
            {
                selectedPatternLocations.Clear();
                selectedPatternColumns.Clear();

                selectedPatternLocations.UnionWith(pastedLocations);
                timeOnlySelection = false;

                if (selectedPatternLocations.Count > 0)
                {
                    selectionMin = new PatternLocation(selectedPatternLocations.Min(l => l.ChannelIndex), selectedPatternLocations.Min(l => l.PatternIndex));
                    selectionMax = new PatternLocation(selectedPatternLocations.Max(l => l.ChannelIndex), selectedPatternLocations.Max(l => l.PatternIndex));
                }
                else
                {
                    selectionMin = PatternLocation.Invalid;
                    selectionMax = PatternLocation.Invalid;
                }

                UpdateSelectedPatternRefCounts();
                SelectionChanged?.Invoke();
            }

            song.InvalidateCumulativePatternCache();
            song.DeleteNotesPastMaxInstanceLength();

            App.UndoRedoManager.EndTransaction();
            PatternsPasted?.Invoke();
            RebuildChannelMap();
            MarkDirty();
        }

        private void PasteInternalWithConflictDialog(bool insert, bool extend, int repeat)
        {
            if (!ClipboardUtils.GetClipboardContentFlags(Song, false, out var instFlags, out var arpFlags, out var sampleFlags, out var patternFlags))
            {
                return;
            }

            var anyConflicts =
                instFlags    != ClipboardContentFlags.None ||
                arpFlags     != ClipboardContentFlags.None ||
                sampleFlags  != ClipboardContentFlags.None ||
                patternFlags != ClipboardContentFlags.None;

            if (anyConflicts)
            {
                var dlg = new PasteConflictDialog(window, instFlags, arpFlags, sampleFlags, patternFlags);
                dlg.ShowDialogAsync((r) =>
                {
                    if (r == DialogResult.OK)
                    {
                        PasteInternal(insert, extend, repeat, dlg.InstrumentFlags, dlg.ArpeggioFlags, dlg.DPCMSampleFlags, dlg.PatternFlags);
                    }
                });
            }
            else
            {
                PasteInternal(insert, extend, repeat, ClipboardImportFlags.MatchByName, ClipboardImportFlags.MatchByName, ClipboardImportFlags.MatchByName, ClipboardImportFlags.MatchByName);
            }
        }

        public void Paste()
        {
            if (legacySelectMode && !IsSelectionValid())
                return;

            PasteInternalWithConflictDialog(false, false, 1);
        }

        public void PasteSpecial()
        {
            if (!IsSelectionValid())
                return;

            var dialog = new PropertyDialog(ParentWindow, PasteSpecialTitle, 200);
            dialog.Properties.AddLabelCheckBox(InsertLabel, false, 0, InsertTooltip); // 0
            dialog.Properties.AddLabelCheckBox(ExtendSongLabel, false, 0, ExtendSongTooltip); // 1
            dialog.Properties.AddNumericUpDown(RepeatLabel.Colon, 1, 1, 32, 1, RepeatTooltip); // 2
            dialog.Properties.SetPropertyEnabled(1, false);
            dialog.Properties.PropertyChanged += PasteSpecialDialog_PropertyChanged;
            dialog.Properties.Build();

            dialog.ShowDialogAsync((r) =>
            {
                if (r == DialogResult.OK)
                {
                    PasteInternalWithConflictDialog(
                        dialog.Properties.GetPropertyValue<bool>(0),
                        dialog.Properties.GetPropertyValue<bool>(1),
                        dialog.Properties.GetPropertyValue<int> (2));
                }
            });
        }

        private void PasteSpecialDialog_PropertyChanged(PropertyPage props, int propIdx, int rowIdx, int colIdx, object value)
        {
            if (propIdx == 0)
                props.SetPropertyEnabled(1, (bool)value);
        }

        protected void UpdateCursor()
        {
            if (captureOperation == CaptureOperation.ResizeSequencer || IsPointInResizeArea(mouseLastY))
            {
                Cursor = Cursors.SizeNS;
            }
            else if (captureOperation == CaptureOperation.DragSelection || HasPatternForCoord(mouseLastX, mouseLastY))
            {
                Cursor = Cursors.DragCursor;
            }
            else
            {
                Cursor = Cursors.Default;
            }
        }

        private int IncrementChannelIndex(int channelIdx, int rowDelta, bool clamp)
        {
            if (hideEmptyChannels)
            {
                var idx = channelToRow[channelIdx] + rowDelta;

                if (clamp)
                    idx = Utils.Clamp(idx, 0, rowToChannel.Length - 1);
                else if (idx < 0 || idx >= rowToChannel.Length)
                    return -1;

                return rowToChannel[idx];
            }
            else
            {
                var idx = channelIdx + rowDelta;

                if (clamp)
                    idx = Utils.Clamp(idx, 0, Song.Channels.Length - 1);

                return idx;
            }
        }

        private int IncrementPatternIndex(int patternIndex, int delta, bool clamp)
        {
            patternIndex += delta;

            if (clamp)
                patternIndex = Utils.Clamp(patternIndex, 0, Song.Length - 1);

            return patternIndex;
        }

        private Pattern CreateUniquePatternClone(Pattern pattern, Channel channel)
        {
            var newName = pattern.Name;
            if (!channel.IsPatternNameUnique(newName))
                newName = channel.GenerateUniquePatternNameSmart(pattern.Name);

            var newPattern = pattern.ShallowClone(channel);
            newPattern.RemoveUnsupportedChannelFeatures();
            newPattern.Color = Theme.RandomCustomColor();
            channel.RenamePattern(newPattern, newName);

            return newPattern;
        }

        private void MoveCopyOrDuplicateSelection(int rowIdxDelta, int patternIdxDelta, bool copy, bool duplicate, bool endTransaction = true)
        {
            App.UndoRedoManager.BeginTransaction(TransactionScope.Song, Song.Id);

            var tmpPatterns = GetSelectedPatterns(out var customSettings);

            if (!copy)
                DeleteSelection(false, customSettings != null && !copy, false);

            var duplicatePatternMap = new Dictionary<Pattern, Pattern>();

            for (int i = selectionMin.ChannelIndex; i <= selectionMax.ChannelIndex; i++)
            {
                for (int j = selectionMin.PatternIndex; j <= selectionMax.PatternIndex; j++)
                {
                    var sourceLocation = new PatternLocation(i, j);

                    if (!legacySelectMode && !IsPatternSelected(sourceLocation))
                        continue;

                    var ni = IncrementChannelIndex(i, rowIdxDelta, false);
                    var nj = IncrementPatternIndex(j, patternIdxDelta, false);

                    if (nj >= 0 && nj < Song.Length && ni >= 0 && ni < Song.Channels.Length)
                    {
                        var sourcePattern = tmpPatterns[j - selectionMin.PatternIndex, i - selectionMin.ChannelIndex];

                        if (duplicate && sourcePattern != null)
                        {
                            if (!duplicatePatternMap.TryGetValue(sourcePattern, out var duplicatedPattern))
                            {
                                var destChannel = Song.Channels[ni];

                                duplicatedPattern = CreateUniquePatternClone(sourcePattern, destChannel);
                                duplicatePatternMap.Add(sourcePattern, duplicatedPattern);
                            }

                            Song.Channels[ni].PatternInstances[nj] = duplicatedPattern;
                        }
                        else
                        {
                            Song.Channels[ni].PatternInstances[nj] = sourcePattern;
                        }
                    }
                }
            }

            if (customSettings != null)
            {
                for (int j = selectionMin.PatternIndex; j <= selectionMax.PatternIndex; j++)
                {
                    var settings = customSettings[j - selectionMin.PatternIndex];

                    var nj = j + patternIdxDelta;
                    if (nj >= 0 && nj < Song.Length)
                    {
                        if (settings.useCustomSettings)
                        {
                            Song.SetPatternCustomSettings(
                                nj,
                                customSettings[j - selectionMin.PatternIndex].patternLength,
                                customSettings[j - selectionMin.PatternIndex].beatLength,
                                customSettings[j - selectionMin.PatternIndex].groove,
                                customSettings[j - selectionMin.PatternIndex].groovePaddingMode);
                        }
                        else
                        {
                            Song.ClearPatternCustomSettings(nj);
                        }
                    }
                }
            }

            Song.RemoveUnsupportedEffects();
            Song.RemoveUnsupportedInstruments();
            Song.DeleteNotesPastMaxInstanceLength();
            Song.InvalidateCumulativePatternCache();

            if (endTransaction)
                App.UndoRedoManager.EndTransaction();
        }

        private void EndDragSelection(int x, int y)
        {
            if (captureThresholdMet)
            {
                if (!IsSelectionValid()) // No clue how we end up here with invalid selection.
                {
                    CancelDragSelection();
                }
                else
                {
                    var noteIdx = GetNoteForPixel(x - channelNameSizeX);

                    if (noteIdx >= 0 && noteIdx < Song.GetPatternStartAbsoluteNoteIndex(Song.Length))
                    {
                        var patternIdx = Song.PatternIndexFromAbsoluteNoteIndex(GetNoteForPixel(x - channelNameSizeX));
                        var patternIdxDelta = patternIdx - selectionDragAnchorPatternIdx;
                        var rowIdxDelta = GetDragSelectionRowDelta(y);

                        // No need to proceed if the patterns haven't moved.
                        if (rowIdxDelta == 0 && patternIdxDelta == 0)
                            return;

                        var copy = ModifierKeys.IsControlDown;
                        var duplicate = copy && ModifierKeys.IsShiftDown || rowIdxDelta != 0;

                        MoveCopyOrDuplicateSelection(rowIdxDelta, patternIdxDelta, copy, duplicate, false);

                        var timeOnly = IsValidTimeOnlySelection();

                        if (legacySelectMode)
                        {
                            var newSelectionMin = new PatternLocation(
                                IncrementChannelIndex(selectionMin.ChannelIndex, rowIdxDelta,     true),
                                IncrementPatternIndex(selectionMin.PatternIndex, patternIdxDelta, true));

                            var newSelectionMax = new PatternLocation(
                                IncrementChannelIndex(selectionMax.ChannelIndex, rowIdxDelta,     true),
                                IncrementPatternIndex(selectionMax.PatternIndex, patternIdxDelta, true));

                            if (timeOnly)
                            {
                                newSelectionMin.ChannelIndex = 0;
                                newSelectionMax.ChannelIndex = Song.Channels.Length - 1;
                            }

                            SetSelection(newSelectionMin, newSelectionMax, timeOnly);
                        }
                        else
                        {
                            if (selectedPatternColumns.Count > 0)
                            {
                                var movedColumns = new HashSet<int>();

                                foreach (var selectedColumnIdx in selectedPatternColumns)
                                {
                                    var newPatternIdx = IncrementPatternIndex(selectedColumnIdx, patternIdxDelta, false);

                                    if (newPatternIdx >= 0 && newPatternIdx < Song.Length)
                                        movedColumns.Add(newPatternIdx);
                                }

                                selectedPatternColumns.Clear();

                                foreach (var movedColumnIdx in movedColumns)
                                    selectedPatternColumns.Add(movedColumnIdx);
                            }

                            var movedSelection = new HashSet<PatternLocation>();

                            foreach (var location in selectedPatternLocations)
                            {
                                var newChannelIdx =  IncrementChannelIndex(location.ChannelIndex, rowIdxDelta, false);
                                var newPatternIdx = IncrementPatternIndex(location.PatternIndex, patternIdxDelta, false);
                                if (newChannelIdx < 0 || newChannelIdx >= Song.Channels.Length ||
                                    newPatternIdx < 0 || newPatternIdx >= Song.Length)
                                {
                                    continue;
                                }

                                movedSelection.Add(new PatternLocation(newChannelIdx, newPatternIdx));
                            }

                            selectedPatternLocations.Clear();

                            foreach (var location in movedSelection)
                                selectedPatternLocations.Add(location);

                            timeOnlySelection = timeOnly && selectedPatternColumns.Count > 0;

                            if (selectedPatternLocations.Count > 0 ||
                                selectedPatternColumns.Count > 0)
                            {
                                var minChannelIdx = int.MaxValue;
                                var maxChannelIdx = int.MinValue;
                                var minPatternIdx = int.MaxValue;
                                var maxPatternIdx = int.MinValue;

                                foreach (var location in selectedPatternLocations)
                                {
                                    minChannelIdx = Math.Min(minChannelIdx, location.ChannelIndex);
                                    maxChannelIdx = Math.Max(maxChannelIdx, location.ChannelIndex);
                                    minPatternIdx = Math.Min(minPatternIdx, location.PatternIndex);
                                    maxPatternIdx = Math.Max(maxPatternIdx, location.PatternIndex);
                                }

                                foreach (var columnIdx in selectedPatternColumns)
                                {
                                    minChannelIdx = 0;
                                    maxChannelIdx = Song.Channels.Length - 1;
                                    minPatternIdx = Math.Min(minPatternIdx, columnIdx);
                                    maxPatternIdx = Math.Max(maxPatternIdx, columnIdx);
                                }

                                selectionMin = new PatternLocation(minChannelIdx, minPatternIdx);
                                selectionMax = new PatternLocation(maxChannelIdx, maxPatternIdx);
                            }
                            else
                            {
                                selectionMin = PatternLocation.Invalid;
                                selectionMax = PatternLocation.Invalid;
                            }

                            UpdateSelectedPatternRefCounts();
                        }

                        App.UndoRedoManager.EndTransaction();

                        MarkDirty();
                        PatternModified?.Invoke();
                        SelectionChanged?.Invoke();
                    }
                }
            }
        }

        internal void EndCaptureOperation(int x, int y)
        {
            if (captureOperation == CaptureOperation.None)
                return;

            var operation = captureOperation;

            switch (operation)
            {
                case CaptureOperation.DragSelection:
                    EndDragSelection(x, y);
                    break;

                case CaptureOperation.DragSeekBar:
                    UpdateSeekDrag(x, y, true);
                    break;

                case CaptureOperation.MobilePan:
                case CaptureOperation.MobileZoom:
                    canFling = true;
                    break;
            }

            panning = false;
            captureOperation = CaptureOperation.None;
            ReleasePointer();

            if ((operation == CaptureOperation.SelectRectangle || operation == CaptureOperation.SelectColumn) && IsSelectionValid())
            {
                UpdateSelectedPatternRefCounts();
            }

            MarkDirty();
            UpdateCursor();
        }

        protected override void OnPointerUp(PointerEventArgs e)
        {
            bool middle = e.Middle;

            if (middle)
            {
                panning = false;
            }
            else
            {
                EndCaptureOperation(e.X, e.Y);
            }

            UpdateCursor();

            if (e.Right && IsSelectionValid())
            {
                UpdateSelectedPatternRefCounts();
            }
        }

        internal void AbortCaptureOperation()
        {
            if (captureOperation != CaptureOperation.None)
            {
                if (App.UndoRedoManager.HasTransactionInProgress)
                    App.UndoRedoManager.AbortTransaction();

                panning = false;
                canFling = false;
                captureOperation = CaptureOperation.None;

                ReleasePointer();
                MarkDirty();
            }
            else
            {
                Debug.Assert(!App.UndoRedoManager.HasTransactionInProgress);
            }
        }

        protected void CancelDragSelection()
        {
            if (captureOperation == CaptureOperation.DragSelection)
            {
                selectionDragAnchorPatternIdx = -1;
                selectionDragAnchorPatternXFraction = -1.0f;
                captureOperation = CaptureOperation.None;
            }

            captureMouseX = -1;
            captureMouseY = -1;
        }

        public void DeleteSelection()
        {
            DeleteSelection(true, IsValidTimeOnlySelection());
        }

        internal void DeleteSelection(bool trans = true, bool clearCustomSettings = false, bool deleteNotesPastMax = true)
        {
            if (trans)
            {
                App.UndoRedoManager.BeginTransaction(TransactionScope.Song, Song.Id);
            }

            for (int i = selectionMin.ChannelIndex; i <= selectionMax.ChannelIndex; i++)
            {
                for (int j = selectionMin.PatternIndex; j <= selectionMax.PatternIndex; j++)
                {
                    var location = new PatternLocation(i, j);

                    if (!legacySelectMode && !IsPatternSelected(location))
                        continue;

                    var pattern = Song.Channels[i].PatternInstances[j];

                    if (pattern != null)
                        patternArea.NotifyPatternChange(pattern);

                    Song.Channels[i].PatternInstances[j] = null;
                }
            }

            if (clearCustomSettings)
            {
                for (int j = selectionMin.PatternIndex; j <= selectionMax.PatternIndex; j++)
                    Song.ClearPatternCustomSettings(j);
            }

            Song.InvalidateCumulativePatternCache();

            if (deleteNotesPastMax)
                Song.DeleteNotesPastMaxInstanceLength();

            if (trans)
            {
                ClearSelection();
                App.UndoRedoManager.EndTransaction();
                MarkDirty();
                PatternModified?.Invoke();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Key == Keys.Escape)
            {
                CancelDragSelection();
                UpdateCursor();
                ClearSelection();
                MarkDirty();
            }
            else if (IsActiveControl)
            {
                if (Settings.CopyShortcut.Matches(e))
                { 
                    Copy();
                }
                else if (Settings.CutShortcut.Matches(e))
                { 
                    Cut();
                }
                else if (Settings.PasteShortcut.Matches(e))
                { 
                    Paste();
                }
                else if (Settings.PasteSpecialShortcut.Matches(e))
                { 
                    PasteSpecial();
                }
                else if (Settings.DeleteShortcut.Matches(e) && IsSelectionValid())
                {
                    CancelDragSelection();
                    DeleteSelection();
                }
                else if (IsActiveControl && Settings.SelectAllShortcut.Matches(e))
                {
                    SetSelection(new PatternLocation(0, 0), new PatternLocation(Song.Channels.Length - 1, Song.Length - 1), true);
                }
            }

            if (captureOperation == CaptureOperation.DragSelection)
            {
                UpdateCursor();
                MarkDirty();
            }
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (captureOperation == CaptureOperation.DragSelection)
            {
                UpdateCursor();
                MarkDirty();
            }
        }

        private void UpdateSeekDrag(int mouseX, int mouseY, bool final)
        {
            ScrollIfNearEdge(mouseX, mouseY);

            dragSeekPosition = GetNoteForPixel(mouseX - channelNameSizeX);

            if (final)
                App.SeekSong(dragSeekPosition);

            MarkDirty();
        }

        private void ScrollIfNearEdge(int x, int y, bool scrollHorizontal = true, bool scrollVertical = false)
        {
            if (scrollHorizontal)
            {
                int posMinX = 0;
                int posMaxX = Platform.IsDesktop ? width + channelNameSizeX : (IsLandscape ? width + headerSizeY : width);
                int marginMinX = channelNameSizeX;
                int marginMaxX = Platform.IsDesktop ? channelNameSizeX : headerSizeY;

                scrollX += Utils.ComputeScrollAmount(x, posMinX, marginMinX, App.AverageTickRate * ScrollSpeedFactor, true);
                scrollX += Utils.ComputeScrollAmount(x, posMaxX, marginMaxX, App.AverageTickRate * ScrollSpeedFactor, false);
                ClampScroll();
            }

            if (scrollVertical)
            {
                int posMinY = 0;
                int posMaxY = Platform.IsMobile && !IsLandscape || Platform.IsDesktop ? height + headerSizeY : height;
                int marginMinY = headerSizeY;
                int marginMaxY = headerSizeY;

                scrollY += Utils.ComputeScrollAmount(y, posMinY, marginMinY, App.AverageTickRate * ScrollSpeedFactor, true);
                scrollY += Utils.ComputeScrollAmount(y, posMaxY, marginMaxY, App.AverageTickRate * ScrollSpeedFactor, false);
                ClampScroll();
            }
        }

        private void UpdateSelection(int x, int y, bool timeOnly, bool first = false)
        {
            ScrollIfNearEdge(x, y, true, !timeOnly && CanScrollVertically());

            if (timeOnly)
            {
                Debug.Assert(capturePatternIdx >= 0);

                var currentColumnIdx = capturePatternIdx;

                if (!first)
                    GetClampedPatternForCoord(x, y, out _, out currentColumnIdx);

                var minPatternIdx = Math.Min(currentColumnIdx, capturePatternIdx);
                var maxPatternIdx = Math.Max(currentColumnIdx, capturePatternIdx);

                captureSelectionMin.PatternIndex = minPatternIdx;
                captureSelectionMax.PatternIndex = maxPatternIdx;

                selectedPatternColumns.Clear();

                if (!legacySelectMode && ModifierKeys.IsControlDown)
                {
                    foreach (var patternIdx in captureSelectedPatternColumns)
                        selectedPatternColumns.Add(patternIdx);
                }

                for (var patternIdx = minPatternIdx; patternIdx <= maxPatternIdx; patternIdx++)
                    selectedPatternColumns.Add(patternIdx);

                selectionMin.PatternIndex = selectedPatternColumns.Min();
                selectionMax.PatternIndex = selectedPatternColumns.Max();
                selectionMin.ChannelIndex = 0;
                selectionMax.ChannelIndex = Song.Channels.Length - 1;

                if (!legacySelectMode)
                {
                    RebuildPatternLocationsFromSelectedColumns();

                    var hasPatternOutsideSelectedColumns = false;

                    if (ModifierKeys.IsControlDown)
                    {
                        foreach (var location in captureSelectedPatternLocations)
                        {
                            selectedPatternLocations.Add(location);

                            if (!selectedPatternColumns.Contains(location.PatternIndex))
                            {
                                hasPatternOutsideSelectedColumns = true;
                                selectionMin.PatternIndex = Math.Min(selectionMin.PatternIndex, location.PatternIndex);
                                selectionMax.PatternIndex = Math.Max(selectionMax.PatternIndex, location.PatternIndex);
                            }
                        }
                    }

                    timeOnlySelection = !hasPatternOutsideSelectedColumns;
                }
                else
                {
                    timeOnlySelection = true;
                }

                MarkDirty();
                SelectionChanged?.Invoke();
                return;
            }

            if (legacySelectMode)
            {
                Debug.Assert(captureChannelIdx >= 0 && capturePatternIdx >= 0);

                if (first)
                {
                    selectionMin = new PatternLocation(captureChannelIdx, capturePatternIdx);
                    selectionMax = selectionMin;
                }
                else
                {
                    GetClampedPatternForCoord(x, y, out var channelIdx, out var patternIdx);

                    selectionMin = new PatternLocation(Math.Min(channelIdx, captureChannelIdx), Math.Min(patternIdx, capturePatternIdx));
                    selectionMax = new PatternLocation(Math.Max(channelIdx, captureChannelIdx), Math.Max(patternIdx, capturePatternIdx));
                }

                timeOnlySelection = false;

                MarkDirty();
                SelectionChanged?.Invoke();
                return;
            }

            Debug.Assert(captureChannelIdx >= 0 && capturePatternIdx >= 0);

            var currentChannelIdx = captureChannelIdx;
            var currentPatternIdx = capturePatternIdx;

            if (!first)
                GetClampedPatternForCoord(x, y, out currentChannelIdx, out currentPatternIdx);

            var marqueeMinChannel = Math.Min(currentChannelIdx, captureChannelIdx);
            var marqueeMaxChannel = Math.Max(currentChannelIdx, captureChannelIdx);
            var marqueeMinPattern = Math.Min(currentPatternIdx, capturePatternIdx);
            var marqueeMaxPattern = Math.Max(currentPatternIdx, capturePatternIdx);

            captureSelectionMin = new PatternLocation(marqueeMinChannel, marqueeMinPattern);
            captureSelectionMax = new PatternLocation(marqueeMaxChannel, marqueeMaxPattern);

            selectedPatternColumns.Clear();

            if (ModifierKeys.IsControlDown)
            {
                foreach (var patternIdx in captureSelectedPatternColumns)
                    selectedPatternColumns.Add(patternIdx);
            }

            var result = ModifierKeys.IsControlDown ? new HashSet<PatternLocation>(captureSelectedPatternLocations) : new HashSet<PatternLocation>();

            for (var channel = marqueeMinChannel; channel <= marqueeMaxChannel; channel++)
            {
                for (var pattern = marqueeMinPattern; pattern <= marqueeMaxPattern; pattern++)
                {
                    var location = new PatternLocation(channel, pattern);

                    if (Song.GetPatternInstance(location) != null)
                        result.Add(location);
                }
            }

            selectedPatternLocations.Clear();

            if (result.Count > 0)
            {
                var minChannelIdx = int.MaxValue;
                var maxChannelIdx = int.MinValue;
                var minPatternIdx = int.MaxValue;
                var maxPatternIdx = int.MinValue;

                foreach (var location in result)
                {
                    minChannelIdx = Math.Min(minChannelIdx, location.ChannelIndex);
                    maxChannelIdx = Math.Max(maxChannelIdx, location.ChannelIndex);
                    minPatternIdx = Math.Min(minPatternIdx, location.PatternIndex);
                    maxPatternIdx = Math.Max(maxPatternIdx, location.PatternIndex);

                    selectedPatternLocations.Add(location);
                }

                selectionMin = new PatternLocation(minChannelIdx, minPatternIdx);
                selectionMax = new PatternLocation(maxChannelIdx, maxPatternIdx);
            }
            else
            {
                selectionMin = PatternLocation.Invalid;
                selectionMax = PatternLocation.Invalid;
            }

            timeOnlySelection = false;

            MarkDirty();
            SelectionChanged?.Invoke();
        }

        private void UpdateDragSelection(int x, int y)
        {
            ScrollIfNearEdge(x, y, true, Platform.IsMobile || CanScrollVertically());
            MarkDirty();
        }

        private void UpdateSequencerResize(int y)
        {
            var newHeight = captureSequencerHeight + (y - captureMouseY);
            var minHeight = DpiScaling.ScaleForWindow(100);
            var maxHeight = (int)Math.Round(ParentWindow.Height * 0.9f);

            newHeight = Utils.Clamp(newHeight, minHeight, maxHeight);

            var constantSize  = DpiScaling.ScaleForWindow(DefaultHeaderSizeY + scrollBarThickness + resizeBarSizeY + 1);
            var prevHeight    = sequencerHeightOverride;

            sequencerHeightOverride = (int)Math.Round((newHeight - constantSize) / DpiScaling.Window);

            // Don't spam layout updates if we haven't resized.
            if (sequencerHeightOverride != prevHeight)
                ParentTopContainer.UpdateLayout();
        }

        private void UpdateAltZoom(int x, int y)
        {
            var deltaY = y - captureMouseY;

            if (Math.Abs(deltaY) > 50)
            {
                ZoomAtLocation(x, deltaY < 0.0f ? 2.0f : 0.5f);
                captureMouseY = y;
            }
        }

        internal void NotifyPatternClicked(PatternLocation location)
        {
            PatternClicked?.Invoke(location.ChannelIndex, location.PatternIndex, false);
        }

        internal void StartAltZoom(Control control, PointerEventArgs e)
        {
            var p = WindowToControl(control.ControlToWindow(e.Position));
            StartCaptureOperation(p.X, p.Y, CaptureOperation.AltZoom, false);
        }

        internal void ResetCaptureThreshold()
        {
            captureThresholdMet = false;
        }

        private void UpdateTooltip()
        {
            App.SetToolTip(IsPointInResizeArea(mouseLastY) ?$"<MouseLeft><Drag> {ResizeSequencerTooltip}" : "");
        }

        private void UpdateScrollBarX(int x, int y)
        {
            GetScrollBarParams(true, out _, out var scrollBarSizeX, out _);
            GetMinMaxScroll(out _, out var maxScrollX, out _, out _);
            int scrollAreaSizeX = width - channelNameSizeX;
            scrollX = (int)Math.Round(captureScrollX + ((x - captureMouseX) / (float)(scrollAreaSizeX - scrollBarSizeX) * maxScrollX));
            ClampScroll();
            MarkDirty();
        }

        private void UpdateScrollBarY(int x, int y)
        {
            GetScrollBarParams(false, out _, out var scrollBarSizeY, out _);
            GetMinMaxScroll(out _, out _, out _, out var maxScrollY);
            int scrollAreaSizeY = height - headerSizeY - scrollBarThickness;
            scrollY = (int)Math.Round(captureScrollY + ((y - captureMouseY) / (float)(scrollAreaSizeY - scrollBarSizeY) * maxScrollY));
            ClampScroll();
            MarkDirty();
        }

        private bool CanScrollVertically()
        {
            GetMinMaxScroll(out _, out _, out var minScrollY, out var maxScrollY);
            return minScrollY != maxScrollY;
        }

        internal void UpdatePointerCapture(int x, int y)
        {
            UpdateCaptureOperation(x, y);

            if (panning)
                DoScroll(x - mouseLastX, y - mouseLastY);

            SetMouseLastPos(x, y);
            UpdateCursor();
        }

        internal void UpdateColumnSelection(int x, int y)
        {
            if (captureOperation == CaptureOperation.SelectColumn)
                UpdateCaptureOperation(x, y);
        }

        internal void EndColumnSelection(int x, int y)
        {
            if (captureOperation != CaptureOperation.SelectColumn)
                return;

            EndCaptureOperation(x, y);

            if (IsSelectionValid())
                UpdateSelectedPatternRefCounts();
        }

        private void UpdateCaptureOperation(int x, int y, float scale = 1.0f, bool realTime = false)
        {
            const int CaptureThreshold = Platform.IsDesktop ? 5 : 50;

            var captureThresholdJustMet = false;

            if (captureOperation != CaptureOperation.None)
            {
                if (!captureThresholdMet)
                {
                    if (Math.Abs(x - captureMouseX) >= CaptureThreshold ||
                        Math.Abs(y - captureMouseY) >= CaptureThreshold)
                    {
                        captureThresholdMet = true;
                        captureThresholdJustMet = true;
                    }
                }

                mouseMovedDuringCapture |= (x != captureMouseX || y != captureMouseY);
            }

            if (captureOperation != CaptureOperation.None && captureThresholdMet && (captureRealTimeUpdate || !realTime))
            {
                switch (captureOperation)
                {
                    case CaptureOperation.SelectColumn:
                        UpdateSelection(x, y, true, captureThresholdJustMet);
                        break;
                    case CaptureOperation.SelectRectangle:
                        UpdateSelection(x, y, false, captureThresholdJustMet);
                        break;
                    case CaptureOperation.AltZoom:
                        UpdateAltZoom(x, y);
                        break;
                    case CaptureOperation.DragSeekBar:
                        UpdateSeekDrag(x, y, false);
                        break;
                    case CaptureOperation.ScrollBarX:
                        UpdateScrollBarX(x, y);
                        break;
                    case CaptureOperation.ScrollBarY:
                        UpdateScrollBarY(x, y);
                        break;
                    case CaptureOperation.MobilePan:
                        DoScroll(x - mouseLastX, y - mouseLastY);
                        break;
                    case CaptureOperation.DragSelection:
                        UpdateDragSelection(x, y);
                        break;
                    case CaptureOperation.ResizeSequencer:
                        UpdateSequencerResize(y);
                        break;
                    case CaptureOperation.MobileZoom:
                        ZoomAtLocation(x, scale);
                        DoScroll(x - mouseLastX, y - mouseLastY);
                        break;
                    default:
                        MarkDirty();
                        break;
                }
            }
        }

        protected override void OnPointerMove(PointerEventArgs e)
        {
            var x = e.X;
            var y = e.Y;

            bool middle = e.Middle || (e.Left && ModifierKeys.IsAltDown && Settings.AltLeftForMiddle);

            base.OnPointerMove(e);

            UpdateCaptureOperation(x, y);

            if (middle)
                DoScroll(x - mouseLastX, y - mouseLastY);
            else if (e.Right && selectedPatternRefCounts.Count > 0 && (captureOperation == CaptureOperation.SelectRectangle || captureOperation == CaptureOperation.SelectColumn))
                selectedPatternRefCounts.Clear();

            ClearHover();
            SetMouseLastPos(x, y);
            UpdateTooltip();
            UpdateCursor();
        }

        internal void SetPatternAreaHover(int rowIdx, int patternIdx)
        {
            if (!Platform.IsDesktop)
                return;

            if (captureOperation == CaptureOperation.ResizeSequencer)
                rowIdx = -1;

            channelArea.SetHover(rowIdx);
            timeline.SetHoverPattern(patternIdx);
        }

        internal void SetTimelineHover(int patternIdx)
        {
            if (!Platform.IsDesktop)
                return;

            channelArea.SetHover(-1);
            timeline.SetHoverPattern(patternIdx);
        }

        internal void SetChannelHover(int rowIdx)
        {
            if (!Platform.IsDesktop)
                return;

            channelArea.SetHover(rowIdx);
            timeline.SetHoverPattern(-1);
        }

        internal void ClearHover()
        {
            if (!Platform.IsDesktop)
                return;

            channelArea.SetHover(-1);
            timeline.SetHoverPattern(-1);
        }

        protected override void OnPointerLeave(EventArgs e)
        {
            base.OnPointerLeave(e);
            ClearHover();
            UpdateCursor();
        }

        // Custom pattern.
        private void EditPatternCustomSettings(Point pt, int patternIdx)
        {
            var dlg = new PropertyDialog(ParentWindow, CustomPatternTitle, new Point(left + pt.X, top + pt.Y), 300);
            var song = Song;
            var enabled = song.PatternHasCustomSettings(patternIdx);

            var minPattern = patternIdx;
            var maxPattern = patternIdx;

            if (HasTimelineSelection)
            {
                minPattern = selectionMin.PatternIndex;
                maxPattern = selectionMax.PatternIndex;
            }

            var tempoProperties = new TempoProperties(dlg.Properties, song, patternIdx, minPattern, maxPattern, IsValidTimeOnlySelection() && !legacySelectMode ? IsPatternColumnSelected : null);

            dlg.Properties.AddCheckBox(CustomPatternLabel.Colon, song.PatternHasCustomSettings(patternIdx), CustomPatternTooltip); // 0
            tempoProperties.AddProperties();
            tempoProperties.EnableProperties(enabled);
            dlg.Properties.PropertyChanged += PatternCustomSettings_PropertyChanged;
            dlg.Properties.PropertiesUserData = tempoProperties;
            dlg.Properties.Build();

            dlg.ShowDialogAsync((r) =>
            {
                if (r == DialogResult.OK)
                {
                    App.UndoRedoManager.BeginTransaction(TransactionScope.Song, song.Id);
                    tempoProperties.ApplyAsync(ParentWindow, dlg.Properties.GetPropertyValue<bool>(0), () =>
                    {
                        App.UndoRedoManager.EndTransaction();
                        MarkDirty();
                        PatternModified?.Invoke();
                    });
                }
            });
        }

        private void PatternCustomSettings_PropertyChanged(PropertyPage props, int propIdx, int rowIdx, int colIdx, object value)
        {
            if (propIdx == 0)
            {
                var tempoProperties = props.PropertiesUserData as TempoProperties;
                tempoProperties.EnableProperties((bool)value);
            }
        }

        internal void EditPatternProperties(Point pt, Pattern pattern, PatternLocation location, bool selection = true)
        {
            bool multipleChannelsSelected = selection && IsSelectionValid() && (selectionMax.ChannelIndex != selectionMin.ChannelIndex);
            bool multiplePatternsSelected = selection && IsSelectionValid() && ((selectionMax.ChannelIndex != selectionMin.ChannelIndex) || (selectionMin.PatternIndex != selectionMax.PatternIndex));

            var dlg = new PropertyDialog(ParentWindow, PatternPropertiesTitle, new Point(left + pt.X, top + pt.Y), 240, false, false, false);
            dlg.Properties.AddColoredTextBox(multiplePatternsSelected ? MultiplePatternsSelectedLabel : pattern.Name, pattern.Color);
            dlg.Properties.SetPropertyEnabled(0, !multiplePatternsSelected);
            dlg.Properties.AddColorPicker(pattern.Color);
            dlg.Properties.Build();

            dlg.ShowDialogAsync((r) =>
            {
                if (r == DialogResult.OK)
                {
                    if (!multipleChannelsSelected)
                        App.UndoRedoManager.BeginTransaction(TransactionScope.Channel, Song.Id, location.ChannelIndex);
                    else
                        App.UndoRedoManager.BeginTransaction(TransactionScope.Song, Song.Id);

                    var newName  = dlg.Properties.GetPropertyValue<string>(0).Trim();
                    var newColor = dlg.Properties.GetPropertyValue<Color>(1);

                    if (multiplePatternsSelected)
                    {
                        for (int i = selectionMin.ChannelIndex; i <= selectionMax.ChannelIndex; i++)
                        {
                            for (int j = selectionMin.PatternIndex; j <= selectionMax.PatternIndex; j++)
                            {
                                var pat = Song.Channels[i].PatternInstances[j];
                                if (pat != null)
                                    pat.Color = newColor;
                            }
                        }
                        App.UndoRedoManager.EndTransaction();
                    }
                    else if (Song.Channels[location.ChannelIndex].RenamePattern(pattern, newName))
                    {
                        pattern.Color = newColor;
                        App.UndoRedoManager.EndTransaction();
                    }
                    else
                    {
                        App.UndoRedoManager.AbortTransaction();
                        App.DisplayNotification(ErrorRenamingPattern, true);
                    }

                    MarkDirty();
                    PatternModified?.Invoke();
                }
            });
        }

        private void ZoomAtLocation(int x, float scale)
        {
            if (scale == 1.0f)
                return;

            // When continuously following, zoom at the seek bar location.
            if (continuouslyFollowing)
                x = (int)(Width * Settings.FollowPercent);

            Debug.Assert(Platform.IsMobile || scale == 0.5f || scale == 2.0f);

            var pixelX = x - channelNameSizeX;
            var absoluteX = pixelX + scrollX;
            var prevNoteSizeX = noteSizeX;

            zoom *= scale;
            zoom = Utils.Clamp(zoom, MinZoom, MaxZoom);

            // This will update the noteSizeX.
            UpdateRenderCoords();

            absoluteX = (int)Math.Round(absoluteX * (noteSizeX / (double)prevNoteSizeX));
            scrollX = absoluteX - pixelX;

            ClampScroll();
            MarkDirty();
        }

        protected override void OnMouseWheel(PointerEventArgs e)
        {
            base.OnMouseWheel(e);
            HandleMouseWheel(e.X, e);
        }

        protected override void OnMouseHorizontalWheel(PointerEventArgs e)
        {
            scrollX += Utils.SignedCeil(e.ScrollX);
            ClampScroll();
            MarkDirty();
        }

        protected bool EnsureSeekBarVisible(float percent = float.MinValue)
        {
            if (percent == float.MinValue)
                percent = Settings.FollowPercent;

            var seekX = GetPixelForNote(App.CurrentFrame);
            var minX = 0;
            var maxX = (int)((width - channelNameSizeX) * percent);

            // Keep everything visible 
            if (seekX < minX)
                scrollX -= (minX - seekX);
            else if (seekX > maxX)
                scrollX += (seekX - maxX);

            ClampScroll();

            seekX = GetPixelForNote(App.CurrentFrame);
            return seekX == maxX;
        }

        public void UpdateFollowMode(bool force = false)
        {
            continuouslyFollowing = false;

            if ((App.IsPlaying || force) && App.FollowModeEnabled && Settings.FollowSync != Settings.FollowSyncPianoRoll && !panning && 
                captureOperation == CaptureOperation.None && !window.IsAsyncDialogInProgress && !window.IsOutOfProcessDialogInProgress)
            {
                var frame = App.CurrentFrame;
                var seekX = GetPixelForNote(App.CurrentFrame);

                if (Settings.FollowMode == Settings.FollowModeJump)
                {
                    var maxX = width - channelNameSizeX;
                    if (seekX < 0 || seekX > maxX)
                        scrollX = GetPixelForNote(frame, false);
                }
                else
                {
                    continuouslyFollowing = EnsureSeekBarVisible();
                }

                ClampScroll();
            }
        }

        private void TickFling(float delta)
        {
            if (flingVelX != 0.0f ||
                flingVelY != 0.0f)
            {
                var deltaPixelX = (int)Math.Round(flingVelX * delta);
                var deltaPixelY = (int)Math.Round(flingVelY * delta);

                if ((deltaPixelX != 0 || deltaPixelY != 0) && DoScroll(deltaPixelX, deltaPixelY))
                {
                    flingVelX *= (float)Math.Exp(delta * -4.5f);
                    flingVelY *= (float)Math.Exp(delta * -4.5f);
                }
                else
                {
                    flingVelX = 0.0f;
                    flingVelY = 0.0f;
                }
            }
        }

        private void UpdateShyIcon()
        {
            if (hideEmptyChannels && numRows == 0)
            {
                SetAndMarkDirty(ref forceShyOff, Utils.Frac(Platform.TimeSeconds()) < 0.25f);
            }
            else
            {
                forceShyOff = false;
            }

            if (channelArea != null)
                channelArea.ForceShyOff = forceShyOff;
        }

        public override void Tick(float delta)
        {
            if (App == null)
                return;

            UpdateCaptureOperation(mouseLastX, mouseLastY, 1.0f, true);
            UpdateFollowMode();
            UpdateShyIcon();
            TickFling(delta);
        }

        public void SongModified()
        {
            UpdateRenderCoords();
            InvalidatePatternCache();
            ClearSelection();
            ClampScroll();
            MarkDirty();
        }

        public void InvalidatePatternCache()
        {
            patternArea.InvalidatePatternCache();
        }

        private void SerializeSelectedPatternLocations(ProjectBuffer buffer)
        {
            var count = selectedPatternLocations.Count;
            buffer.Serialize(ref count);

            if (buffer.IsWriting)
            {
                foreach (var location in selectedPatternLocations.OrderBy(p => p.ChannelIndex).ThenBy(p => p.PatternIndex))
                {
                    var channelIdx = location.ChannelIndex;
                    var patternIdx = location.PatternIndex;

                    buffer.Serialize(ref channelIdx);
                    buffer.Serialize(ref patternIdx);
                }
            }
            else
            {
                selectedPatternLocations.Clear();

                for (var i = 0; i < count; i++)
                {
                    var channelIdx = 0;
                    var patternIdx = 0;

                    buffer.Serialize(ref channelIdx);
                    buffer.Serialize(ref patternIdx);

                    selectedPatternLocations.Add(new PatternLocation(channelIdx, patternIdx));
                }
            }
        }

        private void SerializeSelectedPatternColumns(ProjectBuffer buffer)
        {
            var count = selectedPatternColumns.Count;
            buffer.Serialize(ref count);

            if (buffer.IsWriting)
            {
                foreach (var patternIdx in selectedPatternColumns.OrderBy(p => p))
                {
                    var idx = patternIdx;
                    buffer.Serialize(ref idx);
                }
            }
            else
            {
                selectedPatternColumns.Clear();

                for (var i = 0; i < count; i++)
                {
                    var patternIdx = 0;
                    buffer.Serialize(ref patternIdx);
                    selectedPatternColumns.Add(patternIdx);
                }
            }
        }

        public void Serialize(ProjectBuffer buffer)
        {
            if (Settings.RestoreViewOnUndoRedo || buffer.IsWriting)
            {
                buffer.Serialize(ref scrollX);
                buffer.Serialize(ref zoom);
            }
            else
            {
                var dummyScroll = 0;
                var dummyZoom = 0.0f;
                buffer.Serialize(ref dummyScroll);
                buffer.Serialize(ref dummyZoom);
            }

            buffer.Serialize(ref selectionMin.ChannelIndex);
            buffer.Serialize(ref selectionMax.ChannelIndex);
            buffer.Serialize(ref selectionMin.PatternIndex);
            buffer.Serialize(ref selectionMax.PatternIndex);
            buffer.Serialize(ref timeOnlySelection);
            buffer.Serialize(ref hideEmptyChannels);

            SerializeSelectedPatternLocations(buffer);
            SerializeSelectedPatternColumns(buffer);

            if (buffer.IsReading)
            {
                // TODO: This is overly aggressive. We should have the 
                // scope on the transaction on the buffer and filter by that.
                UpdateSelectedPatternRefCounts();
                InvalidatePatternCache();
                UpdateRenderCoords();
                CancelDragSelection();
                ClearHighlightedPattern();
                MarkDirty();
            }
        }
    }
}