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
            .WithColumn("group_id").AsInt32().Nullable()
            .WithColumn("declares_production_start").AsBoolean().WithDefaultValue(false)
            .WithColumn("declares_production_finished").AsBoolean().WithDefaultValue(false)
            .WithColumn("opc_node").AsString(1024).Nullable()
            .WithColumn("opc_subscribe_to_changes").AsBoolean()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable().WithDefault(SystemMethods.CurrentDateTimeOffset)
            .WithColumn("created_by").AsString(255).NotNullable().WithDefaultValue("[system]")
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsString(255).Nullable()
            .WithColumn("deleted").AsBoolean().NotNullable().WithDefaultValue(false);

        Create.Index("idx_equipment_name").OnTable("equipment").OnColumn("name");
        Create.Index("idx_equipment_deleted").OnTable("equipment").OnColumn("deleted");
        Create.ForeignKey("fk_equipment_equipment_groups")
            .FromTable("equipment").ForeignColumn("group_id")
            .ToTable("equipment_groups").PrimaryColumn("id");

        Create.Table("equipment_schedules")
            .WithColumn("id").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("equipment_id").AsInt32().NotNullable()
            .WithColumn("start").AsDateTimeOffset().NotNullable()
            .WithColumn("stop").AsDateTimeOffset().NotNullable()
            .WithColumn("scheduled_units").AsInt32().NotNullable()
            .WithColumn("cylce_time_seconds").AsInt32().NotNullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable().WithDefault(SystemMethods.CurrentDateTimeOffset)
            .WithColumn("created_by").AsString(255).NotNullable().WithDefaultValue("[system]")
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsString(255).Nullable()
            .WithColumn("deleted").AsBoolean().NotNullable().WithDefaultValue(false);

        Create.Index("idx_equipment_schedules_start").OnTable("equipment_schedules").OnColumn("start");
        Create.ForeignKey("fk_equipment_schedules_equipment")
            .FromTable("equipment_schedules").ForeignColumn("equipment_id")
            .ToTable("equipment").PrimaryColumn("id");

        Create.Table("equipment_schedules_updates")
            .WithColumn("id").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("equipment_schedule_id").AsInt32().NotNullable()
            .WithColumn("updated_at").AsDateTimeOffset().NotNullable()
            .WithColumn("updated_by").AsString(255).NotNullable()
            .WithColumn("updated_reason").AsString(2048).NotNullable();

        Create.ForeignKey("fk_equipment_schedules_updates_equipment_schedules")
            .FromTable("equipment_schedules_updates").ForeignColumn("equipment_schedule_id")
            .ToTable("equipment_schedules").PrimaryColumn("id");

        Create.Table("equipment_downtimeevent_reasons")
            .WithColumn("id").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("planned").AsBoolean().WithDefaultValue(false)
            .WithColumn("created_at").AsDateTimeOffset().NotNullable().WithDefault(SystemMethods.CurrentDateTimeOffset)
            .WithColumn("created_by").AsString(255).NotNullable().WithDefaultValue("[system]")
            .WithColumn("updated_at").AsDateTimeOffset().Nullable()
            .WithColumn("updated_by").AsString(255).Nullable()
            .WithColumn("deleted").AsBoolean().NotNullable().WithDefaultValue(false);

        Create.Index("idx_equipment_downtimeevent_reasons_name").OnTable("equipment_downtimeevent_reasons").OnColumn("name");

        Insert.IntoTable("equipment_downtimeevent_reasons").Rows([
           new {name="Planned", planned=true},
           new {name="Unplanned", planned=false},
        ]);

        Create.Table("equipment_downtimeevents")
            .WithColumn("id").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("equipment_id").AsInt32().NotNullable()
            .WithColumn("start").AsDateTimeOffset().NotNullable()
            .WithColumn("stop").AsDateTimeOffset().Nullable()
            .WithColumn("reason_id").AsInt32().NotNullable()
            .WithColumn("created_at").AsDateTimeOffset().NotNullable().WithDefault(SystemMethods.CurrentDateTimeOffset)
            .WithColumn("created_by").AsString(255).NotNullable().WithDefaultValue("[system]")
            .WithColumn("deleted").AsBoolean().NotNullable().WithDefaultValue(false);

        Create.Index("idx_equipment_downtimeevents_equipment_id").OnTable("equipment_downtimeevents").OnColumn("equipment_id");
        Create.Index("idx_equipment_downtimeevents_start").OnTable("equipment_downtimeevents").OnColumn("start");

        Create.ForeignKey("fk_equipment_downtimeevents_equipment")
            .FromTable("equipment_downtimeevents").ForeignColumn("equipment_id")
            .ToTable("equipment").PrimaryColumn("id");
        Create.ForeignKey("fk_equipment_downtimeevents_equipment_downtimeevent_reasons")
            .FromTable("equipment_downtimeevents").ForeignColumn("reason_id")
            .ToTable("equipment_downtimeevent_reasons").PrimaryColumn("id");
    }

    public override void Down() {
        Delete.ForeignKey("fk_equipment_equipment_groups").OnTable("equipment");
        Delete.ForeignKey("fk_equipment_schedules_equipment").OnTable("equipment_schedules");
        Delete.ForeignKey("fk_equipment_schedules_updates_equipment_schedules").OnTable("equipment_schedules_updates");
        Delete.ForeignKey("fk_equipment_downtimeevents_equipment").OnTable("equipment_downtimeevents");
        Delete.ForeignKey("fk_equipment_downtimeevents_equipment_downtimeevent_reasons").OnTable("equipment_downtimeevents");

        Delete.Table("users");
        Delete.Table("equipment");
        Delete.Table("equipment_groups");
        Delete.Table("equipment_schedules");
        Delete.Table("equipment_schedules_updates");
        Delete.Table("equipment_downtimeevent_reasons");
        Delete.Table("equipment_downtimeevents");

    }
}
