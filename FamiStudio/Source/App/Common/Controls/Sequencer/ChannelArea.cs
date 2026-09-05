using System;
using System.Diagnostics;

namespace FamiStudio
{
    internal class ChannelArea : Container
    {
        const int   RowClipInset    = 1;

        static readonly float HeaderIconScale = Platform.IsMobile ? DpiScaling.ScaleForWindowFloat(0.5f)  : 1.0f;
        static readonly float IconScale       = Platform.IsMobile ? DpiScaling.ScaleForWindowFloat(0.25f) : 1.0f;

        private readonly Sequencer sequencer;
        private Container rowsContainer;
        private ChannelRow[] rows;
        private Button shyButton;
        private bool forceShyOff;
        private int bottomY;

        private Song Song => App?.SelectedSong;

        private Sequencer.SequencerViewport Viewport => sequencer.Viewport;

        private bool ShowExpansionIcons => sequencer.ShowExpansionIcons;
        private bool HideEmptyChannels  => sequencer.HideEmptyChannels;
        private int[] ChannelToRow      => sequencer.ChannelToRow;
        private int ChannelNameSizeX    => sequencer.ChannelNameSizeX;
        private int ContentBottomY      => sequencer.ContentBottomY;
        private int HeaderSizeY         => sequencer.HeaderSizeY;

        internal LocalizedString MoreOptionsText => sequencer.MoreOptionsText;

        public bool ForceShyOff
        {
            get => forceShyOff;
            set
            {
                if (forceShyOff != value)
                {
                    forceShyOff = value;
                    MarkDirty();
                }
            }
        }

        LocalizedString ShyModeTooltip;

        internal ChannelArea(Sequencer sequencer)
        {
            this.sequencer = sequencer;
            Localization.Localize(this);
        }

        protected override void OnAddedToContainer()
        {
            rowsContainer = new Container();

            shyButton = new Button("ShyOff")
            {
                Transparent = true,
                ImageScale = HeaderIconScale,
                ToolTip = $"<MouseLeft> {ShyModeTooltip}"
            };

            shyButton.ImageEvent += ShyButton_ImageEvent;
            shyButton.Click      += ShyButton_Clicked;

            AddControl(rowsContainer);
            AddControl(shyButton);
        }

        private string ShyButton_ImageEvent(Control sender, ref Color tint)
        {
            tint = Theme.LightGreyColor2;
            return HideEmptyChannels && !forceShyOff ? "ShyOn" : "ShyOff";
        }

        private void ShyButton_Clicked(Control sender)
        {
            sequencer.SetHideEmptyChannels(!HideEmptyChannels);
            sequencer.LayoutChanged();
        }

        private void RecreateRows()
        {
            if (rows != null)
            {
                foreach (var row in rows)
                    rowsContainer.RemoveControl(row);
            }

            rows = new ChannelRow[Song.Channels.Length];

            for (var i = 0; i < rows.Length; i++)
            {
                var row = new ChannelRow(this, i);

                row.IconClicked += App.ToggleChannelActive;
                row.Clicked += (idx) =>
                {
                    if (idx != App.SelectedChannelIndex)
                        App.SelectedChannelIndex = idx;
                };
                row.SoloToggled += (idx) =>
                {
                    App.ToggleChannelSolo(idx, true);
                    MarkDirty();
                };
                row.ForceDisplayClicked += App.ToggleChannelForceDisplay;
                row.ForceDisplaySoloToggled += (idx) =>
                {
                    App.ToggleChannelForceDisplayAll(idx, true);
                    MarkDirty();
                };

                row.IconScale = IconScale;

                rows[i] = row;
                rowsContainer.AddControl(row);
            }
        }

        public void Reset()
        {
            RecreateRows();
        }

        public void UpdateLayout()
        {
            Resize(ChannelNameSizeX, ContentBottomY + sequencer.ScrollBarThickness);

            rowsContainer.Move(0, HeaderSizeY + RowClipInset);

            shyButton.Move(Width - HeaderSizeY, 0);
            shyButton.Resize(HeaderSizeY, HeaderSizeY);

            if (ChannelToRow == null)
                return;

            if (rows == null || rows.Length != ChannelToRow.Length)
                RecreateRows();
                
            var vp     = Viewport;
            var maxRow = -1;

            for (var i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                var rowIdx = ChannelToRow[i];

                if (rowIdx < 0)
                {
                    row.Visible = false;
                    row.Hovered = false;
                    continue;
                }

                maxRow = Math.Max(maxRow, rowIdx);

                row.Visible = true;

                row.Resize(Width, vp.ChannelSizeY);
                row.Move(0, rowIdx * vp.ChannelSizeY - RowClipInset);
            }

            var rowCount = maxRow + 1;

            rowsContainer.Visible = rowCount > 0;
            rowsContainer.Resize(Width, rowCount * vp.ChannelSizeY - RowClipInset);

            // Only enable tick when we have zero channels visible. Used for updating the shy icon.
            var flashShy = HideEmptyChannels && !rowsContainer.Visible;
            SetTickEnabled(flashShy);

            if (!flashShy)
                SetAndMarkDirty(ref forceShyOff, false);

            bottomY = HeaderSizeY + rowCount * vp.ChannelSizeY;
        }

        private void UpdateShyIcon()
        {
            SetAndMarkDirty(ref forceShyOff, Utils.Frac(Platform.TimeSeconds()) < 0.25f);
        }

        public void SetHover(int hoverRow)
        {
            for (var i = 0; i < rows.Length; i++)
            {
                var rowIdx = ChannelToRow[i];
                rows[i].Hovered = rowIdx >= 0 && rowIdx == hoverRow;
            }
        }

        public void UpdateScroll(int y)
        {
            rowsContainer.ScrollY = y;
        }

        public override void Tick(float delta)
        {
            base.Tick(delta);
            UpdateShyIcon();
        }

        public override void OnContainerPointerMoveNotify(Control control, PointerEventArgs e)
        {
            base.OnContainerPointerMoveNotify(control, e);

            for (var i = 0; i < rows.Length; i++)
            {
                var row = rows[i];

                if (control == row || control.IsInContainer(row))
                {
                    sequencer.SetChannelHover(ChannelToRow[i]);
                    return;
                }
            }

            sequencer.ClearHover();
        }

        protected override void OnMouseWheel(PointerEventArgs e)
        {
            base.OnMouseWheel(e);

            if (!e.Handled)
            {
                if (Settings.AllowSequencerVerticalScroll)
                    sequencer.AdjustPatternHeight(this, e);
                else
                    sequencer.HandleMouseWheel(this, e);

                e.MarkHandled();
            }
        }

        public override void OnContainerMouseWheelNotify(Control control, PointerEventArgs e)
        {
            base.OnContainerMouseWheelNotify(control, e);

            if (!e.Handled)
            {
                if (Settings.AllowSequencerVerticalScroll)
                    sequencer.AdjustPatternHeight(control, e);
                else
                    sequencer.HandleMouseWheel(this, e);

                e.MarkHandled();
            }
        }

        protected override void OnPointerEnter(EventArgs e)
        {
            base.OnPointerEnter(e);

            // This is outside the channel rows, and not the shy button.
            App.SetToolTip("");
            sequencer.ClearHover();
        }

        protected override void OnRender(Graphics g)
        {
            var c = g.DefaultCommandList;

            c.FillRectangle(0, 0, Width, Height, Theme.DarkGreyColor2);

            base.OnRender(g);

            c.DrawLine(Width - 1, 0, Width - 1, Height, Theme.BlackColor);
            c.DrawLine(0, 0, Width, 0, Theme.BlackColor);
            c.DrawLine(0, HeaderSizeY, Width, HeaderSizeY, Theme.BlackColor);

            if (rowsContainer.Visible)
                c.DrawLine(0, bottomY - Viewport.ScrollY, Width, bottomY - Viewport.ScrollY, Theme.BlackColor);

            if (Platform.IsMobile && IsLandscape)
                c.DrawLine(0, 0, 0, Height, Theme.BlackColor);
        }

        private class ChannelRow : Container
        {
            const int DefaultChannelIconPosX  = 2;
            const int DefaultChannelIconPosY  = 3;
            const int DefaultChannelNamePosX  = 21;
            const int DefaultGhostNoteOffsetX = 16;
            const int DefaultGhostNoteOffsetY = Platform.IsMobile ? 16 : 15;
            const int DefaultChannelIconSize  = 16;
            const int DefaultGhostIconSize    = 12;
            
            private readonly ChannelArea channelArea;
            private Button channelButton;
            private Button forceDisplayButton;
            private bool hovered;
            private float iconScale = 1.0f;

            public delegate void ChannelDelegate(int channelIdx);

            public ChannelDelegate Clicked;
            public ChannelDelegate IconClicked;
            public ChannelDelegate SoloToggled;
            public ChannelDelegate ForceDisplayClicked;
            public ChannelDelegate ForceDisplaySoloToggled;

            public int ChannelIndex { get; private set; }
            public bool Hovered
            {
                get => hovered;
                set
                {
                    if (hovered != value)
                    {
                        hovered = value;
                        MarkDirty();
                    }
                }
            }

            public float IconScale
            {
                get => iconScale;
                set
                {
                    iconScale = value;

                    if (channelButton != null)
                        channelButton.ImageScale = value;

                    if (forceDisplayButton != null)
                        forceDisplayButton.ImageScale = value;
                }
            }

            LocalizedString MuteChannelTooltip;
            LocalizedString SoloChannelTooltip;
            LocalizedString ForceDisplayTooltip;
            LocalizedString ForceDisplayAllChannelsTooltip;
            LocalizedString MakeActiveTooltip;
            LocalizedString ToggleMuteLabel;
            LocalizedString ToggleSoloLabel;
            LocalizedString ForceDisplayLabel;

            public ChannelRow(ChannelArea channelArea, int channelIdx)
            {
                this.channelArea = channelArea;
                Localization.Localize(this);
                ChannelIndex = channelIdx;
                UpdateToolTip();
            }

            protected override void OnAddedToContainer()
            {
                channelButton = new ChannelIconButton(ChannelType.Icons[ChannelType.Square1], () => SoloToggled?.Invoke(ChannelIndex))
                {
                    Transparent = true,
                    ImageScale = iconScale
                };

                forceDisplayButton = new ChannelIconButton("GhostSmall", () => ForceDisplaySoloToggled?.Invoke(ChannelIndex))
                {
                    Transparent = true,
                    ImageScale = iconScale
                };

                channelButton.Click      += (s) => IconClicked?.Invoke(ChannelIndex);
                forceDisplayButton.Click += (s) => ForceDisplayClicked?.Invoke(ChannelIndex);

                channelButton.ImageEvent       += ChannelButton_ImageEvent;
                forceDisplayButton.DimmedEvent += ForceDisplayButton_DimmedEvent;

                channelButton.ToolTip      = $"<MouseLeft> {MuteChannelTooltip} - <MouseLeft><MouseLeft> {SoloChannelTooltip}";

                UpdateForceDisplayToolTip();

                AddControl(channelButton);
                AddControl(forceDisplayButton);
            }

            private void UpdateToolTip()
            {
                var tooltip = $"<MouseLeft> {MakeActiveTooltip}";

                if (ChannelIndex >= 0 && ChannelIndex < Settings.ActiveChannelShortcuts.Length)
                {
                    tooltip += $" {Settings.ActiveChannelShortcuts[ChannelIndex].TooltipString}";
                }

                tooltip += $" - <MouseRight> {channelArea.MoreOptionsText}";

                ToolTip = tooltip;
            }

            private void UpdateForceDisplayToolTip()
            {
                var tooltip =
                    $"<MouseLeft> {ForceDisplayTooltip}\n" +
                    $"<MouseLeft><MouseLeft> {ForceDisplayAllChannelsTooltip}";

                if (ChannelIndex >= 0 && ChannelIndex < Settings.DisplayChannelShortcuts.Length)
                {
                    tooltip += $" {Settings.DisplayChannelShortcuts[ChannelIndex].TooltipString}";
                }

                forceDisplayButton.ToolTip = tooltip;
            }

            private void ShowContextMenu()
            {
                var channelIdx = ChannelIndex;

                App.ShowContextMenuAsync(new[]
                {
                    new ContextMenuOption("MenuMute", ToggleMuteLabel, () => App.ToggleChannelActive(channelIdx)),
                    new ContextMenuOption("MenuSolo", ToggleSoloLabel, () => App.ToggleChannelSolo(channelIdx)),
                    new ContextMenuOption("MenuForceDisplay", ForceDisplayLabel, () => App.ToggleChannelForceDisplay(channelIdx))
                });
            }

            private string ChannelButton_ImageEvent(Control sender, ref Color tint)
            {
                var song    = App.SelectedSong;
                var channel = song.Channels[ChannelIndex];
                var showExp = channelArea.ShowExpansionIcons;
                var opacity = (App.ChannelMask & (1L << ChannelIndex)) != 0 ? 255 : 50;

                tint = Theme.LightGreyColor1.Transparent(opacity);

                return showExp ? ExpansionType.Icons[channel.Expansion] : ChannelType.Icons[channel.Type];
            }

            private bool ForceDisplayButton_DimmedEvent(Control sender, ref int dimming)
            {
                dimming = 50;
                return (App.ForceDisplayChannelMask & (1L << ChannelIndex)) == 0;
            }

            protected override void OnPointerDown(PointerEventArgs e)
            {
                base.OnPointerDown(e);
                
                if (e.Left)
                    Clicked?.Invoke(ChannelIndex);

                Hovered = true;
            }

            protected override void OnPointerUp(PointerEventArgs e)
            {
                base.OnPointerUp(e);

                if (e.Right)
                {
                    Hovered = false;
                    ShowContextMenu();
                }
            }

            protected override void OnPointerEnter(EventArgs e)
            {
                base.OnPointerEnter(e);
                App.SetToolTip(ToolTip);
            }

            public override void OnContainerPointerUpNotify(Control control, PointerEventArgs e)
            {
                base.OnContainerPointerUpNotify(control, e);

                if (e.Right)
                {
                    Hovered = false;
                    ShowContextMenu();
                }
            }

            protected override void OnResize(EventArgs e)
            {
                var iconSize  = DpiScaling.ScaleForWindow(DefaultChannelIconSize);
                var ghostSize = DpiScaling.ScaleForWindow(DefaultGhostIconSize);

                channelButton.Move(DpiScaling.ScaleForWindow(DefaultChannelIconPosX), DpiScaling.ScaleForWindow(DefaultChannelIconPosY));
                channelButton.Resize(iconSize, iconSize);
                forceDisplayButton.Move(Width - DpiScaling.ScaleForWindow(DefaultGhostNoteOffsetX), Height - DpiScaling.ScaleForWindow(DefaultGhostNoteOffsetY) - 1);
                forceDisplayButton.Resize(ghostSize, ghostSize);
            }

            protected override void OnRender(Graphics g)
            {
                if (ChannelIndex >= App.SelectedSong.Channels.Length)
                    return;

                var c = g.DefaultCommandList;
                var channel = App.SelectedSong.Channels[ChannelIndex];
                var dim = Settings.DimUnsupportedChannels && !channel.SupportsInstrument(App.SelectedInstrument, false);
                var font = ChannelIndex == App.SelectedChannelIndex ? Fonts.FontMediumBold : Fonts.FontMedium;
                var iconHeight = channelButton.Height;

                c.FillRectangle(0, 0, Width, Height, Hovered ? Theme.MediumGreyColor1.Transparent(dim ? 192 : 255) : Theme.DarkGreyColor2);

                if (dim)
                    c.FillRectangle(0, 0, Width, Height, Theme.BlackColor.Transparent(80));

                c.DrawLine(0, 0, Width, 0, Theme.BlackColor);
                c.DrawLine(Width - 1, 0, Width - 1, Height, Theme.BlackColor);
                c.DrawText(channel.LocalizedName, font, DpiScaling.ScaleForWindow(DefaultChannelNamePosX), DpiScaling.ScaleForWindow(DefaultChannelIconPosY), Theme.LightGreyColor2.Transparent(dim ? 80 : 255), TextFlags.MiddleLeft, 0, iconHeight);

                base.OnRender(g);
            }

            private class ChannelIconButton : Button
            {
                private readonly Action doubleClick;

                public ChannelIconButton(string image, Action doubleClick) : base(image)
                {
                    this.doubleClick = doubleClick;
                    SetSupportsDoubleClick(true);
                }

                protected override void OnMouseDoubleClick(PointerEventArgs e)
                {
                    if (Enabled && e.Left)
                    {
                        doubleClick?.Invoke();
                        e.MarkHandled();
                    }
                }

                protected override void OnTouchDoubleClick(PointerEventArgs e)
                {
                    OnMouseDoubleClick(e);
                }
            }
        }
    }
}