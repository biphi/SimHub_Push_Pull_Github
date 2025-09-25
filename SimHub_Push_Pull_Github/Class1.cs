namespace SimHub_Push_Pull_Github
{
    // Placeholder for SimHub plugin entry in the future.
    // For now, exposes a simple API you can call from tests or a host.
    public class Class1
    {
        public DashboardSyncService CreateForPath(string dashboardsPath)
        {
            return new DashboardSyncService(dashboardsPath);
        }
    }
}
