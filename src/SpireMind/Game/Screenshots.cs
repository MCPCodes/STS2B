using System.Text.Json.Nodes;

namespace SpireMind.Game;

/// <summary>
/// A picture of what the game is drawing, saved for GET /debug/screenshot, so the chat box and
/// the bubbles can be checked by eye from outside the game. Main thread only.
/// </summary>
internal static class Screenshots
{
    /// <summary>Wider pictures are scaled down to this; a Retina screen draws several times as many pixels.</summary>
    private const int MaxWidth = 1920;

    /// <summary>A few files reused in turn, so a long session doesn't fill the temp folder.</summary>
    private const int Kept = 20;

    private static int _count;

    public static JsonObject Take()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var image = tree.Root.GetTexture().GetImage();
        if (image is null || image.IsEmpty())
            return new JsonObject { ["error"] = "the game gave no picture" };

        var (width, height) = (image.GetWidth(), image.GetHeight());
        if (width > MaxWidth)
            image.Resize(MaxWidth, Math.Max(1, height * MaxWidth / width), Image.Interpolation.Bilinear);

        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "spiremind");
        Directory.CreateDirectory(folder);
        var path = System.IO.Path.Combine(folder, $"shot-{_count++ % Kept:00}.png");
        var saved = image.SavePng(path);
        if (saved != Error.Ok)
            return new JsonObject { ["error"] = $"saving {path}: {saved}" };

        return new JsonObject
        {
            ["path"] = path,
            ["width"] = image.GetWidth(),
            ["height"] = image.GetHeight(),
            ["drawn_width"] = width,
            ["drawn_height"] = height,
        };
    }
}
