namespace SpireMind.Core.Coop;

/// <summary>
/// A bot copy the co-op relay has talked to, as it last saw it. Character is the last one the
/// bot reported (it forgets its character between runs, the relay doesn't). Playing is "api",
/// "subscription", "none" or null for unknown. Live is false once the copy stopped answering.
/// </summary>
public sealed record BotPeer(int Port, string Session, string? Character, ulong? NetId,
    string? Playing, bool Paused, bool Live);
