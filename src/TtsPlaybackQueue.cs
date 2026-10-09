using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace RSTGameTranslation
{
    /// <summary>
    /// Plays synthesized audio clips one after another, straight from memory. Speak calls return
    /// as soon as their clip is queued, so the next line can be synthesized while the current
    /// one plays. Used by every TTS engine, which also gives one place to know whether any TTS
    /// audio is playing (to keep it out of speech capture) and how far speech is lagging behind
    /// (to speed it up).
    /// </summary>
    public sealed class TtsPlaybackQueue
    {
        // ---- State shared by all engines ----

        // Clips queued or playing, across every queue
        private static int s_backlog;
        private static int s_playing;
        private static long s_lastPlaybackEndTicks;

        // Auto speed-up: +15% per clip waiting behind the one playing, capped
        private const double SpeedStepPerQueuedClip = 0.15;
        public const double MaxAutoSpeedFactor = 1.5;

        static TtsPlaybackQueue()
        {
            DeleteLegacyTempFiles();
        }

        /// <summary>
        /// True while TTS audio is playing, or finished less than <paramref name="tail"/> ago
        /// (output devices keep sounding for a moment after playback stops).
        /// </summary>
        public static bool IsPlayingOrRecent(TimeSpan tail)
        {
            if (Volatile.Read(ref s_playing) > 0) return true;
            long endTicks = Interlocked.Read(ref s_lastPlaybackEndTicks);
            return endTicks != 0 && DateTime.UtcNow.Ticks - endTicks < tail.Ticks;
        }

        /// <summary>
        /// Speech-rate multiplier for the next line: 1.0 when nothing is waiting, faster as clips
        /// pile up, so reading catches up with the game instead of lagging further behind.
        /// </summary>
        public static double GetAutoSpeedFactor()
        {
            if (!ConfigManager.Instance.IsTtsAutoSpeedUpEnabled()) return 1.0;
            int waiting = Volatile.Read(ref s_backlog) - 1; // clips behind the one playing
            if (waiting <= 0) return 1.0;
            double factor = Math.Min(MaxAutoSpeedFactor, 1.0 + SpeedStepPerQueuedClip * waiting);
            Console.WriteLine($"[TTS] {waiting} clip(s) waiting, speeding up next line x{factor:F2}");
            return factor;
        }

        /// <summary>
        /// Older versions played TTS from temp/tts_*.(wav|mp3) files; remove any left behind.
        /// </summary>
        private static void DeleteLegacyTempFiles()
        {
            try
            {
                string tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp");
                if (!Directory.Exists(tempDir)) return;
                foreach (string file in Directory.GetFiles(tempDir, "tts_*.*"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TTS] Legacy temp file cleanup failed: {ex.Message}");
            }
        }

        // ---- Per-engine queue ----

        private readonly string name;
        private readonly object gate = new object();
        private readonly Queue<Func<WaveStream>> pending = new Queue<Func<WaveStream>>();
        private bool running;
        private int generation;
        private WaveOutEvent? current;

        public TtsPlaybackQueue(string name)
        {
            this.name = name;
        }

        /// <param name="openClip">Creates the stream to play; invoked on the playback thread.</param>
        public void Enqueue(Func<WaveStream> openClip)
        {
            Interlocked.Increment(ref s_backlog);
            lock (gate)
            {
                pending.Enqueue(openClip);
                if (running) return;
                running = true;
            }
            _ = Task.Run(PlayLoopAsync);
        }

        /// <summary>Stop the clip being played and drop everything queued.</summary>
        public void Stop()
        {
            lock (gate)
            {
                Interlocked.Add(ref s_backlog, -pending.Count);
                pending.Clear();
                generation++;
                try { current?.Stop(); } catch { }
            }
        }

        private async Task PlayLoopAsync()
        {
            while (true)
            {
                Func<WaveStream> openClip;
                int clipGeneration;
                lock (gate)
                {
                    if (pending.Count == 0)
                    {
                        running = false;
                        return;
                    }
                    openClip = pending.Dequeue();
                    clipGeneration = generation;
                }

                bool played = false;
                try
                {
                    using var clip = openClip();
                    using var output = new WaveOutEvent { DesiredLatency = 100 };
                    var finished = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    output.PlaybackStopped += (_, e) => finished.TrySetResult(e.Exception == null);
                    output.Init(clip);

                    lock (gate)
                    {
                        // Stop() was called while this clip was being opened
                        if (clipGeneration != generation) continue;
                        current = output;
                        Interlocked.Increment(ref s_playing);
                        played = true;
                        output.Play();
                        AudioTiming.Log("9. Playback start", name);
                    }

                    await finished.Task;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TTS:{name}] Playback error: {ex.Message}");
                }
                finally
                {
                    lock (gate) current = null;
                    if (played)
                    {
                        Interlocked.Exchange(ref s_lastPlaybackEndTicks, DateTime.UtcNow.Ticks);
                        Interlocked.Decrement(ref s_playing);
                    }
                    Interlocked.Decrement(ref s_backlog);
                }
            }
        }
    }
}
