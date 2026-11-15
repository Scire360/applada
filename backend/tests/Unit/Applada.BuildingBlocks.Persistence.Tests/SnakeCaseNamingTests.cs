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

    [Fact]
    public void Owned_type_shares_conventional_owner_table()
    {
        var order = Entity<Order>();
        var owned = order.FindNavigation(nameof(Order.ShippingAddress))!.TargetEntityType;

        Assert.Equal("orders", order.GetTableName());
        Assert.Equal("orders", owned.GetTableName());
    }

    [Fact]
    public void Owned_type_stays_in_explicit_owner_table()
    {
        var owner = Entity<LegacyOwner>();
        var owned = owner.FindNavigation(nameof(LegacyOwner.Address))!.TargetEntityType;
        var table = StoreObjectIdentifier.Table("LegacyOwners");

        Assert.Equal("LegacyOwners", owner.GetTableName());
        Assert.Equal("LegacyOwners", owned.GetTableName());
        Assert.Equal("address_street_name", owned.FindProperty(nameof(Address.StreetName))!.GetColumnName(table));
    }

    [Fact]
    public void Derived_tph_type_stays_in_explicit_root_table()
    {
        var root = Entity<Animal>();
        var derived = Entity<Dog>();

        Assert.Equal("LegacyAnimals", root.GetTableName());
        Assert.Equal("LegacyAnimals", derived.GetTableName());
        Assert.Equal("breed", derived.FindProperty(nameof(Dog.Breed))!.GetColumnName(StoreObjectIdentifier.Table("LegacyAnimals")));
    }

    [Fact]
    public void Derived_tpt_type_gets_own_snake_case_table()
    {
        var root = Entity<Vehicle>();
        var derived = Entity<Car>();

        Assert.Equal("vehicles", root.GetTableName());
        Assert.Equal("car", derived.GetTableName());
        Assert.Equal("door_count", derived.FindProperty(nameof(Car.DoorCount))!.GetColumnName(StoreObjectIdentifier.Table("car")));
    }

    [Fact]
    public void Derived_tpc_type_gets_own_snake_case_table()
    {
        var root = Entity<Shape>();
        var derived = Entity<Circle>();

        Assert.Equal("shapes", root.GetTableName());
        Assert.Equal("circle", derived.GetTableName());
        Assert.Equal("radius_length", derived.FindProperty(nameof(Circle.RadiusLength))!.GetColumnName(StoreObjectIdentifier.Table("circle")));
    }

    [Fact]
    public void Schema_is_left_intact()
    {
        using var schemaContext = new SchemaContext();
        var conventional = schemaContext.Model.FindEntityType(typeof(SampleEntity))!;
        var explicitSchema = Entity<SchemaEntity>();

        Assert.Equal("matches", conventional.GetSchema());
        Assert.Equal("sample_entities", conventional.GetTableName());
        Assert.Equal("identity", explicitSchema.GetSchema());
        Assert.Equal("SchemaEntities", explicitSchema.GetTableName());
    }

    [Fact]
    public void Explicit_key_and_index_names_are_kept()
    {
        var entity = Entity<SchemaEntity>();

        Assert.Equal("PK_Custom", entity.FindPrimaryKey()!.GetName());
        Assert.Equal("IX_Custom", entity.GetIndexes().Single().GetDatabaseName());
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
        public DbSet<LegacyOwner> LegacyOwners => Set<LegacyOwner>();
        public DbSet<Animal> Animals => Set<Animal>();
        public DbSet<SchemaEntity> SchemaEntities => Set<SchemaEntity>();
        public DbSet<Vehicle> Vehicles => Set<Vehicle>();
        public DbSet<Shape> Shapes => Set<Shape>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseNpgsql("Host=localhost;Database=unused");

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.UseSnakeCaseNames();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Order>().OwnsOne(o => o.ShippingAddress);
            modelBuilder.Entity<LegacyOwner>(b =>
            {
                b.ToTable("LegacyOwners");
                b.OwnsOne(o => o.Address);
            });
            modelBuilder.Entity<Animal>(b =>
            {
                b.ToTable("LegacyAnimals");
                b.HasDiscriminator<string>("Kind").HasValue<Animal>("animal").HasValue<Dog>("dog");
            });
            modelBuilder.Entity<Dog>();
            modelBuilder.Entity<Vehicle>().UseTptMappingStrategy();
            modelBuilder.Entity<Car>();
            modelBuilder.Entity<Shape>().UseTpcMappingStrategy();
            modelBuilder.Entity<Circle>();
            modelBuilder.Entity<SchemaEntity>(b =>
            {
                b.ToTable("SchemaEntities", "identity");
                b.HasKey(e => e.Id).HasName("PK_Custom");
                b.HasIndex(e => e.Code).HasDatabaseName("IX_Custom");
            });
            modelBuilder.Entity<LegacyEntity>(b =>
            {
                b.ToTable("LegacyTable");
                b.Property(e => e.SomeValue).HasColumnName("LegacyCol");
            });
        }
    }

    private sealed class SchemaContext : DbContext
    {
        public DbSet<SampleEntity> SampleEntities => Set<SampleEntity>();

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseNpgsql("Host=localhost;Database=unused");

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
            configurationBuilder.UseSnakeCaseNames();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.HasDefaultSchema("matches");
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

    private sealed class LegacyOwner
    {
        public Guid Id { get; set; }
        public Address Address { get; set; } = new();
    }

    private class Animal
    {
        public Guid Id { get; set; }
    }

    private sealed class Dog : Animal
    {
        public string Breed { get; set; } = "";
    }

    private class Vehicle
    {
        public Guid Id { get; set; }
    }

    private sealed class Car : Vehicle
    {
        public int DoorCount { get; set; }
    }

    private class Shape
    {
        public Guid Id { get; set; }
    }

    private sealed class Circle : Shape
    {
        public int RadiusLength { get; set; }
    }

    private sealed class SchemaEntity
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = "";
    }

    private sealed class LegacyEntity
    {
        public Guid Id { get; set; }
        public int SomeValue { get; set; }
        public int OtherValue { get; set; }
    }
}
