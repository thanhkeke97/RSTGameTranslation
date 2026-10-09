using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NAudio.Wave;

namespace RSTGameTranslation
{
    /// <summary>
    /// Plays synthesized audio clips one after another, straight from memory. Speak calls return
    /// as soon as their clip is queued, so the next line can be synthesized while the current
    /// one plays — previously ElevenLabs waited for playback to finish before releasing the
    /// caller, leaving a full API round trip of silence between consecutive lines.
    /// </summary>
    public sealed class TtsPlaybackQueue
    {
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
                }
            }
        }
    }
}
