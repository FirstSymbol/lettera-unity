using System;
using UnityEngine;

namespace FirstSymbol.Lettera
{
    public enum LetteraWorkerMode { Auto, Inline, Parallel }

    /// <summary>
    /// Runs the stages of a rebuild that touch nothing shared, across threads where a platform has them and inline where it has not. The same code runs either way,
    /// and a batch starts and ends before control goes back to Unity, so nothing ever sees a text half done.
    /// </summary>
    public static class LetteraWorkers
    {
        /// <summary>Auto takes threads in a player on a machine with more than two cores, and none in edit mode or on the web.</summary>
        public static LetteraWorkerMode Mode = LetteraWorkerMode.Auto;

        /// <summary>How many codepoints a text needs before threads are worth their cost. A smaller one runs inline, with no queue and nothing shared.</summary>
        public static int ParallelFromCodepoints = 4096;

        /// <summary>How many batches went to threads. Tests read this.</summary>
        public static int ParallelBatches { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
        // Managed threads do not exist here: the only path is the inline one, and nothing of the thread machinery is compiled.
        public static int WorkerCount => 0;
        internal static bool ShouldRun(int codepoints) => false;
        internal static void Run(int count, Action<int> body)
        {
            for (int i = 0; i < count; i++) body(i);
        }
        internal static void Shutdown() { }
#else
        static readonly object s_Gate = new object();
        static System.Threading.Thread[] s_Threads;
        static Action<int> s_Body;
        static int s_Count, s_Next, s_Pending, s_Generation;
        static bool s_Stopping;
        static Exception s_Failure;
        static int s_RequestedWorkers = -1;

        /// <summary>How many threads help the main one. Setting it takes effect at the next batch.</summary>
        public static int WorkerCount
        {
            get
            {
                if (s_RequestedWorkers >= 0) return s_RequestedWorkers;
                int cores = SystemInfo.processorCount;
                int spare = Application.isMobilePlatform ? cores - 2 : cores - 1;
                return Mathf.Clamp(spare, 1, Application.isMobilePlatform ? 3 : 4);
            }
            set
            {
                Shutdown();
                s_RequestedWorkers = value;
            }
        }

        internal static bool ShouldRun(int codepoints)
        {
            if (codepoints < ParallelFromCodepoints) return false;
            switch (Mode)
            {
                case LetteraWorkerMode.Inline: return false;
                case LetteraWorkerMode.Parallel: return WorkerCount > 0;
                default: return Application.isPlaying && SystemInfo.processorCount > 2 && WorkerCount > 0;
            }
        }

        static void Start()
        {
            if (s_Threads != null) return;
            s_Stopping = false;
            s_Threads = new System.Threading.Thread[WorkerCount];
            for (int i = 0; i < s_Threads.Length; i++)
            {
                s_Threads[i] = new System.Threading.Thread(Loop) { IsBackground = true, Name = "Lettera Worker" };
                s_Threads[i].Start();
            }
        }

        static void Loop()
        {
            int seen = 0;
            while (true)
            {
                Action<int> body;
                lock (s_Gate)
                {
                    while (!s_Stopping && s_Generation == seen) System.Threading.Monitor.Wait(s_Gate);
                    if (s_Stopping) return;
                    seen = s_Generation;
                    body = s_Body;
                }
                Work(body);
            }
        }

        static void Work(Action<int> body)
        {
            while (true)
            {
                int index = System.Threading.Interlocked.Increment(ref s_Next) - 1;
                if (index >= s_Count) return;
                try
                {
                    body(index);
                }
                catch (Exception e)
                {
                    lock (s_Gate) s_Failure = s_Failure ?? e;
                }
                if (System.Threading.Interlocked.Decrement(ref s_Pending) == 0)
                    lock (s_Gate)
                        System.Threading.Monitor.PulseAll(s_Gate);
            }
        }

        /// <summary>Calls a body once per index, on the main thread and the workers, and returns when all are done. A failure on any comes out here.</summary>
        internal static void Run(int count, Action<int> body)
        {
            if (count <= 0) return;
            if (count == 1)
            {
                body(0);
                return;
            }
            Start();
            ParallelBatches++;
            lock (s_Gate)
            {
                s_Failure = null;
                s_Body = body;
                s_Count = count;
                s_Next = 0;
                s_Pending = count;
                s_Generation++;
                System.Threading.Monitor.PulseAll(s_Gate);
            }
            // The main thread does its share, so a batch never waits on a thread that has not woken yet.
            Work(body);
            lock (s_Gate)
            {
                while (s_Pending > 0) System.Threading.Monitor.Wait(s_Gate);
                s_Body = null;
                if (s_Failure != null)
                {
                    Exception failure = s_Failure;
                    s_Failure = null;
                    throw new InvalidOperationException("A Lettera worker failed: " + failure.Message, failure);
                }
            }
        }

        /// <summary>Stops the threads and waits for them. A thread alive across a domain reload or into a quit is a crash.</summary>
        internal static void Shutdown()
        {
            if (s_Threads == null) return;
            lock (s_Gate)
            {
                s_Stopping = true;
                System.Threading.Monitor.PulseAll(s_Gate);
            }
            foreach (System.Threading.Thread thread in s_Threads) thread.Join();
            s_Threads = null;
        }

        internal static bool HasThreads => s_Threads != null;
#endif
    }
}
