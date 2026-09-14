namespace Ged.Domain.Tests;

/// <summary>
/// Asserts on the <em>type</em> of the violated rule rather than on its message. Rules are types,
/// so a test never has to parse a string and a message can be reworded without breaking the suite.
/// </summary>
internal static class RuleAssertions
{
    public static TRule ShouldBreak<TRule>(this Action act)
        where TRule : IBusinessRule
    {
        var ex = Should.Throw<BusinessRuleViolationException>(act);
        ex.ViolatedRule.ShouldBeOfType<TRule>();
        return (TRule)ex.ViolatedRule;
    }
}
