# .NET 9 & EF CORE (POSTGRESQL) ARCHITECTURAL STANDARDS

## 1. POSTGRESQL & EF CORE INTEGRITY
- **Database Context:** `SunHotelDbContext` is registered with `Npgsql.EntityFrameworkCore.PostgreSQL`.
- **Timestamp Configuration:**
  - `AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);` is enabled in `Program.cs`.
  - Prefer `DateTime.UtcNow` for timestamps (e.g., `CreatedAt`, `UpdatedAt`, `PaymentTime`).
- **Currency & Decimal Types:**
  - Room prices, invoice totals, and transaction amounts are stored in VND as `decimal` (`decimal(18,2)`).
  - Never use `float` or `double` for currency.
- **Transactions & Concurrency:**
  - Operations modifying multiple tables (e.g., booking creation + room status update + invoice creation) MUST run inside an EF Core transaction:
    ```csharp
    await using var transaction = await _context.Database.BeginTransactionAsync(ct);
    try {
        // ... modifications
        await _context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    } catch {
        await transaction.RollbackAsync(ct);
        throw;
    }
    ```
- **Asynchronous Execution:**
  - Always use `ToListAsync()`, `FirstOrDefaultAsync()`, `AnyAsync()`, `CountAsync()`, `SaveChangesAsync()`.
  - Avoid blocking calls (`.Result`, `.Wait()`) which cause thread pool starvation.
- **Avoid N+1 Queries:**
  - Eager load required navigation properties using `.Include()` and `.ThenInclude()`.
  - Use `.AsNoTracking()` for read-only reporting and listing queries to minimize EF change tracker overhead.

## 2. CONTROLLERS & ROUTING
- **Route Attributes & REST Conventions:**
  - Standard MVC controllers inherit from `Controller`.
  - API controllers use `[ApiController]`, `[Route("api/[controller]")]`, and inherit from `ControllerBase` (or `Controller`).
- **Response Consistency:**
  - API endpoints must return structured JSON: `{ success = true/false, message = "...", data = ... }`.
  - Use appropriate HTTP status codes: `200 OK`, `201 Created`, `400 BadRequest`, `401 Unauthorized`, `403 Forbidden`, `404 NotFound`, `429 TooManyRequests`.
- **Model Validation:**
  - Always verify `ModelState.IsValid` before persisting entities from user input.

## 3. DEPENDENCY INJECTION LIFECYCLES
- **Scoped:** Services interacting with `SunHotelDbContext` (`BookingService`, `RoomService`, `UserService`, `AiChatService`, `AiToolExecutor`, `SePayService`).
- **Singleton:** Stateless thread-safe singletons (`EmailRateLimiter.Instance`).
- **Hosted Services:** Background workers inheriting from `BackgroundService` (`SePayPollingService`, `EmailRetryBackgroundService`).
  - When accessing DbContext inside a background service, ALWAYS create a new scope:
    ```csharp
    using var scope = _serviceProvider.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<SunHotelDbContext>();
    ```
