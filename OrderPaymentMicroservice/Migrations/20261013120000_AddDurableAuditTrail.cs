using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace OrderPaymentMicroservice.Migrations;

public partial class AddDurableAuditTrail : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("AuditEvents", columns: table => new
        {
            Id = table.Column<Guid>("uuid", nullable: false),
            CorrelationId = table.Column<Guid>("uuid", nullable: false),
            ActorKind = table.Column<string>("character varying(16)", maxLength: 16, nullable: false),
            ActorUserId = table.Column<int>("integer", nullable: true),
            Action = table.Column<string>("character varying(64)", maxLength: 64, nullable: false),
            ResourceType = table.Column<string>("character varying(16)", maxLength: 16, nullable: false),
            ResourceId = table.Column<int>("integer", nullable: true),
            Phase = table.Column<string>("character varying(16)", maxLength: 16, nullable: false),
            Outcome = table.Column<string>("character varying(32)", maxLength: 32, nullable: false),
            HttpStatusCode = table.Column<int>("integer", nullable: true),
            CreatedAt = table.Column<DateTime>("timestamp with time zone", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_AuditEvents", e => e.Id));
        migrationBuilder.CreateIndex("IX_AuditEvents_CorrelationId", "AuditEvents", "CorrelationId");
        migrationBuilder.CreateIndex("IX_AuditEvents_CreatedAt", "AuditEvents", "CreatedAt");
        migrationBuilder.Sql("""
            CREATE FUNCTION reject_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Audit events are append-only' USING ERRCODE = 'P0001'; END; $$;
            CREATE TRIGGER audit_append_only BEFORE UPDATE OR DELETE ON "AuditEvents"
              FOR EACH ROW EXECUTE FUNCTION reject_audit_mutation();
            CREATE TRIGGER audit_no_truncate BEFORE TRUNCATE ON "AuditEvents"
              FOR EACH STATEMENT EXECUTE FUNCTION reject_audit_mutation();
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AuditEvents");
        migrationBuilder.Sql("DROP FUNCTION reject_audit_mutation();");
    }
}
