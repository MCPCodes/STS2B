using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace SpireMind.Game;

/// <summary>Small Godot scene-tree helpers.</summary>
internal static class Nodes
{
    /// <summary>All descendants of <paramref name="root"/> of type T, depth-first, in tree order.</summary>
    public static List<T> FindAll<T>(Node? root) where T : class
    {
        var found = new List<T>();
        if (root is null || !GodotObject.IsInstanceValid(root)) return found;
        Walk(root, found);
        return found;
    }

    public static T? FindFirst<T>(Node? root) where T : class => FindAll<T>(root).FirstOrDefault();

    private static void Walk<T>(Node node, List<T> found) where T : class
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T t) found.Add(t);
            Walk(child, found);
        }
    }

    /// <summary>GetNodeOrNull with a type check; "%Name" unique names work relative to the scene owner.</summary>
    public static T? Get<T>(Node? from, string path) where T : class
    {
        if (from is null || !GodotObject.IsInstanceValid(from)) return null;
        return from.GetNodeOrNull(path) as T;
    }

    /// <summary>True when a CanvasItem and all its ancestors are visible.</summary>
    public static bool IsShown(Node? node) =>
        node is CanvasItem ci && GodotObject.IsInstanceValid(ci) && ci.IsVisibleInTree();

    /// <summary>Not freed, still in the tree and not queued for deletion.</summary>
    public static bool Alive([NotNullWhen(true)] Node? node) =>
        node is not null && GodotObject.IsInstanceValid(node) && node.IsInsideTree() && !node.IsQueuedForDeletion();

    /// <summary>Freed, detached, queued for deletion or hidden: a claimed reward or an old page's button on its way out.</summary>
    public static bool Gone([NotNullWhen(false)] Node? node) => !Alive(node) || !IsShown(node);

    /// <summary>The top overlay screen, or null when none; compared by reference to notice a screen opening.</summary>
    public static object? TopOverlay() =>
        NOverlayStack.Instance is { ScreenCount: > 0 } stack ? stack.Peek() : null;

    /// <summary>True while the overlay with this instance id is the one on top of the stack.</summary>
    public static bool IsTopOverlay(ulong instanceId) =>
        NOverlayStack.Instance?.Peek() is Node top && GodotObject.IsInstanceValid(top) && top.GetInstanceId() == instanceId;

    /// <summary>A popup is up in NModalContainer, above every screen.</summary>
    public static bool ModalOpen() =>
        NModalContainer.Instance?.OpenModal is Node modal && GodotObject.IsInstanceValid(modal);

    /// <summary>What a Label or RichTextLabel draws, cleaned of BBCode; "" for any other node.</summary>
    public static string LabelText(Node? node) => node switch
    {
        Godot.RichTextLabel rich => Describe.Text(rich.GetParsedText()),
        Godot.Label plain => Describe.Text(plain.Text),
        _ => "",
    };

    /// <summary>The first visible, non-empty label text under a node, e.g. a button's caption.</summary>
    public static string FirstLabelText(Node? root)
    {
        foreach (var control in FindAll<Godot.Control>(root))
        {
            if (!control.IsVisibleInTree()) continue;
            var text = LabelText(control);
            if (text.Length > 0) return text;
        }
        return "";
    }

    /// <summary>Invoke a non-public instance method by name (used sparingly, e.g. virtualized card grids).</summary>
    public static object? CallPrivate(object target, string method, params object?[] args)
    {
        var type = target.GetType();
        while (type is not null)
        {
            var mi = type.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (mi is not null) return mi.Invoke(target, args);
            type = type.BaseType;
        }
        throw new MissingMethodException(target.GetType().FullName, method);
    }

    /// <summary>Read a non-public field or property by name.</summary>
    public static object? GetPrivate(object target, string member)
    {
        var type = target.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
        while (type is not null)
        {
            if (type.GetField(member, flags) is { } f) return f.GetValue(target);
            if (type.GetProperty(member, flags) is { } p) return p.GetValue(target);
            type = type.BaseType;
        }
        return null;
    }

    /// <summary><see cref="GetPrivate"/> for guessed members: null instead of an exception when the getter throws.</summary>
    public static object? TryGetPrivate(object target, string member)
    {
        try { return GetPrivate(target, member); }
        catch { return null; }
    }
}
