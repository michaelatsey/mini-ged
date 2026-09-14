namespace Ged.Domain.Abstractions;

/// <summary>
/// Validates business rules from contexts where no aggregate instance exists yet.
/// </summary>
/// <remarks>
/// <c>AggregateRoot&lt;TId&gt;.CheckRule</c> is an instance method, so a static factory cannot
/// call it before the aggregate is constructed — yet that is exactly where creation-time rules
/// such as a depth limit must run. This helper applies the same rule contract and throws the
/// same exception. It becomes unnecessary once <c>CheckRule</c> is static in MicroKit; see
/// <c>docs/microkit-deviations.md</c>.
/// </remarks>
public static class DomainRules
{
    /// <summary>Throws when the supplied rule is broken.</summary>
    /// <param name="rule">The rule to evaluate.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule"/> is null.</exception>
    /// <exception cref="BusinessRuleViolationException">Thrown when the rule is broken.</exception>
    public static void Check(IBusinessRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        if (rule.IsBroken())
            throw new BusinessRuleViolationException(rule);
    }
}
