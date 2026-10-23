namespace Labaik.Domain;

public sealed record RuleResult(bool IsSuccess, string? Reason)
{
    public static RuleResult Allow() => new(true, null);
    public static RuleResult Deny(string reason) => new(false, reason);
}
