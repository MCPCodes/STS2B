namespace SpireMind.Arena;

/// <summary>
/// The events of an Overgrowth act that the arena has not built yet, each left out by name
/// with the part of the run-mode plan that builds it. An event that is left out never comes
/// up: the act's list walks past it (<see cref="EventList"/>), and a strict run that would
/// meet it ends as unrepresentable.
/// <para>
/// Whoever builds one of these registers it (<c>r.Event&lt;ThisOrThat&gt;()</c> in the content
/// file of its chunk) and takes its line out of this file: the registry fails the self-check
/// on a name that is both registered and left out.
/// </para>
/// </summary>
public sealed class EventsLeftOut : IContentModule
{
    public void Register(Registry r)
    {
        void LeaveOut(string reason, params string[] events)
        {
            foreach (var name in events) r.Exclude(ContentKind.Event, name, reason);
        }

        LeaveOut("not built yet: chunk 24 of the run-mode plan (events-b)",
            "LuminousChoir", "Wellspring", "WhisperingHollow", "RoomFullOfCheese", "SlipperyBridge", "TeaMaster", "TheLegendsWereTrue");
        LeaveOut("not built yet: needs the whole colorless card pool; chunk 28 of the run-mode plan (colorless-c-and-users)",
            "BrainLeech");
        LeaveOut("not built yet: needs enchantments; chunk 32 of the run-mode plan (enchantment-users)",
            "SapphireSeed", "WoodCarvings", "SelfHelpBook");
        LeaveOut("not built yet: needs a fight on top of an event room; chunk 34 of the run-mode plan (events-c)",
            "DenseVegetation");
        LeaveOut("not built yet: its options are made from the potions held, each for a card reward of upgraded cards; chunk 34 of the run-mode plan (events-c)",
            "TheFutureOfPotions");
        LeaveOut("not built yet: needs pets and the rest site option Hatch; chunk 35 of the run-mode plan (pets-and-byrdonis)",
            "ByrdonisNest");
    }
}
