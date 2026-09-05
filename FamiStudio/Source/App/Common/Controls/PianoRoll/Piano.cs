using System;

namespace FamiStudio
{
    internal class Piano : Control
    {
        const int DefaultRecordingKeyOffsetY = 12;

        const int NumOctaves = 8;
        const int NumNotes   = NumOctaves * 12;

        private readonly PianoRoll pianoRoll;

        Color whiteKeyPressedColor    = Color.FromArgb( 70, Theme.BlackColor);
        Color blackKeyPressedColor    = Color.FromArgb( 90, Theme.WhiteColor);
        Color whiteKeyHoverColor      = Color.FromArgb( 40, Theme.BlackColor);
        Color blackKeyHoverColor      = Color.FromArgb( 60, Theme.WhiteColor);
        Color invalidDpcmMappingColor = Color.FromArgb( 64, Theme.BlackColor);

        private ValueTuple<int, Color>[] videoHighlightKeys;

        private bool playing = false;

        private int highlightNote = Note.NoteInvalid;
        private int hoverNote = -1;
        private int lastNote  = -1;

        private int recordingKeyOffsetY;

        internal int HighlightNote
        {
            get => highlightNote;
            set => SetAndMarkDirty(ref highlightNote, value);
        }

        internal int HoverNote
        {
            get => hoverNote;
            set => SetAndMarkDirty(ref hoverNote, value);
        }

        internal ValueTuple<int, Color>[] VideoHighlightKeys
        {
            get => videoHighlightKeys;
            set => videoHighlightKeys = value;
        }

        private int   ViewScrollY          => pianoRoll.ViewScrollY;
        private int   VirtualSizeY         => pianoRoll.VirtualSizeY;
        private int   OctaveSizeY          => pianoRoll.OctaveSizeY;
        private int   NoteSizeY            => pianoRoll.NoteSizeY;
        private int   WhiteKeySizeY        => pianoRoll.WhiteKeySizeY;
        private int   BlackKeySizeX        => pianoRoll.BlackKeySizeX;
        private int   BlackKeySizeY        => pianoRoll.BlackKeySizeY;
        private int   PianoSizeX           => pianoRoll.PianoSizeX;
        private int   HeaderAndEffectSizeY => pianoRoll.HeaderAndEffectSizeY;
        private bool  IsVideoRecording     => pianoRoll.IsEditingVideo;
        private bool  IsMaximized          => pianoRoll.IsMaximized;
        private bool  DrawDpcmColorKeys    => pianoRoll.DrawDpcmColorKeysOnPiano;
        private bool  ShowQwertyLabels     => Platform.IsDesktop && App != null && (App.IsRecording || App.IsQwertyPianoEnabled);

        LocalizedString PlayPianoTooltip;
        LocalizedString PanTooltip;


        internal Piano(PianoRoll pianoRoll)
        {
            this.pianoRoll = pianoRoll;
            Localization.Localize(this);
            ToolTip = $"<MouseLeft> {PlayPianoTooltip} - <MouseWheel><Drag> {PanTooltip}";
        }

        protected override void OnAddedToContainer()
        {
            recordingKeyOffsetY = DpiScaling.ScaleForWindow(DefaultRecordingKeyOffsetY);
        }

        internal void UpdateLayout()
        {
            Visible = pianoRoll.PianoVisible;

            if (!Visible)
                return;

            var scrollThickness = IsVideoRecording ? 0 : pianoRoll.ScrollBarThickness;

            Move(0, HeaderAndEffectSizeY);
            Resize(PianoSizeX, pianoRoll.Height - HeaderAndEffectSizeY - scrollThickness + 1);
        }

        private void GetVisibleOctaveRange(out int minOctave, out int maxOctave)
        {
            var minNote = IsVideoRecording ? -10000 : 0;
            var maxNote = IsVideoRecording ?  10000 : NumNotes;

            var maxVisibleNote = NumNotes - Utils.Clamp((int)Math.Floor(ViewScrollY / (float)NoteSizeY), minNote, maxNote);
            var minVisibleNote = NumNotes - Utils.Clamp((int)Math.Ceiling((ViewScrollY + Height) / (float)NoteSizeY), minNote, maxNote);

            maxOctave = (int)Math.Ceiling(maxVisibleNote / 12.0f);
            minOctave = (int)Math.Floor(minVisibleNote / 12.0f);
        }

        private void StartPlayPiano(int note)
        {
            UpdatePlayPiano(note);
        }

        private void UpdatePlayPiano(int note)
        {
            if (note >= 0 && note != lastNote)
            {
                lastNote = note;
                HighlightNote = note;
                App.PlayInstrumentNote(note, true, true);
            }
        }

        private void EndPlayPiano()
        {
            App.StopOrReleaseIntrumentNote(false);
            lastNote = -1;
            HighlightNote = Note.NoteInvalid;
        }

        private static bool IsBlackKey(int key)
        {
            return key == 1 || key == 3 || key == 6 || key == 8 || key == 10;
        }

        private Rectangle GetKeyRectangle(int octave, int key)
        {
            if (IsBlackKey(key))
            {
                return new Rectangle(
                    IsVideoRecording ? PianoSizeX - BlackKeySizeX : 0,
                    VirtualSizeY - OctaveSizeY * octave - (key + 1) * NoteSizeY - ViewScrollY,
                    BlackKeySizeX,
                    BlackKeySizeY);
            }
            else
            {
                int keySizeY = key > 4 ? (NoteSizeY * 12 - WhiteKeySizeY * 3) / 4 : WhiteKeySizeY;

                return new Rectangle(
                    0,
                    VirtualSizeY - OctaveSizeY * octave - (key <= 4 ? ((key / 2 + 1) * WhiteKeySizeY) : ((WhiteKeySizeY * 3) + ((key - 4) / 2 + 1) * keySizeY)) - ViewScrollY,
                    PianoSizeX,
                    keySizeY);
            }
        }

        internal int GetPianoNote(int x, int y)
        {
            for (int i = 0; i < NumOctaves; i++)
            {
                for (int j = 0; j < 12 && i * 12 + j < NumNotes; j++)
                {
                    if (IsBlackKey(j) && GetKeyRectangle(i, j).Contains(x, y))
                        return i * 12 + j + 1;
                }
                for (int j = 0; j < 12 && i * 12 + j < NumNotes; j++)
                {
                    if (!IsBlackKey(j) && GetKeyRectangle(i, j).Contains(x, y))
                        return i * 12 + j + 1;
                }
            }

            return -1;
        }

        private bool HighlightPianoNoteInternal(CommandList c, int note, Color color, bool whiteKey)
        {
            if (Note.IsMusicalNote(note) || IsVideoRecording)
            {
                Note.GetOctaveAndNote(note, out var octave, out var octaveNote);

                if (whiteKey == !IsBlackKey(octaveNote))
                    c.FillRectangle(GetKeyRectangle(octave, octaveNote), color);

                return true;
            }

            return false;
        }

        protected override void OnTouchScaleBegin(PointerEventArgs e)
        {
            base.OnTouchScaleBegin(e);

            var p = pianoRoll.WindowToControl(ControlToWindow(e.Position));
            pianoRoll.StartMobileZoom(p.X, p.Y, true);
        }

        protected override void OnPointerMove(PointerEventArgs e)
        {
            base.OnPointerMove(e);

            var note = GetPianoNote(
                Utils.Clamp(e.X, 0, Width - 1),
                e.Y);

            HoverNote = note;

            if (playing)
                UpdatePlayPiano(note);
        }

        protected override void OnPointerEnter(EventArgs e)
        {
            base.OnPointerEnter(e);
            App.SetToolTip($"<MouseLeft> {PlayPianoTooltip} - <MouseWheel><Drag> {PanTooltip}");
        }

        protected override void OnPointerDown(PointerEventArgs e)
        {
            base.OnPointerDown(e);

            if (e.IsTouchEvent)
                return;

            var middle =
                e.Middle ||
                (e.Left && ModifierKeys.IsAltDown && Settings.AltLeftForMiddle);

            if (middle)
            {
                var p = pianoRoll.WindowToControl(ControlToWindow(e.Position));
                pianoRoll.StartPan(p.X, p.Y);
                return;
            }

            if (e.Left)
            {
                var note = GetPianoNote(e.X, e.Y);

                if (note >= 0)
                {
                    CapturePointer();
                    playing = true;
                    StartPlayPiano(note);
                }
            }
        }

        protected override void OnPointerUp(PointerEventArgs e)
        {
            base.OnPointerUp(e);

            if (playing)
            {
                playing = false;
                EndPlayPiano();
            }
        }

        internal void OnTouchDownInternal(int localX, int localY)
        {
            var note = GetPianoNote(localX, localY);
            if (note < 0)
                return;

            playing = true;
            StartPlayPiano(note);
        }

        protected override void OnRender(Graphics g)
        {
            var c = g.DefaultCommandList;

            c.PushClipRegion(0, 0, Width, Height);
            c.FillRectangleGradient(0, 0, PianoSizeX, Height, Theme.LightGreyColor1, Theme.LightGreyColor2, false, PianoSizeX);

            if (IsMaximized && Platform.IsDesktop)
                c.DrawLine(0, 0, PianoSizeX, 0, Color.Black);

            GetVisibleOctaveRange(out var minVisibleOctave, out var maxVisibleOctave);

            if (DrawDpcmColorKeys)
            {
                for (int i = minVisibleOctave; i < maxVisibleOctave; i++)
                {
                    for (int j = 0; j < 12; j++)
                    {
                        if (!IsBlackKey(j) && pianoRoll.GetDPCMKeyColor(i * 12 + j + 1, out var color))
                            c.FillRectangleGradient(GetKeyRectangle(i, j), Theme.Darken(color, 20), color, false, PianoSizeX);
                    }
                }
            }

            // Highlight play/hover note (white keys)
            if (videoHighlightKeys != null)
            {
                foreach (var pair in videoHighlightKeys)
                    HighlightPianoNoteInternal(c, pair.Item1, pair.Item2, true);
            }
            else
            {
                if (!HighlightPianoNoteInternal(c, highlightNote, whiteKeyPressedColor, true))
                    HighlightPianoNoteInternal(c, hoverNote, whiteKeyHoverColor, true);
            }

            // Draw the piano
            for (int i = minVisibleOctave; i < maxVisibleOctave; i++)
            {
                var octaveBaseY = (VirtualSizeY - OctaveSizeY * i) - ViewScrollY;

                for (int j = 0; j < 12; j++)
                {
                    var noteIdx = i * 12 + j;
                    if (noteIdx >= NumNotes && !IsVideoRecording)
                        break;

                    if (IsBlackKey(j))
                    {
                        if (DrawDpcmColorKeys && pianoRoll.GetDPCMKeyColor(noteIdx + 1, out var color))
                            c.FillAndDrawRectangleGradient(GetKeyRectangle(i, j), Theme.Darken(color, 40), Theme.Darken(color, 20), Theme.BlackColor, false, BlackKeySizeX);
                        else
                            c.FillRectangleGradient(GetKeyRectangle(i, j), Theme.DarkGreyColor4, Theme.DarkGreyColor5, false, BlackKeySizeX);
                    }

                    int y = octaveBaseY - j * NoteSizeY;
                    if (j == 0 || j == 5)
                        c.DrawLine(0, y, PianoSizeX, y, Theme.BlackColor);
                }

                if (pianoRoll.ShowOctaveLabels && Fonts.FontSmall.Size < NoteSizeY)
                    c.DrawText("C" + i, Fonts.FontSmall, DpiScaling.Window, octaveBaseY - NoteSizeY + 1, Theme.BlackColor, TextFlags.Middle, PianoSizeX - DpiScaling.Window * 2, NoteSizeY);
            }

            // Highlight play/hover note (black keys)
            if (videoHighlightKeys != null)
            {
                foreach (var pair in videoHighlightKeys)
                    HighlightPianoNoteInternal(c, pair.Item1, pair.Item2, false);
            }
            else
            {
                if (!HighlightPianoNoteInternal(c, highlightNote, blackKeyPressedColor, false))
                    HighlightPianoNoteInternal(c, hoverNote, blackKeyHoverColor, false);
            }

            // QWERTY key labels.
            if (ShowQwertyLabels)
            {
                for (int i = minVisibleOctave; i < maxVisibleOctave; i++)
                {
                    var octaveBaseY = (VirtualSizeY - OctaveSizeY * i) - ViewScrollY;

                    for (int j = 0; j < 12; j++)
                    {
                        var noteIdx = i * 12 + j + 1;
                        var keyString = App.GetRecordingKeyString(noteIdx);

                        if (keyString == null)
                            continue;

                        int y = octaveBaseY - j * NoteSizeY;

                        Color color;
                        if (App.IsRecording)
                            color = IsBlackKey(j) ? Theme.LightRedColor : Theme.DarkRedColor;
                        else
                            color = IsBlackKey(j) ? Theme.LightGreyColor2 : Theme.BlackColor;

                        c.DrawText(keyString, Fonts.FontVerySmall, 0, y - recordingKeyOffsetY + 1, color, TextFlags.MiddleCenter, BlackKeySizeX, NoteSizeY - 1);
                    }
                }
            }

            c.DrawLine(PianoSizeX - 1, 0, PianoSizeX - 1, Height, Theme.BlackColor);
            c.PopClipRegion();
        }
    }
}