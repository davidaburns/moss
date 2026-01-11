namespace Moss.Database.Migrations;

using FluentMigrator;

[Migration(20260110748, "Initial migration")]
public class InitialMigration : Migration {
    public override void Up() {
        Create.Table("users")
            .WithColumn("id").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("username").AsString(255).NotNullable().Unique()
            .WithColumn("email").AsString(255).NotNullable().Unique()
            .WithColumn("first_name").AsString(255).Nullable()
            .WithColumn("last_name").AsString(255).Nullable()
            .WithColumn("locked").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable().WithDefault(SystemMethods.CurrentDateTimeOffset)
            .WithColumn("created_by").AsString(255).NotNullable().WithDefaultValue("[system]")
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsString(255).Nullable()
            .WithColumn("deleted").AsBoolean().NotNullable().WithDefaultValue(false);

        Create.Index("idx_users_username").OnTable("users").OnColumn("username");
        Create.Index("idx_users_email").OnTable("users").OnColumn("email");

        Create.Table("equipment_groups")
            .WithColumn("id").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("name").AsString(255).NotNullable();

        Insert.IntoTable("equipment_groups").Rows([
            new {name="Facility"},
            new {name="Area"},
            new {name="Production Line"},
            new {name="Workcenter"},
            new {name="Workstation"}
        ]);

        Create.Table("equipment")
            .WithColumn("id").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("name").AsString(1024).NotNullable()
            .WithColumn("parent_id").AsInt32().Nullable()
            .WithColumn("declared_production").AsBoolean().WithDefaultValue(false)
            .WithColumn("opc_node").AsString(1024).Nullable()
            .WithColumn("opc_subscribe_to_changes").AsBoolean()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable().WithDefault(SystemMethods.CurrentDateTimeOffset)
            .WithColumn("created_by").AsString(255).NotNullable().WithDefaultValue("[system]")
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsString(255).Nullable()
            .WithColumn("deleted").AsBoolean().NotNullable().WithDefaultValue(false);

        Create.Index("idx_equipment_name").OnTable("equipment").OnColumn("name");
        Create.Index("idx_equipment_deleted").OnTable("equipment").OnColumn("deleted");
    }

    public override void Down() {
        Delete.Table("users");
        Delete.Table("equipment");
    }
}
