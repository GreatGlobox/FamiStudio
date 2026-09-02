using System;

namespace FamiStudio
{
    internal class ChannelArea : Container
    {
        private readonly Sequencer sequencer;
        private Container rowsContainer;
        private ChannelRow[] rows;
        private Button shyButton;
        private bool hideEmptyChannels;
        private bool forceShyOff;
        private float headerIconScale = 1.0f;
        private int bottomY;
        private int lastScrollY;

        private Song Song => App?.SelectedSong;

        internal bool ShowExpansionIcons => sequencer.ShowExpansionIcons;
        private int[] ChannelToRow       => sequencer.ChannelToRow;
        private int ChannelNameSizeX     => sequencer.ChannelNameSizeX;
        private int ChannelSizeY         => sequencer.ChannelSizeY;
        private int ContentBottomY       => sequencer.ContentBottomY;
        private int HeaderSizeY          => sequencer.HeaderSizeY;
        private int ViewScrollY          => sequencer.ViewScrollY;

        internal LocalizedString MoreOptionsText => sequencer.MoreOptionsText;

        public bool HideEmptyChannels
        {
            get => hideEmptyChannels;
            set
            {
                if (hideEmptyChannels != value)
                {
                    hideEmptyChannels = value;
                    MarkDirty();
                }
            }
        }

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

        public float HeaderIconScale
        {
            get => headerIconScale;
            set
            {
                headerIconScale = value;

                if (shyButton != null)
                    shyButton.ImageScale = value;
            }
        }

        public event Action ShyClicked;

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
                ImageScale = headerIconScale,
                ToolTip = $"<MouseLeft> {ShyModeTooltip}"
            };

            shyButton.ImageEvent += ShyButton_ImageEvent;
            shyButton.Click += (s) => ShyClicked?.Invoke();

            AddControl(rowsContainer);
            AddControl(shyButton);
        }

        private string ShyButton_ImageEvent(Control sender, ref Color tint)
        {
            tint = Theme.LightGreyColor2;
            return hideEmptyChannels && !forceShyOff ? "ShyOn" : "ShyOff";
        }

        public void RecreateRows(float iconScale)
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

                row.IconScale = iconScale;

                rows[i] = row;
                rowsContainer.AddControl(row);
            }
        }

        public void UpdateLayout()
        {
            Resize(ChannelNameSizeX, ContentBottomY);

            rowsContainer.Move(0, HeaderSizeY);

            shyButton.Move(Width - HeaderSizeY, 0);
            shyButton.Resize(HeaderSizeY, HeaderSizeY);

            if (rows == null || ChannelToRow == null)
                return;

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
                row.Resize(Width, ChannelSizeY);
            }

            var rowCount = maxRow + 1;

            rowsContainer.Visible = rowCount > 0;
            rowsContainer.Resize(Width, rowCount * ChannelSizeY);

            bottomY = HeaderSizeY + rowCount * ChannelSizeY;

            UpdateRowPositions();
        }

        private void UpdateRowPositions()
        {
            if (rows == null || ChannelToRow == null)
                return;

            lastScrollY = ViewScrollY;

            for (var i = 0; i < rows.Length; i++)
            {
                var rowIdx = ChannelToRow[i];

                if (rowIdx >= 0)
                    rows[i].Move(0, rowIdx * ChannelSizeY - ViewScrollY);
            }
        }

        private void ConditionalUpdateRowScroll()
        {
            if (lastScrollY != ViewScrollY)
                UpdateRowPositions();
        }

        public void SetHover(int hoverRow)
        {
            for (var i = 0; i < rows.Length; i++)
            {
                var rowIdx = ChannelToRow[i];
                rows[i].Hovered = rowIdx >= 0 && rowIdx == hoverRow;
            }
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

        protected override void OnPointerEnter(EventArgs e)
        {
            base.OnPointerEnter(e);

            // This is outside the channel rows, and not the shy button.
            App.SetToolTip("");
            sequencer.ClearHover();
        }

        protected override void OnRender(Graphics g)
        {
            ConditionalUpdateRowScroll();

            var c = g.DefaultCommandList;

            c.FillRectangle(0, 0, Width, Height, Theme.DarkGreyColor2);

            base.OnRender(g);

            c.DrawLine(Width - 1, 0, Width - 1, Height, Theme.BlackColor);
            c.DrawLine(0, 0, Width, 0, Theme.BlackColor);
            c.DrawLine(0, HeaderSizeY, Width, HeaderSizeY, Theme.BlackColor);

            if (Platform.IsMobile && IsLandscape)
                c.DrawLine(0, 0, 0, Height, Theme.BlackColor);

            c.DrawLine(0, bottomY, Width, bottomY, Theme.BlackColor);
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

                if (ChannelIndex >= 0 &&
                    ChannelIndex < Settings.DisplayChannelShortcuts.Length)
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

            protected override void OnPointerUp(PointerEventArgs e)
            {
                base.OnPointerUp(e);

                if (e.Right)
                {
                    hovered = false;
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
                    hovered = false;
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

                c.FillRectangle(0, 0, Width, Height, hovered ? Theme.MediumGreyColor1.Transparent(dim ? 192 : 255) : Theme.DarkGreyColor2);

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