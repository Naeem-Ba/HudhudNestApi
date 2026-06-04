using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
// SaveChanges belongs to Unit of Work — NOT to individual repositories.
// This prevents the anti-pattern of calling repo.SaveChangesAsync()
// mid-operation before all changes are complete.

namespace PropertyApi.Application.Common.Interfaces;

public interface IUnitOfWork : IDisposable
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task BeginTransactionAsync();
    Task CommitTransactionAsync();
    Task RollbackTransactionAsync();
}
