# SUN HOTEL — AGENT OPERATING MANIFESTO & ANTI-SLOP MANDATE
**Classification:** STRICT WORKSPACE RULEBOOK  
**Target Codebase:** Sun Hotel Management System (ASP.NET Core 9 MVC + EF Core 9 + PostgreSQL + AI Concierge)

---

## 🚨 MISSION CRITICAL: ZERO AI SLOP MANDATE

This repository is **STRICTLY ANTI-AI SLOP**. Any contribution containing half-baked implementations, hallucinated dependencies, dummy placeholders, or superficial code is completely unacceptable.

### ⛔ FORBIDDEN PATTERNS (AUTOMATIC REJECTION)

1. **NO PLACEHOLDERS OR PSEUDO-CODE:**
   - NEVER emit `// TODO: Implement later`, `throw new NotImplementedException()`, `// ... rest of code unchanged ...` in generated code.
   - NEVER create empty `catch` blocks or swallow exceptions without structured logging (`_logger.LogError(ex, ...)`).
   - NEVER return mocked or dummy hardcoded data in production pathways where real database tables and services exist.

2. **NO ARCHITECTURAL DRIFT OR DEPENDENCY BLOAT:**
   - NEVER install unapproved NuGet packages (e.g., AutoMapper, MediatR, FluentValidation, Dapper) when native C# 13 / .NET 9 idiomatic patterns already solve the problem.
   - Do NOT introduce MySQL (`MySqlConnector`), SQL Server (`UseSqlServer`), or SQLite assumptions. The active database is **PostgreSQL 16** via `Npgsql.EntityFrameworkCore.PostgreSQL`.
   - NEVER use `Console.WriteLine` or `Debug.WriteLine`. Always inject `ILogger<T>` and use Serilog structured logging.

3. **NO SHALLOW / MONO-LANGUAGE ASSUMPTIONS:**
   - The hotel operates for both domestic Vietnamese guests and international foreign tourists.
   - NEVER hardcode Vietnamese-only responses, error messages, or regex patterns in guest-facing surfaces (AI concierge, booking flow, lookup).
   - NEVER write naive regex patterns that misclassify normal English words (e.g., matching `available` or `tomorrow` as booking codes).

4. **NO WHOLESALE DESTRUCTIVE FILE OVERWRITES:**
   - NEVER overwrite a 500+ line file from scratch when a surgical targeted edit (`replace_file_content` / `multi_replace_file_content`) accomplishes the change.
   - Always preserve existing comments, XML documentation, region structures, and error handling context.

---

## 🏛️ CORE ARCHITECTURAL COMPASS

### 1. Technology Stack
- **Framework:** ASP.NET Core 9.0 (C# 13, Nullable Enabled, Implicit Usings)
- **Database:** PostgreSQL 16 (Alpine) via EF Core 9 (`SunHotelDbContext`)
- **Authentication:** Cookie Authentication (`SunHotel.Auth`) + BCrypt.Net-Next
- **Containerization:** Docker + Docker Compose (`sunhotel_app`, `sunhotel_postgres`)
- **AI Engine:** Google Gemini (`gemini-2.5-flash`, `gemini-2.0-flash`, `gemini-1.5-flash`) + Local Deterministic Multi-Turn Fallback Engine
- **Payments:** SePay (QR / Webhook / IPN), VietQR (MBBank), MoMo
- **Frontend:** Razor MVC (`.cshtml`), Vanilla CSS design tokens (`site.css`, `ai-chat-widget.css`), Vanilla ES6 JS, Bootstrap Icons

### 2. Layering & Separation of Concerns
```
Controllers (HTTP / Routing / Auth / ViewModel)
    └── Services (Business Logic / Transactions / AI Orchestration)
            └── Data (SunHotelDbContext / PostgreSQL Entities / Migrations)
```
- **Controllers:** Thin controllers. Responsible for model validation, authorization attributes, calling services, and returning appropriate `IActionResult` (Views, JSON, redirects).
- **Services:** Heavy business logic. Manage EF Core transactions, currency conversions, email dispatches, and third-party webhook verifications.
- **Data:** `SunHotelDbContext` with explicit configurations in `OnModelCreating`, decimal precisions (`decimal(18,2)`), and PostgreSQL index constraints.

---

## 🛡️ CORE QUALITY CHECKLIST (PRE-COMMIT VERIFICATION)

Before completing any task in this repository, you must verify:
1. **Compilation:** `dotnet build` passes with **0 errors**.
2. **Database Integrity:** No PostgreSQL-incompatible SQL functions (e.g., no `GETDATE()`, no `ISNULL()`; use `DateTime.UtcNow`, `COALESCE`, or native EF Core LINQ).
3. **Multilingual Safety:** Guest interactions adapt seamlessly to language (Vietnamese for `vi`, English for all international guests).
4. **Security & RBAC:** Protected endpoints have explicit `[Authorize(Roles = "...")]`. Internal operational metrics (revenue, occupancy) are strictly prohibited from guest exposure.
5. **State & Fallback:** If third-party APIs (Gemini, SePay, SMTP) fail or time out, the system degrades gracefully with high-fidelity conversational fallbacks without crashing.

---

## 📂 DETAILED GUIDELINES

Refer to specialized rules in `.agents/rules/`:
- [Anti-Slop Enforcement](file:///.agents/rules/anti-slop-enforcement.md)
- [.NET 9 & EF Core Standards](file:///.agents/rules/dotnet-architecture-efcore.md)
- [AI Concierge & Multilingual Standards](file:///.agents/rules/ai-agent-multilingual-standards.md)
- [Security, Auth & Payments](file:///.agents/rules/security-and-payments.md)
