namespace Lucode;

/// <summary>A record: named fields in the order they were written. Never changed in place; <see cref="With"/> builds a new one.</summary>
public sealed class RecordValue
{
    private readonly List<KeyValuePair<string, object>> _fields;

    public RecordValue(IEnumerable<KeyValuePair<string, object>> fields) => _fields = fields.ToList();

    public IReadOnlyList<KeyValuePair<string, object>> Fields => _fields;

    public IEnumerable<string> Names => _fields.Select(f => f.Key);

    public int Count => _fields.Count;

    public bool TryGet(string name, out object value)
    {
        foreach (var field in _fields)
        {
            if (field.Key != name) continue;
            value = field.Value;
            return true;
        }
        value = null!;
        return false;
    }

    /// <summary>The same record with this field set; a new field goes on the end.</summary>
    public RecordValue With(string name, object value)
    {
        var copy = _fields.ToList();
        int at = copy.FindIndex(f => f.Key == name);
        var field = new KeyValuePair<string, object>(name, value);
        if (at >= 0) copy[at] = field;
        else copy.Add(field);
        return new RecordValue(copy);
    }
}
