using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace OrderPaymentMicroservice.Migrations;

public partial class AddAtomicAuditAndOrderIdempotency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>("CreationKeyHash", "Orders", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>("CreationRequestHash", "Orders", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>("CreationResponseJson", "Orders", type: "jsonb", nullable: true);
        migrationBuilder.CreateIndex("IX_Orders_CustomerId_CreationKeyHash", "Orders", new[] { "CustomerId", "CreationKeyHash" }, unique: true);
        migrationBuilder.Sql("""
            CREATE FUNCTION audit_business_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE
              resource text;
              record_id integer;
              state integer;
              correlation uuid;
              actor integer;
              kind text;
            BEGIN
              resource := CASE WHEN TG_TABLE_NAME = 'Orders' THEN 'ORDER' ELSE 'PAYMENT' END;
              IF TG_OP = 'DELETE' THEN record_id := OLD."Id"; state := OLD."Status";
              ELSE record_id := NEW."Id"; state := NEW."Status"; END IF;
              correlation := COALESCE(NULLIF(current_setting('app.audit_correlation', true), '')::uuid, gen_random_uuid());
              actor := NULLIF(current_setting('app.audit_actor', true), '')::integer;
              kind := COALESCE(NULLIF(current_setting('app.audit_kind', true), ''), 'SYSTEM');
              INSERT INTO "AuditEvents" ("Id", "CorrelationId", "ActorKind", "ActorUserId", "Action",
                "ResourceType", "ResourceId", "Phase", "Outcome", "HttpStatusCode", "CreatedAt")
              VALUES (gen_random_uuid(), correlation, kind, actor, resource || '_' || TG_OP,
                resource, record_id, 'COMMIT', CASE WHEN TG_OP = 'DELETE' THEN 'DELETED' ELSE 'STATE_' || state END,
                NULL, clock_timestamp());
              IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
            END; $$;
            CREATE TRIGGER orders_atomic_audit AFTER INSERT OR UPDATE OR DELETE ON "Orders"
              FOR EACH ROW EXECUTE FUNCTION audit_business_commit();
            CREATE TRIGGER payments_atomic_audit AFTER INSERT OR UPDATE OR DELETE ON "Payments"
              FOR EACH ROW EXECUTE FUNCTION audit_business_commit();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER orders_atomic_audit ON "Orders";
            DROP TRIGGER payments_atomic_audit ON "Payments";
            DROP FUNCTION audit_business_commit();
            """);
        migrationBuilder.DropIndex("IX_Orders_CustomerId_CreationKeyHash", "Orders");
        migrationBuilder.DropColumn("CreationKeyHash", "Orders");
        migrationBuilder.DropColumn("CreationRequestHash", "Orders");
        migrationBuilder.DropColumn("CreationResponseJson", "Orders");
    }
}
