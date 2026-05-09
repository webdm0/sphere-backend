namespace SphereBackend.Features.Cards
{
    internal static class CardValidationRules
    {
        public const int MaxContentLength = 4000;
        public const string ContentTooLongMessage = "Content must be 4000 characters or fewer.";
        public const string InvalidPriorityMessage = "Priority must be one of: low, medium, high, critical.";

        private static readonly HashSet<string> AllowedPriorities = new(StringComparer.OrdinalIgnoreCase)
        {
            "low",
            "medium",
            "high",
            "critical"
        };

        public static bool IsAllowedPriority(string value)
        {
            return AllowedPriorities.Contains(value);
        }
    }
}
