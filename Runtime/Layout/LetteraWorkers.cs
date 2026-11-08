using System;
using System.Threading;
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

        /// <summary>
        /// Short Canvas texts that change in the same frame are shaped, broken into lines, placed and, when plain, meshed as one batch across the threads;
        /// each then hands its mesh to Unity on the main thread. Off, each text does all of it alone in its turn. The result is the same either way.
        /// </summary>
        public static bool BatchShortTexts = true;

        /// <summary>How many texts must change in one frame before waking the threads for them is worth it.</summary>
        public static int BatchFromTexts = 8;

        /// <summary>How many frames sent their short texts to threads. Tests read this.</summary>
        public static int TextBatches { get; internal set; }

#if UNITY_WEBGL && !UNITY_EDITOR
        // Managed threads do not exist here: the only path is the inline one, and nothing of the thread machinery is compiled.
        public static int WorkerCount => 0;
        internal static bool ShouldRun(int codepoints) => false;
        internal static bool ShouldBatch(int texts) => false;
        internal static void Run(int count, Action<int> body)
        {
            for (int i = 0; i < count; i++) body(i);
        }
        static Action<int> s_Body;
        static int s_Ready;
        internal static void Open(Action<int> body)
        {
            s_Body = body;
            s_Ready = 0;
        }
        internal static void Publish(int ready)
        {
            while (s_Ready < ready) s_Body(s_Ready++);
        }
        internal static void Close() => s_Body = null;
        internal static void Shutdown() { }
        internal static void Quit() { }
        internal static void Resume() { }
        internal static bool HasThreads => false;
        internal static void Prewake() { }
#else
        static readonly object s_Gate = new object();
        static Thread[] s_Threads;
        static Action<int> s_Body;
        // Items below Ready may run; Next is the first not yet taken, Done counts those finished. Closed says no more will come.
        static int s_Next, s_Ready, s_Done, s_Generation, s_Active, s_Wake;
        static volatile bool s_Closed;
        static bool s_Stopping;
        // Set once the application is quitting: no thread starts again and every batch runs on the main thread alone.
        static volatile bool s_Quit;
        static Exception s_Failure;
        static int s_RequestedWorkers = -1;
        // How long a thread stays awake waiting for the main thread to publish more of a batch, before leaving the rest to it.
        static readonly long s_AwakeTicks = System.Diagnostics.Stopwatch.Frequency / 2000;
        // A rebuild runs its batches a few hundredths of a millisecond apart: a thread done with one stays awake this long for the next,
        // and the main thread waits awake this long for a batch's last share. Waking a sleeping thread costs more than that.
        static readonly long s_SpinTicks = System.Diagnostics.Stopwatch.Frequency / 5000;

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
            if (s_Quit) return false;
            if (codepoints < ParallelFromCodepoints) return false;
            switch (Mode)
            {
                case LetteraWorkerMode.Inline: return false;
                case LetteraWorkerMode.Parallel: return WorkerCount > 0;
                default: return Application.isPlaying && SystemInfo.processorCount > 2 && WorkerCount > 0;
            }
        }

        internal static bool ShouldBatch(int texts)
        {
            if (s_Quit) return false;
            if (!BatchShortTexts || texts < BatchFromTexts) return false;
            switch (Mode)
            {
                case LetteraWorkerMode.Inline: return false;
                case LetteraWorkerMode.Parallel: return WorkerCount > 0;
                default: return Application.isPlaying && SystemInfo.processorCount > 2 && WorkerCount > 0;
            }
        }

        static void Start()
        {
            if (s_Threads != null || s_Quit) return;
            s_Stopping = false;
            s_Threads = new Thread[WorkerCount];
            for (int i = 0; i < s_Threads.Length; i++)
            {
                s_Threads[i] = new Thread(Loop) { IsBackground = true, Name = "Lettera Worker" };
                s_Threads[i].Start();
            }
        }

        static void Loop()
        {
            int seen = 0, seenWake = 0;
            while (true)
            {
                long until = System.Diagnostics.Stopwatch.GetTimestamp() + s_SpinTicks;
                while (!s_Stopping && Volatile.Read(ref s_Generation) == seen && System.Diagnostics.Stopwatch.GetTimestamp() < until) Thread.SpinWait(32);
                Action<int> body;
                lock (s_Gate)
                {
                    // A batch that ended before this thread woke is past: it waits for the next.
                    while (!s_Stopping && (s_Generation == seen || s_Body == null) && s_Wake == seenWake)
                    {
                        seen = s_Generation;
                        Monitor.Wait(s_Gate);
                    }
                    if (s_Stopping) return;
                    seenWake = s_Wake;
                    // Woken ahead of a batch, or after one ended: it stays awake for the next.
                    if (s_Generation == seen || s_Body == null)
                    {
                        seen = s_Generation;
                        continue;
                    }
                    seen = s_Generation;
                    body = s_Body;
                    s_Active++;
                }
                Work(body);
                lock (s_Gate)
                    if (--s_Active == 0)
                        Monitor.PulseAll(s_Gate);
            }
        }

        /// <summary>Items the main thread did itself, for tests that see how a batch was shared.</summary>
        internal static int MainItems;

        static int Work(Action<int> body)
        {
            int done = 0;
            long idleSince = 0;
            while (true)
            {
                int next = Volatile.Read(ref s_Next);
                if (next < Volatile.Read(ref s_Ready))
                {
                    if (Interlocked.CompareExchange(ref s_Next, next + 1, next) != next) continue;
                    try
                    {
                        body(next);
                    }
                    catch (Exception e)
                    {
                        lock (s_Gate) s_Failure = s_Failure ?? e;
                    }
                    if (Interlocked.Increment(ref s_Done) == Volatile.Read(ref s_Ready) && s_Closed)
                        lock (s_Gate)
                            Monitor.PulseAll(s_Gate);
                    idleSince = 0;
                    done++;
                    continue;
                }
                if (s_Closed) return done;
                // Items published one by one come microseconds apart, so the next is waited for awake, for a while.
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (idleSince == 0) idleSince = now;
                else if (now - idleSince > s_AwakeTicks) return done;
                Thread.SpinWait(16);
            }
        }

        /// <summary>Calls a body once per index, on the main thread and the workers, and returns when all are done. A failure on any comes out here.</summary>
        internal static void Run(int count, Action<int> body)
        {
            if (count <= 0) return;
            // One item, or a batch already open around this call: the main thread does it alone.
            if (count == 1 || s_Body != null)
            {
                for (int i = 0; i < count; i++) body(i);
                return;
            }
            Begin(body, count, true);
            Close();
        }

        /// <summary>
        /// Starts a batch whose items the main thread readies one by one: the threads wake while it works, and take each item once it is published.
        /// The main thread helps with what is left at <see cref="Close"/>.
        /// </summary>
        internal static void Open(Action<int> body) => Begin(body, 0, false);

        /// <summary>Items below this may run now.</summary>
        internal static void Publish(int ready) => Volatile.Write(ref s_Ready, ready);

        static void Begin(Action<int> body, int ready, bool closed)
        {
            Start();
            ParallelBatches++;
            lock (s_Gate)
            {
                s_Failure = null;
                s_Body = body;
                s_Next = s_Done = 0;
                s_Ready = ready;
                s_Closed = closed;
                s_Generation++;
                Monitor.PulseAll(s_Gate);
            }
        }

        /// <summary>No more items: the main thread does its share of what is published, and returns when every item is done and every thread is out.</summary>
        internal static void Close()
        {
            Action<int> body;
            lock (s_Gate)
            {
                s_Closed = true;
                body = s_Body;
            }
            // The main thread does its share, so a batch never waits on a thread that has not woken yet.
            if (body != null) MainItems += Work(body);
            long until = System.Diagnostics.Stopwatch.GetTimestamp() + s_SpinTicks;
            while ((Volatile.Read(ref s_Done) < Volatile.Read(ref s_Ready) || Volatile.Read(ref s_Active) > 0) && System.Diagnostics.Stopwatch.GetTimestamp() < until)
                Thread.SpinWait(32);
            lock (s_Gate)
            {
                while (Volatile.Read(ref s_Done) < s_Ready) Monitor.Wait(s_Gate);
                // A thread that took this batch's body leaves before the next batch can start, so it never runs this body on that batch's items.
                s_Body = null;
                while (s_Active > 0) Monitor.Wait(s_Gate);
                if (s_Failure != null)
                {
                    Exception failure = s_Failure;
                    s_Failure = null;
                    throw new InvalidOperationException("A Lettera worker failed: " + failure.Message, failure);
                }
            }
        }

        /// <summary>Wakes the threads ahead of batches that are about to come, so the first of them does not wait for a thread to wake.</summary>
        internal static void Prewake()
        {
            if (s_Threads == null) return;
            lock (s_Gate)
            {
                s_Wake++;
                Monitor.PulseAll(s_Gate);
            }
        }

        /// <summary>Stops the threads and waits for them, at most about a second in all. A thread alive across a domain reload or into a quit is a crash. Safe to call again, and from a thread that is not the main one.</summary>
        internal static void Shutdown()
        {
            Thread[] threads;
            lock (s_Gate)
            {
                threads = s_Threads;
                if (threads == null) return;
                s_Threads = null;
                s_Stopping = true;
                Monitor.PulseAll(s_Gate);
            }
            long deadline = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency;
            foreach (Thread thread in threads)
            {
                long left = (deadline - System.Diagnostics.Stopwatch.GetTimestamp()) * 1000 / System.Diagnostics.Stopwatch.Frequency;
                // They are background threads: one that does not leave in time dies with the process.
                if (thread != Thread.CurrentThread) thread.Join((int)Math.Max(left, 1));
            }
        }

        /// <summary>The application is quitting: the threads stop and none starts again.</summary>
        internal static void Quit()
        {
            s_Quit = true;
            Shutdown();
        }

        /// <summary>Undoes <see cref="Quit"/>. Tests only.</summary>
        internal static void Resume() => s_Quit = false;

        internal static bool HasThreads => s_Threads != null;
#endif
    }
}
