using System.Runtime.CompilerServices;
using Gambit.Core.Board;

namespace Gambit.Engine.Search;

public enum Bound : byte
{
    None = 0,
    Upper = 1, // fail-low: score is at most this
    Lower = 2, // fail-high: score is at least this
    Exact = 3,
}

/// <summary>Hash table of previously searched positions (16-byte entries, depth-preferred with aging).</summary>
public sealed class TranspositionTable
{
    private struct Entry
    {
        public ulong Key;
        public ushort Move;
        public short Score;
        public short Eval;
        public byte Depth;
        public byte BoundAndAge; // low 2 bits bound, high 6 bits age
    }

    private Entry[] _entries = [];
    private ulong _mask;
    private byte _age;

    public TranspositionTable(int megabytes = 16) => Resize(megabytes);

    public void Resize(int megabytes)
    {
        long bytes = Math.Max(1, megabytes) * 1024L * 1024L;
        long count = 1;
        while (count * 2 * 16 <= bytes) count *= 2;
        _entries = new Entry[count];
        _mask = (ulong)(count - 1);
    }

    public void Clear() => Array.Clear(_entries);

    public void NewSearch() => _age = (byte)((_age + 1) & 63);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Probe(ulong key, int ply, out Move move, out int score, out int eval, out int depth, out Bound bound)
    {
        ref Entry e = ref _entries[key & _mask];
        if (e.Key == key && (e.BoundAndAge & 3) != 0)
        {
            move = Move.FromRaw(e.Move);
            score = FromTt(e.Score, ply);
            eval = e.Eval;
            depth = e.Depth;
            bound = (Bound)(e.BoundAndAge & 3);
            return true;
        }
        move = Move.None;
        score = eval = depth = 0;
        bound = Bound.None;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Store(ulong key, Move move, int score, int eval, int depth, Bound bound, int ply)
    {
        ref Entry e = ref _entries[key & _mask];
        bool sameKey = e.Key == key;
        int existingAge = e.BoundAndAge >> 2;

        // Replace if: empty, different/older position, or not much shallower than what is stored.
        if (!sameKey && existingAge == _age && e.Depth > depth + 3 && (e.BoundAndAge & 3) == (int)Bound.Exact)
            return;

        if (move.IsNone && sameKey) move = Move.FromRaw(e.Move);

        e.Key = key;
        e.Move = move.Raw;
        e.Score = (short)ToTt(score, ply);
        e.Eval = (short)Math.Clamp(eval, short.MinValue, short.MaxValue);
        e.Depth = (byte)Math.Clamp(depth, 0, 255);
        e.BoundAndAge = (byte)((int)bound | (_age << 2));
    }

    /// <summary>Mate scores are stored relative to the node, not the root.</summary>
    private static int ToTt(int score, int ply) =>
        score >= Searcher.MateBound ? score + ply : score <= -Searcher.MateBound ? score - ply : score;

    private static int FromTt(int score, int ply) =>
        score >= Searcher.MateBound ? score - ply : score <= -Searcher.MateBound ? score + ply : score;

    /// <summary>Permille of sampled entries written during the current search (UCI "hashfull").</summary>
    public int HashFull()
    {
        int used = 0, sample = Math.Min(1000, _entries.Length);
        for (int i = 0; i < sample; i++)
            if ((_entries[i].BoundAndAge & 3) != 0 && (_entries[i].BoundAndAge >> 2) == _age) used++;
        return used * 1000 / sample;
    }
}
