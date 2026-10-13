using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace OrderPaymentMicroservice.Data;

public static class PersistenceConflicts
{
    public static bool IsOrderCreationCollision(Exception exception)
    {
        for(Exception? current=exception;current!=null;current=current.InnerException)
            if(current is PostgresException {SqlState:"23505",ConstraintName:"IX_Orders_CustomerId_CreationKeyHash"})return true;
        return false;
    }
    public static bool IsConcurrentWrite(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
            if (current is DbUpdateConcurrencyException || current is PostgresException { SqlState: "40001" or "40P01" })
                return true;
        return false;
    }
}
