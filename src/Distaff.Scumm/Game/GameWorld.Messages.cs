using System.Globalization;
using System.Text;
using Distaff.Scumm.Graphics;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.View;

namespace Distaff.Scumm.Game;

/// <summary>
/// Where and how a text slot prints: one of ScummVM's <c>_string[]</c>, with the defaults
/// <c>print</c> starts from and saves back.
/// </summary>
internal sealed class TextSlot
{
    internal int X { get; set; }

    internal int Y { get; set; }

    internal int Right { get; set; } = GameWorld.ScreenWidth - 1;

    internal int Height { get; set; }

    internal int Color { get; set; } = 0x0F;

    internal bool Center { get; set; }

    internal bool Overhead { get; set; }

    internal int Charset { get; set; }

    private (int X, int Y, int Right, int Height, int Color, bool Center, bool Overhead, int Charset) _default = (0, 0, GameWorld.ScreenWidth - 1, 0, 0x0F, false, false, 0);

    /// <summary>The charset the slot starts from (<c>_default.charset</c>), which <c>initCharset</c> sets for slots 0 and 1.</summary>
    internal int DefaultCharset
    {
        get => _default.Charset;
        set => _default = _default with { Charset = value };
    }

    /// <summary>Mirrors <c>StringTab::loadDefault</c>.</summary>
    internal void LoadDefault() => (X, Y, Right, Height, Color, Center, Overhead, Charset) = _default;

    /// <summary>Mirrors <c>StringTab::saveDefault</c>.</summary>
    internal void SaveDefault() => _default = (X, Y, Right, Height, Color, Center, Overhead, Charset);

    /// <summary>
    /// Keeps the position, line height and colour as the defaults, but not the rest: versions 1 to 3
    /// have no defaults, so the text's place carries on to the next (the end of <c>decodeParseStringTextString</c>).
    /// </summary>
    internal void KeepPlace() => _default = _default with { X = X, Y = Y, Height = Height, Color = Color };

    /// <summary>The slot as a save holds it, each field with its default; the line height is not saved (<c>syncWithSerializer(StringTab)</c>).</summary>
    internal void SaveTo(Saves.ScummState.StringState s)
    {
        (s.X, s.Y, s.Right, s.Color, s.Charset, s.Center, s.Overhead) = (X, Y, Right, (sbyte)Color, Charset, Center ? 1 : 0, Overhead ? 1 : 0);
        (s.DefaultX, s.DefaultY, s.DefaultRight, s.DefaultColor, s.DefaultCharset) = (_default.X, _default.Y, _default.Right, (sbyte)_default.Color, _default.Charset);
        (s.DefaultCenter, s.DefaultOverhead) = (_default.Center ? 1 : 0, _default.Overhead ? 1 : 0);
    }

    /// <summary>The slot from a save; the line heights stay as they are.</summary>
    internal void LoadFrom(Saves.ScummState.StringState s)
    {
        (X, Y, Right, Color, Charset, Center, Overhead) = (s.X, s.Y, s.Right, s.Color & 0xFF, s.Charset, s.Center != 0, s.Overhead != 0);
        _default = (s.DefaultX, s.DefaultY, s.DefaultRight, _default.Height, s.DefaultColor & 0xFF, s.DefaultCenter != 0, s.DefaultOverhead != 0, s.DefaultCharset);
    }
}

/// <summary>
/// Messages: what actors and the narrator say, how long it stays, and the text drawn on screen.
/// Mirrors ScummVM's <c>printString</c>, <c>actorTalk</c>, <c>stopTalk</c>, <c>displayDialog</c>,
/// <c>newLine</c>, <c>drawString</c>, <c>restoreCharsetBg</c>, <c>handleNextCharsetCode</c> and
/// <c>convertMessageToString</c> for version 3, and <c>CharsetRendererV3</c>'s <c>printChar</c>
/// and <c>setColor</c>: each character keeps the message up <c>VAR_CHARINC</c> ticks longer, on
/// top of 60. Speech over the room goes into the text surface; speech in another band, and text
/// <c>print</c> draws in slot 1, goes into the band itself.
/// </summary>
public sealed partial class GameWorld
{
    private int _haveMsg;
    private int _talkDelay;
    private bool _keepText;
    private bool _useTalkAnims;
    private bool _egoPositioned;
    private byte[] _charsetBuffer = [];
    private int _charsetBufPos;
    private readonly StringBuilder _shown = new();

    // what the message draws next, and the colour actorTalk chose (_nextLeft, _nextTop, _charsetColor)
    private int _nextLeft;
    private int _nextTop;
    private int _charsetColor;
    private bool _haveActorSpeechMsg;

    // the charset renderer's pen (_top, _left, _startLeft, _right, _center, _color, _shadowType, _firstChar, _disableOffsX)
    private int _textTop;
    private int _textLeft;
    private int _startLeft;
    private int _textRight;
    private bool _textCenter;
    private byte _textColor;
    private ShadowType _shadow;
    private bool _firstChar;
    private bool _disableOffsX;

    // the rectangle drawn since the first character (_str), and where speech was drawn (_hasMask, _textScreenID)
    private Rect _str;

    // the FM Towns' text, where the message last drew (_curStringRect), -1 on the left for none
    private Rect _curStringRect = Rect.FromBounds(-1, -1, -1, -1);
    private bool _hasMask;
    private VirtScreenNumber _textScreen;

    /// <summary>The text slots: actors' speech, the three system slots, and the one text verbs draw in (<c>_string</c>).</summary>
    internal TextSlot[] TextSlots { get; } = [new(), new(), new(), new(), new(), new()];

    /// <summary>Who <c>print</c> prints for: an actor, or 0xFC to 0xFF for the system slots (<c>_actorToPrintStrFor</c>).</summary>
    internal int ActorToPrintFor { get; private set; }

    /// <summary>Whether a message is up: 0 none, 0xFF being shown, 1 shown in full (<c>_haveMsg</c>).</summary>
    internal int HaveMessage => _haveMsg;

    /// <summary>The text of the message up now, as far as it has been shown.</summary>
    public string MessageText => _shown.ToString();

    /// <summary>Every message shown so far, in order, as it was finished: for tests and the console.</summary>
    public IReadOnlyList<string> MessageLog => _messageLog;

    private readonly List<string> _messageLog = [];

    /// <summary>The actor talking, or 0xFF for none (<c>VAR_TALK_ACTOR</c>).</summary>
    internal int TalkingActor
    {
        get => Engine.Variables[Vars.TalkActor];
        private set => Engine.Variables[Vars.TalkActor] = value;
    }

    /// <summary>
    /// Prints a message in a text slot. Mirrors <c>printString</c>: slot 0 is speech, slot 1 is
    /// drawn at once, slot 2 a debug message (<c>debugMessage</c>), slot 3 a message dialog
    /// (<c>showMessageDialog</c>).
    /// </summary>
    private void PrintString(int slot, byte[] message)
    {
        switch (slot)
        {
            case 0:
                ActorTalk(message);
                break;
            case 1:
                _messageLog.Add(Plain(ConvertMessage(message)));
                DrawString(1, message);
                break;
            case 2:
                DebugMessage(message);
                break;
            default:
                ShowMessageDialog(message);
                break;
        }
    }

    /// <summary>
    /// A debug message (<c>debugMessage</c>): ScummVM writes it to its debug log in debug mode and
    /// otherwise does nothing with it, as here, unless it is the code that plays a digital voice
    /// (0xFF 10), which no Loom release sends. The VGA CD prints a few: "assigning-drafts" when it
    /// sets the drafts up, others in scripts the game never runs.
    /// </summary>
    private void DebugMessage(byte[] message)
    {
        var converted = ConvertMessage(message);
        if (converted is [0xFF, 10, ..])
        {
            _unhandled.Add($"printString(2, talkSound)");
        }
    }

    /// <summary>
    /// A message dialog (<c>showMessageDialog</c>, the original interface's): the message in a
    /// banner, in slot 3's colour (4 when it has none), until a key or a click; the key's code
    /// then goes to the scripts' key variable, 0 for a click. The VGA CD's F9 shows whether its
    /// text appears ("Text WILL appear on the screen."). ScummVM waits inside the instruction;
    /// here the banner holds the game from the next step, so the rest of the script's turn runs
    /// first, before the key is in the variable (the VGA CD's dialogs end their turn after it).
    /// </summary>
    private void ShowMessageDialog(byte[] message)
    {
        var slot = TextSlots[3];
        if (slot.Color == 0)
        {
            slot.Color = 4;
        }

        ShowOldStyleBanner(Plain(message), slot.Color, -1, key => Engine.Variables[Vars.KeyPress] = key?.Ascii ?? 0);
    }

    /// <summary>Mirrors <c>actorTalk</c>.</summary>
    private void ActorTalk(byte[] message)
    {
        _charsetBuffer = ConvertMessage(message);
        if (ActorToPrintFor == 0xFF)
        {
            if (!_keepText)
            {
                StopTalk();
            }

            TalkingActor = 0xFF;
        }
        else
        {
            // derefActor takes actor 0, who is never in the room: a message for it is not shown
            var actor = Actors[ActorToPrintFor is >= 0 and < ActorCount ? ActorToPrintFor : CheckActor(ActorToPrintFor)];
            if (!actor.IsInRoom(CurrentRoomNumber))
            {
                return;
            }

            if (!_keepText)
            {
                StopTalk();
            }

            TalkingActor = actor.Number;

            // Loom does not animate an actor's talk for an empty message
            if (_charsetBuffer.Length > 0)
            {
                RunTalkAnimation(actor, actor.TalkStartFrame);
                _useTalkAnims = true;
            }
        }

        _charsetColor = TalkingActor > 0x7F ? TextSlots[0].Color : Actors[TalkingActor].TalkColor;
        _haveActorSpeechMsg = true;
        _charsetBufPos = 0;
        _talkDelay = 0;
        _haveMsg = 0xFF;
        Engine.Variables[Vars.HaveMessage] = 0xFF;
        DisplayDialog();
    }

    /// <summary>Mirrors <c>stopTalk</c>.</summary>
    private void StopTalk()
    {
        _haveMsg = 0;
        _talkDelay = 0;
        int talking = TalkingActor;
        if (talking is > 0 and < 0x80)
        {
            var actor = Actors[talking];
            if (actor.IsInRoom(CurrentRoomNumber) && _useTalkAnims)
            {
                RunTalkAnimation(actor, actor.TalkStopFrame);
                _useTalkAnims = false;
            }

            TalkingActor = 0xFF;
        }

        _keepText = false;
        if (_shown.Length > 0)
        {
            _messageLog.Add(_shown.ToString());
            _shown.Clear();
        }

        RestoreCharsetBg();
    }

    /// <summary>
    /// Moves the message on: when its time is up, the text before goes unless it is kept, the next
    /// piece draws, up to a wait code or its end, each line centred when the slot is, and the time
    /// it stays is 60 ticks and <c>VAR_CHARINC</c> more for each character. Mirrors
    /// <c>displayDialog</c> for version 3. Loom's scripts never print over an actor's head, so the
    /// overhead position is left out.
    /// </summary>
    private void DisplayDialog()
    {
        if (_haveMsg == 0)
        {
            return;
        }

        var slot = TextSlots[0];
        _textTop = slot.Y;
        _startLeft = _textLeft = slot.X;
        _textRight = slot.Right;
        _textCenter = slot.Center;
        SetColor(_charsetColor);
        if (FmTowns && (_keepText || _haveMsg == 0xFF))
        {
            _str = _curStringRect;
        }

        if (_talkDelay != 0)
        {
            return;
        }

        if (_haveMsg == 1)
        {
            StopTalk();
            return;
        }

        var talker = TalkingActor is > 0 and < ActorCount ? Actors[TalkingActor] : null;
        if (talker is not null && _charsetBufPos < _charsetBuffer.Length)
        {
            RunTalkAnimation(talker, talker.TalkStartFrame);
            _useTalkAnims = true;
        }

        _talkDelay = 60;
        if (!_keepText)
        {
            if (_shown.Length > 0)
            {
                _messageLog.Add(_shown.ToString());
            }

            _shown.Clear();
            RestoreCharsetBg();
        }

        if (_textCenter)
        {
            _nextLeft = Math.Max(0, _nextLeft - StringWidth(0, _charsetBuffer.AsSpan(_charsetBufPos)) / 2);
        }

        _disableOffsX = _firstChar = !_keepText;
        int c;
        while (true)
        {
            c = NextCharsetCode(talker, out bool goOn);
            if (!goOn)
            {
                break;
            }

            if (c == 0)
            {
                _haveMsg = 1;
                _keepText = false;
                break;
            }

            if (c == 13)
            {
                _shown.Append(' ');
                NewLine();
                continue;
            }

            // a line running past the screen's edge stops drawing there
            _nextLeft = Math.Min(_nextLeft, ScreenWidth);
            _textLeft = _nextLeft;
            _textTop = _nextTop;

            // the VGA CD's voice says the line instead, with the subtitles off (Loom's case in CHARSET_1)
            if (_loader.Version <= 3 || Subtitles || !Sound.Cd.IsPlaying)
            {
                PrintChar(c, ignoreCharsetMask: false);
            }

            _nextLeft = _textLeft;
            _nextTop = _textTop;

            _shown.Append((char)c);
            _talkDelay += Engine.Variables[Vars.CharInc];
        }

        // the loop ends at the message's end or a wait (codes 2 and 3)
        if (FmTowns && c is 0 or 2 or 3)
        {
            _curStringRect = _str;
        }
    }

    /// <summary>
    /// Mirrors <c>newLine</c> for version 3: back to the slot's left, centred again, down the
    /// slot's line height or the font's; the FM Towns' always the font's. Version 4's next character
    /// starts the line without its x offset.
    /// </summary>
    private void NewLine()
    {
        _nextLeft = TextSlots[0].X;
        if (_textCenter)
        {
            _nextLeft = Math.Max(0, _nextLeft - StringWidth(0, _charsetBuffer.AsSpan(_charsetBufPos)) / 2);
        }

        _nextTop += TextSlots[0].Height != 0 && !FmTowns ? TextSlots[0].Height : _font?.Height ?? 8;
        if (_loader.Version > 3)
        {
            _disableOffsX = true;
        }
    }

    /// <summary>
    /// Draws a message at once, into the band it falls in. Mirrors <c>drawString</c> for version
    /// 3: centred on the slot's position, code 1 or 8 starting a line, code 12 changing the colour;
    /// the slot's position moves to where the text ends.
    /// </summary>
    private void DrawString(int a, ReadOnlySpan<byte> message)
    {
        byte[] buf = ConvertMessage(message);
        var slot = TextSlots[a];
        _textTop = slot.Y;
        _startLeft = _textLeft = slot.X;
        _textRight = slot.Right;
        _textCenter = slot.Center;
        SetColor(slot.Color);
        _disableOffsX = _firstChar = true;

        bool shadowModeFlag = Screens.Find(_textTop)?.Number == VirtScreenNumber.Main;
        int fontHeight = _font?.Height ?? 8;
        if (_textCenter)
        {
            _textLeft -= StringWidth(a, buf) / 2;
        }

        if (buf.Length == 0)
        {
            _str = new Rect(_textLeft, _textTop, 0, 0);
        }

        int i = 0;
        int Next() => i < buf.Length ? buf[i++] : 0;
        while (true)
        {
            int c = Next();
            if (c == 0)
            {
                break;
            }

            if (c is not (0xFF or 0xFE))
            {
                PrintChar(c, ignoreCharsetMask: true);
                continue;
            }

            switch (Next())
            {
                case 9 or 10 or 13 or 14:
                    i += 2;
                    break;
                case 1 or 8:
                    // as ScummVM has it: a centred line starts its whole width left, and a slot 0
                    // line height moves the message's next line, not this text
                    _textLeft = _textCenter ? _startLeft - StringWidth(a, buf.AsSpan(Math.Min(i, buf.Length))) : _startLeft;
                    if (TextSlots[0].Height != 0 && !FmTowns)
                    {
                        _nextTop += TextSlots[0].Height;
                    }
                    else
                    {
                        _textTop += fontHeight;
                    }

                    break;
                case 12:
                    {
                        int color = Next() | (Next() << 8);
                        SetColor(color == 0xFF ? slot.Color : color, shadowModeFlag);
                    }

                    break;
            }
        }

        if (a == 0)
        {
            _nextLeft = _textLeft;
            _nextTop = _textTop;
        }

        slot.X = _str.Right;
    }

    /// <summary>
    /// Mirrors <c>CharsetRendererV3::setColor</c> for Loom version 3: bit 6 asks for an outline
    /// (none when <paramref name="shadowModeSpecialFlag"/>), bit 7 for a shadow, and the low four
    /// bits are the colour. The FM Towns' (<c>GF_OLD256</c>) has no outline, and its colour is the
    /// low four bits doubled, colour 0 its shadow's, 0x88 (<c>CharsetRendererTownsV3::setShadowMode</c>).
    /// </summary>
    private void SetColor(int color, bool shadowModeSpecialFlag = false)
    {
        if (FmTowns)
        {
            _shadow = (color & 0x80) != 0 ? ShadowType.RightAndBelow : ShadowType.None;
            int low = color & 0x0F;
            _textColor = low == 0 ? TownsShadowColor : (byte)(low | (low << 4));
            return;
        }

        if (_font is { IsClassic: true })
        {
            // version 4: the colour as it is, no shadow (CharsetRendererCommon::setColor)
            _shadow = ShadowType.None;
            _textColor = (byte)color;
            return;
        }

        _shadow = (color & 0x40) != 0 ? (shadowModeSpecialFlag ? ShadowType.None : ShadowType.Outline)
            : (color & 0x80) != 0 ? ShadowType.Normal : ShadowType.None;
        _textColor = (byte)(color & 0x0F);
    }

    /// <summary>
    /// Draws a character at the pen and moves it on. Mirrors <c>CharsetRendererV3::printChar</c>:
    /// nothing past the slot's right edge; speech over the room goes into the text surface, other
    /// text into the band, but all the FM Towns' text into the text surface; the drawn rectangle
    /// grows by the character and its shadow.
    /// </summary>
    private void PrintChar(int chr, bool ignoreCharsetMask)
    {
        if (_font is null || Screens.Find(_textTop) is not { Front: { } front } vs || chr == '@')
        {
            return;
        }

        if (_font.IsClassic)
        {
            PrintCharClassic(chr, ignoreCharsetMask, vs, front);
            return;
        }

        int origWidth = _font.CharWidth(chr);
        int height = 8;
        if (_textLeft + origWidth > _textRight + 1)
        {
            return;
        }

        if (_shadow is ShadowType.Normal or ShadowType.RightAndBelow)
        {
            height++;
        }
        else if (_shadow == ShadowType.Outline)
        {
            height += 2;
        }

        if (_firstChar)
        {
            _str = new Rect(_textLeft, _textTop, 0, 0);
            _firstChar = false;
        }

        if (!ignoreCharsetMask)
        {
            _hasMask = true;
            _textScreen = vs.Number;
        }

        Screens.MarkDirty(vs, _textLeft, _textLeft + origWidth, _textTop - vs.Top, _textTop - vs.Top + height);
        if (FmTowns)
        {
            Charset.DrawBits1(Screens.TextSurface, _textLeft, _textTop, _font.Glyph(chr), origWidth, 8, _textColor, _shadow, TownsShadowColor);
        }
        else if (ignoreCharsetMask || !vs.HasTwoBuffers)
        {
            int xstart = vs.Number == VirtScreenNumber.Main ? Camera.Left : 0;
            Charset.DrawBits1(front, _textLeft + xstart, _textTop - vs.Top, _font.Glyph(chr), origWidth, 8, _textColor, _shadow);
        }
        else
        {
            Charset.DrawBits1(Screens.TextSurface, _textLeft, _textTop, _font.Glyph(chr), origWidth, 8, _textColor, _shadow);
        }

        int left = Math.Min(_str.Left, _textLeft);
        _textLeft += origWidth;
        int right = _str.Right;
        if (right < _textLeft)
        {
            right = _textLeft + (_shadow != ShadowType.None ? 1 : 0);
        }

        _str = Rect.FromBounds(left, _str.Top, right, Math.Max(_str.Bottom, _textTop + height));
    }

    /// <summary>
    /// Draws a version 4 character at the pen and moves it on. Mirrors
    /// <c>CharsetRendererClassic::printChar</c>: the text colour is the colour map's colour 1; the
    /// character's offsets move the pen while it is drawn, but for the x offset of a string's or a
    /// line's first character drawn (<c>_disableOffsX</c>); nothing past the slot's right edge or
    /// left of the screen; there are no shadows.
    /// </summary>
    private void PrintCharClassic(int chr, bool ignoreCharsetMask, VirtScreen vs, Surface8 front)
    {
        CharsetColorMap[1] = _textColor;
        if (_font!.ClassicGlyph(chr) is not { } glyph)
        {
            return;
        }

        if (_firstChar)
        {
            _str = default;
        }

        _textTop += glyph.OffsetY;
        _textLeft += _disableOffsX ? 0 : glyph.OffsetX;
        if (_textLeft + glyph.Width > _textRight + 1 || _textLeft < 0)
        {
            _textLeft += glyph.Width;
            _textTop -= glyph.OffsetY;
            return;
        }

        _disableOffsX = false;

        if (_firstChar)
        {
            _str = new Rect(_textLeft, _textTop, 0, 0);
            _firstChar = false;
        }

        _str = Rect.FromBounds(Math.Min(_str.Left, _textLeft), Math.Min(_str.Top, _textTop), _str.Right, _str.Bottom);
        if (!ignoreCharsetMask)
        {
            _hasMask = true;
            _textScreen = vs.Number;
        }

        if (ignoreCharsetMask || !vs.HasTwoBuffers)
        {
            int xstart = vs.Number == VirtScreenNumber.Main ? Camera.Left : 0;
            _font.DrawBitsN(front, _textLeft + xstart, _textTop - vs.Top, glyph, CharsetColorMap);
        }
        else
        {
            _font.DrawBitsN(Screens.TextSurface, _textLeft, _textTop, glyph, CharsetColorMap);
        }

        _textLeft += glyph.Width;
        _str = Rect.FromBounds(_str.Left, _str.Top, Math.Max(_str.Right, _textLeft), Math.Max(_str.Bottom, _textTop + glyph.Height));
        _textTop -= glyph.OffsetY;
    }

    /// <summary>
    /// Picks a version 4 charset for all text (<c>initCharset</c>): its font, from <c>90x.LFL</c>, and
    /// the colours set for it. A charset that is missing leaves no font.
    /// </summary>
    private void InitCharset(int id)
    {
        UseCharset(id);
        TextSlots[0].DefaultCharset = TextSlots[1].DefaultCharset = id;
        _charsetData[id].CopyTo(CharsetColorMap, 0);
    }

    /// <summary>Draws version 4 text in a charset from now on.</summary>
    private void UseCharset(int id)
    {
        _charsetId = id;
        _font = _loader.LoadCharset(id) is { } data ? Charset.ParseClassic(data) : null;
    }

    /// <summary>The charset version 4 text is drawn in (<c>_string[].charset</c>, which <c>initCharset</c> sets for slots 0 and 1, the ones Loom prints with).</summary>
    private int _charsetId;

    /// <summary>The colours each version 4 charset was given (<c>_charsetData</c>), 16 for each of the 9.</summary>
    private readonly byte[][] _charsetData = [.. Enumerable.Range(0, 9).Select(_ => new byte[16])];

    /// <summary>The colour each pixel value of a version 4 character is drawn in (<c>_charsetColorMap</c>); 1 is the text's colour.</summary>
    internal byte[] CharsetColorMap { get; } = new byte[16];

    /// <summary>
    /// Takes the speech off the screen. Mirrors <c>restoreCharsetBg</c>: the next message starts at
    /// slot 0's position; speech over a lit room only leaves the text surface, and a band without
    /// a room under it is cleared to black.
    /// </summary>
    private void RestoreCharsetBg()
    {
        if (FmTowns)
        {
            // towns_restoreCharsetBg: only where the message last drew
            if (_curStringRect.Left != -1)
            {
                RestoreBackground(_curStringRect, 0);
                _curStringRect = Rect.FromBounds(-1, _curStringRect.Top, _curStringRect.Right, _curStringRect.Bottom);
                _hasMask = false;
            }

            _nextLeft = TextSlots[0].X;
            _nextTop = TextSlots[0].Y;
            return;
        }

        _nextLeft = TextSlots[0].X;
        _nextTop = TextSlots[0].Y;
        if (!_hasMask)
        {
            return;
        }

        _hasMask = false;
        _textLeft = -1;
        _str = Rect.FromBounds(-1, _str.Top, _str.Right, _str.Bottom);

        var vs = _textScreen switch
        {
            VirtScreenNumber.Main => Screens.Main,
            VirtScreenNumber.Text => Screens.Text,
            _ => Screens.Verb,
        };

        if (vs.Front is not { } front)
        {
            return;
        }

        bool lightsOn = (Engine.Variables[Vars.CurrentLights] & 2) != 0;
        if (!(vs.HasTwoBuffers && CurrentRoomNumber != 0 && lightsOn))
        {
            front.Fill(0);
        }

        if (vs.HasTwoBuffers)
        {
            Screens.ClearTextSurface();
        }
    }

    private int StringWidth(int arg, ReadOnlySpan<byte> text) => _font?.StringWidth(arg, text) ?? 0;

    /// <summary>
    /// The next character of the message, carrying out the codes before it. Mirrors
    /// <c>handleNextCharsetCode</c>: <paramref name="goOn"/> is false after code 2 (keep the text
    /// and wait) or 3 (wait).
    /// </summary>
    private int NextCharsetCode(Actor? talker, out bool goOn)
    {
        goOn = true;
        int Next() => _charsetBufPos < _charsetBuffer.Length ? _charsetBuffer[_charsetBufPos++] : 0;
        int Word() => Next() | (Next() << 8);

        while (true)
        {
            int c = Next();
            if (c is not (0xFF or 0xFE))
            {
                return c;
            }

            c = Next();
            switch (c)
            {
                case 1:
                    return 13;
                case 2:
                    _haveMsg = 0;
                    _keepText = true;
                    goOn = false;
                    return c;
                case 3:
                    _haveMsg = 0xFF;
                    _keepText = false;
                    goOn = false;
                    return c;
                case 8:
                    break;
                case 9:
                    {
                        int frame = Word();
                        talker?.StartAnim(frame);
                    }

                    break;
                case 10:
                    _charsetBufPos += 14;
                    break;
                case 12 or 13:
                    Word();
                    break;
                case 14:
                    Next();
                    Word();
                    break;
                default:
                    throw new InvalidOperationException($"Message code {c} is not one ScummVM knows.");
            }
        }
    }

    /// <summary>
    /// A message with its codes for numbers, verbs, names and strings replaced by what they stand
    /// for. Mirrors <c>convertMessageToString</c>: codes 1, 2, 3 and 8 stay, 4 is a variable's
    /// value, 5 a verb's name, 6 an object's or actor's, 7 a string, and 9 to 14 stay with their word.
    /// </summary>
    private byte[] ConvertMessage(ReadOnlySpan<byte> message)
    {
        var output = new List<byte>();
        int at = 0;
        while (at < message.Length)
        {
            byte c = message[at++];
            if (c == 0)
            {
                break;
            }

            if (c != 0xFF)
            {
                output.Add(c);
                continue;
            }

            c = at < message.Length ? message[at++] : (byte)0;
            if (c is 1 or 2 or 3 or 8)
            {
                output.Add(0xFF);
                output.Add(c);
                continue;
            }

            int value = at + 1 < message.Length ? message[at] | (message[at + 1] << 8) : 0;
            switch (c)
            {
                case 4:
                    output.AddRange(Encoding.ASCII.GetBytes(Engine.ReadVar(value).ToString(CultureInfo.InvariantCulture)));
                    break;
                case 5:
                    {
                        int verb = Engine.ReadVar(value);
                        // the FM Towns' version 3 takes a saved verb's name too (convertVerbMessage)
                        var slot = verb != 0 ? Verbs.Skip(1).FirstOrDefault(v => v.VerbId == verb && v.Type == 0 && (v.SaveId == 0 || FmTowns)) : null;
                        if (slot?.Name is { } name)
                        {
                            output.AddRange(ConvertMessage(name));
                        }
                    }

                    break;
                case 6:
                    {
                        int obj = Engine.ReadVar(value);
                        if (obj != 0 && NameOf(obj) is { } name)
                        {
                            output.AddRange(ConvertMessage(name));
                        }
                    }

                    break;
                case 7:
                    {
                        int number = Engine.ReadVar(value);
                        if (number is > 0 and < StringCount && _strings[number] is { } text)
                        {
                            output.AddRange(ConvertMessage(text));
                        }
                    }

                    break;
                case 9 or 10 or 12 or 13 or 14:
                    output.Add(0xFF);
                    output.Add(c);
                    output.Add(message[at]);
                    output.Add(message[at + 1]);
                    break;
                default:
                    // an unknown code is copied as it is, its word read as text
                    output.Add(0xFF);
                    output.Add(c);
                    at -= 2;
                    break;
            }

            at += 2;
        }

        return [.. output];
    }

    /// <summary>The printable characters of a message, codes left out.</summary>
    private static string Plain(ReadOnlySpan<byte> message)
    {
        var text = new StringBuilder();
        for (int i = 0; i < message.Length && message[i] != 0; i++)
        {
            if (message[i] == 0xFF && i + 1 < message.Length)
            {
                i += message[i + 1] is 1 or 2 or 3 or 8 ? 1 : 3;
                continue;
            }

            text.Append((char)message[i]);
        }

        return text.ToString();
    }

    /// <summary>Mirrors <c>runActorTalkScript</c> for an actor without a talk script: the frame starts unless it shows already.</summary>
    private void RunTalkAnimation(Actor actor, int frame)
    {
        if (TalkingActor != 0 && actor.IsInCurrentRoom && actor.Frame != frame)
        {
            actor.StartAnim(frame);
        }
    }

    private static int CheckActor(int actor)
        => actor is > 0 and < ActorCount ? actor : throw new InvalidOperationException($"Actor {actor} is out of range (1 to {ActorCount - 1}).");
}
