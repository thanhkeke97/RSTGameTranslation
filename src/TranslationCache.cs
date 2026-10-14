using System;
using System.Collections.Generic;

namespace RSTGameTranslation
{
    /// <summary>
    /// In-memory LRU cache of translations, so text seen before (menus, repeated lines, text that
    /// flickers out and back in) is shown instantly instead of going through the translation service.
    /// Entries belong to one context (service, model, languages, prompt, game info); when the context
    /// changes the cache is emptied, so a translation is only reused under the settings that produced it.
    /// </summary>
    public sealed class TranslationCache
    {
        public static TranslationCache Instance { get; } = new TranslationCache(1000);

        private readonly int _capacity;
        private readonly Dictionary<string, LinkedListNode<(string Text, string Translation)>> _entries = new();
        private readonly LinkedList<(string Text, string Translation)> _recency = new(); // most recent first
        private readonly object _lock = new object();
        private string _context = "";

        private TranslationCache(int capacity)
        {
            _capacity = capacity;
        }

        // Switch to the given context, dropping entries made under another one
        public void UseContext(string context)
        {
            lock (_lock)
            {
                if (context == _context)
                    return;
                _context = context;
                _entries.Clear();
                _recency.Clear();
            }
        }

        public bool TryGet(string text, out string translation)
        {
            lock (_lock)
            {
                if (_entries.TryGetValue(text, out var node))
                {
                    _recency.Remove(node);
                    _recency.AddFirst(node);
                    translation = node.Value.Translation;
                    return true;
                }
            }
            translation = "";
            return false;
        }

        public void Set(string text, string translation)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(translation))
                return;

            lock (_lock)
            {
                if (_entries.TryGetValue(text, out var existing))
                {
                    _recency.Remove(existing);
                }
                else if (_entries.Count >= _capacity && _recency.Last != null)
                {
                    _entries.Remove(_recency.Last.Value.Text);
                    _recency.RemoveLast();
                }

                _entries[text] = _recency.AddFirst((text, translation));
            }
        }

        // Store only if the cache is still on the given context, for results that arrive late
        // (a prefetch started before the user changed the language, model, ...)
        public void SetIfContext(string context, string text, string translation)
        {
            lock (_lock)
            {
                if (context != _context)
                    return;
            }
            Set(text, translation);
        }

        public string Context
        {
            get { lock (_lock) return _context; }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _entries.Clear();
                _recency.Clear();
            }
        }
    }
}
