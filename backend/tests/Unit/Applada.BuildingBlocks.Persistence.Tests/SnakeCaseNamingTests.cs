using Applada.BuildingBlocks.Persistence;
using Applada.BuildingBlocks.Persistence.Naming;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Applada.BuildingBlocks.Persistence.Tests;

public sealed class SnakeCaseNamingTests
{
    [Theory]
    [InlineData("ConfirmedCount", "confirmed_count")]
    [InlineData("Id", "id")]
    [InlineData("OutboxMessages", "outbox_messages")]
    [InlineData("HTTPStatus", "http_status")]
    [InlineData("UserId2", "user_id2")]
    [InlineData("confirmed_count", "confirmed_count")]
    [InlineData("PK_SampleEntities", "pk_sample_entities")]
    [InlineData("Address_Street", "address_street")]
    public void ToSnakeCase_converts_names(string input, string expected) =>
        Assert.Equal(expected, SnakeCaseNamingConvention.ToSnakeCase(input));

    [Fact]
    public void Model_uses_snake_case()
    {
        var entity = Entity<SampleEntity>();
        var table = StoreObjectIdentifier.Table("sample_entities");

        Assert.Equal("sample_entities", entity.GetTableName());
        Assert.Equal("confirmed_count", entity.FindProperty(nameof(SampleEntity.ConfirmedCount))!.GetColumnName(table));
        Assert.Equal("id", entity.FindProperty(nameof(SampleEntity.Id))!.GetColumnName(table));
    }

    [Fact]
    public void Model_uses_snake_case_for_keys_foreign_keys_and_indexes()
    {
        var line = Entity<OrderLine>();

        Assert.Equal("order_lines", line.GetTableName());
        Assert.Equal("pk_order_lines", line.FindPrimaryKey()!.GetName());
        Assert.Equal("fk_order_lines_orders_order_id", line.GetForeignKeys().Single().GetConstraintName());
        Assert.Equal("ix_order_lines_order_id", line.GetIndexes().Single().GetDatabaseName());
    }

    [Fact]
    public void Model_uses_snake_case_for_owned_type_columns()
    {
        var order = Entity<Order>();
        var table = StoreObjectIdentifier.Create(order, StoreObjectType.Table)!.Value;

        var street = order.FindNavigation(nameof(Order.ShippingAddress))!.TargetEntityType
            .FindProperty(nameof(Address.StreetName))!;

        Assert.Equal("shipping_address_street_name", street.GetColumnName(table));
    }

    [Fact]
    public void Model_keeps_explicit_table_and_column_names()
    {
        var entity = Entity<LegacyEntity>();
        var table = StoreObjectIdentifier.Table("LegacyTable");

        Assert.Equal("LegacyTable", entity.GetTableName());
        Assert.Equal("LegacyCol", entity.FindProperty(nameof(LegacyEntity.SomeValue))!.GetColumnName(table));
        Assert.Equal("other_value", entity.FindProperty(nameof(LegacyEntity.OtherValue))!.GetColumnName(table));
    }

    private static IEntityType Entity<T>()
    {
        using var context = new SampleContext();
        return context.Model.FindEntityType(typeof(T))!;
    }

    private sealed class SampleContext : DbContext
    {
        public DbSet<SampleEntity> SampleEntities => Set<SampleEntity>();
        public DbSet<Order> Orders => Set<Order>();
        public DbSet<OrderLine> OrderLines => Set<OrderLine>();
        public DbSet<LegacyEntity> Legacy => Set<LegacyEntity>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseNpgsql("Host=localhost;Database=unused");

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.UseSnakeCaseNames();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().OwnsOne(o => o.ShippingAddress);
            modelBuilder.Entity<LegacyEntity>(b =>
            {
                b.ToTable("LegacyTable");
                b.Property(e => e.SomeValue).HasColumnName("LegacyCol");
            });
        }
    }

    private sealed class SampleEntity
    {
        public Guid Id { get; set; }
        public int ConfirmedCount { get; set; }
    }

    private sealed class Order
    {
        public Guid Id { get; set; }
        public Address ShippingAddress { get; set; } = new();
    }

    private sealed class Address
    {
        public string StreetName { get; set; } = "";
    }

    private sealed class OrderLine
    {
        public Guid Id { get; set; }
        public Guid OrderId { get; set; }
        public Order? Order { get; set; }
    }

    private sealed class LegacyEntity
    {
        public Guid Id { get; set; }
        public int SomeValue { get; set; }
        public int OtherValue { get; set; }
    }
}
