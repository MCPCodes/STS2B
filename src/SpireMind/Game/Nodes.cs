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
}
