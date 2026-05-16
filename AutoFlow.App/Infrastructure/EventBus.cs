namespace AutoFlow.App.Infrastructure;

public sealed class EventBus : IEventBus
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();

    public IDisposable Subscribe<TMessage>(Action<TMessage> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_syncRoot)
        {
            var messageType = typeof(TMessage);
            if (!_handlers.TryGetValue(messageType, out var registrations))
            {
                registrations = [];
                _handlers[messageType] = registrations;
            }

            registrations.Add(handler);
        }

        return new Subscription<TMessage>(this, handler);
    }

    public void Publish<TMessage>(TMessage message)
    {
        List<Delegate>? registrations;

        lock (_syncRoot)
        {
            if (!_handlers.TryGetValue(typeof(TMessage), out registrations) || registrations.Count == 0)
            {
                return;
            }

            registrations = [.. registrations];
        }

        foreach (var registration in registrations.Cast<Action<TMessage>>())
        {
            registration(message);
        }
    }

    private void Unsubscribe<TMessage>(Action<TMessage> handler)
    {
        lock (_syncRoot)
        {
            if (!_handlers.TryGetValue(typeof(TMessage), out var registrations))
            {
                return;
            }

            registrations.Remove(handler);
            if (registrations.Count == 0)
            {
                _handlers.Remove(typeof(TMessage));
            }
        }
    }

    private sealed class Subscription<TMessage> : IDisposable
    {
        private readonly EventBus _eventBus;
        private Action<TMessage>? _handler;

        public Subscription(EventBus eventBus, Action<TMessage> handler)
        {
            _eventBus = eventBus;
            _handler = handler;
        }

        public void Dispose()
        {
            if (_handler is null)
            {
                return;
            }

            _eventBus.Unsubscribe(_handler);
            _handler = null;
        }
    }
}
