namespace ETS2LA.Notifications;

public class NotificationHandler
{
    private static readonly Lazy<NotificationHandler> _instance = new(() => new NotificationHandler());
    /// <summary>
    ///  This Instance property gives access to the ETS2LA-wide notification handler instance.
    ///  No matter where this is called from, it will always return the same instance.
    /// </summary>
    public static NotificationHandler Current => _instance.Value;

    public List<Notification> ActiveNotifications { get; private set; } = new();

    public event EventHandler<Notification> OnNotificationAdded;
    public event EventHandler<Notification> OnNotificationUpdated;
    public event EventHandler<string> OnNotificationRemoved;

    public NotificationHandler()
    {
        new Thread(NotificationTimeoutThread) { IsBackground = true }.Start();
    }

    public void UpdateNotification(Notification notification)
    {
        var toUpdate = ActiveNotifications.FirstOrDefault(x => x.Id == notification.Id);
        if (toUpdate == null)
            return;

        toUpdate.Title = notification.Title;
        toUpdate.Content = notification.Content;
        toUpdate.Level = notification.Level;
        toUpdate.Progress = notification.Progress;
        toUpdate.IsProgressIndeterminate = notification.IsProgressIndeterminate;
        toUpdate.CloseAfter = notification.CloseAfter;
        toUpdate.ShowCloseButtonAfter = notification.ShowCloseButtonAfter;
        toUpdate.CreatedAt = DateTime.UtcNow;

        OnNotificationUpdated?.Invoke(this, toUpdate);
    }

    public void SendNotification(Notification notification)
    {
        if (ActiveNotifications.Any(x => x.Id == notification.Id))
        {
            UpdateNotification(notification);
            return;
        }

        ActiveNotifications.Add(notification);
        OnNotificationAdded?.Invoke(this, notification);
    }

    public void CloseNotification(string id)
    {
        for (int i = 0; i < ActiveNotifications.Count; i++)
        {
            Notification candidate = ActiveNotifications[i];
            if (candidate.Id != id)
                continue;

            try
            {
                ActiveNotifications.Remove(candidate);
                OnNotificationRemoved?.Invoke(this, id);
               return;
            } catch { return; }
        }
    }

    public void NotificationTimeoutThread()
    {
        while (true)
        {
            Thread.Sleep(1000);

            var now = DateTime.UtcNow;
            var toRemove = new List<string>();

            foreach (var notification in ActiveNotifications)
            {
                if (notification.CloseAfter > 0 && (now - notification.CreatedAt).TotalSeconds >= notification.CloseAfter)
                {
                    toRemove.Add(notification.Id);
                }
            }

            foreach (var id in toRemove)
            {
                CloseNotification(id);
            }
        }
    }

    public List<Notification> GetActiveNotifications()
    {
        return ActiveNotifications;
    }
}
