using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace WPF_OpenStreetmap_Editor.Services;

/// <summary>内存瓦片缓存：LRU 淘汰策略，同时受条目数和总字节数限制</summary>
public sealed class TileMemoryCache {
    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry> _entries = [];
    private readonly LinkedList<string> _leastRecentlyUsed = [];  // LRU 链表（首 = 最近使用）
    private readonly int _maxEntries;
    private readonly long _maxBytes;
    private long _currentBytes;

    /// <summary>构造函数：指定最大条目数和最大字节数</summary>
    public TileMemoryCache(int maxEntries, long maxBytes) {
        if (maxEntries <= 0) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));

        _maxEntries = maxEntries;
        _maxBytes = maxBytes;
    }

    /// <summary>当前缓存条目数</summary>
    public int Count {
        get { lock (_gate) { return _entries.Count; } }
    }

    /// <summary>当前缓存总字节数</summary>
    public long CurrentBytes {
        get { lock (_gate) { return _currentBytes; } }
    }

    /// <summary>获取缓存项：命中后将该条目移到 LRU 链表头部</summary>
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

    /// <summary>添加或更新缓存项，然后按预算淘汰</summary>
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

    /// <summary>清空缓存</summary>
    public void Clear() {
        lock (_gate) {
            _entries.Clear();
            _leastRecentlyUsed.Clear();
            _currentBytes = 0;
        }
    }

    /// <summary>从 LRU 尾部淘汰，直到条目数和字节数都在预算内</summary>
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

    /// <summary>估算 BitmapSource 占用字节数</summary>
    private static long EstimateBytes(BitmapSource source) {
        var bitsPerPixel = Math.Max(source.Format.BitsPerPixel, 32);
        return (long)source.PixelWidth * source.PixelHeight * bitsPerPixel / 8;
    }

    private sealed record CacheEntry(BitmapSource Source, LinkedListNode<string> Node, long Bytes);
}
