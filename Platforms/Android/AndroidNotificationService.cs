using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using JustAnotherHemaClub.Services;

namespace JustAnotherHemaClub.Platforms.Android;

public sealed class AndroidNotificationService : INotificationService
{
    private const string ChannelId = "jahc_general";

    public void Show(int id, string title, string message)
    {
        var context = global::Android.App.Application.Context;
        var manager = (NotificationManager?)context.GetSystemService(Context.NotificationService);
        if (manager is null) return;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(ChannelId, "JAHC Notifications", NotificationImportance.Default);
            manager.CreateNotificationChannel(channel);
        }

        var intent = new Intent(context, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        intent.PutExtra(MainActivity.NavigateToHomeExtra, true);

        var pendingFlags = Build.VERSION.SdkInt >= BuildVersionCodes.M
            ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
            : PendingIntentFlags.UpdateCurrent;
        var pendingIntent = PendingIntent.GetActivity(context, id, intent, pendingFlags);

        var builder = new NotificationCompat.Builder(context, ChannelId)
            .SetSmallIcon(Resource.Mipmap.appicon2)
            .SetContentTitle(title)
            .SetContentText(message)
            .SetContentIntent(pendingIntent)
            .SetAutoCancel(true)
            .SetPriority(NotificationCompat.PriorityDefault);

        manager.Notify(id, builder.Build());
    }
}