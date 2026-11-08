using System.Threading;
using NUnit.Framework;

namespace FirstSymbol.Lettera.Editor.Tests
{
    /// <summary>A player's end: threads joined, nothing of Lettera's running after, and every step safe to repeat.</summary>
    public class QuitShutdownTests : TextFixture
    {
        [TearDown]
        public void Restore()
        {
            GlyphAtlasRegistration.ResumeAfterQuit();
            LetteraWorkers.Mode = LetteraWorkerMode.Auto;
            LetteraWorkers.WorkerCount = -1;
        }

        static void StartThreads()
        {
            LetteraWorkers.WorkerCount = 3;
            var seen = new int[2000];
            LetteraWorkers.Run(seen.Length, i => { Interlocked.Increment(ref seen[i]); Thread.SpinWait(100); });
            Assert.IsTrue(LetteraWorkers.HasThreads, "the pool never started");
        }

        [Test]
        public void QuitJoinsTheWorkers()
        {
            StartThreads();
            GlyphAtlasRegistration.ShutdownForQuit();
            Assert.IsFalse(LetteraWorkers.HasThreads);
            Assert.IsTrue(GlyphAtlasRegistration.Quitting);
        }

        [Test]
        public void QuitTwiceIsHarmless()
        {
            StartThreads();
            GlyphAtlasRegistration.ShutdownForQuit();
            Assert.DoesNotThrow(GlyphAtlasRegistration.ShutdownForQuit);
            Assert.DoesNotThrow(GlyphAtlasRegistration.Shutdown);
            Assert.DoesNotThrow(GlyphAtlasRegistration.Shutdown);
            Assert.DoesNotThrow(() => LetteraWorkers.Quit());
        }

        [Test]
        public void NothingStartsAfterQuitAndABatchStillCompletesOnTheMainThread()
        {
            StartThreads();
            GlyphAtlasRegistration.ShutdownForQuit();
            LetteraWorkers.Mode = LetteraWorkerMode.Parallel;
            Assert.IsFalse(LetteraWorkers.ShouldRun(1 << 20));
            Assert.IsFalse(LetteraWorkers.ShouldBatch(1000));
            var seen = new int[500];
            int main = Thread.CurrentThread.ManagedThreadId;
            int other = 0;
            LetteraWorkers.Run(seen.Length, i =>
            {
                seen[i]++;
                if (Thread.CurrentThread.ManagedThreadId != main) Interlocked.Increment(ref other);
            });
            foreach (int n in seen) Assert.AreEqual(1, n);
            Assert.AreEqual(0, other, "a worker ran after the quit");
            Assert.IsFalse(LetteraWorkers.HasThreads);
        }

        [Test]
        public void ShutdownWhileAWorkerIsMidJobWaitsForItAndDoesNotHang()
        {
            LetteraWorkers.WorkerCount = 3;
            int running = 0, finished = 0;
            var release = new ManualResetEventSlim(false);
            LetteraWorkers.Open(i =>
            {
                Interlocked.Increment(ref running);
                Thread.SpinWait(2000);
                Interlocked.Increment(ref finished);
            });
            LetteraWorkers.Publish(3000);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            LetteraWorkers.Close();
            LetteraWorkers.Shutdown();
            LetteraWorkers.Shutdown();
            Assert.Less(clock.ElapsedMilliseconds, 3000);
            Assert.IsFalse(LetteraWorkers.HasThreads);
            Assert.AreEqual(running, finished, "a job was cut in half");
            release.Dispose();
        }
    }
}
