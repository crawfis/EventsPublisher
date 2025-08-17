namespace CrawfisSoftware.Events
{
    public interface IStackEventsPublisher<T> : IEventsPublisher<T>
    {
        /// <summary>
        /// Pushes a new events publisher onto the internal stack.
        /// </summary>
        /// <remarks>This method adds a new instance of an events publisher to the internal stack, 
        /// allowing it to be used for event publishing. The stack maintains the order  of publishers, with the most
        /// recently pushed publisher being used first.</remarks>
        void Push();

        /// <summary>
        /// Removes and returns the most recently added events publisher from the stack.
        /// </summary>
        /// <remarks>Use this method to retrieve and remove the top <see cref="IEventsPublisher{T}"/> from
        /// the stack.  If the stack is empty, the method returns <see langword="null"/>.</remarks>
        /// <returns>The most recently added <see cref="IEventsPublisher{T}"/> instance, or <see langword="null"/> if the stack
        /// is empty.</returns>
        IEventsPublisher<T> Pop();
    }
}