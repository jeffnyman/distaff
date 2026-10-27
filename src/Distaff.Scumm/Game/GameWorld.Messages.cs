using System.Globalization;
using System.Text;
using Distaff.Scumm.Scripts;

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
}

/// <summary>
/// Messages: what actors and the narrator say, and how long it stays. Mirrors ScummVM's
/// <c>printString</c>, <c>actorTalk</c>, <c>stopTalk</c>, <c>displayDialog</c>,
/// <c>handleNextCharsetCode</c> and <c>convertMessageToString</c> for version 3, without the
/// drawing: each character keeps the message up <c>VAR_CHARINC</c> ticks longer, on top of 60.
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

    /// <summary>The four text slots: actors' speech, then the three system slots (<c>_string</c>).</summary>
    public TextSlot[] TextSlots { get; } = [new(), new(), new(), new()];

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
    }

    /// <summary>
    /// Moves the message on: when its time is up, the next piece shows, up to a wait code or
    /// its end, and the time it stays is 60 ticks and <c>VAR_CHARINC</c> more for each character.
    /// Mirrors the timing of <c>displayDialog</c> for version 3.
    /// </summary>
    private void DisplayDialog()
    {
        if (_haveMsg == 0 || _talkDelay != 0)
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
        }

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
                continue;
            }

            _shown.Append((char)c);
            _talkDelay += Engine.Variables[Vars.CharInc];
        }
    }

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
