# STRICT ANTI-SLOP ENFORCEMENT PROTOCOL

## 1. PRINCIPLE OF COMPLETENESS (NO HALFWAY WORK)
- **Zero Mocking in Production Paths:** Never create fake in-memory lists, dummy strings, or stub responses when modifying controllers or services that have backing PostgreSQL entities (`Rooms`, `Bookings`, `Users`, `Invoices`, `Services`, `Tickets`).
- **Zero Unimplemented Stubs:** Every method must have a full, working implementation. Methods with `throw new NotImplementedException()`, empty bodies, or `return null` as a placeholder are strictly forbidden.
- **Defensive Error Handling:** Every `try-catch` block must either handle the exception gracefully (with structured logging) or rethrow an informative domain exception. Empty catch blocks (`catch (Exception) {}`) are considered critical bugs.

## 2. SURGICAL MODIFICATION DISCIPLINE
- **No Indiscriminate File Wipes:** When editing an existing file, use `replace_file_content` or `multi_replace_file_content`. Never rewrite hundreds of unchanged lines. Preserving comments, business invariants, and helper methods is non-negotiable.
- **Maintain Style & Conventions:**
  - Follow the existing C# naming conventions: `PascalCase` for classes/methods/public properties, `_camelCase` for private fields, `camelCase` for local variables/parameters.
  - Keep using `async/await` throughout I/O pipelines with proper `CancellationToken` support where available.
  - Follow existing dependency injection patterns (Constructor Injection).

## 3. DEPENDENCY & PACKAGE INTEGRITY
- **Zero Unnecessary Packages:** Do not run `dotnet add package` unless explicitly asked by the user or required for an essential architectural upgrade.
- **Leverage Built-In Capabilities:**
  - JSON serialization: Use `System.Text.Json` (or `Newtonsoft.Json` already in `.csproj`).
  - Validation: Use standard data annotations (`[Required]`, `[MaxLength]`, `[EmailAddress]`) or explicit service validations.
  - HTTP clients: Use `IHttpClientFactory` via `AddHttpClient()`.

## 4. LOGGING HYGIENE
- NEVER use `Console.WriteLine()` or `Debug.WriteLine()` in application code.
- Inject `ILogger<T>` and emit structured Serilog logs:
  ```csharp
  // CORRECT
  _logger.LogInformation("Processing booking {BookingCode} for guest {GuestName}", bookingCode, guestName);
  _logger.LogError(ex, "Failed to execute booking creation for room type {RoomTypeId}", roomTypeId);

  // WRONG
  Console.WriteLine("Error: " + ex.Message);
  ```

## 5. DOCKER & RUNTIME ENVIRONMENT COMPLIANCE
- The application runs inside Docker on Alpine Linux (`mcr.microsoft.com/dotnet/aspnet:9.0-alpine`).
- Remember: Alpine images are invariant culture by default. ICU globalization is enabled via `icu-libs` and `InvariantGlobalization=false` in `QuanLyKhachSan.csproj`.
- Do not assume local Windows or macOS path separators. Always use `Path.Combine()` or forward slashes `/`.
