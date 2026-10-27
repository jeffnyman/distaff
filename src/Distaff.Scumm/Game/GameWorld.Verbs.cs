using Distaff.Scumm.Graphics;
using Distaff.Scumm.Scripts;
using Distaff.Scumm.View;

namespace Distaff.Scumm.Game;

/// <summary>A verb on screen, as the scripts set it up. Mirrors ScummVM's <c>VerbSlot</c>.</summary>
public sealed class VerbSlot
{
    public int VerbId { get; set; }

    /// <summary>Where the scripts put the verb; its right and bottom come from drawing it (<c>curRect</c>).</summary>
    public Rect CurRect { get; set; }

    /// <summary>What was drawn last, to erase, or null (<c>oldRect</c>, whose left is -1 when there is none).</summary>
    public Rect? OldRect { get; set; }

    public int X => CurRect.Left;

    public int Y => CurRect.Top;

    public int Color { get; set; } = 2;

    public int HiColor { get; set; }

    public int DimColor { get; set; }

    public int BackColor { get; set; }

    /// <summary>0 for off, 1 for on, 2 for dimmed (<c>curmode</c>).</summary>
    public int Mode { get; set; }

    /// <summary>0 for text, 1 for an image (<c>type</c>).</summary>
    public int Type { get; set; }

    public int Key { get; set; }

    public bool Center { get; set; }

    /// <summary>Non-zero while saved by <c>saveRestoreVerbs</c> (<c>saveid</c>).</summary>
    public int SaveId { get; set; }

    /// <summary>The object whose image the verb shows (<c>imgindex</c>).</summary>
    public int ImageObject { get; set; }

    /// <summary>
    /// The image copied from the object, kept when the room changes (the <c>rtVerb</c> resource):
    /// its picture, its width in strips and its height in pixels.
    /// </summary>
    public (ReadOnlyMemory<byte> Picture, int Strips, int Height)? Image { get; set; }

    public byte[]? Name { get; set; }
}

/// <summary>
/// Verbs: what the scripts set up, drawn into the band they are in, and found under a click.
/// Mirrors ScummVM's <c>o5_verbOps</c>, <c>o5_saveRestoreVerbs</c>, <c>getVerbSlot</c>,
/// <c>killVerb</c>, <c>setVerbObject</c>, <c>drawVerb</c>, <c>drawVerbBitmap</c>,
/// <c>restoreVerbBG</c>, <c>restoreBackground</c> and <c>findVerbAtPos</c>. Loom's verbs are all
/// images, the distaff and its notes; text verbs draw as ScummVM draws them, without the
/// highlight under the mouse.
/// </summary>
public sealed partial class GameWorld
{
    /// <summary>The slot holding a verb with this save id, or 0 (<c>getVerbSlot</c>).</summary>
    private int VerbSlotOf(int verb, int saveId)
    {
        for (int i = 1; i < VerbCount; i++)
        {
            if (Verbs[i].VerbId == verb && Verbs[i].SaveId == saveId)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>Mirrors <c>killVerb</c>: the verb goes, and is erased unless it was saved.</summary>
    private void KillVerb(int slot)
    {
        if (slot == 0)
        {
            return;
        }

        var vs = Verbs[slot];
        vs.VerbId = 0;
        vs.Mode = 0;
        vs.Name = null;
        vs.Image = null;
        if (vs.SaveId == 0)
        {
            DrawVerb(slot);
        }

        vs.SaveId = 0;
    }

    /// <summary>Mirrors <c>o5_verbOps</c>; slot 0 takes what is set for a verb no slot holds. The verb is drawn again after.</summary>
    private void VerbOps(int verb, IReadOnlyList<SubOperation> subOps, IReadOnlyList<int[]> args)
    {
        int slot = VerbSlotOf(verb, 0);
        var vs = Verbs[slot];
        vs.VerbId = verb;
        for (int i = 0; i < subOps.Count; i++)
        {
            int[] a = args[i];
            switch (subOps[i].Code)
            {
                case 1:
                    if (slot != 0)
                    {
                        SetVerbObject(a[0], vs);
                        vs.Type = 1;
                    }

                    break;
                case 2:
                    vs.Name = slot != 0 ? subOps[i].Operands[0].Text : null;
                    vs.Type = 0;
                    vs.ImageObject = 0;
                    break;
                case 3:
                    vs.Color = a[0];
                    break;
                case 4:
                    vs.HiColor = a[0];
                    break;
                case 5:
                    vs.CurRect = vs.CurRect with { X = a[0], Y = a[1] };
                    break;
                case 6:
                    vs.Mode = 1;
                    break;
                case 7:
                    vs.Mode = 0;
                    break;
                case 8:
                    KillVerb(slot);
                    break;
                case 9:
                    slot = VerbSlotOf(verb, 0);
                    if (slot == 0)
                    {
                        slot = Array.FindIndex(Verbs, 1, v => v.VerbId == 0);
                        if (slot < 0)
                        {
                            throw new InvalidOperationException("Too many verbs.");
                        }
                    }

                    vs = Verbs[slot];
                    vs.VerbId = verb;
                    vs.Color = 2;
                    vs.HiColor = 14;
                    vs.DimColor = 8;
                    vs.Type = 0;
                    vs.Mode = 0;
                    vs.SaveId = 0;
                    vs.Key = 0;
                    vs.Center = false;
                    vs.ImageObject = 0;
                    break;
                case 16:
                    vs.DimColor = a[0];
                    break;
                case 17:
                    vs.Mode = 2;
                    break;
                case 18:
                    vs.Key = a[0];
                    break;
                case 19:
                    vs.Center = true;
                    break;
                case 20:
                    vs.Name = slot != 0 && a[0] is > 0 and < StringCount ? _strings[a[0]]?.ToArray() : null;
                    vs.Type = 0;
                    vs.ImageObject = 0;
                    break;
                case 22:
                    if (slot != 0 && vs.ImageObject != a[0])
                    {
                        SetVerbObject(a[0], vs);
                        vs.Type = 1;
                        vs.ImageObject = a[0];
                    }

                    break;
                case 23:
                    vs.BackColor = a[0];
                    break;
            }
        }

        DrawVerb(slot);
    }

    /// <summary>Mirrors <c>o5_saveRestoreVerbs</c>: a saved verb is erased, a restored one drawn.</summary>
    private void SaveRestoreVerbs(int op, int[] a)
    {
        for (int verb = a[0]; verb <= a[1]; verb++)
        {
            switch (op)
            {
                case 1:
                    {
                        int slot = VerbSlotOf(verb, 0);
                        if (slot != 0 && Verbs[slot].SaveId == 0)
                        {
                            Verbs[slot].SaveId = a[2];
                            DrawVerb(slot);
                        }
                    }

                    break;
                case 2:
                    if (VerbSlotOf(verb, a[2]) != 0)
                    {
                        KillVerb(VerbSlotOf(verb, 0));
                        int slot = VerbSlotOf(verb, a[2]);
                        Verbs[slot].SaveId = 0;
                        DrawVerb(slot);
                    }

                    break;
                case 3:
                    KillVerb(VerbSlotOf(verb, a[2]));
                    break;
            }
        }
    }

    /// <summary>
    /// Copies an object's image of the room on screen into the verb. Mirrors <c>setVerbObject</c>
    /// for <c>GF_OLD_BUNDLE</c>: its width in strips from the object's code and its height; an
    /// object the room does not hold leaves the verb's image as it was.
    /// </summary>
    private void SetVerbObject(int obj, VerbSlot vs)
    {
        if (CurrentRoom?.FindObject(obj) is { } o && o.ImageOffset != 0)
        {
            vs.Image = (CurrentRoom.Data[o.ImageOffset..], o.Width / 8, o.Height);
        }
    }

    /// <summary>
    /// Draws a verb that is on and not saved, or erases it. Mirrors <c>drawVerb</c> for version 3:
    /// an image draws where the verb is; text draws in slot 4 in the verb's colour, dimmed when it
    /// is, and its rectangle is the text's.
    /// </summary>
    private void DrawVerb(int slot)
    {
        if (slot == 0)
        {
            return;
        }

        var vs = Verbs[slot];
        if (vs.SaveId != 0 || vs.Mode == 0 || vs.VerbId == 0)
        {
            RestoreVerbBG(vs);
            return;
        }

        if (vs.Type == 1)
        {
            DrawVerbBitmap(vs);
            return;
        }

        RestoreVerbBG(vs);
        var text = TextSlots[4];
        text.X = vs.CurRect.Left;
        text.Y = vs.CurRect.Top;
        text.Right = ScreenWidth - 1;
        text.Center = vs.Center;
        text.Color = vs.Mode == 2 ? vs.DimColor : vs.Color;
        if (vs.Name is not { } name)
        {
            return;
        }

        bool center = _textCenter;
        DrawString(4, name);
        _textCenter = center;
        vs.CurRect = Rect.FromBounds(vs.CurRect.Left, vs.CurRect.Top, _str.Right, _str.Bottom);
        vs.OldRect = _str;
        _str = Rect.FromBounds(_str.Right, _str.Top, _str.Right, _str.Bottom);
    }

    /// <summary>
    /// Draws a verb's image, strip by strip, into the band it is in, in the room's colours.
    /// Mirrors <c>drawVerbBitmap</c>: the verb covers the image's strips and rows.
    /// </summary>
    private void DrawVerbBitmap(VerbSlot vs)
    {
        if (Screens.Find(vs.CurRect.Top) is not { Front: { } front } band || vs.Image is not { } image)
        {
            return;
        }

        int xstart = band.Number == VirtScreenNumber.Main ? Camera.Left : 0;
        var target = new MaskedImage(front, new Surface8(front.Width, front.Height));
        EgaBitmap.Draw(image.Picture.Span, 0, image.Strips, image.Height, target, xstart + vs.CurRect.Left / 8 * 8, vs.CurRect.Top - band.Top, RoomPalette);

        vs.CurRect = new Rect(vs.CurRect.Left, vs.CurRect.Top, image.Strips * 8, image.Height / 8 * 8);
        vs.OldRect = vs.CurRect;
    }

    /// <summary>Erases what the verb drew last with its back colour. Mirrors <c>restoreVerbBG</c>.</summary>
    private void RestoreVerbBG(VerbSlot vs)
    {
        if (vs.OldRect is { } old)
        {
            RestoreBackground(old, (byte)vs.BackColor);
            vs.OldRect = null;
        }
    }

    /// <summary>
    /// Shows the room again in a rectangle of the screen, or fills it with a colour where no room
    /// is under it. Mirrors <c>restoreBackground</c>.
    /// </summary>
    private void RestoreBackground(Rect rect, byte backColor)
    {
        if (rect.Top < 0)
        {
            rect = Rect.FromBounds(rect.Left, 0, rect.Right, rect.Bottom);
        }

        if (rect.IsEmpty || Screens.Find(rect.Top) is not { Front: { } front } vs || rect.Left > ScreenWidth)
        {
            return;
        }

        var local = rect.Offset(0, -vs.Top).Intersect(new Rect(0, 0, ScreenWidth, vs.Height));
        if (local.IsEmpty)
        {
            return;
        }

        bool lightsOn = (Engine.Variables[Vars.CurrentLights] & LightsRoomOn) != 0;
        if (vs.HasTwoBuffers && CurrentRoomNumber != 0 && lightsOn)
        {
            Screens.RestoreMain(local.Offset(Camera.Left, 0));
            if (_hasMask)
            {
                Screens.TextSurface.FillRect(local.Offset(0, vs.Top), VirtScreens.Transparent);
            }
        }
        else
        {
            front.FillRect(vs.HasTwoBuffers ? local.Offset(Camera.Left, 0) : local, backColor);
        }
    }

    /// <summary>
    /// The verb under a point of the screen, last slot first, or 0. Mirrors <c>findVerbAtPos</c>:
    /// only verbs that are on and not saved; a centred verb reaches as far left of its place as
    /// its right edge is right of it.
    /// </summary>
    public int FindVerbAtPos(int x, int y)
    {
        for (int i = VerbCount - 1; i > 0; i--)
        {
            var vs = Verbs[i];
            var r = vs.CurRect;
            if (vs.Mode != 1 || vs.VerbId == 0 || vs.SaveId != 0 || y < r.Top || y >= r.Bottom)
            {
                continue;
            }

            if (vs.Center ? x < -(r.Right - 2 * r.Left) || x >= r.Right : x < r.Left || x >= r.Right)
            {
                continue;
            }

            return i;
        }

        return 0;
    }
}
