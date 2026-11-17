namespace Ravensguard.Sim.Core;

/// <summary>
/// Typed event bus. Events are queued on Publish and delivered in publish order at Flush (tick barrier).
/// Events published by handlers during a Flush are delivered in the same Flush, after those already queued.
/// </summary>
public sealed class SimEvents
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();
    private readonly Queue<(Type Type, object Evt)> _queue = new();

    public int Pending => _queue.Count;

    public void Publish<T>(T evt) where T : notnull => _queue.Enqueue((typeof(T), evt));

    public IDisposable Subscribe<T>(Action<T> handler)
    {
        if (!_handlers.TryGetValue(typeof(T), out var list)) _handlers[typeof(T)] = list = new();
        list.Add(handler);
        return new Sub(list, handler);
    }

    /// <summary>Deliver all queued events in publish order; handlers in subscription order.</summary>
    public void Flush()
    {
        while (_queue.Count > 0)
        {
            var (type, evt) = _queue.Dequeue();
            if (!_handlers.TryGetValue(type, out var list)) continue;
            foreach (var h in list.ToArray()) h.DynamicInvoke(evt);
        }
    }

    private sealed class Sub(List<Delegate> list, Delegate handler) : IDisposable
    {
        public void Dispose() => list.Remove(handler);
    }
}
