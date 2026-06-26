#!/usr/bin/env python3
"""Generate ERD diagram as PNG using matplotlib."""

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import matplotlib.patches as mpatches
from matplotlib.patches import FancyBboxPatch

# ── Table definitions ─────────────────────────────────────────────────────────
TABLES = {
    "room_type": {
        "color": "#4A90D9",
        "cols": [
            ("PK", "id", "INT"),
            ("", "name", "VARCHAR(100)"),
            ("", "description", "TEXT"),
            ("", "base_price", "DECIMAL(12,2)"),
            ("", "max_guests", "INT"),
            ("", "amenities", "TEXT"),
            ("", "image_url", "VARCHAR(255)"),
            ("", "is_active", "BOOL"),
            ("", "created_at", "DATETIME"),
            ("", "updated_at", "DATETIME"),
        ],
    },
    "room": {
        "color": "#5BA85A",
        "cols": [
            ("PK", "id", "INT"),
            ("FK", "room_type_id", "INT"),
            ("", "room_number", "VARCHAR(10)"),
            ("", "floor", "INT"),
            ("", "status", "INT (RoomStatus)"),
            ("", "created_at", "DATETIME"),
            ("", "updated_at", "DATETIME"),
        ],
    },
    "user": {
        "color": "#E67E22",
        "cols": [
            ("PK", "id", "INT"),
            ("", "username", "VARCHAR(50)"),
            ("", "password", "VARCHAR(255)"),
            ("", "full_name", "VARCHAR(100)"),
            ("", "role", "INT (UserRole)"),
            ("", "email", "VARCHAR(100)"),
            ("", "phone", "VARCHAR(20)"),
            ("", "is_active", "BOOL"),
            ("", "last_login", "DATETIME"),
            ("", "created_at", "DATETIME"),
            ("", "updated_at", "DATETIME"),
        ],
    },
    "booking": {
        "color": "#9B59B6",
        "cols": [
            ("PK", "id", "INT"),
            ("", "booking_code", "VARCHAR(20)"),
            ("FK", "room_id", "INT (nullable)"),
            ("FK", "room_type_id", "INT"),
            ("", "guest_name", "VARCHAR(100)"),
            ("", "phone", "VARCHAR(20)"),
            ("", "email", "VARCHAR(100)"),
            ("", "id_number", "VARCHAR(20)"),
            ("", "check_in_date", "DATE"),
            ("", "check_out_date", "DATE"),
            ("", "actual_check_in", "DATETIME"),
            ("", "actual_check_out", "DATETIME"),
            ("", "num_guests", "INT"),
            ("", "status", "INT (BookingStatus)"),
            ("", "total_amount", "DECIMAL(12,2)"),
            ("", "notes", "TEXT"),
            ("", "created_by", "INT"),
            ("", "created_at", "DATETIME"),
            ("", "updated_at", "DATETIME"),
        ],
    },
    "service": {
        "color": "#1ABC9C",
        "cols": [
            ("PK", "id", "INT"),
            ("FK", "booking_id", "INT"),
            ("", "service_name", "VARCHAR(100)"),
            ("", "service_type", "INT (ServiceType)"),
            ("", "quantity", "INT"),
            ("", "unit_price", "DECIMAL(12,2)"),
            ("", "total_amount", "DECIMAL(12,2)"),
            ("", "notes", "TEXT"),
            ("", "created_at", "DATETIME"),
        ],
    },
    "invoice": {
        "color": "#E74C3C",
        "cols": [
            ("PK", "id", "INT"),
            ("FK", "booking_id", "INT"),
            ("FK", "created_by", "INT"),
            ("", "invoice_number", "VARCHAR(20)"),
            ("", "room_charge", "DECIMAL(12,2)"),
            ("", "service_charge", "DECIMAL(12,2)"),
            ("", "discount", "DECIMAL(12,2)"),
            ("", "total_amount", "DECIMAL(12,2)"),
            ("", "payment_method", "INT (PaymentMethod)"),
            ("", "payment_status", "INT (PaymentStatus)"),
            ("", "payment_date", "DATETIME"),
            ("", "vietqr_*", "(VietQR fields)"),
            ("", "momo_*", "(MoMo fields)"),
            ("", "sepay_*", "(SePay fields)"),
            ("", "created_at", "DATETIME"),
            ("", "updated_at", "DATETIME"),
        ],
    },
    "ticket": {
        "color": "#F39C12",
        "cols": [
            ("PK", "id", "INT"),
            ("", "ticket_number", "VARCHAR(30)"),
            ("FK", "room_id", "INT"),
            ("", "type", "INT (TicketType)"),
            ("", "description", "TEXT"),
            ("", "priority", "INT (TicketPriority)"),
            ("", "status", "INT (TicketStatus)"),
            ("FK", "reported_by", "INT"),
            ("FK", "assigned_to", "INT (nullable)"),
            ("", "image_url", "VARCHAR(255)"),
            ("", "resolution_notes", "TEXT"),
            ("", "created_at", "DATETIME"),
            ("", "resolved_at", "DATETIME"),
            ("", "closed_at", "DATETIME"),
        ],
    },
    "audit_log": {
        "color": "#7F8C8D",
        "cols": [
            ("PK", "id", "INT"),
            ("FK", "user_id", "INT (nullable)"),
            ("", "action", "VARCHAR(100)"),
            ("", "entity_type", "VARCHAR(50)"),
            ("", "entity_id", "INT"),
            ("", "old_value", "TEXT"),
            ("", "new_value", "TEXT"),
            ("", "ip_address", "VARCHAR(45)"),
            ("", "created_at", "DATETIME"),
        ],
    },
    "email_queue": {
        "color": "#2C3E50",
        "cols": [
            ("PK", "id", "INT"),
            ("", "to_email", "VARCHAR(100)"),
            ("", "subject", "VARCHAR(255)"),
            ("", "template", "VARCHAR(50)"),
            ("", "template_data", "TEXT"),
            ("", "status", "INT (EmailStatus)"),
            ("", "attempts", "INT"),
            ("", "last_error", "TEXT"),
            ("", "sent_at", "DATETIME"),
            ("", "created_at", "DATETIME"),
        ],
    },
}

# ── Layout positions (x, y) in data coords ────────────────────────────────────
POS = {
    "room_type": (0.0, 7.0),
    "room": (4.0, 7.0),
    "booking": (8.5, 7.0),
    "service": (13.0, 7.0),
    "invoice": (8.5, 1.5),
    "user": (13.0, 1.5),
    "ticket": (4.0, 1.5),
    "audit_log": (0.0, 1.5),
    "email_queue": (0.0, -4.5),
}

ROW_H = 0.38
HEADER_H = 0.55
TABLE_W = 3.6


def table_height(name):
    return HEADER_H + len(TABLES[name]["cols"]) * ROW_H + 0.1


def table_bottom(name):
    x, y = POS[name]
    return y - table_height(name)


def col_center_y(name, col_index):
    x, y = POS[name]
    return y - HEADER_H - (col_index + 0.5) * ROW_H


def table_mid_y(name):
    x, y = POS[name]
    return y - table_height(name) / 2


def draw_table(ax, name, info):
    x, y = POS[name]
    color = info["color"]
    cols = info["cols"]
    h = table_height(name)

    # Shadow
    shadow = FancyBboxPatch(
        (x + 0.07, y - h - 0.07),
        TABLE_W,
        h,
        boxstyle="round,pad=0.05",
        linewidth=0,
        facecolor="#00000022",
        zorder=1,
    )
    ax.add_patch(shadow)

    # Body
    body = FancyBboxPatch(
        (x, y - h),
        TABLE_W,
        h,
        boxstyle="round,pad=0.05",
        linewidth=1.2,
        edgecolor="#333333",
        facecolor="white",
        zorder=2,
    )
    ax.add_patch(body)

    # Header
    header = FancyBboxPatch(
        (x, y - HEADER_H),
        TABLE_W,
        HEADER_H,
        boxstyle="round,pad=0.05",
        linewidth=0,
        facecolor=color,
        zorder=3,
    )
    ax.add_patch(header)

    ax.text(
        x + TABLE_W / 2,
        y - HEADER_H / 2,
        name,
        ha="center",
        va="center",
        fontsize=9.5,
        fontweight="bold",
        color="white",
        zorder=4,
    )

    # Column rows
    for i, (key, col, dtype) in enumerate(cols):
        row_y = y - HEADER_H - i * ROW_H
        # Alternating row bg
        bg_color = "#F0F4FF" if i % 2 == 0 else "white"
        row_rect = FancyBboxPatch(
            (x + 0.05, row_y - ROW_H + 0.02),
            TABLE_W - 0.1,
            ROW_H - 0.02,
            boxstyle="round,pad=0.01",
            linewidth=0,
            facecolor=bg_color,
            zorder=2,
        )
        ax.add_patch(row_rect)

        # Key badge
        if key == "PK":
            kc = "#FFD700"
            kt = "#7A5700"
        elif key == "FK":
            kc = "#90C8F0"
            kt = "#1A5276"
        else:
            kc = None
            kt = None

        if kc:
            badge = FancyBboxPatch(
                (x + 0.1, row_y - ROW_H + 0.07),
                0.32,
                ROW_H - 0.18,
                boxstyle="round,pad=0.02",
                linewidth=0,
                facecolor=kc,
                zorder=4,
            )
            ax.add_patch(badge)
            ax.text(
                x + 0.26,
                row_y - ROW_H / 2,
                key,
                ha="center",
                va="center",
                fontsize=5.5,
                fontweight="bold",
                color=kt,
                zorder=5,
            )

        ax.text(
            x + 0.52,
            row_y - ROW_H / 2,
            col,
            ha="left",
            va="center",
            fontsize=7.2,
            color="#1a1a1a",
            zorder=4,
        )
        ax.text(
            x + TABLE_W - 0.08,
            row_y - ROW_H / 2,
            dtype,
            ha="right",
            va="center",
            fontsize=6.0,
            color="#666666",
            style="italic",
            zorder=4,
        )


# ── Relations (start_table, start_col_idx, end_table, end_col_idx, label) ─────
RELATIONS = [
    # room_type → room (room_type_id col 1)
    ("room_type", None, "room", 1, "1", "N"),
    # room_type → booking (room_type_id col 3)
    ("room_type", None, "booking", 3, "1", "N"),
    # room → booking (room_id col 2)
    ("room", None, "booking", 2, "1", "N"),
    # room → ticket (room_id col 2)
    ("room", None, "ticket", 2, "1", "N"),
    # booking → service (booking_id col 1)
    ("booking", None, "service", 1, "1", "N"),
    # booking → invoice (booking_id col 1)
    ("booking", None, "invoice", 1, "1", "1"),
    # user → invoice (created_by col 2)
    ("user", None, "invoice", 2, "1", "N"),
    # user → ticket (reported_by col 7)
    ("user", None, "ticket", 7, "1", "N"),
    # user → audit_log (user_id col 1)
    ("user", None, "audit_log", 1, "1", "N"),
]


def draw_arrow(ax, t1, t2, card1, card2):
    x1, y1 = POS[t1]
    x2, y2 = POS[t2]
    # mid points of right/left/top/bottom edges
    cx1, cy1 = x1 + TABLE_W / 2, table_mid_y(t1)
    cx2, cy2 = x2 + TABLE_W / 2, table_mid_y(t2)

    # Determine best edge to connect
    dx = cx2 - cx1
    dy = cy2 - cy1

    if abs(dx) >= abs(dy):
        if dx > 0:
            p1 = (x1 + TABLE_W, table_mid_y(t1))
            p2 = (x2, table_mid_y(t2))
        else:
            p1 = (x1, table_mid_y(t1))
            p2 = (x2 + TABLE_W, table_mid_y(t2))
    else:
        if dy > 0:
            p1 = (x1 + TABLE_W / 2, y1)
            p2 = (x2 + TABLE_W / 2, table_bottom(t2))
        else:
            p1 = (x1 + TABLE_W / 2, table_bottom(t1))
            p2 = (x2 + TABLE_W / 2, y2)

    ax.annotate(
        "",
        xy=p2,
        xytext=p1,
        arrowprops=dict(arrowstyle="-|>", color="#555555", lw=1.2, mutation_scale=12),
        zorder=0,
    )

    mx = (p1[0] + p2[0]) / 2
    my = (p1[1] + p2[1]) / 2
    ax.text(
        mx + 0.05,
        my + 0.12,
        f"{card1}:{card2}",
        fontsize=7,
        color="#333333",
        ha="center",
        zorder=6,
        bbox=dict(facecolor="white", edgecolor="none", alpha=0.7, pad=1),
    )


# ── Build figure ─────────────────────────────────────────────────────────────
fig, ax = plt.subplots(figsize=(22, 17))
ax.set_xlim(-0.5, 17.5)
ax.set_ylim(-7.5, 9.0)
ax.axis("off")
fig.patch.set_facecolor("#F7F9FC")

ax.set_title(
    "QuanLyKhachSan — Database ERD\n(Sun Hotel Management System)",
    fontsize=15,
    fontweight="bold",
    color="#2C3E50",
    pad=14,
)

# Draw relations first (behind tables)
for t1, _, t2, ci, c1, c2 in RELATIONS:
    draw_arrow(ax, t1, t2, c1, c2)

# Draw tables
for name, info in TABLES.items():
    draw_table(ax, name, info)

# ── Legend ────────────────────────────────────────────────────────────────────
pk_patch = mpatches.Patch(
    facecolor="#FFD700", edgecolor="#7A5700", label="PK – Primary Key"
)
fk_patch = mpatches.Patch(
    facecolor="#90C8F0", edgecolor="#1A5276", label="FK – Foreign Key"
)
ax.legend(
    handles=[pk_patch, fk_patch],
    loc="lower right",
    fontsize=8,
    framealpha=0.9,
    edgecolor="#cccccc",
)

plt.tight_layout()
plt.savefig(
    "database_erd.png", dpi=150, bbox_inches="tight", facecolor=fig.get_facecolor()
)
print("Saved: database_erd.png")
