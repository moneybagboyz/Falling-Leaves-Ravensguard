using System.Collections.Concurrent;
using Ravensguard.Sim.Core;
using Ravensguard.Sim.Economy;
using Ravensguard.Sim.People;
using Ravensguard.Sim.People.Vital;

namespace Ravensguard.Sim.Abstract;

/// <summary>What the live tier or the UI asks of the economy records (abstract spec 2.2). Handlers for the market kinds are registered by the markets system.</summary>
public enum CommandKind : byte { Buy, Sell, PayCoin, GiveStock, TakeStock, HireWorker, PostOrder, CaravanLoss, PersonDied, ItemMaterialise, ItemDematerialise }

/// <summary>
/// One queued request. <c>(Frame, Seq)</c> orders commands: the pump executes in ascending order whatever order they were enqueued in (G-A6).
/// For <see cref="CommandKind.PayCoin"/>: A = mites, Actor pays Target. For <see cref="CommandKind.PersonDied"/>: Target = the person, A = a <see cref="DeathCause"/>.
/// </summary>
public readonly record struct EconCommand(long Frame, int Seq, CommandKind Kind, EntityId Actor, EntityId Target, long A = 0, long B = 0, int Good = 0, byte Tier = 0);

public enum CommandError : byte { None, NoHandler, UnknownRecord, NotAlive, Insufficient, Invalid }

/// <summary>The outcome of a command: ok or the reason not, the totals it actually moved and the hour it executed at.</summary>
public readonly record struct CommandResult(bool Ok, CommandError Error, long TickKey, long ActualA = 0, long ActualB = 0)
{
    public static CommandResult Success(long tick, long a = 0, long b = 0) => new(true, CommandError.None, tick, a, b);
    public static CommandResult Fail(CommandError e, long tick) => new(false, e, tick);
}

/// <summary>What a handler gets: the world to mutate, the outbox to report into and the hour the command is applied at.</summary>
public sealed class CommandContext(SimWorld world, RegionOutbox outbox, long tickKey)
{
    public SimWorld World { get; } = world;
    public RegionOutbox Outbox { get; } = outbox;
    public long TickKey { get; } = tickKey;
    public GameTime Time => GameTime.FromGameHours(TickKey);
}

public delegate CommandResult CommandHandler(in EconCommand command, CommandContext ctx);

/// <summary>An executed command with its result, kept in order for replay and tests.</summary>
public sealed record ExecutedCommand(long TickKey, EconCommand Command, CommandResult Result);

/// <summary>The thread-safe queue the live tier writes to (abstract spec 3.1); drained only at a barrier or between hours.</summary>
public sealed class CommandQueue
{
    private readonly ConcurrentQueue<EconCommand> _q = new();
    private int _seq;

    public int Pending => _q.Count;

    public void Enqueue(EconCommand c) => _q.Enqueue(c);

    /// <summary>Enqueues with the next sequence number of this queue (for callers that have no sequence of their own).</summary>
    public EconCommand Enqueue(long frame, CommandKind kind, EntityId actor, EntityId target, long a = 0, long b = 0, int good = 0, byte tier = 0)
    {
        var c = new EconCommand(frame, Interlocked.Increment(ref _seq), kind, actor, target, a, b, good, tier);
        _q.Enqueue(c);
        return c;
    }

    /// <summary>Takes everything queued so far, stable-sorted by (Frame, Seq).</summary>
    public List<EconCommand> DrainAll()
    {
        var l = new List<EconCommand>();
        while (_q.TryDequeue(out var c)) l.Add(c);
        return l.OrderBy(c => c.Frame).ThenBy(c => c.Seq).ToList();
    }
}

/// <summary>Executes commands against the world through registered handlers; the built-in kinds are <see cref="CommandKind.PayCoin"/> and <see cref="CommandKind.PersonDied"/>.</summary>
public sealed class CommandPump
{
    private readonly Dictionary<CommandKind, CommandHandler> _handlers = [];

    public CommandPump()
    {
        Register(CommandKind.PayCoin, PayCoin);
        Register(CommandKind.PersonDied, PersonDied);
    }

    public void Register(CommandKind kind, CommandHandler handler) => _handlers[kind] = handler;

    public CommandResult Execute(in EconCommand c, CommandContext ctx)
        => _handlers.TryGetValue(c.Kind, out var h) ? h(c, ctx) : CommandResult.Fail(CommandError.NoHandler, ctx.TickKey);

    private static bool TryPurse(EconomyState e, EntityId owner, out PurseId purse)
    {
        var kind = owner.Kind == IdKind.Household ? PurseKind.Household : PurseKind.Person;
        return e.Ledger.TryFind(kind, owner, out purse);
    }

    private static CommandResult PayCoin(in EconCommand c, CommandContext ctx)
    {
        var e = ctx.World.Economy;
        if (c.A <= 0) return CommandResult.Fail(CommandError.Invalid, ctx.TickKey);
        if (!TryPurse(e, c.Actor, out var from) || !TryPurse(e, c.Target, out var to) || from == to) return CommandResult.Fail(CommandError.UnknownRecord, ctx.TickKey);
        return e.Ledger.Transfer(from, to, Coin.FromMites(c.A), TransferReason.Gift) ? CommandResult.Success(ctx.TickKey, c.A) : CommandResult.Fail(CommandError.Insufficient, ctx.TickKey);
    }

    private static CommandResult PersonDied(in EconCommand c, CommandContext ctx)
    {
        var w = ctx.World;
        int p = w.People.IndexOf(c.Target);
        if (p < 0) return CommandResult.Fail(CommandError.UnknownRecord, ctx.TickKey);
        if (w.People.Status[p] != PersonStatus.Alive) return CommandResult.Fail(CommandError.NotAlive, ctx.TickKey);
        var cause = Enum.IsDefined((DeathCause)c.A) && c.A != 0 ? (DeathCause)c.A : DeathCause.Violence;
        // B: the live tier's exposure death (1 cold, 2 heat), else a death of the live tier
        string note = c.B switch { 1 => "cold: caught out of doors in the freezing weather", 2 => "heat: collapsed in the heat", _ => "killed in the live tier" };
        Demography.Kill(w, p, cause, ctx.Time, ctx.Outbox, note);
        return CommandResult.Success(ctx.TickKey);
    }
}
