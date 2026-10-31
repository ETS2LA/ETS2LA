namespace ETS2LA.Networking.Users;

[Serializable]
public enum UserRole
{
    User,
    Admin,
    Developer,
    Supporter
}

[Serializable]
public enum Region
{
    Global,
    MainlandChina
}

[Serializable]
public class User
{
    public Guid Id { get; set; } = Guid.Empty;
    public string Username { get; set; } = "Anonymous";
    public string JwtToken { get; set; } = string.Empty;
    public DateTime Expiry { get; set; } = DateTime.MinValue;
    public UserRole Role { get; set; } = UserRole.User;
    public Region Region { get; set; } = Region.Global;
    public PaddleCustomer? Paddle { get; set; } = null;
}

// These classes are for paddle based subscriptions. If you're reading this before
// they are implemented, don't worry! They'll only be used server side for server side features.
// We'll inform more about the features we plan on adding to the subscription in the future.
// Again, no self driving or local features!

[Serializable]
public enum SubscriptionStatus
{
    Trialing,
    Active,
    PastDue,
    Paused,
    Canceled
}

[Serializable]
public class Subscription
{
    public string SubscriptionId { get; set; } = string.Empty;
    public SubscriptionStatus SubscriptionStatus { get; set; } = SubscriptionStatus.Trialing;
    public string ProductId { get; set; } = string.Empty;
    public DateTime ScheduledChangeAt { get; set; } = DateTime.MinValue;
    public DateTime UpdatedAt { get; set; } = DateTime.MinValue;
}

[Serializable]
public class PaddleCustomer
{
    public string CustomerId { get; set; } = string.Empty;
    public List<Subscription> Subscriptions { get; set; } = new List<Subscription>();
}