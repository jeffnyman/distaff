using Distaff.Scumm.Rooms;

namespace Distaff.Scumm.Graphics;

/// <summary>Draws rooms and their objects, in the 16 colour numbers of <see cref="Palettes.Ega"/>.</summary>
public static class RoomRenderer
{
    /// <summary>The bits of an object's state that make it draw its image. Mirrors the mask in <c>drawRoomObjects</c> for version 3.</summary>
    public const int ImageStates = 0x0F;

    /// <summary>The room's background picture and mask, without objects, its colours drawn through <paramref name="colorMap"/> when there is one.</summary>
    public static MaskedImage DrawBackground(Room room, byte[]? colorMap = null)
    {
        var image = new MaskedImage(new Surface8(room.Width, room.Height), new Surface8(room.Width, room.Height));
        EgaBitmap.Draw(room.Data.Span[room.ImageOffset..], 0, room.Width / 8, room.Height, image, 0, 0, colorMap);
        return image;
    }

    /// <summary>
    /// Whether the object's image offset points into the room's images, which lie between the
    /// room's picture and the first object code block. Some objects without an image point it at
    /// that code block.
    /// </summary>
    public static bool HasImage(Room room, RoomObject obj)
        => obj.Width > 0 && obj.Height > 0 && obj.ImageOffset > room.ImageOffset
            && obj.ImageOffset < room.Objects.Min(o => o.CodeOffset);

    /// <summary>
    /// Whether the object's image is its own: one with as many strips as the object is wide.
    /// Objects without an image of their own mostly point at another object's. The game never
    /// draws them, because their state stays 0; this test only matters when drawing images
    /// regardless of state.
    /// </summary>
    public static bool OwnsImage(Room room, RoomObject obj)
        => HasImage(room, obj) && EgaBitmap.StripCount(room.Data.Span[obj.ImageOffset..]) == obj.Width / 8;

    /// <summary>
    /// Draws the room with the objects whose state, given by global object number, makes them
    /// visible. Mirrors ScummVM's <c>drawRoomObjects</c>, <c>drawRoomObject</c> and
    /// <c>drawObject</c>: objects are drawn from the last to the first, each only when its state
    /// is not 0 and every parent up the chain is in the state its child expects. An object's mask
    /// replaces the room's mask where it is drawn.
    /// </summary>
    public static MaskedImage Render(Room room, Func<int, int> stateOf)
    {
        var image = DrawBackground(room);
        foreach (var obj in VisibleObjects(room, stateOf))
        {
            DrawObject(room, obj, image);
        }

        return image;
    }

    /// <summary>The objects <see cref="Render"/> draws, in the order it draws them.</summary>
    public static IEnumerable<RoomObject> VisibleObjects(Room room, Func<int, int> stateOf)
    {
        for (int i = room.Objects.Count - 1; i >= 0; i--)
        {
            var obj = room.Objects[i];
            if (obj.Number != 0 && (stateOf(obj.Number) & ImageStates) != 0 && ParentsAllow(room.Objects, obj, stateOf))
            {
                yield return obj;
            }
        }
    }

    /// <summary>
    /// Draws the room with every image that belongs to its object (<see cref="OwnsImage"/>),
    /// whatever the states: for looking at the images, not as the game shows the room.
    /// </summary>
    public static MaskedImage RenderAll(Room room)
    {
        var image = DrawBackground(room);
        for (int i = room.Objects.Count - 1; i >= 0; i--)
        {
            if (OwnsImage(room, room.Objects[i]))
            {
                DrawObject(room, room.Objects[i], image);
            }
        }

        return image;
    }

    /// <summary>
    /// Draws an object's image and mask into a picture of its room, where the object stands or
    /// where a script moved it to.
    /// </summary>
    public static void DrawObject(Room room, RoomObject obj, MaskedImage target, byte[]? colorMap = null, (int X, int Y)? at = null)
    {
        if (HasImage(room, obj))
        {
            var (x, y) = at ?? (obj.X, obj.Y);
            EgaBitmap.Draw(room.Data.Span[obj.ImageOffset..], 0, obj.Width / 8, obj.Height, target, x, y, colorMap);
        }
    }

    /// <summary>Whether every parent of an object, among its room's objects, is in the state the object needs to show.</summary>
    public static bool ParentsAllow(IReadOnlyList<RoomObject> objects, RoomObject obj, Func<int, int> stateOf)
    {
        // the parent indexes are 1-based; walk up while each parent's state matches what its child expects
        var current = obj;
        for (int depth = 0; depth < objects.Count; depth++)
        {
            if (current.Parent == 0)
            {
                return true;
            }

            if (current.Parent > objects.Count)
            {
                return false;
            }

            var parent = objects[current.Parent - 1];
            if ((stateOf(parent.Number) & ImageStates) != current.ParentState)
            {
                return false;
            }

            current = parent;
        }

        return false;
    }
}
