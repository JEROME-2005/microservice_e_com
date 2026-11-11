using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LankaMart.Orders.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "orders");

            migrationBuilder.CreateSequence(
                name: "order_number_seq",
                schema: "orders",
                startValue: 880001L);

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_number = table.Column<string>(type: "text", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    subtotal_cents = table.Column<long>(type: "bigint", nullable: false),
                    delivery_fee_cents = table.Column<long>(type: "bigint", nullable: false),
                    discount_cents = table.Column<long>(type: "bigint", nullable: false),
                    total_cents = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    delivery_address_recipient_name = table.Column<string>(type: "text", nullable: false),
                    delivery_address_phone = table.Column<string>(type: "text", nullable: false),
                    delivery_address_line1 = table.Column<string>(type: "text", nullable: false),
                    delivery_address_line2 = table.Column<string>(type: "text", nullable: true),
                    delivery_address_city = table.Column<string>(type: "text", nullable: false),
                    delivery_address_district = table.Column<string>(type: "text", nullable: false),
                    delivery_address_postal_code = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payment_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancel_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "order_events",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    actor = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_events_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "order_items",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_name_snapshot = table.Column<string>(type: "text", nullable: false),
                    sku = table.Column<string>(type: "text", nullable: false),
                    image_url_snapshot = table.Column<string>(type: "text", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price_cents = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_items_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shipments",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipments", x => x.id);
                    table.ForeignKey(
                        name: "fk_shipments_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_events_order_id",
                schema: "orders",
                table: "order_events",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_items_order_id",
                schema: "orders",
                table: "order_items",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer_id_created_at",
                schema: "orders",
                table: "orders",
                columns: new[] { "customer_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_order_number",
                schema: "orders",
                table: "orders",
                column: "order_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_status_payment_expires_at",
                schema: "orders",
                table: "orders",
                columns: new[] { "status", "payment_expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_shipments_order_id",
                schema: "orders",
                table: "shipments",
                column: "order_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "order_events",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_items",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "shipments",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "orders");

            migrationBuilder.DropSequence(
                name: "order_number_seq",
                schema: "orders");
        }
    }
}
