using System.Reflection;

namespace Ravensguard.Content;

/// <summary>Maps JSON "type" names to definition record types.</summary>
public sealed class ContentTypeRegistry
{
    private readonly Dictionary<string, Type> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, string> _byType = [];

    private static readonly Lazy<ContentTypeRegistry> _default = new(() =>
        new ContentTypeRegistry().RegisterAssembly(typeof(ContentTypeRegistry).Assembly));

    /// <summary>Registry holding every [ContentType] record in this assembly.</summary>
    public static ContentTypeRegistry Default => _default.Value;

    public ContentTypeRegistry RegisterAssembly(Assembly assembly)
    {
        foreach (var t in assembly.GetTypes())
            if (t.GetCustomAttribute<ContentTypeAttribute>(false) != null) Register(t);
        return this;
    }

    public ContentTypeRegistry Register(Type type)
    {
        var attr = type.GetCustomAttribute<ContentTypeAttribute>(false)
            ?? throw new ArgumentException($"{type} has no [ContentType] attribute");
        if (!typeof(ContentDef).IsAssignableFrom(type))
            throw new ArgumentException($"{type} must derive from ContentDef");
        if (attr.Name is "patch" or "migration")
            throw new ArgumentException($"'{attr.Name}' is a reserved type name");
        if (_byName.TryGetValue(attr.Name, out var existing) && existing != type)
            throw new ArgumentException($"Content type '{attr.Name}' already registered by {existing}");
        _byName[attr.Name] = type;
        _byType[type] = attr.Name;
        return this;
    }

    public bool TryGetType(string name, out Type type) => _byName.TryGetValue(name, out type!);
    public IEnumerable<string> Names => _byName.Keys;

    public string NameOf(Type type) =>
        _byType.TryGetValue(type, out var n) ? n : throw new ArgumentException($"{type} is not a registered content type");
}
