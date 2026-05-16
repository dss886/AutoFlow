namespace AutoFlow.App.Infrastructure;

public interface IEventBus
{
    IDisposable Subscribe<TMessage>(Action<TMessage> handler);

    void Publish<TMessage>(TMessage message);
}
