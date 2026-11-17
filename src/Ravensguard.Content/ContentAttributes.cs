namespace Ravensguard.Content;

/// <summary>Registers a definition record under a JSON "type" name.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ContentTypeAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

/// <summary>Marks a string (or list of strings) property that must reference an existing id of <see cref="Target"/>.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public sealed class ContentRefAttribute(Type target) : Attribute
{
    public Type Target { get; } = target;
}
