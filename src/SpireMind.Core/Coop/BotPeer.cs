namespace SpireMind.Core.Coop;

/// <summary>
/// A bot copy the co-op relay has talked to, as it last saw it. Character is the last one the
/// bot reported (it forgets its character between runs, the relay doesn't). Playing is "api",
/// "subscription", "none" or null for unknown. Live is false once the copy stopped answering.
/// Label is what the human's chat calls it ("Ironclad 2"), Status where it last said it stands
/// in co-op (null when it doesn't say), and Thinking whether it was deciding at the last poll.
/// </summary>
public sealed record BotPeer(int Port, string Session, string? Character, ulong? NetId,
    string? Playing, bool Paused, bool Live, string? Label = null, BotStatus? Status = null, bool Thinking = false);
