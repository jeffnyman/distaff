using Distaff.Scumm.Detection;
using Distaff.Scumm.Storage;

// For now the player only identifies a release; it gains a window once rooms can be drawn.
if (args.Length != 1 || args[0] is "-h" or "--help")
{
    Console.Error.WriteLine("usage: distaff <game folder>");
    return 2;
}

if (!Directory.Exists(args[0]))
{
    Console.Error.WriteLine($"distaff: no such folder: {args[0]}");
    return 1;
}

if (ScummDetector.Identify(new DirectoryGameContainer(args[0])) is not { } game)
{
    Console.Error.WriteLine($"distaff: no Loom release found in {args[0]}");
    return 1;
}

Console.WriteLine($"Loom ({game.Variant}), SCUMM version {game.Version}, {game.Layout} layout");
Console.WriteLine($"language {game.Language ?? "unknown"}, platform {game.Platform ?? "unknown"}, index MD5 {game.Entry.Md5}");
return 0;
