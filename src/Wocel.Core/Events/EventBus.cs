using System.Collections.Concurrent;

namespace Wocel.Core.Events;

public interface IEventBus
{
    void Publish<TEvent>(TEvent @event);
    IDisposable Subscribe<TEvent>(Action<TEvent> handler);
}

public class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _subscribers = new();
    private readonly object _lock = new();

    public void Publish<TEvent>(TEvent @event)
    {
        if (@event == null) return;
        
        List<Delegate>? handlersCopy = null;
        lock (_lock)
        {
            if (_subscribers.TryGetValue(typeof(TEvent), out var handlers))
            {
                handlersCopy = new List<Delegate>(handlers);
            }
        }

        if (handlersCopy != null)
        {
            foreach (var handler in handlersCopy)
            {
                if (handler is Action<TEvent> typedHandler)
                {
                    typedHandler(@event);
                }
            }
        }
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
    {
        var type = typeof(TEvent);
        lock (_lock)
        {
            if (!_subscribers.TryGetValue(type, out var list))
            {
                list = new List<Delegate>();
                _subscribers[type] = list;
            }
            list.Add(handler);
        }

        return new Unsubscriber(() =>
        {
            lock (_lock)
            {
                if (_subscribers.TryGetValue(type, out var list))
                {
                    list.Remove(handler);
                }
            }
        });
    }

    private sealed class Unsubscriber(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;

        public void Dispose()
        {
            Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
        }
    }
}

public record CellChangedEvent(string SheetId, string Address, object? OldValue, object? NewValue);
public record DocumentUpdatedEvent(string DocumentId, string ChangeType);
public record InteropSyncRequestedEvent(string SourceSheetId, string SourceRange, string TargetDocId, string TargetBlockId);
