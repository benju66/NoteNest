using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace NoteNest.UI.Collections
{
    /// <summary>
    /// Enhanced ObservableCollection that supports batch updates to eliminate UI flickering.
    /// When in batch mode, collection change notifications are suppressed until the batch completes,
    /// then a single Reset notification is sent to update the UI in one frame.
    /// </summary>
    /// <typeparam name="T">The type of items in the collection</typeparam>
    public class SmartObservableCollection<T> : ObservableCollection<T>
    {
        private bool _suppressNotifications = false;
        private int _batchDepth = 0;
        private readonly object _batchLock = new object();

        /// <summary>
        /// Begins a batch update operation. All collection changes within the batch
        /// will be accumulated and applied as a single UI update when the batch is disposed.
        /// Batches can be nested - notifications are only resumed when all batches complete.
        /// </summary>
        /// <returns>Disposable token that ends the batch when disposed</returns>
        public IDisposable BatchUpdate()
        {
            lock (_batchLock)
            {
                _batchDepth++;
                _suppressNotifications = true;
            }
            
            return new BatchUpdateToken(EndBatch);
        }

        /// <summary>
        /// Adds multiple items to the collection efficiently.
        /// Uses batch update internally to minimize UI notifications.
        /// </summary>
        public void AddRange(IEnumerable<T> items)
        {
            if (items == null) return;
            
            using (BatchUpdate())
            {
                foreach (var item in items)
                {
                    Add(item);
                }
            }
        }

        /// <summary>
        /// Replaces all items in the collection with new items.
        /// Uses batch update to minimize UI flicker - appears as single change to UI.
        /// </summary>
        public void ReplaceAll(IEnumerable<T> newItems)
        {
            using (BatchUpdate())
            {
                Clear();
                if (newItems != null)
                {
                    foreach (var item in newItems)
                    {
                        Add(item);
                    }
                }
            }
        }

        /// <summary>
        /// Moves an item from one position to another within the collection.
        /// Uses batch update for smooth visual transition.
        /// </summary>
        public void MoveTo(T item, int newIndex)
        {
            var oldIndex = IndexOf(item);
            if (oldIndex == -1 || oldIndex == newIndex) return;

            using (BatchUpdate())
            {
                RemoveAt(oldIndex);
                Insert(newIndex > oldIndex ? newIndex - 1 : newIndex, item);
            }
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (!_suppressNotifications)
            {
                base.OnCollectionChanged(e);
            }
        }

        protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (!_suppressNotifications)
            {
                base.OnPropertyChanged(e);
            }
        }

        private void EndBatch()
        {
            lock (_batchLock)
            {
                _batchDepth--;
                
                if (_batchDepth <= 0)
                {
                    _batchDepth = 0;
                    _suppressNotifications = false;
                    
                    // Send single Reset notification to update UI
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                    OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
                    OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
                }
            }
        }

        /// <summary>
        /// Internal class to handle batch completion
        /// </summary>
        private class BatchUpdateToken : IDisposable
        {
            private readonly Action _onDispose;
            private bool _disposed = false;

            public BatchUpdateToken(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    _onDispose?.Invoke();
                }
            }
        }
    }
}
