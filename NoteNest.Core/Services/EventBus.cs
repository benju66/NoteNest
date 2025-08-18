using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NoteNest.Core.Services
{
    public interface IEventBus
    {
        Task PublishAsync<TEvent>(TEvent eventData) where TEvent : class;
        void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class;
        void Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : class;
        void Unsubscribe<TEvent>(Delegate handler) where TEvent : class;
    }

    public class EventBus : IEventBus
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new();
        private readonly ReaderWriterLockSlim _lock = new();

        public async Task PublishAsync<TEvent>(TEvent eventData) where TEvent : class
        {
            if (eventData == null) return;

            List<Delegate> handlersToInvoke = null;

            _lock.EnterReadLock();
            try
            {
                if (_handlers.TryGetValue(typeof(TEvent), out var handlers) && handlers.Count > 0)
                {
                    handlersToInvoke = handlers.ToList();
                }
            }
            finally
            {
                _lock.ExitReadLock();
            }

            if (handlersToInvoke == null || handlersToInvoke.Count == 0) return;

            var tasks = new List<Task>(handlersToInvoke.Count);
            foreach (var handler in handlersToInvoke)
            {
                try
                {
                    if (handler is Action<TEvent> sync)
                    {
                        tasks.Add(Task.Run(() => sync(eventData)));
                    }
                    else if (handler is Func<TEvent, Task> async)
                    {
                        tasks.Add(async(eventData));
                    }
                }
                catch
                {
                    // Continue collecting tasks; individual handler exceptions will surface in WhenAll
                }
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch
            {
                // Aggregate exceptions are swallowed to avoid crashing publisher; individual handlers should log
            }
        }

        public void Subscribe<TEvent>(Action<TEvent> handler) where TEvent : class
        {
            if (handler == null) return;
            _lock.EnterWriteLock();
            try
            {
                if (!_handlers.TryGetValue(typeof(TEvent), out var list))
                {
                    list = new List<Delegate>();
                    _handlers[typeof(TEvent)] = list;
                }
                list.Add(handler);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Subscribe<TEvent>(Func<TEvent, Task> handler) where TEvent : class
        {
            if (handler == null) return;
            _lock.EnterWriteLock();
            try
            {
                if (!_handlers.TryGetValue(typeof(TEvent), out var list))
                {
                    list = new List<Delegate>();
                    _handlers[typeof(TEvent)] = list;
                }
                list.Add(handler);
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void Unsubscribe<TEvent>(Delegate handler) where TEvent : class
        {
            if (handler == null) return;
            _lock.EnterWriteLock();
            try
            {
                if (_handlers.TryGetValue(typeof(TEvent), out var list))
                {
                    list.RemoveAll(d => d == handler);
                    if (list.Count == 0)
                    {
                        _handlers.Remove(typeof(TEvent));
                    }
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }
    }
}


