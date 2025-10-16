using Microsoft.EntityFrameworkCore;
using TodoApi.Models;

namespace TodoApi.Data;

public class TodoContext : DbContext
{
    public TodoContext(DbContextOptions<TodoContext> options)
        : base(options) { }

    public DbSet<TodoList> TodoList { get; set; } = default!;
    public DbSet<TodoItem> TodoItems { get; set; } = default!;
    public DbSet<DeletedEntity> DeletedEntities { get; set; } = default!;

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified);

        foreach (var entry in entries)
        {
            var now = DateTime.UtcNow;

            switch (entry.State)
            {
                case EntityState.Added when entry.Entity is TodoList list:
                {
                    if (list.CreatedAt == default)
                    {
                        list.CreatedAt = now;
                    }
                    
                    if (list.UpdatedAt == default)
                    {
                        list.UpdatedAt = now;
                    }

                    break;
                }
                case EntityState.Added:
                {
                    if (entry.Entity is TodoItem item)
                    {
                        if (item.CreatedAt == default)
                        {
                            item.CreatedAt = now;
                        }
                        
                        if (item.UpdatedAt == default)
                        {
                            item.UpdatedAt = now;
                        }
                    }

                    break;
                }
                case EntityState.Modified when entry.Entity is TodoList list:
                {
                    if (list.UpdatedAt == default || list.LastSyncedAt == null)
                    {
                        list.UpdatedAt = now;
                    }

                    break;
                }
                case EntityState.Modified:
                {
                    if (entry.Entity is TodoItem item)
                    {
                        if (item.UpdatedAt == default || item.LastSyncedAt == null)
                        {
                            item.UpdatedAt = now;
                        }
                    }

                    break;
                }
            }
        }
    }
}