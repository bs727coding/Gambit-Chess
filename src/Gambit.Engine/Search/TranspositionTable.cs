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

/// <summary>
/// Hash table of previously searched positions (16-byte entries, depth-preferred with aging). Safe to
/// share between searcher threads without locks: each entry stores its data in one 64-bit word and
/// the key XOR the data in the other, so a half-written entry (two threads writing at once) fails
/// the key check and reads as a miss.
/// </summary>
public sealed class TranspositionTable
{
    private struct Entry
    {
        public ulong Check; // key ^ Data
        public ulong Data;  // move (16) | score (16) | eval (16) | depth (8) | bound + age (8)
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
        ulong data = e.Data;
        if ((e.Check ^ data) == key && (BoundAndAge(data) & 3) != 0)
        {
            move = Move.FromRaw((ushort)data);
            score = FromTt((short)(data >> 16), ply);
            eval = (short)(data >> 32);
            depth = (byte)(data >> 48);
            bound = (Bound)(BoundAndAge(data) & 3);
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
        ulong old = e.Data;
        bool sameKey = (e.Check ^ old) == key;
        int oldBoundAndAge = BoundAndAge(old);

        // Replace if: empty, different/older position, or not much shallower than what is stored.
        if (!sameKey && oldBoundAndAge >> 2 == _age && (byte)(old >> 48) > depth + 3 && (oldBoundAndAge & 3) == (int)Bound.Exact)
            return;

        if (move.IsNone && sameKey) move = Move.FromRaw((ushort)old);

        ulong data = move.Raw
            | (ulong)(ushort)(short)ToTt(score, ply) << 16
            | (ulong)(ushort)(short)Math.Clamp(eval, short.MinValue, short.MaxValue) << 32
            | (ulong)(byte)Math.Clamp(depth, 0, 255) << 48
            | (ulong)(byte)((int)bound | (_age << 2)) << 56;
        e.Data = data;
        e.Check = key ^ data;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int BoundAndAge(ulong data) => (int)(data >> 56);

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
        {
            int boundAndAge = BoundAndAge(_entries[i].Data);
            if ((boundAndAge & 3) != 0 && boundAndAge >> 2 == _age) used++;
        }
        return used * 1000 / sample;
    }
}
