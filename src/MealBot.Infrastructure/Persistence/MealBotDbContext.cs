using MealBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MealBot.Infrastructure.Persistence;

public sealed class MealBotDbContext(DbContextOptions<MealBotDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductAlias> ProductAliases => Set<ProductAlias>();

    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();

    public DbSet<MealPlan> MealPlans => Set<MealPlan>();

    public DbSet<PlannedMeal> PlannedMeals => Set<PlannedMeal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureUser(modelBuilder.Entity<User>());
        ConfigureProduct(modelBuilder.Entity<Product>());
        ConfigureProductAlias(modelBuilder.Entity<ProductAlias>());
        ConfigureInventoryItem(modelBuilder.Entity<InventoryItem>());
        ConfigureMealPlan(modelBuilder.Entity<MealPlan>());
        ConfigurePlannedMeal(modelBuilder.Entity<PlannedMeal>());
    }

    private static void ConfigureUser(EntityTypeBuilder<User> entity)
    {
        entity.ToTable("Users");
        entity.HasKey(user => user.Id);
        entity.Property(user => user.TelegramId).IsRequired();
        entity.HasIndex(user => user.TelegramId).IsUnique();
        entity.Property(user => user.FirstName).HasMaxLength(255);
        entity.Property(user => user.UserName).HasMaxLength(255);
        entity.Property(user => user.CreatedAtUtc).IsRequired();
        entity.Property(user => user.UpdatedAtUtc).IsRequired();
    }

    private static void ConfigureProduct(EntityTypeBuilder<Product> entity)
    {
        entity.ToTable("Products");
        entity.HasKey(product => product.Id);
        entity.Property(product => product.Name).HasMaxLength(100).IsRequired();
        entity.Property(product => product.NormalizedName).HasMaxLength(100).IsRequired();
        entity.HasIndex(product => product.NormalizedName).IsUnique();
        entity.Property(product => product.CreatedAtUtc).IsRequired();
    }

    private static void ConfigureProductAlias(EntityTypeBuilder<ProductAlias> entity)
    {
        entity.ToTable("ProductAliases");
        entity.HasKey(alias => alias.Id);
        entity.Property(alias => alias.Alias).HasMaxLength(100).IsRequired();
        entity.Property(alias => alias.NormalizedAlias).HasMaxLength(100).IsRequired();
        entity.Property(alias => alias.CreatedAtUtc).IsRequired();
        entity.HasIndex(alias => alias.NormalizedAlias).IsUnique();
        entity.HasOne(alias => alias.Product)
            .WithMany(product => product.Aliases)
            .HasForeignKey(alias => alias.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureInventoryItem(EntityTypeBuilder<InventoryItem> entity)
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
    }

    private static void ConfigureMealPlan(EntityTypeBuilder<MealPlan> entity)
    {
        entity.ToTable("MealPlans");
        entity.HasKey(plan => plan.Id);
        entity.Property(plan => plan.StartDate)
            .HasColumnType("date")
            .IsRequired();
        entity.Property(plan => plan.DaysCount).IsRequired();
        entity.Property(plan => plan.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        entity.Property(plan => plan.CreatedAtUtc).IsRequired();
        entity.Property(plan => plan.UpdatedAtUtc).IsRequired();
        entity.HasOne(plan => plan.User)
            .WithMany(user => user.MealPlans)
            .HasForeignKey(plan => plan.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigurePlannedMeal(EntityTypeBuilder<PlannedMeal> entity)
    {
        entity.ToTable("PlannedMeals");
        entity.HasKey(meal => meal.Id);
        entity.Property(meal => meal.MealDate)
            .HasColumnType("date")
            .IsRequired();
        entity.Property(meal => meal.MealType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        entity.Property(meal => meal.Servings).IsRequired();
        entity.Property(meal => meal.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        entity.Property(meal => meal.CreatedAtUtc).IsRequired();
        entity.Property(meal => meal.UpdatedAtUtc).IsRequired();
        entity.HasIndex(meal => new { meal.MealPlanId, meal.MealDate, meal.MealType })
            .IsUnique();
        entity.HasOne(meal => meal.MealPlan)
            .WithMany(plan => plan.PlannedMeals)
            .HasForeignKey(meal => meal.MealPlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
