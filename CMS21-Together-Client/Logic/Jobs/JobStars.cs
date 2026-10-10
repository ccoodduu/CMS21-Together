namespace CMS21Together.Logic.Jobs;

// The order tab's star sets PartScript.markImportantPart and only the star clears it again. A starred part that now
// counts as repaired for its job (the job check's IsRepaired) loses the star, whoever mounted or repaired it.
public static class JobStars
{
	public static void Refresh(CarLoader carLoader, PartScript script)
	{
		if (carLoader == null || script == null || !script.markImportantPart || script.IsUnmounted) return;
		int loader = CarLoaderPlaces.Get().GetCarLoaderId(carLoader);
		var job = loader < 0 ? null : Singleton<GameManager>.Instance?.OrderGenerator?.GetJobForCarLoader(loader);
		if (job == null || !script.IsRepaired(job.globalCondition)) return;
		script.markImportantPart = false;
	}
}
