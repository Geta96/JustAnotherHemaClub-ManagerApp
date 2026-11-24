using Android.App;
using Android.App.Job;
using Android.Content;
using Android.OS;
using JustAnotherHemaClub.Services;
using Microsoft.Extensions.DependencyInjection;

namespace JustAnotherHemaClub.Platforms.Android;

[Service(
    Name = "com.companyname.justanotherhemaclub.NotificationJobService",
    Permission = "android.permission.BIND_JOB_SERVICE",
    Exported = true)]
public sealed class NotificationJobService : JobService
{
    public const int JobId = 1001;
    private static readonly long FifteenMinutesMs = 15L * 60L * 1000L;

    public static void Schedule(Context context)
    {
        try
        {
            var scheduler = (JobScheduler?)context.GetSystemService(Context.JobSchedulerService);
            if (scheduler is null) return;

            // If already scheduled, keep the existing periodic schedule so we don't reset the 15-min timer on every launch.
            if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
            {
                if (scheduler.GetPendingJob(JobId) is not null)
                    return;
            }
            else
            {
                if (scheduler.AllPendingJobs?.Any(j => j.Id == JobId) == true)
                    return;
            }

            var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(NotificationJobService)));
            var builder = new JobInfo.Builder(JobId, component)
                .SetRequiredNetworkType(NetworkType.Any)
                .SetPersisted(false)
                .SetPeriodic(FifteenMinutesMs);

            scheduler.Schedule(builder.Build());
        }
        catch
        {
            // Best-effort scheduling
        }
    }

    public override bool OnStartJob(JobParameters? @params)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var services = IPlatformApplication.Current?.Services;
                if (services is not null)
                {
                    var sync = services.GetService<NotificationSyncService>();
                    if (sync is not null)
                        await sync.CheckAndNotifyAsync();
                }
            }
            catch
            {
                // Ignore transient network/sheets errors during background poll
            }
            finally
            {
                if (@params is not null)
                    JobFinished(@params, wantsReschedule: false);
            }
        });

        // Work continues asynchronously on a background thread
        return true;
    }

    public override bool OnStopJob(JobParameters? @params)
    {
        // Reschedule if the system interrupted the job before completion
        return true;
    }
}
