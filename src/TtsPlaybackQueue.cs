using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace RSTGameTranslation
{
    /// <summary>
    /// Plays synthesized audio clips one after another, straight from memory. Speak calls return
    /// as soon as their clip is queued, so the next line can be synthesized while the current
    /// one plays. Used by every TTS engine, which also gives one place to know whether any TTS
    /// audio is playing (to keep it out of speech capture) and how far speech is lagging behind
    /// (to speed it up).
    /// Clips are joined into one continuous output stream. Opening a new output device per clip
    /// added ~0.2s of dead air between clips, which piled up into seconds of lag during fast
    /// dialogue. The device is released after a few seconds without audio.
    /// </summary>
    public sealed class TtsPlaybackQueue
    {
        // ---- State shared by all engines ----

        // Every clip is converted to this format, so clips from any engine can share one output
        private const int OutputRate = 48000;
        private static readonly WaveFormat OutputFormat = WaveFormat.CreateIeeeFloatWaveFormat(OutputRate, 2);

        // Audio (in output frames) queued or still playing, across every queue
        private static long s_queuedFrames;
        private static int s_playing;
        private static long s_lastPlaybackEndTicks;

        // Auto speed-up starts once more than this much audio is waiting, then grows linearly
        private const double SpeedUpAfterSeconds = 1.0;
        private const double SpeedStepPerSecond = 0.125;
        public const double MaxAutoSpeedFactor = 1.5;

        static TtsPlaybackQueue()
        {
            DeleteLegacyTempFiles();
        }

        /// <summary>Seconds of TTS audio queued or still to be played, across every queue.</summary>
        public static double QueuedSeconds => Math.Max(0, Interlocked.Read(ref s_queuedFrames)) / (double)OutputRate;

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
        /// Speech-rate multiplier for the next line: 1.0 while little audio is waiting, faster as
        /// the backlog grows, so reading catches up with the game instead of lagging further
        /// behind. Based on seconds of audio waiting rather than clip count: one long clip lags as
        /// much as several short ones.
        /// </summary>
        public static double GetAutoSpeedFactor()
        {
            if (!ConfigManager.Instance.IsTtsAutoSpeedUpEnabled()) return 1.0;
            double lag = QueuedSeconds;
            if (lag <= SpeedUpAfterSeconds) return 1.0;
            double factor = Math.Min(MaxAutoSpeedFactor, 1.0 + SpeedStepPerSecond * (lag - SpeedUpAfterSeconds));
            Console.WriteLine($"[TTS] {lag:F1}s of speech waiting, speeding up next line x{factor:F2}");
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
        // Clips not opened yet. They are opened on the feeder task, never on the audio thread,
        // so decoding (MP3, Media Foundation) cannot stall playback.
        private readonly Queue<Func<WaveStream>> pending = new Queue<Func<WaveStream>>();
        private bool feederRunning;
        private int generation;
        private readonly ClipSequencer sequencer;
        private WaveOutEvent? output;

        public TtsPlaybackQueue(string name)
        {
            this.name = name;
            sequencer = new ClipSequencer(name);
        }

        /// <param name="openClip">Creates the stream to play; invoked on a background thread.</param>
        public void Enqueue(Func<WaveStream> openClip)
        {
            lock (gate)
            {
                pending.Enqueue(openClip);
                if (feederRunning) return;
                feederRunning = true;
            }
            _ = Task.Run(FeedLoop);
        }

        /// <summary>Stop the clip being played and drop everything queued.</summary>
        public void Stop()
        {
            lock (gate)
            {
                pending.Clear();
                generation++;
                sequencer.Clear();
            }
        }

        private void FeedLoop()
        {
            while (true)
            {
                Func<WaveStream> openClip;
                int clipGeneration;
                lock (gate)
                {
                    if (pending.Count == 0)
                    {
                        feederRunning = false;
                        return;
                    }
                    openClip = pending.Dequeue();
                    clipGeneration = generation;
                }

                WaveStream? stream = null;
                try
                {
                    stream = openClip();
                    var clip = Clip.Create(stream);
                    lock (gate)
                    {
                        // Stop() was called while this clip was being opened
                        if (clipGeneration != generation)
                        {
                            stream.Dispose();
                            continue;
                        }
                        sequencer.Add(clip);
                        EnsureOutputPlaying();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[TTS:{name}] Playback error: {ex.Message}");
                    try { stream?.Dispose(); } catch { }
                }
            }
        }

        /// <summary>
        /// Start an output device if none is running, or if the current one has gone idle and is
        /// shutting down. Each device reads through its own session; an old device that is still
        /// draining its last buffer only ever gets silence, so two devices never pull the same
        /// audio. Must be called under <see cref="gate"/>.
        /// </summary>
        private void EnsureOutputPlaying()
        {
            if (output != null && !sequencer.Drained) return;

            int session = sequencer.StartSession();
            var device = new WaveOutEvent { DesiredLatency = 100 };
            device.PlaybackStopped += (_, e) => OnPlaybackStopped(device, e.Exception);
            device.Init(new SessionTap(sequencer, session));
            output = device;
            device.Play();
        }

        private void OnPlaybackStopped(WaveOutEvent device, Exception? error)
        {
            bool restart = false;
            lock (gate)
            {
                if (ReferenceEquals(output, device))
                {
                    output = null;
                    if (error != null)
                    {
                        // A broken device (unplugged, driver error) would fail the same way again;
                        // drop what is queued rather than leave it stuck.
                        Console.WriteLine($"[TTS:{name}] Playback error: {error.Message}");
                        sequencer.Clear();
                    }
                    else
                    {
                        // A clip may have arrived just as the device went idle
                        restart = sequencer.HasAudio;
                    }
                }
                if (restart) EnsureOutputPlaying();
            }
            // Not from inside the device's own stop callback
            _ = Task.Run(device.Dispose);
        }

        // ---- Clips ----

        private sealed class Clip
        {
            public readonly WaveStream Stream;
            public readonly ISampleProvider Samples;
            // Output frames still counted in s_queuedFrames for this clip (an estimate from the
            // stream length, corrected when the clip ends)
            public long RemainingFrames;

            private Clip(WaveStream stream, ISampleProvider samples, long frames)
            {
                Stream = stream;
                Samples = samples;
                RemainingFrames = frames;
            }

            public static Clip Create(WaveStream stream)
            {
                ISampleProvider samples = stream.ToSampleProvider();
                if (samples.WaveFormat.Channels > 2)
                {
                    samples = new MultiChannelToMonoSampleProvider(samples);
                }
                if (samples.WaveFormat.SampleRate != OutputRate)
                {
                    samples = new WdlResamplingSampleProvider(samples, OutputRate);
                }
                if (samples.WaveFormat.Channels == 1)
                {
                    samples = new MonoToStereoSampleProvider(samples);
                }

                long frames = 0;
                try { frames = (long)(stream.TotalTime.TotalSeconds * OutputRate); } catch { }
                frames = Math.Max(0, frames);
                Interlocked.Add(ref s_queuedFrames, frames);
                return new Clip(stream, samples, frames);
            }

            public void Consumed(int frames)
            {
                long counted = Math.Min(frames, RemainingFrames);
                RemainingFrames -= counted;
                Interlocked.Add(ref s_queuedFrames, -counted);
            }

            public void Release()
            {
                Interlocked.Add(ref s_queuedFrames, -RemainingFrames);
                RemainingFrames = 0;
                try { Stream.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// One continuous sample stream made of the queued clips back to back, with silence while
        /// nothing is queued. After <see cref="IdleFramesBeforeStop"/> of silence it ends the
        /// stream, so the output device closes instead of holding the audio endpoint open.
        /// </summary>
        private sealed class ClipSequencer
        {
            private const int IdleFramesBeforeStop = OutputRate * 3;

            private readonly string name;
            private readonly object sync = new object();
            private readonly Queue<Clip> clips = new Queue<Clip>();
            private Clip? current;
            private int session;
            private long idleFrames;

            public ClipSequencer(string name)
            {
                this.name = name;
            }

            /// <summary>True once the current session has ended its stream for being idle.</summary>
            public bool Drained { get; private set; }

            public bool HasAudio
            {
                get { lock (sync) return current != null || clips.Count > 0; }
            }

            public int StartSession()
            {
                lock (sync)
                {
                    session++;
                    idleFrames = 0;
                    Drained = false;
                    return session;
                }
            }

            public void Add(Clip clip)
            {
                lock (sync) clips.Enqueue(clip);
            }

            public void Clear()
            {
                lock (sync)
                {
                    if (current != null) FinishCurrent();
                    while (clips.Count > 0) clips.Dequeue().Release();
                }
            }

            public int Read(int readerSession, float[] buffer, int offset, int count)
            {
                int clipsStarted = 0;
                int result = ReadLocked(readerSession, buffer, offset, count, ref clipsStarted);
                // Logged outside the lock: this runs on the audio thread
                for (int i = 0; i < clipsStarted; i++) AudioTiming.Log("9. Playback start", name);
                return result;
            }

            private int ReadLocked(int readerSession, float[] buffer, int offset, int count, ref int clipsStarted)
            {
                lock (sync)
                {
                    // A device from an earlier session that is still draining
                    if (readerSession != session || Drained) return 0;

                    int channels = OutputFormat.Channels;
                    int written = 0;
                    while (written < count)
                    {
                        if (current == null)
                        {
                            if (clips.Count == 0) break;
                            current = clips.Dequeue();
                            Interlocked.Increment(ref s_playing);
                            clipsStarted++;
                        }

                        int read;
                        try
                        {
                            read = current.Samples.Read(buffer, offset + written, count - written);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[TTS:{name}] Playback error: {ex.Message}");
                            read = 0;
                        }

                        if (read > 0)
                        {
                            written += read;
                            current.Consumed(read / channels);
                        }
                        else
                        {
                            FinishCurrent();
                        }
                    }

                    if (written > 0) idleFrames = 0;
                    if (written < count)
                    {
                        if (written == 0)
                        {
                            idleFrames += count / channels;
                            if (idleFrames >= IdleFramesBeforeStop)
                            {
                                Drained = true;
                                return 0;
                            }
                        }
                        Array.Clear(buffer, offset + written, count - written);
                    }
                    return count;
                }
            }

            private void FinishCurrent()
            {
                current!.Release();
                current = null;
                Interlocked.Exchange(ref s_lastPlaybackEndTicks, DateTime.UtcNow.Ticks);
                Interlocked.Decrement(ref s_playing);
            }
        }

        /// <summary>The sample provider one output device plays: the sequencer, tagged with its session.</summary>
        private sealed class SessionTap : ISampleProvider
        {
            private readonly ClipSequencer sequencer;
            private readonly int session;

            public SessionTap(ClipSequencer sequencer, int session)
            {
                this.sequencer = sequencer;
                this.session = session;
            }

            public WaveFormat WaveFormat => OutputFormat;

            public int Read(float[] buffer, int offset, int count) => sequencer.Read(session, buffer, offset, count);
        }
    }
}
