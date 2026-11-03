namespace Distaff.Scumm.Rooms;

/// <summary>A point in room pixels. Mirrors ScummVM's <c>Common::Point</c> as the box code uses it.</summary>
internal readonly record struct Point(int X, int Y)
{
    public int SquareDistance(Point other) => (X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y);
}

/// <summary>
/// The geometry walking needs: the closest point on a line or a box, where two boxes meet, and
/// which side of a line a point is on. Mirrors ScummVM's <c>closestPtOnLine</c>,
/// <c>getClosestPtOnBox</c>, <c>getGates</c>, <c>inBoxQuickReject</c> and <c>compareSlope</c> in
/// <c>boxes.cpp</c>, integer arithmetic and all.
/// </summary>
internal static class BoxGeometry
{
    /// <summary>The corners of a box: upper left, upper right, lower right, lower left.</summary>
    public static (Point UL, Point UR, Point LR, Point LL) Corners(WalkBox box)
        => (new(box.UpperLeftX, box.UpperLeftY), new(box.UpperRightX, box.UpperRightY),
            new(box.LowerRightX, box.LowerRightY), new(box.LowerLeftX, box.LowerLeftY));

    /// <summary>Whether <paramref name="p3"/> is on the inner side of the line from 1 to 2. Mirrors <c>compareSlope</c>.</summary>
    public static bool CompareSlope(Point p1, Point p2, Point p3)
        => (p2.Y - p1.Y) * (p3.X - p1.X) <= (p3.Y - p1.Y) * (p2.X - p1.X);

    /// <summary>The point of a line segment closest to <paramref name="p"/>. Mirrors <c>closestPtOnLine</c>.</summary>
    public static Point ClosestPointOnLine(Point start, Point end, Point p)
    {
        int lxdiff = end.X - start.X;
        int lydiff = end.Y - start.Y;
        Point result;
        if (end.X == start.X)
        {
            result = new(start.X, p.Y);
        }
        else if (end.Y == start.Y)
        {
            result = new(p.X, start.Y);
        }
        else
        {
            int dist = lxdiff * lxdiff + lydiff * lydiff;
            if (Math.Abs(lxdiff) > Math.Abs(lydiff))
            {
                int a = start.X * lydiff / lxdiff;
                int b = p.X * lxdiff / lydiff;
                int c = (a + b - start.Y + p.Y) * lydiff * lxdiff / dist;
                result = new(c, c * lydiff / lxdiff - a + start.Y);
            }
            else
            {
                int a = start.Y * lxdiff / lydiff;
                int b = p.Y * lydiff / lxdiff;
                int c = (a + b - start.X + p.X) * lydiff * lxdiff / dist;
                result = new(c * lxdiff / lydiff - a + start.X, c);
            }
        }

        if (Math.Abs(lydiff) < Math.Abs(lxdiff))
        {
            if (lxdiff > 0)
            {
                if (result.X < start.X)
                {
                    result = start;
                }
                else if (result.X > end.X)
                {
                    result = end;
                }
            }
            else if (result.X > start.X)
            {
                result = start;
            }
            else if (result.X < end.X)
            {
                result = end;
            }
        }
        else if (lydiff > 0)
        {
            if (result.Y < start.Y)
            {
                result = start;
            }
            else if (result.Y > end.Y)
            {
                result = end;
            }
        }
        else if (result.Y > start.Y)
        {
            result = start;
        }
        else if (result.Y < end.Y)
        {
            result = end;
        }

        return result;
    }

    /// <summary>The point on a box's edges closest to (x, y), and its square distance. Mirrors <c>getClosestPtOnBox</c>.</summary>
    public static (int Distance, Point Point) ClosestPointOnBox(WalkBox box, int x, int y)
    {
        var (ul, ur, lr, ll) = Corners(box);
        var p = new Point(x, y);
        int best = 0xFFFFFF;
        var bestPoint = p;
        foreach (var (a, b) in new[] { (ul, ur), (ur, lr), (lr, ll), (ll, ul) })
        {
            var tmp = ClosestPointOnLine(a, b, p);
            int dist = p.SquareDistance(tmp);
            if (dist < best)
            {
                best = dist;
                bestPoint = tmp;
            }
        }

        return (best, bestPoint);
    }

    /// <summary>Whether a point is clearly more than <paramref name="threshold"/> outside a box. Mirrors <c>inBoxQuickReject</c>.</summary>
    public static bool QuickReject(WalkBox box, int x, int y, int threshold)
    {
        var (ul, ur, lr, ll) = Corners(box);
        int t = x - threshold;
        if (t > ul.X && t > ur.X && t > lr.X && t > ll.X)
        {
            return true;
        }

        t = x + threshold;
        if (t < ul.X && t < ur.X && t < lr.X && t < ll.X)
        {
            return true;
        }

        t = y - threshold;
        if (t > ul.Y && t > ur.Y && t > lr.Y && t > ll.Y)
        {
            return true;
        }

        t = y + threshold;
        return t < ul.Y && t < ur.Y && t < lr.Y && t < ll.Y;
    }

    /// <summary>
    /// Where two boxes meet: two lines across the gap, each from a corner of one box to the closest
    /// point of the other, the first point of each in <paramref name="box1"/>. Mirrors <c>getGates</c>.
    /// </summary>
    public static (Point A0, Point A1, Point B0, Point B1) Gates(WalkBox box1, WalkBox box2)
    {
        var (ul1, ur1, lr1, ll1) = Corners(box1);
        var (ul2, ur2, lr2, ll2) = Corners(box2);
        Point[] corner = [ul1, ur1, lr1, ll1, ul2, ur2, lr2, ll2];
        var closest = new Point[8];
        var dist = new int[8];
        for (int i = 0; i < 8; i++)
        {
            (dist[i], closest[i]) = ClosestPointOnBox(i < 4 ? box2 : box1, corner[i].X, corner[i].Y);
        }

        var minDist = new int[3];
        var near = new int[3];
        var inSecond = new bool[3];
        for (int j = 0; j < 3; j++)
        {
            minDist[j] = 0xFFFF;
            for (int i = 0; i < 8; i++)
            {
                if (dist[i] < minDist[j])
                {
                    minDist[j] = dist[i];
                    near[j] = i;
                }
            }

            dist[near[j]] = 0xFFFF;
            minDist[j] = (int)Math.Sqrt(minDist[j]);
            inSecond[j] = near[j] > 3;
        }

        int line1, line2;
        if (inSecond[0] == inSecond[1] && Math.Abs(minDist[0] - minDist[1]) < 4)
        {
            (line1, line2) = (near[0], near[1]);
        }
        else if (inSecond[0] == inSecond[1] && minDist[0] == minDist[1])
        {
            (line1, line2) = (near[0], near[1]);
        }
        else if (inSecond[0] == inSecond[2] && minDist[0] == minDist[2])
        {
            (line1, line2) = (near[0], near[2]);
        }
        else if (inSecond[1] == inSecond[2] && minDist[1] == minDist[2])
        {
            (line1, line2) = (near[1], near[2]);
        }
        else if (inSecond[0] == inSecond[2] && Math.Abs(minDist[0] - minDist[2]) < 4)
        {
            (line1, line2) = (near[0], near[2]);
        }
        else if (Math.Abs(minDist[0] - minDist[2]) < 4)
        {
            (line1, line2) = (near[1], near[2]);
        }
        else if (Math.Abs(minDist[0] - minDist[1]) < 4)
        {
            (line1, line2) = (near[0], near[1]);
        }
        else
        {
            (line1, line2) = (near[0], near[0]);
        }

        var (a0, a1) = line1 < 4 ? (corner[line1], closest[line1]) : (closest[line1], corner[line1]);
        var (b0, b1) = line2 < 4 ? (corner[line2], closest[line2]) : (closest[line2], corner[line2]);
        return (a0, a1, b0, b1);
    }
}
