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
public sealed class TextSlot
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Right { get; set; } = GameWorld.ScreenWidth - 1;

    public int Height { get; set; }

    public int Color { get; set; } = 0x0F;

    public bool Center { get; set; }

    public bool Overhead { get; set; }

    public int Charset { get; set; }

    private (int X, int Y, int Right, int Height, int Color, bool Center, bool Overhead, int Charset) _default = (0, 0, GameWorld.ScreenWidth - 1, 0, 0x0F, false, false, 0);

    /// <summary>Mirrors <c>StringTab::loadDefault</c>.</summary>
    public void LoadDefault() => (X, Y, Right, Height, Color, Center, Overhead, Charset) = _default;

    /// <summary>Mirrors <c>StringTab::saveDefault</c>.</summary>
    public void SaveDefault() => _default = (X, Y, Right, Height, Color, Center, Overhead, Charset);

    /// <summary>
    /// Keeps the position, line height and colour as the defaults, but not the rest: versions 1 to 3
    /// have no defaults, so the text's place carries on to the next (the end of <c>decodeParseStringTextString</c>).
    /// </summary>
    public void KeepPlace() => _default = _default with { X = X, Y = Y, Height = Height, Color = Color };

    /// <summary>The slot as a save holds it, each field with its default; the line height is not saved (<c>syncWithSerializer(StringTab)</c>).</summary>
    public void SaveTo(Saves.ScummState.StringState s)
    {
        (s.X, s.Y, s.Right, s.Color, s.Charset, s.Center, s.Overhead) = (X, Y, Right, (sbyte)Color, Charset, Center ? 1 : 0, Overhead ? 1 : 0);
        (s.DefaultX, s.DefaultY, s.DefaultRight, s.DefaultColor, s.DefaultCharset) = (_default.X, _default.Y, _default.Right, (sbyte)_default.Color, _default.Charset);
        (s.DefaultCenter, s.DefaultOverhead) = (_default.Center ? 1 : 0, _default.Overhead ? 1 : 0);
    }

    /// <summary>The slot from a save; the line heights stay as they are.</summary>
    public void LoadFrom(Saves.ScummState.StringState s)
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

    // the charset renderer's pen (_top, _left, _startLeft, _right, _center, _color, _shadowType, _firstChar)
    private int _textTop;
    private int _textLeft;
    private int _startLeft;
    private int _textRight;
    private bool _textCenter;
    private byte _textColor;
    private ShadowType _shadow;
    private bool _firstChar;

    // the rectangle drawn since the first character (_str), and where speech was drawn (_hasMask, _textScreenID)
    private Rect _str;
    private bool _hasMask;
    private VirtScreenNumber _textScreen;

    /// <summary>The text slots: actors' speech, the three system slots, and the one text verbs draw in (<c>_string</c>).</summary>
    public TextSlot[] TextSlots { get; } = [new(), new(), new(), new(), new(), new()];

    /// <summary>Who <c>print</c> prints for: an actor, or 0xFC to 0xFF for the system slots (<c>_actorToPrintStrFor</c>).</summary>
    public int ActorToPrintFor { get; private set; }

    /// <summary>Whether a message is up: 0 none, 0xFF being shown, 1 shown in full (<c>_haveMsg</c>).</summary>
    public int HaveMessage => _haveMsg;

    /// <summary>The text of the message up now, as far as it has been shown.</summary>
    public string MessageText => _shown.ToString();

    /// <summary>Every message shown so far, in order, as it was finished: for tests and the console.</summary>
    public List<string> MessageLog { get; } = [];

    /// <summary>The actor talking, or 0xFF for none (<c>VAR_TALK_ACTOR</c>).</summary>
    public int TalkingActor
    {
        get => Engine.Variables[Vars.TalkActor];
        private set => Engine.Variables[Vars.TalkActor] = value;
    }

    /// <summary>
    /// Prints a message in a text slot. Mirrors <c>printString</c>: slot 0 is speech, slot 1 is
    /// drawn at once, slots 2 and 3 are debug and dialog messages.
    /// </summary>
    private void PrintString(int slot, byte[] message)
    {
        switch (slot)
        {
            case 0:
                ActorTalk(message);
                break;
            case 1:
                MessageLog.Add(Plain(ConvertMessage(message)));
                DrawString(1, message);
                break;
            default:
                _unhandled.Add($"printString({slot}, \"{Plain(message)}\")");
                break;
        }
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
            var actor = Actors[CheckActor(ActorToPrintFor)];
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
            MessageLog.Add(_shown.ToString());
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
                MessageLog.Add(_shown.ToString());
            }

            _shown.Clear();
            RestoreCharsetBg();
        }

        if (_textCenter)
        {
            _nextLeft = Math.Max(0, _nextLeft - StringWidth(0, _charsetBuffer.AsSpan(_charsetBufPos)) / 2);
        }

        _firstChar = !_keepText;
        while (true)
        {
            int c = NextCharsetCode(talker, out bool goOn);
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
            PrintChar(c, ignoreCharsetMask: false);
            _nextLeft = _textLeft;
            _nextTop = _textTop;

            _shown.Append((char)c);
            _talkDelay += Engine.Variables[Vars.CharInc];
        }
    }

    /// <summary>Mirrors <c>newLine</c> for version 3: back to the slot's left, centred again, down the slot's line height or the font's.</summary>
    private void NewLine()
    {
        _nextLeft = TextSlots[0].X;
        if (_textCenter)
        {
            _nextLeft = Math.Max(0, _nextLeft - StringWidth(0, _charsetBuffer.AsSpan(_charsetBufPos)) / 2);
        }

        _nextTop += TextSlots[0].Height != 0 ? TextSlots[0].Height : _font?.Height ?? 8;
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
        _firstChar = true;

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
                    if (TextSlots[0].Height != 0)
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
    /// bits are the colour.
    /// </summary>
    private void SetColor(int color, bool shadowModeSpecialFlag = false)
    {
        _shadow = (color & 0x40) != 0 ? (shadowModeSpecialFlag ? ShadowType.None : ShadowType.Outline)
            : (color & 0x80) != 0 ? ShadowType.Normal : ShadowType.None;
        _textColor = (byte)(color & 0x0F);
    }

    /// <summary>
    /// Draws a character at the pen and moves it on. Mirrors <c>CharsetRendererV3::printChar</c>:
    /// nothing past the slot's right edge; speech over the room goes into the text surface, other
    /// text into the band; the drawn rectangle grows by the character and its shadow.
    /// </summary>
    private void PrintChar(int chr, bool ignoreCharsetMask)
    {
        if (_font is null || Screens.Find(_textTop) is not { Front: { } front } vs || chr == '@')
        {
            return;
        }

        int origWidth = _font.CharWidth(chr);
        int height = 8;
        if (_textLeft + origWidth > _textRight + 1)
        {
            return;
        }

        if (_shadow == ShadowType.Normal)
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

        if (ignoreCharsetMask || !vs.HasTwoBuffers)
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
    /// Takes the speech off the screen. Mirrors <c>restoreCharsetBg</c>: the next message starts at
    /// slot 0's position; speech over a lit room only leaves the text surface, and a band without
    /// a room under it is cleared to black.
    /// </summary>
    private void RestoreCharsetBg()
    {
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
                        var slot = verb != 0 ? Verbs.Skip(1).FirstOrDefault(v => v.VerbId == verb && v.Type == 0 && v.SaveId == 0) : null;
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
