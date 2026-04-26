using System;
using System.Collections.Generic;
using System.Threading;

namespace TestExecutor
{
    public enum WorkerState
    {
        Started,
        Stopped,
        Faulted,
        Replaced
    }

    public class WorkerEventArgs : EventArgs
    {
        public int WorkerId { get; init; }
        public WorkerState State { get; init; }
        public Exception? Exception { get; init; }
    }

    public class QueueEventArgs : EventArgs
    {
        public int QueueLength { get; init; }
    }

    public class ThreadPoolTask
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Action Action { get; }
        public DateTime EnqueuedAt { get; } = DateTime.UtcNow;

        public ThreadPoolTask(Action action)
        {
            Action = action ?? throw new ArgumentNullException(nameof(action));
        }
    }

    public class ThreadPoolConfig
    {
        public int MinThreads { get; init; } = 1;
        public int MaxThreads { get; init; } = 8;
        public TimeSpan IdleWorkerLifetime { get; init; } = TimeSpan.FromSeconds(10);
        public TimeSpan MaxQueueWait { get; init; } = TimeSpan.FromSeconds(1);
    }

    public class DynamicThreadPool : IDisposable
    {
        private readonly ThreadPoolConfig _config;
        private readonly Queue<ThreadPoolTask> _queue = new();
        private readonly object _lock = new();
        private readonly Dictionary<int, WorkerInfo> _workers = new();
        private bool _isShutdown;

        private int _nextWorkerId = 0;

        private class WorkerInfo
        {
            public int Id { get; init; }
            public Thread Thread { get; init; } = null!;
            public DateTime LastActivity { get; set; } = DateTime.UtcNow;
        }

        
        public event EventHandler<WorkerEventArgs>? WorkerStateChanged;
        public event EventHandler<QueueEventArgs>? QueueChanged;

        public DynamicThreadPool(ThreadPoolConfig config)
        {
            _config = config;
            for (int i = 0; i < _config.MinThreads; i++)
                CreateWorker();
        }

        public void Enqueue(Action action)
        {
            var task = new ThreadPoolTask(action);
            lock (_lock)
            {
                _queue.Enqueue(task);
                QueueChanged?.Invoke(this, new QueueEventArgs { QueueLength = _queue.Count });
                Monitor.PulseAll(_lock); // разбудить воркеры

                TryScaleUp_NoLock(task);
            }
        }

        private void CreateWorker()
        {
            var id = Interlocked.Increment(ref _nextWorkerId);
            var thread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name = $"DynamicPoolWorker-{id}"
            };
            var info = new WorkerInfo { Id = id, Thread = thread };
            _workers[id] = info;

            WorkerStateChanged?.Invoke(this, new WorkerEventArgs
            {
                WorkerId = id,
                State = WorkerState.Started
            });

            thread.Start(info);
        }

        private void WorkerLoop(object? state)
        {
            var info = (WorkerInfo)state!;
            try
            {
                while (true)
                {
                    ThreadPoolTask? task = null;
                    lock (_lock)
                    {
                        while (!_isShutdown && _queue.Count == 0)
                        {
                            // если поток простаивает слишком долго – выходим
                            if (DateTime.UtcNow - info.LastActivity > _config.IdleWorkerLifetime &&
                                _workers.Count > _config.MinThreads)
                            {
                                _workers.Remove(info.Id);
                                WorkerStateChanged?.Invoke(this, new WorkerEventArgs
                                {
                                    WorkerId = info.Id,
                                    State = WorkerState.Stopped
                                });
                                return;
                            }

                            Monitor.Wait(_lock, TimeSpan.FromMilliseconds(200));
                        }

                        if (_isShutdown)
                            return;

                        if (_queue.Count > 0)
                        {
                            task = _queue.Dequeue();
                            QueueChanged?.Invoke(this, new QueueEventArgs { QueueLength = _queue.Count });
                        }
                    }

                    if (task != null)
                    {
                        info.LastActivity = DateTime.UtcNow;
                        try
                        {
                            task.Action();
                        }
                        catch (Exception ex)
                        {
                            WorkerStateChanged?.Invoke(this, new WorkerEventArgs
                            {
                                WorkerId = info.Id,
                                State = WorkerState.Faulted,
                                Exception = ex
                            });
                            // замена упавшего воркера
                            lock (_lock)
                            {
                                _workers.Remove(info.Id);
                                if (!_isShutdown)
                                {
                                    CreateWorker();
                                    WorkerStateChanged?.Invoke(this, new WorkerEventArgs
                                    {
                                        WorkerId = info.Id,
                                        State = WorkerState.Replaced,
                                        Exception = ex
                                    });
                                }
                            }
                        }
                    }
                }
            }
            finally
            {
                lock (_lock)
                {
                    _workers.Remove(info.Id);
                }
            }
        }

        // масштабирование при росте очереди / времени ожидания
        private void TryScaleUp_NoLock(ThreadPoolTask newTask)
        {
            if (_workers.Count >= _config.MaxThreads)
                return;

            // критерий по длине очереди
            if (_queue.Count > _workers.Count)
            {
                CreateWorker();
                return;
            }

            // критерий по времени ожидания задач
            var oldest = _queue.Count > 0 ? _queue.Peek() : newTask;
            var waitTime = DateTime.UtcNow - oldest.EnqueuedAt;
            if (waitTime > _config.MaxQueueWait)
                CreateWorker();
        }

        public void Dispose()
        {
            lock (_lock)
            {
                _isShutdown = true;
                Monitor.PulseAll(_lock);
            }
        }
    }
}