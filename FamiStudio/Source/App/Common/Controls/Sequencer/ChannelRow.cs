using System;

namespace FamiStudio
{
    public class ChannelRow : Container
    {
        private Button channelButton;
        private Button forceDisplayButton;
        private bool showExpansionIcons;
        private float iconScale = 1.0f;

        public int ChannelIndex { get; private set; }

        public bool ShowExpansionIcons
        {
            get => showExpansionIcons;
            set
            {
                if (showExpansionIcons != value)
                {
                    showExpansionIcons = value;
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

        public event Action<int> MuteClicked;
        public event Action<int> SoloToggled;
        public event Action<int> ForceDisplayClicked;
        public event Action<int> ForceDisplaySoloToggled;

        public ChannelRow(int channelIdx)
        {
            ChannelIndex = channelIdx;
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

            channelButton.Click += (s) => MuteClicked?.Invoke(ChannelIndex);
            channelButton.ImageEvent += ChannelButton_ImageEvent;
            forceDisplayButton.Click += (s) => ForceDisplayClicked?.Invoke(ChannelIndex);
            forceDisplayButton.DimmedEvent += ForceDisplayButton_DimmedEvent;

            AddControl(channelButton);
            AddControl(forceDisplayButton);
        }

        private string ChannelButton_ImageEvent(Control sender, ref Color tint)
        {
            var song    = App.SelectedSong;
            var channel = song.Channels[ChannelIndex];
            var showExp = showExpansionIcons && song.Project.UsesAnyExpansionAudio;
            var opacity = (App.ChannelMask & (1L << ChannelIndex)) != 0 ? 255 : 50;

            tint = Theme.LightGreyColor1.Transparent(opacity);

            return showExp ? ExpansionType.Icons[channel.Expansion] : ChannelType.Icons[channel.Type];
        }

        private bool ForceDisplayButton_DimmedEvent(Control sender, ref int dimming)
        {
            dimming = 50;
            return (App.ForceDisplayChannelMask & (1L << ChannelIndex)) == 0;
        }

        protected override void OnResize(EventArgs e)
        {
            var iconSize  = DpiScaling.ScaleForWindow(16);
            var ghostSize = DpiScaling.ScaleForWindow(12);

            channelButton.Move(DpiScaling.ScaleForWindow(2), DpiScaling.ScaleForWindow(3));
            channelButton.Resize(iconSize, iconSize);
            forceDisplayButton.Move(Width - DpiScaling.ScaleForWindow(16), Height - DpiScaling.ScaleForWindow(Platform.IsMobile ? 16 : 15) - 1);
            forceDisplayButton.Resize(ghostSize, ghostSize);
        }

        protected override void OnRender(Graphics g)
        {
            if (App == null ||
                App.SelectedSong == null ||
                ChannelIndex >= App.SelectedSong.Channels.Length)
                return;

            var c = g.DefaultCommandList;
            var channel = App.SelectedSong.Channels[ChannelIndex];
            var dim = Settings.DimUnsupportedChannels && !channel.SupportsInstrument(App.SelectedInstrument, false);
            var font = ChannelIndex == App.SelectedChannelIndex ? Fonts.FontMediumBold : Fonts.FontMedium;
            var iconHeight = channelButton.Height;

            c.FillRectangle(0, 0, Width, Height, dim ? Theme.DarkGreyColor1 : Theme.DarkGreyColor2);
            c.DrawLine(0, 0, Width, 0, Theme.BlackColor);

            c.DrawText(channel.LocalizedName, font, DpiScaling.ScaleForWindow(21), DpiScaling.ScaleForWindow(3), Theme.LightGreyColor2.Transparent(dim ? 80 : 255), TextFlags.MiddleLeft, 0, iconHeight);
                
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