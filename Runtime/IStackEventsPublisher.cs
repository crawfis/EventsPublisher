namespace CrawfisSoftware.Events
{
    public interface IStackEventsPublisher<T> : IEventsPublisher<T>
    {
        void Push();
        IEventsPublisher<T> Pop();
    }
}