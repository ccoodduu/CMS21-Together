using CMS21Together.Logic.Car.Parts;

namespace CMS21Together.Logic.Car;

public static class CarDrain
{
	public const string TryAgain = "Try again in a moment.";

	public static bool Settled(int loader) => !PartChangeTracker.IsPending(loader) && !PartTransactions.HasOpen(loader);
}
