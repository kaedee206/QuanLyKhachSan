# SECURITY, AUTHENTICATION & PAYMENT STANDARDS

## 1. AUTHENTICATION & ROLE-BASED ACCESS CONTROL (RBAC)
- **Cookie Authentication:** Cookie named `SunHotel.Auth` with `HttpOnly = true`, `SameSite = SameSiteMode.Lax`.
- **Password Hashing:** Passwords must be hashed using `BCrypt.Net.BCrypt.HashPassword()`. Never store plaintext or MD5/SHA1 hashes.
- **Roles in Hierarchy:**
  - `Admin`: Full system control, user management, configuration, audit logs.
  - `Manager`: Reports, revenue analytics, room pricing, staff scheduling.
  - `Receptionist`: Check-in, check-out, onsite bookings, invoice creation.
  - `Housekeeping`: Room status toggles (Clean, Dirty, Cleaning).
  - `Maintenance`: Facility tickets, repair statuses.
  - `Customer`: Personal booking history, customer portal lookup.
  - `Guest`: Anonymous public browsing, public booking, AI concierge.
- **Attribute Enforcement:**
  - Controllers/Actions requiring authorization MUST declare `[Authorize(Roles = "...")]`.
  - Never trust client-submitted user IDs or role claims; always retrieve the identity from `User.FindFirst(ClaimTypes.NameIdentifier)`.

## 2. PAYMENT INTEGRITY & WEBHOOKS
- **Payment Providers:**
  - `SePay`: Dynamic VietQR transfer with automatic transaction reconciliation.
  - `MoMo`: Payment gateway with signature verification.
  - `VietQR`: Public QR code generation with exact payment descriptions.
- **Webhook Security:**
  - Webhooks (`/webhook/sepay`) MUST validate the secret key or bearer token before processing payment notifications.
  - Ensure **Idempotency**: Check if the transaction ID (`transactionId` / `sepayId`) has already been credited to prevent double-spending or duplicate invoice settlements.
  - Update `Invoice.Status = Paid` and `Booking.Status = Confirmed` atomically inside a database transaction.

## 3. SECRETS & ENVIRONMENT HYGIENE
- NEVER commit secrets (`.env`, `credentials.generated.txt`, private keys) to git.
- `.env.example` serves as the template with dummy placeholder values.
- In Docker, environment variables are passed via `docker-compose.yml` (`env_file: .env` and explicit overrides).
- All sensitive configuration keys (`JWT_SECRET`, `SEPAY_SECRET_KEY`, `EMAIL_PASSWORD`, `AI_API_KEY`) must be read via `IConfiguration` or `DotNetEnv`.

## 4. INPUT VALIDATION & DEFENSE AGAINST INJECTION
- Prevent SQL injection: Always use parameterized queries or EF Core LINQ. Never concatenate raw strings into raw SQL (`FromSqlRaw` must always take parameter objects).
- Prevent XSS: Razor views encode output by default (`@Model.Property`). When using `@Html.Raw()`, ensure the content has been rigorously sanitized.
- Rate Limiting: `AiRateLimitMiddleware` restricts AI endpoint flooding per IP/Session.
