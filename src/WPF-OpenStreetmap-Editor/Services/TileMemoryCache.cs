using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace WPF_OpenStreetmap_Editor.Services;

public sealed class TileMemoryCache {
    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry> _entries = [];
    private readonly LinkedList<string> _leastRecentlyUsed = [];
    private readonly int _maxEntries;
    private readonly long _maxBytes;
    private long _currentBytes;

    public TileMemoryCache(int maxEntries, long maxBytes) {
        if (maxEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));

        _maxEntries = maxEntries;
        _maxBytes = maxBytes;
    }

    public int Count {
        get {
            lock (_gate) {
                return _entries.Count;
            }
        }
    }

    public long CurrentBytes {
        get {
            lock (_gate) {
                return _currentBytes;
            }
        }
    }

    public bool TryGetValue(string key, out BitmapSource source) {
        lock (_gate) {
            if (_entries.TryGetValue(key, out var entry)) {
                _leastRecentlyUsed.Remove(entry.Node);
                _leastRecentlyUsed.AddFirst(entry.Node);
                source = entry.Source;
                return true;
            }
        }

        source = null!;
        return false;
    }

    public void Add(string key, BitmapSource source) {
        var bytes = EstimateBytes(source);

        lock (_gate) {
            if (_entries.Remove(key, out var existing)) {
                _leastRecentlyUsed.Remove(existing.Node);
                _currentBytes -= existing.Bytes;
            }

            var node = _leastRecentlyUsed.AddFirst(key);
            _entries[key] = new CacheEntry(source, node, bytes);
            _currentBytes += bytes;
            TrimToBudget();
        }
    }

    public void Clear() {
        lock (_gate) {
            _entries.Clear();
            _leastRecentlyUsed.Clear();
            _currentBytes = 0;
        }
    }

    private void TrimToBudget() {
        while (_entries.Count > _maxEntries || _currentBytes > _maxBytes) {
            var node = _leastRecentlyUsed.Last;
            if (node is null) return;

            var key = node.Value;
            _leastRecentlyUsed.RemoveLast();
            if (_entries.Remove(key, out var entry)) {
                _currentBytes -= entry.Bytes;
            }
        }
    }

    private static long EstimateBytes(BitmapSource source) {
        var bitsPerPixel = Math.Max(source.Format.BitsPerPixel, 32);
        return (long)source.PixelWidth * source.PixelHeight * bitsPerPixel / 8;
    }

    private sealed record CacheEntry(BitmapSource Source, LinkedListNode<string> Node, long Bytes);
}
