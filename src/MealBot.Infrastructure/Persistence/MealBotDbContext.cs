using MealBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace MealBot.Infrastructure.Persistence;

public sealed class MealBotDbContext(DbContextOptions<MealBotDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.TelegramId).IsRequired();
            entity.HasIndex(item => item.TelegramId).IsUnique();
            entity.Property(item => item.FirstName).HasMaxLength(255);
            entity.Property(item => item.UserName).HasMaxLength(255);
            entity.Property(item => item.CreatedAtUtc).IsRequired();
            entity.Property(item => item.UpdatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Products");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(100).IsRequired();
            entity.Property(item => item.NormalizedName).HasMaxLength(100).IsRequired();
            entity.HasIndex(item => item.NormalizedName).IsUnique();
            entity.Property(item => item.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<InventoryItem>(entity =>
        {
            entity.ToTable("InventoryItems");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Quantity).HasPrecision(18, 3).IsRequired();
            entity.Property(item => item.ReservedQuantity).HasPrecision(18, 3).IsRequired();
            entity.Property(item => item.Unit)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(item => item.CreatedAtUtc).IsRequired();
            entity.Property(item => item.UpdatedAtUtc).IsRequired();
            entity.HasIndex(item => new { item.UserId, item.ProductId, item.Unit }).IsUnique();
            entity.HasOne(item => item.User)
                .WithMany(user => user.InventoryItems)
                .HasForeignKey(item => item.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Product)
                .WithMany(product => product.InventoryItems)
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
