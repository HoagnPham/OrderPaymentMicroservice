using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderPaymentMicroservice.Data;
using Xunit;

namespace OrderPaymentMicroservice.Tests;

public sealed class PersistenceConflictTests
{
    [Theory]
    [InlineData("40001", true)] [InlineData("40P01", true)]
    [InlineData("23514", false)] [InlineData("23505", false)]
    public void DetectsWrappedPostgresConcurrencyWithoutMisclassifyingOtherConstraints(string state, bool expected)
    {
        var postgres = new PostgresException("Test diagnostic", "ERROR", "ERROR", state);
        var wrapped = new InvalidOperationException("Execution strategy wrapper", new DbUpdateException("SaveChanges", postgres));
        Assert.Equal(expected, PersistenceConflicts.IsConcurrentWrite(wrapped));
    }
}
